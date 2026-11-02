using System.Collections.Frozen;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Keeps what a visitor typed from becoming a link in mail to an address a visitor typed (section 4.10, ADR-0160): values are
/// escaped already, so no markup gets through, but mail clients make links of bare addresses by themselves. A zero-width space
/// after each dot, in any of the forms clients read as one, unless a space follows it, and in each <c>://</c> stops them, and a
/// reader sees the same words. A value may end in a dot and the next tag start with a name, so the end of a value counts too.
/// </summary>
internal static class VisitorLinks
{
    private const char ZeroWidthSpace = (char)0x200b;

    // What the site itself writes: these sources and these paths of the entry hold nothing a visitor typed.
    private static readonly FrozenSet<string> SiteSources = new[] { "form", "page", "site", "date", "dictionary" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Whether a tag's value can hold what a visitor typed; a site's own source may, as nothing says it does not.</summary>
    public static bool CanHoldVisitorText(string source, string path) =>
        !SiteSources.Contains(source) && (source != "entry" || path is "answers" or "table");

    /// <summary>The value with its links broken, in its text and in its markup; numbers and dates behind it stay.</summary>
    public static MergeTagValue? Broken(MergeTagValue? value) => Guarded(value, Broken);

    /// <summary>The value with this done to its text and its markup; numbers and dates behind it stay.</summary>
    public static MergeTagValue? Guarded(MergeTagValue? value, Func<string, string> guard) => value is null
        ? null
        : value with
        {
            Items = [.. value.Items.Select(item => item with { Text = guard(item.Text) })],
            Html = value.Html is { } html ? guard(html) : null,
        };

    /// <summary>The text with a zero-width space where a client would see the start of a link.</summary>
    public static string Broken(string text)
    {
        var broken = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            broken.Append(text[i]);
            if ((IsDot(text[i]) && (i + 1 == text.Length || !char.IsWhiteSpace(text[i + 1])))
                || (text[i] == ':' && text.AsSpan(i + 1).StartsWith("//")))
            {
                broken.Append(ZeroWidthSpace);
            }
        }

        return broken.ToString();
    }

    private static bool IsDot(char c) => c is '.' or (char)0x3002 or (char)0xff0e or (char)0xff61;
}
