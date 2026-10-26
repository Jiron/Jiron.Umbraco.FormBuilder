using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Content and member values as merge tags show them (ADR-0067): a property's value as text in the culture, with the
/// wire number or date behind a number or a date, a list for a value that holds several, and a content item's name in
/// the culture. Rich text shows as its text without the markup; a value of a kind it does not know, such as blocks,
/// shows as nothing rather than as the name of its type.
/// </summary>
internal static class MergeTagPropertyValues
{
    /// <summary>The elements whose end starts a new word, so that their texts do not run together.</summary>
    private static readonly FrozenSet<string> Breaks = new[]
    {
        "address", "article", "aside", "blockquote", "br", "dd", "div", "dl", "dt", "figcaption", "figure", "footer",
        "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "table",
        "td", "th", "tr", "ul",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// A property's value in a culture (an invariant property has one value for every culture); null when the element has
    /// no such property or the property has no value in the culture.
    /// </summary>
    /// <param name="element">The page, an ancestor or the member.</param>
    /// <param name="alias">The property's alias.</param>
    /// <param name="culture">The culture Umbraco reads variants in.</param>
    /// <param name="formats">The formats of the template's culture.</param>
    public static MergeTagValue? Of(IPublishedElement element, string alias, string culture, CultureFormats formats)
    {
        if (element.GetProperty(alias) is not { } property || !property.HasValue(culture))
        {
            return null;
        }

        return Of(property.GetValue(culture), culture, formats);
    }

    /// <summary>A property's converted value: one item, a list for a value that holds several, or the empty text.</summary>
    /// <param name="value">The value as Umbraco converts it.</param>
    /// <param name="culture">The culture of the words for yes and no and of content names.</param>
    /// <param name="formats">The formats of numbers and dates.</param>
    public static MergeTagValue Of(object? value, string culture, CultureFormats formats)
    {
        if (value is not string && value is IEnumerable many)
        {
            var items = new List<MergeTagItem>();
            foreach (var item in many)
            {
                if (Item(item, culture, formats) is { } found)
                {
                    items.Add(found);
                }
            }

            return MergeTagValue.ListOf(items);
        }

        return Item(value, culture, formats) is { } single ? MergeTagValue.Of(single) : MergeTagValue.Empty;
    }

    /// <summary>
    /// A content item's name in a culture: the culture's own name, the one name of an invariant item, and nothing for an
    /// item that has no name in the culture.
    /// </summary>
    /// <param name="content">The content item.</param>
    /// <param name="culture">The culture Umbraco reads variants in.</param>
    public static string Name(IPublishedContent content, string culture)
    {
        var cultures = content.Cultures;
        foreach (var (name, info) in cultures)
        {
            if (name.Length > 0 && string.Equals(name, culture, StringComparison.OrdinalIgnoreCase))
            {
                return info.Name;
            }
        }

        if (cultures.TryGetValue(string.Empty, out var invariant))
        {
            return invariant.Name;
        }

        return cultures.Count == 0 ? content.Name : string.Empty;
    }

    /// <summary>
    /// The text of rich text: its elements left out, a space where a block or a line break ends, scripts and styles left
    /// out with what they hold, the character references read, and white space run together. The result is text, which
    /// the place's context escapes like any other value, so this never has to make markup safe.
    /// </summary>
    /// <param name="html">The rich text.</param>
    public static string PlainText(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var text = new StringBuilder(html.Length);
        string? skipping = null;
        var at = 0;
        while (at < html.Length)
        {
            var c = html[at];
            if (c != '<')
            {
                if (skipping is null)
                {
                    text.Append(c);
                }

                at++;
                continue;
            }

            if (string.CompareOrdinal(html, at, "<!--", 0, 4) == 0)
            {
                var close = html.IndexOf("-->", at + 4, StringComparison.Ordinal);
                at = close < 0 ? html.Length : close + 3;
                continue;
            }

            var end = html.IndexOf('>', at + 1);
            if (end < 0)
            {
                if (skipping is null)
                {
                    text.Append(html, at, html.Length - at);
                }

                break;
            }

            var closing = at + 1 < end && html[at + 1] == '/';
            var name = ElementName(html, closing ? at + 2 : at + 1, end);
            if (skipping is not null)
            {
                skipping = closing && name == skipping ? null : skipping;
            }
            else if (!closing && name is "script" or "style")
            {
                skipping = name;
            }
            else if (Breaks.Contains(name))
            {
                text.Append(' ');
            }

            at = end + 1;
        }

        var decoded = WebUtility.HtmlDecode(text.ToString());
        var words = new StringBuilder(decoded.Length);
        foreach (var character in decoded)
        {
            if (!char.IsWhiteSpace(character))
            {
                words.Append(character);
            }
            else if (words.Length > 0 && words[^1] != ' ')
            {
                words.Append(' ');
            }
        }

        return words.ToString().TrimEnd(' ');
    }

    private static MergeTagItem? Item(object? value, string culture, CultureFormats formats) => value switch
    {
        null => null,
        string text => text.Length > 0 ? new MergeTagItem(text) : null,
        IHtmlEncodedString html => PlainText(html.ToHtmlString() ?? string.Empty) is { Length: > 0 } text ? new MergeTagItem(text) : null,
        bool yes => new MergeTagItem(yes ? FieldDisplayWords.Yes(culture) : FieldDisplayWords.No(culture)),
        int or long or short or byte or sbyte or uint or ulong or ushort => NumberItem(Convert.ToString(value, CultureInfo.InvariantCulture)!, formats),
        decimal number => NumberItem(Wire(number), formats),
        double or float => Convert.ToDouble(value, CultureInfo.InvariantCulture) is var number && double.IsFinite(number) && Math.Abs(number) < 1e28
            ? NumberItem(Wire((decimal)number), formats)
            : null,
        DateOnly date => CalendarItem(date.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture), "short", formats),
        TimeOnly time => CalendarItem(time.ToString(time.Second == 0 ? "HH':'mm" : "HH':'mm':'ss", CultureInfo.InvariantCulture), "time", formats),
        DateTimeOffset instant => CalendarItem(instant.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture), "dateTime", formats),

        // A date picker without a time gives midnight, which shows as the date alone.
        DateTime instant when instant.TimeOfDay == TimeSpan.Zero => CalendarItem(instant.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture), "short", formats),
        DateTime instant => CalendarItem(instant.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture), "dateTime", formats),
        IPublishedContent content => Name(content, culture) is { Length: > 0 } name ? new MergeTagItem(name) : null,
        Link link => !string.IsNullOrEmpty(link.Name) ? new MergeTagItem(link.Name) : !string.IsNullOrEmpty(link.Url) ? new MergeTagItem(link.Url) : null,
        _ => null,
    };

    /// <summary>A number in the wire form of ADR-0005, without trailing zeros after the point.</summary>
    private static string Wire(decimal number) => number.ToString("0.############################", CultureInfo.InvariantCulture);

    private static MergeTagItem NumberItem(string wire, CultureFormats formats) =>
        CultureFormatting.FormatNumber(wire, null, formats) is { } text ? new MergeTagItem(text, Number: wire) : new MergeTagItem(wire);

    private static MergeTagItem CalendarItem(string wire, string format, CultureFormats formats) =>
        new(CultureFormatting.FormatCalendar(wire, format, formats) ?? wire, Date: wire);

    /// <summary>An element's name in lower case: its ASCII letters and digits from <paramref name="start"/>.</summary>
    private static string ElementName(string html, int start, int end)
    {
        var length = 0;
        while (start + length < end && char.IsAsciiLetterOrDigit(html[start + length]))
        {
            length++;
        }

        return html.Substring(start, length).ToLowerInvariant();
    }
}
