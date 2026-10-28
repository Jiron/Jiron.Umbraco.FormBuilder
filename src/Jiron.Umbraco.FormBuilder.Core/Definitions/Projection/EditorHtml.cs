using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using Ganss.Xss;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

/// <summary>
/// The one sanitiser of editors' rich text (section 4.10, ADR-0084): every <see cref="LocalizedHtml"/> passes it on its
/// way to a visitor, through an established allowlist sanitiser. It keeps text formatting, lists, headings from
/// <c>h2</c> down, quotes, code and links; no style, class, data or event attribute, image, frame, form control or
/// script survives, and a tag it does not keep goes with its content. Links lead to <c>http</c>, <c>https</c>,
/// <c>mailto</c> and <c>tel</c> addresses or to paths on the site; a link may open in a new tab, which then gets
/// <c>rel="noopener noreferrer"</c>. Configured once, it may sanitise on many threads at once.
/// </summary>
internal sealed class EditorHtml
{
    /// <summary>The tags it keeps.</summary>
    public static readonly IReadOnlyList<string> Tags =
    [
        "a", "abbr", "b", "blockquote", "br", "code", "em", "h2", "h3", "h4", "h5", "h6", "hr", "i", "li", "mark", "ol", "p",
        "pre", "s", "small", "span", "strong", "sub", "sup", "u", "ul",
    ];

    /// <summary>The attributes it keeps, on any kept tag.</summary>
    public static readonly IReadOnlyList<string> Attributes = ["dir", "href", "lang", "target", "title"];

    /// <summary>The schemes a link may have; a link without one is a path on the site.</summary>
    public static readonly IReadOnlyList<string> Schemes = ["http", "https", "mailto", "tel"];

    private const string NewTab = "_blank";

    private readonly HtmlSanitizer _sanitizer;

    /// <summary>The sanitiser with Jiron Form Builder's allowlist.</summary>
    public EditorHtml()
    {
        _sanitizer = new HtmlSanitizer(new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(Tags, StringComparer.OrdinalIgnoreCase),
            AllowedAttributes = new HashSet<string>(Attributes, StringComparer.OrdinalIgnoreCase),
            AllowedSchemes = new HashSet<string>(Schemes, StringComparer.OrdinalIgnoreCase),
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
        _sanitizer.PostProcessNode += (_, e) => Link(e.Node);
    }

    /// <summary>The rich text as it may reach a visitor.</summary>
    /// <param name="html">The text as the editor wrote it.</param>
    public string Sanitise(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        return html.Length == 0 ? html : _sanitizer.Sanitize(html);
    }

    // A link opens where it is, or in a new tab without a hold on the page that opened it; any other target goes.
    private static void Link(INode node)
    {
        if (node is not IElement { LocalName: "a" } link || link.GetAttribute("target") is not { } target)
        {
            return;
        }

        if (string.Equals(target, NewTab, StringComparison.OrdinalIgnoreCase))
        {
            link.SetAttribute("target", NewTab);
            link.SetAttribute("rel", "noopener noreferrer");
        }
        else
        {
            link.RemoveAttribute("target");
        }
    }
}
