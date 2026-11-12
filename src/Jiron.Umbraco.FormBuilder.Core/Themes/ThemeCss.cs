using System.Text;
using AngleSharp;
using AngleSharp.Css;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Parser;

namespace Jiron.Umbraco.FormBuilder.Core.Themes;

/// <summary>What the sanitiser took out of a theme's custom CSS, by kind, with the text it took (ADR-0215).</summary>
/// <param name="Code">
/// The kind: <c>import</c>, <c>atRule</c>, <c>nested</c>, <c>address</c>, <c>script</c>, <c>fontFace</c>, <c>outside</c> (a
/// selector of what stands beside the form) or <c>escape</c> (a declaration with an escape outside quotes).
/// </param>
/// <param name="Text">What went, shortened: the rule's name, the selector, the declaration, or the font's family.</param>
internal sealed record ThemeCssRemoval(string Code, string Text);

/// <summary>A theme's custom CSS as the website serves it, and what went.</summary>
/// <param name="Css">The rules that stay, every selector under the theme's form.</param>
/// <param name="Removed">What went, in the order of the text.</param>
internal sealed record SanitisedThemeCss(string Css, IReadOnlyList<ThemeCssRemoval> Removed);

/// <summary>
/// A library theme's custom CSS made safe for the website (ROADMAP \u00a74.10, ADR-0215): AngleSharp's CSS parser, the HTML
/// sanitiser's own, reads it, and the rules are written again, never the text as typed. Every selector goes under the
/// theme's form (<c>.jfb-form.jfb-theme-&lt;alias&gt;</c>), and a selector that starts with <c>.jfb-form</c> is that form;
/// <c>@media</c>, <c>@supports</c>, <c>@container</c> and <c>@keyframes</c> keep their rules; every other at-rule goes;
/// a declaration goes when it calls <c>expression()</c>, names <c>behavior</c> or <c>-moz-binding</c>, mentions
/// <c>javascript:</c>, holds an address that is neither a path on the site nor a <c>data:</c> image, or an escape outside
/// quotes (CSS reads <c>u\72l(</c> as <c>url(</c>); an address with an escape is none of the site's; an <c>@font-face</c>
/// goes when a source is on another host. Nested rules go, as their selectors would escape the scope, and so does a
/// selector that reaches beside the form (<c>.jfb-form ~ *</c>, <c>:root + p</c>).
/// </summary>
internal static class ThemeCss
{
    /// <summary>The longest custom CSS a theme holds.</summary>
    public const int MaxLength = 64 * 1024;

    /// <summary>The longest text a removal quotes.</summary>
    private const int Quoted = 80;

    private static readonly CssParserOptions Options = new()
    {
        IsIncludingUnknownDeclarations = true,
        IsIncludingUnknownRules = false,
        IsToleratingInvalidSelectors = false,
        IsExcludingNesting = false,
    };

    /// <summary>The CSS as the website serves it for the theme of this alias.</summary>
    /// <param name="css">The custom CSS as typed; null or blank for none.</param>
    /// <param name="alias">The theme's alias, which the scope names.</param>
    public static SanitisedThemeCss Sanitise(string? css, string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (string.IsNullOrWhiteSpace(css))
        {
            return new SanitisedThemeCss(string.Empty, []);
        }

        var context = BrowsingContext.New(AngleSharp.Configuration.Default.WithCss().WithOnly<IDeclarationFactory>(new AsWritten()));
        var sheet = new CssParser(Options, context).ParseStyleSheet(css);
        var scope = ScopeOf(alias);
        var output = new StringBuilder();
        var removed = new List<ThemeCssRemoval>();
        WriteRules(sheet.Rules, scope, output, removed, depth: 0);
        return new SanitisedThemeCss(output.ToString(), removed);
    }

    /// <summary>The selector of the theme's forms.</summary>
    public static string ScopeOf(string alias) => $".jfb-form.jfb-theme-{alias}";

    private static void WriteRules(IEnumerable<ICssRule> rules, string scope, StringBuilder output, List<ThemeCssRemoval> removed, int depth)
    {
        foreach (var rule in rules)
        {
            switch (rule)
            {
                case ICssStyleRule style:
                    WriteStyle(style, scope, output, removed, depth);
                    break;
                case ICssMediaRule media:
                    WriteGroup($"@media {media.ConditionText}", media.Rules, scope, output, removed, depth);
                    break;
                case ICssSupportsRule supports:
                    WriteGroup($"@supports {supports.ConditionText}", supports.Rules, scope, output, removed, depth);
                    break;
                case ICssContainerRule container:
                    WriteGroup($"@container {container.ConditionText}", container.Rules, scope, output, removed, depth);
                    break;
                case ICssKeyframesRule keyframes:
                    WriteKeyframes(keyframes, output, removed, depth);
                    break;
                case ICssFontFaceRule fontFace:
                    WriteFontFace(fontFace, output, removed, depth);
                    break;
                case ICssImportRule import:
                    removed.Add(new ThemeCssRemoval("import", Short(import.CssText)));
                    break;
                default:
                    if (rule.Type != CssRuleType.Comment)
                    {
                        removed.Add(new ThemeCssRemoval("atRule", Short(rule.CssText)));
                    }

                    break;
            }
        }
    }

    private static void WriteGroup(string head, ICssRuleList rules, string scope, StringBuilder output, List<ThemeCssRemoval> removed, int depth)
    {
        var inner = new StringBuilder();
        WriteRules(rules, scope, inner, removed, depth + 1);
        if (inner.Length == 0)
        {
            return;
        }

        output.Append(Indent(depth)).Append(head.Trim()).Append(" {\n").Append(inner).Append(Indent(depth)).Append("}\n");
    }

    private static void WriteStyle(ICssStyleRule style, string scope, StringBuilder output, List<ThemeCssRemoval> removed, int depth)
    {
        if (style.Rules.Length > 0)
        {
            removed.Add(new ThemeCssRemoval("nested", Short(style.SelectorText)));
        }

        // A rule whose selector the parser could not read keeps nothing: what follows it is no CSS of the theme's.
        var selectorText = style.SelectorText;
        if (string.IsNullOrWhiteSpace(selectorText) || selectorText.IndexOfAny(['<', '{', '}']) >= 0)
        {
            return;
        }

        var selectors = new List<string>();
        foreach (var selector in SplitSelectors(selectorText))
        {
            if (Scoped(selector, scope) is { } scoped)
            {
                selectors.Add(scoped);
            }
            else
            {
                removed.Add(new ThemeCssRemoval("outside", Short(selector)));
            }
        }

        var declarations = Declarations(style.Style, removed);
        if (declarations.Length == 0 || selectors.Count == 0)
        {
            return;
        }

        output.Append(Indent(depth)).Append(string.Join(", ", selectors)).Append(" {").Append(declarations).Append(" }\n");
    }

    private static void WriteKeyframes(ICssKeyframesRule keyframes, StringBuilder output, List<ThemeCssRemoval> removed, int depth)
    {
        var frames = new StringBuilder();
        foreach (var frame in keyframes.Rules.OfType<ICssKeyframeRule>())
        {
            var declarations = Declarations(frame.Style, removed);
            if (declarations.Length > 0)
            {
                frames.Append(Indent(depth + 1)).Append(frame.KeyText).Append(" {").Append(declarations).Append(" }\n");
            }
        }

        if (frames.Length > 0)
        {
            output.Append(Indent(depth)).Append("@keyframes ").Append(keyframes.Name).Append(" {\n").Append(frames).Append(Indent(depth)).Append("}\n");
        }
    }

    private static void WriteFontFace(ICssFontFaceRule fontFace, StringBuilder output, List<ThemeCssRemoval> removed, int depth)
    {
        var sources = fontFace.Source ?? string.Empty;
        // The rule is written as typed, so an escape that could spell an address (u\72l( is url() takes it, as it takes a
        // declaration; a font family's own quoted escapes stay.
        if (EscapesOutsideQuotes(fontFace.CssText))
        {
            removed.Add(new ThemeCssRemoval("escape", Short(fontFace.Family ?? fontFace.CssText)));
            return;
        }

        if (Addresses(sources).Any(address => !IsLocal(address, imageOnly: false)) || IsScript(sources))
        {
            removed.Add(new ThemeCssRemoval("fontFace", Short(fontFace.Family ?? fontFace.CssText)));
            return;
        }

        output.Append(Indent(depth)).Append(fontFace.CssText).Append('\n');
    }

    // The declarations that stay, each written again from what the parser read: " name: value; \u2026".
    private static string Declarations(ICssStyleDeclaration style, List<ThemeCssRemoval> removed)
    {
        var written = new StringBuilder();
        foreach (var property in style)
        {
            var name = property.Name;
            var value = property.Value ?? string.Empty;
            // An escape spells any name: u\72l( is url(, image-s\65t( is image-set(. A string's own escapes stay, as in
            // content: "\201C", unless the string is an address, which IsLocal refuses.
            if (name.Contains('\\', StringComparison.Ordinal) || EscapesOutsideQuotes(value))
            {
                removed.Add(new ThemeCssRemoval("escape", Short($"{name}: {value}")));
                continue;
            }

            if (name.Equals("behavior", StringComparison.OrdinalIgnoreCase)
                || name.Equals("-moz-binding", StringComparison.OrdinalIgnoreCase)
                || IsScript(value))
            {
                removed.Add(new ThemeCssRemoval("script", Short($"{name}: {value}")));
                continue;
            }

            if (Addresses(value).Any(address => !IsLocal(address, imageOnly: true)))
            {
                removed.Add(new ThemeCssRemoval("address", Short($"{name}: {value}")));
                continue;
            }

            if (value.Length == 0 || value.Contains(';', StringComparison.Ordinal) || value.Contains('}', StringComparison.Ordinal) || value.Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            written.Append(' ').Append(name).Append(": ").Append(value);
            if (property.IsImportant)
            {
                written.Append(" !important");
            }

            written.Append(';');
        }

        return written.ToString();
    }

    // Whether the value holds a backslash outside quotes.
    private static bool EscapesOutsideQuotes(string value)
    {
        char? quote = null;
        for (var at = 0; at < value.Length; at++)
        {
            var character = value[at];
            if (quote is { } inQuote)
            {
                if (character == '\\')
                {
                    at++;
                }
                else if (character == inQuote)
                {
                    quote = null;
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '\\')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScript(string value) =>
        value.Contains("expression(", StringComparison.OrdinalIgnoreCase)
        || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase)
        || value.Contains("vbscript:", StringComparison.OrdinalIgnoreCase);

    // The addresses a value names: the argument of every url(), and the strings of image-set() and image().
    private static List<string> Addresses(string value)
    {
        var found = new List<string>();
        var at = 0;
        while (at < value.Length)
        {
            var url = value.IndexOf("url(", at, StringComparison.OrdinalIgnoreCase);
            var set = value.IndexOf("image-set(", at, StringComparison.OrdinalIgnoreCase);
            var image = value.IndexOf("image(", at, StringComparison.OrdinalIgnoreCase);
            var next = new[] { url, set, image }.Where(index => index >= 0).DefaultIfEmpty(-1).Min();
            if (next < 0)
            {
                break;
            }

            var open = value.IndexOf('(', next);
            var close = CloseOf(value, open);
            var inside = value[(open + 1)..close];
            if (next == url)
            {
                found.Add(Unquoted(inside));
            }
            else
            {
                found.AddRange(Strings(inside));
            }

            at = Math.Min(value.Length, close + 1);
        }

        return found;
    }

    // Where the parenthesis that closes the one at this place is, or the end.
    private static int CloseOf(string value, int open)
    {
        var level = 0;
        char? quote = null;
        for (var at = open; at < value.Length; at++)
        {
            var character = value[at];
            if (quote is { } inQuote)
            {
                if (character == '\\')
                {
                    at++;
                }
                else if (character == inQuote)
                {
                    quote = null;
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character == '(')
            {
                level++;
            }
            else if (character == ')' && --level == 0)
            {
                return at;
            }
        }

        return value.Length;
    }

    private static IEnumerable<string> Strings(string inside)
    {
        foreach (var part in inside.Split(['"', '\''], StringSplitOptions.None).Where((_, index) => index % 2 == 1))
        {
            yield return part;
        }

        foreach (var nested in Addresses(inside))
        {
            yield return nested;
        }
    }

    private static string Unquoted(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length >= 2 && (trimmed[0] is '"' or '\'') && trimmed[^1] == trimmed[0] ? trimmed[1..^1].Trim() : trimmed;
    }

    // A path on the site ("/fonts/a.woff2", "images/x.png"), a fragment, or a data: image; never another host or scheme.
    private static bool IsLocal(string address, bool imageOnly)
    {
        var text = address.Trim();

        // An escape in an address can spell its scheme and host ("https\3a //"), so no such address is the site's.
        if (text.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (text.Length == 0 || text.StartsWith('#'))
        {
            return true;
        }

        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return !imageOnly || text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("data:font/", StringComparison.OrdinalIgnoreCase);
        }

        if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith(@"\\", StringComparison.Ordinal) || text.StartsWith("/\\", StringComparison.Ordinal))
        {
            return false;
        }

        // A scheme before any slash, question mark or hash is another place: https:, http:, ftp:, file:, javascript:.
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var stop = text.IndexOfAny(['/', '?', '#']);
        return colon < 0 || (stop >= 0 && stop < colon);
    }

    // The selectors of a list, split at the commas outside parentheses, brackets and strings.
    internal static IReadOnlyList<string> SplitSelectors(string list)
    {
        var parts = new List<string>();
        var start = 0;
        var level = 0;
        char? quote = null;
        for (var at = 0; at < list.Length; at++)
        {
            var character = list[at];
            if (quote is { } inQuote)
            {
                if (character == '\\')
                {
                    at++;
                }
                else if (character == inQuote)
                {
                    quote = null;
                }

                continue;
            }

            switch (character)
            {
                case '"' or '\'':
                    quote = character;
                    break;
                case '(' or '[':
                    level++;
                    break;
                case ')' or ']':
                    level--;
                    break;
                case ',' when level == 0:
                    parts.Add(list[start..at].Trim());
                    start = at + 1;
                    break;
            }
        }

        parts.Add(list[start..].Trim());
        return parts.Where(part => part.Length > 0).ToList();
    }

    // A selector under the theme's form: one that starts with .jfb-form is that form, any other is inside it. The page
    // around the form (:root, html, body) is the form itself, so nothing reaches outside it; null for a selector that would
    // still reach beside the form, through a + or ~ after the form's own compound selector.
    internal static string? Scoped(string selector, string scope)
    {
        var scoped = Under(selector, scope);
        return ReachesBeside(scoped[scope.Length..]) ? null : scoped;
    }

    private static string Under(string selector, string scope)
    {
        var trimmed = selector.Trim();
        foreach (var root in new[] { ":root", "html", "body" })
        {
            if (trimmed.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return scope;
            }

            // What stands beside the page stands beside the form, as the parser writes it (":root~p") or as typed: the
            // combinator comes right after the scope, and Scoped refuses it.
            if (trimmed.Length > root.Length && trimmed.StartsWith(root, StringComparison.OrdinalIgnoreCase) && trimmed[root.Length] is ' ' or '>' or '+' or '~')
            {
                trimmed = trimmed[root.Length..].TrimStart(' ', '>').Trim();
                return trimmed.Length == 0 ? scope : Under(trimmed, scope);
            }
        }

        if (trimmed.StartsWith(".jfb-form", StringComparison.Ordinal)
            && (trimmed.Length == ".jfb-form".Length || !IsNameCharacter(trimmed[".jfb-form".Length])))
        {
            return scope + trimmed[".jfb-form".Length..];
        }

        return $"{scope} {trimmed}";
    }

    // Whether what follows the scope, in the same compound selector or after it, is a + or ~: a sibling of the form, which
    // is the page's, not the form's.
    private static bool ReachesBeside(string rest)
    {
        var at = 0;
        var depth = 0;
        char? quote = null;
        for (; at < rest.Length; at++)
        {
            var character = rest[at];
            if (quote is { } inQuote)
            {
                if (character == '\\')
                {
                    at++;
                }
                else if (character == inQuote)
                {
                    quote = null;
                }
            }
            else if (character == '\\')
            {
                at++;
            }
            else if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (character is '(' or '[')
            {
                depth++;
            }
            else if (character is ')' or ']')
            {
                depth--;
            }
            else if (depth == 0 && (char.IsWhiteSpace(character) || character is '>' or '+' or '~'))
            {
                break;
            }
        }

        while (at < rest.Length && char.IsWhiteSpace(rest[at]))
        {
            at++;
        }

        return at < rest.Length && rest[at] is '+' or '~';
    }

    private static bool IsNameCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    private static string Indent(int depth) => new(' ', depth * 2);

    // Every property read as one the parser does not know, as it reads a custom property: its value as written, never
    // turned into longhands or another notation, which would change what the author meant ("gap" read as "column-gap").
    private sealed class AsWritten : DefaultDeclarationFactory, IDeclarationFactory
    {
        DeclarationInfo IDeclarationFactory.Create(string propertyName) => CreateDefault(propertyName);
    }

    private static string Short(string? text)
    {
        var flat = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= Quoted ? flat : string.Concat(flat.AsSpan(0, Quoted - 1), "\u2026");
    }
}
