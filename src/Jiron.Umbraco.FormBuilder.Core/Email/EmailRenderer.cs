using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;
using Ganss.Xss;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>What one mail is rendered from.</summary>
/// <param name="Subject">The subject, which becomes the document's title.</param>
/// <param name="Body">The body as rich text; it passes the mail's sanitiser.</param>
/// <param name="Culture">The culture it is written in, for the layout's texts and the writing direction.</param>
/// <param name="Layout">The layout.</param>
/// <param name="SenderName">Who it comes from, for a logo that says nothing of its own.</param>
/// <param name="BaseUrl">The site's address, which a link on the site starts with; never the request's (section 4.10).</param>
internal sealed record EmailPage(string Subject, string Body, string Culture, EmailLayoutDesign Layout, string? SenderName = null, string? BaseUrl = null);

/// <summary>A rendered mail: the one body it is sent with, and the text version every mail has (ADR-0007).</summary>
/// <param name="Html">The whole document, well-formed as XML, with its styles on its elements.</param>
/// <param name="Text">The text version.</param>
internal sealed record RenderedEmail(string Html, string Text);

/// <summary>
/// Turns a mail's rich text into the mail (section 3.3, ADR-0159). What the clients ask for decides the markup: tables, with the
/// styles inline on each element because some clients drop a style sheet and all of them drop the styles of <c>body</c>; a
/// fixed width in <c>&lt;!--[if mso]&gt;</c> tables, because Outlook on Windows ignores <c>max-width</c>; and a fixed table
/// layout with words that may break, so that a long word stays inside a phone's width. The one style sheet only moves the
/// inline colours to the dark scheme, and a client that ignores it shows the light mail. It is serialised as XHTML, so a
/// parser proves it well-formed. Configured once, it may render on many threads at once.
/// </summary>
internal sealed partial class EmailRenderer
{
    private const string Font = "-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif";
    private const string Mono = "ui-monospace,Consolas,Menlo,monospace";
    private const string Wrap = "overflow-wrap:anywhere;word-break:break-word";
    private const string Presentation = "role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"";
    private const int Width = 600;

    internal static readonly EmailColours LightColours = new()
    {
        Accent = "#1f4fbf",
        Page = "#f2f3f5",
        Surface = "#ffffff",
        Text = "#1c1f24",
        Muted = "#596069",
        Rule = "#dfe2e6",
    };

    internal static readonly EmailColours DarkColours = new()
    {
        Accent = "#9dbcff",
        Page = "#121417",
        Surface = "#1c1f24",
        Text = "#e8eaed",
        Muted = "#aab0b9",
        Rule = "#363b43",
    };

    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlParser _parser = new();

    /// <summary>A renderer with the mail's sanitiser: the editors' allowlist, plus the tables of an entry's answers and what wraps them.</summary>
    public EmailRenderer()
    {
        string[] tables = ["div", "table", "caption", "thead", "tbody", "tfoot", "tr", "th", "td"];
        string[] cells = ["colspan", "rowspan", "scope"];
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(EditorHtml.Tags.Concat(tables), StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(EditorHtml.Attributes.Except(["target"]).Concat(cells), StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(EditorHtml.Schemes, StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(["href"], StringComparer.OrdinalIgnoreCase),
            AllowedCssClasses = new HashSet<string>(StringComparer.Ordinal),
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            AllowedAtRules = new HashSet<CssRuleType>(),
            AllowDataAttributes = false,
            AllowCssCustomProperties = false,
        })
        {
            KeepChildNodes = false,
        };
    }

    /// <summary>Renders a mail.</summary>
    public RenderedEmail Render(EmailPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var layout = page.Layout;
        var light = Merged(layout.Look == EmailLook.Letter ? LightColours with { Page = LightColours.Surface } : LightColours, layout.Light);
        var dark = Merged(layout.Look == EmailLook.Letter ? DarkColours with { Page = DarkColours.Surface } : DarkColours, layout.Dark);
        var right = RightToLeft(page.Culture);
        var style = new Styles(light, right);

        var header = Clean(TextOf(layout.Header, page.Culture), page.BaseUrl);
        var body = Clean(page.Body, page.BaseUrl);
        var footer = Clean(TextOf(layout.Footer, page.Culture), page.BaseUrl);

        var html = new StringBuilder();
        Head(html, page, dark, right);
        html.Append(CultureInfo.InvariantCulture, $"<body><table {Presentation} width=\"100%\" class=\"jfb-page\" bgcolor=\"{light.Page}\" style=\"width:100%;background-color:{light.Page}\"><tr>");
        html.Append(CultureInfo.InvariantCulture, $"<td align=\"center\" class=\"jfb-page\" bgcolor=\"{light.Page}\" style=\"padding:{(layout.Look == EmailLook.Card ? "24px 12px" : "24px 16px")};background-color:{light.Page}\">");
        html.Append(CultureInfo.InvariantCulture, $"<!--[if mso]><table {Presentation} width=\"{Width}\" align=\"center\"><tr><td><![endif]-->");
        html.Append(CultureInfo.InvariantCulture, $"<table {Presentation} width=\"100%\" class=\"jfb-surface\" bgcolor=\"{light.Surface}\" style=\"{SurfaceStyle(layout.Look, light)}\">");
        if (layout.Look == EmailLook.Letter)
        {
            Letter(html, page, style, header, body, footer, light);
        }
        else
        {
            Card(html, page, style, header, body, footer, light);
        }

        html.Append("</table><!--[if mso]></td></tr></table><![endif]--></td></tr></table></body></html>");
        return new RenderedEmail(html.ToString(), EmailText.Of(_parser, header, body, footer));
    }

    // The mail's first part, and the style sheet that takes the inline colours to the dark scheme.
    private static void Head(StringBuilder html, EmailPage page, EmailColours dark, bool right)
    {
        html.Append(CultureInfo.InvariantCulture, $"<!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"{Language(page.Culture)}\" dir=\"{(right ? "rtl" : "ltr")}\"><head>");
        html.Append("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.Append("<meta name=\"color-scheme\" content=\"light dark\" /><meta name=\"supported-color-schemes\" content=\"light dark\" />");
        html.Append(CultureInfo.InvariantCulture, $"<title>{Encode(page.Subject)}</title><style>body{{margin:0;padding:0}}");
        var rules = $".jfb-page{{background-color:{dark.Page}!important}}.jfb-surface{{background-color:{dark.Surface}!important;border-color:{dark.Rule}!important}}.jfb-text{{color:{dark.Text}!important}}.jfb-muted{{color:{dark.Muted}!important}}.jfb-link{{color:{dark.Accent}!important}}.jfb-rule{{border-color:{dark.Rule}!important}}";
        html.Append("@media (prefers-color-scheme:dark){").Append(rules).Append('}');
        html.Append("</style><!--[if mso]><style>table,td,p,li,h2,h3,h4,h5,h6,a{font-family:'Segoe UI',Arial,sans-serif !important}</style><![endif]--></head>");
    }

    private static string SurfaceStyle(EmailLook look, EmailColours light) =>
        look == EmailLook.Card
            ? $"width:100%;max-width:{Width}px;table-layout:fixed;border-collapse:separate;background-color:{light.Surface};border:1px solid {light.Rule};border-radius:8px"
            : $"width:100%;max-width:{Width}px;table-layout:fixed;border-collapse:collapse;background-color:{light.Surface}";

    // The card: the logo and the header in their own band, the text, and the footer below a rule.
    private void Card(StringBuilder html, EmailPage page, Styles style, string header, string body, string footer, EmailColours light)
    {
        var logo = LogoOf(page);
        if (logo is not null || header.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"jfb-muted\" style=\"{style.Cell("28px 32px 0")};color:{light.Muted};font-size:14px;line-height:1.5\">{logo}{Style(header, style, muted: true)}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"jfb-text\" style=\"{style.Cell("28px 32px 16px")};color:{light.Text};font-size:16px;line-height:1.6\">{Style(body, style, muted: false)}</td></tr>");
        if (footer.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"jfb-muted jfb-rule\" style=\"{style.Cell("16px 32px 12px")};border-top:1px solid {light.Rule};color:{light.Muted};font-size:13px;line-height:1.5\">{Style(footer, style, muted: true)}</td></tr>");
        }
    }

    // The letter: the logo and the header as a letterhead, the text in a reader's size, and the footer as a sign-off.
    private void Letter(StringBuilder html, EmailPage page, Styles style, string header, string body, string footer, EmailColours light)
    {
        var logo = LogoOf(page);
        if (logo is not null || header.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"jfb-muted\" style=\"{style.Cell("0 0 24px")};color:{light.Muted};font-size:14px;line-height:1.5\">{logo}{Style(header, style, muted: true)}</td></tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"jfb-text\" style=\"{style.Cell("0")};color:{light.Text};font-size:17px;line-height:1.65\">{Style(body, style, muted: false)}</td></tr>");
        if (footer.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"jfb-muted\" style=\"{style.Cell("16px 0 0")};color:{light.Muted};font-size:13px;line-height:1.5\">{Style(footer, style, muted: true)}</td></tr>");
        }
    }

    private static string? LogoOf(EmailPage page)
    {
        if (page.Layout.LogoUrl is not { } address || AbsoluteLogo(address, page.BaseUrl) is not { } uri)
        {
            return null;
        }

        var alt = TextOf(page.Layout.LogoAlt, page.Culture) is { Length: > 0 } said ? said : page.SenderName ?? string.Empty;
        return $"<img src=\"{Encode(uri.AbsoluteUri)}\" alt=\"{Encode(alt)}\" style=\"display:block;max-width:200px;max-height:48px;height:auto;border:0;margin:0 0 16px\" />";
    }

    // The logo's address as a mail needs it: an absolute http or https address as it is, or a path on the site (a media
    // item's, ADR-0215) below the site's base address; none without a base address to put it under.
    private static Uri? AbsoluteLogo(string address, string? baseUrl)
    {
        if (address.StartsWith('/') && !address.StartsWith("//", StringComparison.Ordinal))
        {
            return Uri.TryCreate(baseUrl, UriKind.Absolute, out var site) && site.Scheme is "https" or "http" && Uri.TryCreate(site, address, out var onSite)
                ? onSite
                : null;
        }

        return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
    }

    // A text of the layout in this culture: a layout can hold anything, so one that is not a localised text is left out.
    private static string TextOf(IReadOnlyDictionary<string, string>? texts, string culture)
    {
        if (texts is null || !texts.ContainsKey(string.Empty))
        {
            return string.Empty;
        }

        try
        {
            return new LocalizedHtml(texts).Resolve(culture);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static bool RightToLeft(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture).TextInfo.IsRightToLeft;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static string Language(string culture) => LanguagePattern().IsMatch(culture) ? culture : "en";

    private static EmailColours Merged(EmailColours look, EmailColours? own) => own is null ? look : new EmailColours
    {
        Accent = Colour(own.Accent) ?? look.Accent,
        Page = Colour(own.Page) ?? look.Page,
        Surface = Colour(own.Surface) ?? look.Surface,
        Text = Colour(own.Text) ?? look.Text,
        Muted = Colour(own.Muted) ?? look.Muted,
        Rule = Colour(own.Rule) ?? look.Rule,
    };

    private static string? Colour(string? value) => value is not null && ColourPattern().IsMatch(value) ? value : null;

    private static string Encode(string text) => WebUtility.HtmlEncode(XmlSafe(text));

    // Only what XML can hold: the rest of the control characters and lone surrogates would make the document not well-formed.
    private static string XmlSafe(string text)
    {
        var safe = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                safe.Append(c).Append(text[++i]);
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                safe.Append(c);
            }
        }

        return safe.ToString();
    }

    // A rich text cleaned, with every link that goes anywhere absolute. Both versions of the mail start from it.
    private string Clean(string text, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var document = _parser.ParseDocument($"<!DOCTYPE html><body>{_sanitizer.Sanitize(XmlSafe(text))}");
        foreach (var link in document.Body!.QuerySelectorAll("a[href]"))
        {
            if (Absolute(link.GetAttribute("href")!, baseUrl) is { } address)
            {
                link.SetAttribute("href", address);
            }
            else
            {
                link.RemoveAttribute("href");
            }
        }

        // After the parse, as a numeric reference only becomes such a character there.
        return XmlSafe(document.Body.InnerHtml);
    }

    private static string? Absolute(string href, string? baseUrl)
    {
        // A path that names a share reads as an absolute file address here, and a click on it can give away the reader's sign-in.
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && !href.StartsWith('/'))
        {
            return EditorHtml.Schemes.Contains(absolute.Scheme, StringComparer.OrdinalIgnoreCase) ? absolute.OriginalString : null;
        }

        return baseUrl is not null && Uri.TryCreate(baseUrl, UriKind.Absolute, out var root) && Uri.TryCreate(root, href, out var onSite) ? onSite.AbsoluteUri : null;
    }

    // The cleaned text with its styles on its elements, as XHTML.
    private string Style(string clean, Styles styles, bool muted)
    {
        if (clean.Length == 0)
        {
            return string.Empty;
        }

        var document = _parser.ParseDocument($"<!DOCTYPE html><body>{clean}");
        foreach (var element in document.Body!.Descendants<IElement>())
        {
            styles.Apply(element, muted);
        }

        using var xhtml = new StringWriter(CultureInfo.InvariantCulture);
        foreach (var node in document.Body!.ChildNodes)
        {
            node.ToHtml(xhtml, XhtmlMarkupFormatter.Instance);
        }

        return xhtml.ToString();
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex ColourPattern();

    [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguagePattern();

    // The inline styles of what rich text can hold, from the colours of the light scheme.
    private sealed class Styles(EmailColours colours, bool right)
    {
        private readonly string _align = right ? "right" : "left";

        // A table cell: where the words start, and that they stay inside it.
        public string Cell(string padding) => $"padding:{padding};font-family:{Font};text-align:{_align};{Wrap}";

        public void Apply(IElement element, bool muted)
        {
            var colour = muted ? colours.Muted : colours.Text;
            var textClass = muted ? "jfb-muted" : "jfb-text";

            // A heading that opens the text has the cell's padding above it already.
            var opens = element.PreviousElementSibling is null && element.ParentElement is { LocalName: "body" };
            switch (element.LocalName)
            {
                case "p":
                    Set(element, textClass, $"margin:0 0 16px;color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "h2":
                    Set(element, textClass, $"margin:{(opens ? "0" : "24px")} 0 8px;font-size:22px;line-height:1.3;font-weight:600;color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "h3":
                    Set(element, textClass, $"margin:{(opens ? "0" : "20px")} 0 8px;font-size:18px;line-height:1.35;font-weight:600;color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "h4" or "h5" or "h6":
                    Set(element, textClass, $"margin:{(opens ? "0" : "16px")} 0 8px;font-size:16px;line-height:1.4;font-weight:600;color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "a":
                    Set(element, "jfb-link", $"color:{colours.Accent};text-decoration:underline;{Wrap}");
                    break;
                case "ul" or "ol":
                    Set(element, textClass, right ? $"margin:0 0 16px;padding:0 24px 0 0;color:{colour}" : $"margin:0 0 16px;padding:0 0 0 24px;color:{colour}");
                    break;
                case "li":
                    Set(element, textClass, $"margin:0 0 6px;color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "blockquote":
                    var side = right ? "right" : "left";
                    Set(element, "jfb-rule", $"margin:0 0 16px;padding:0 {(right ? "16px 0 0" : "0 0 16px")};border-{side}:3px solid {colours.Rule};{Wrap}");
                    break;
                case "pre":
                    Set(element, textClass, $"margin:0 0 16px;font-family:{Mono};font-size:14px;line-height:1.5;color:{colour};white-space:pre-wrap;{Wrap}");
                    break;
                case "code":
                    Set(element, textClass, $"font-family:{Mono};font-size:14px;color:{colour};{Wrap}");
                    break;
                case "hr":
                    Set(element, "jfb-rule", $"border:0;border-top:1px solid {colours.Rule};margin:24px 0");
                    break;
                case "table":
                    element.SetAttribute("role", "table");
                    element.SetAttribute("cellspacing", "0");
                    element.SetAttribute("cellpadding", "0");
                    element.SetAttribute("border", "0");
                    element.SetAttribute("width", "100%");
                    Set(element, textClass, $"width:100%;table-layout:fixed;border-collapse:collapse;margin:0 0 16px;color:{colour}");
                    break;
                case "caption":
                    Set(element, textClass, $"text-align:{_align};font-weight:600;padding:0 0 8px;color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "th":
                    Set(element, $"{textClass} jfb-rule", $"padding:8px 12px 8px 0;border-bottom:1px solid {colours.Rule};font-weight:600;vertical-align:top;text-align:{_align};color:{colour};font-family:{Font};{Wrap}");
                    break;
                case "td":
                    Set(element, $"{textClass} jfb-rule", $"padding:8px 12px 8px 0;border-bottom:1px solid {colours.Rule};vertical-align:top;text-align:{_align};color:{colour};font-family:{Font};{Wrap}");
                    break;
            }
        }

        private static void Set(IElement element, string classes, string style)
        {
            element.SetAttribute("class", classes);
            element.SetAttribute("style", style);
        }
    }
}
