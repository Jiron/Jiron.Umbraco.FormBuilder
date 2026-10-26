using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>The filters, the encoders and how a value is written where its tag stands (ADR-0064).</summary>
internal static partial class MergeTags
{
    private const char Backslash = (char)92;
    private const char Tab = (char)9;
    private const char Replacement = (char)0xFFFD;
    private const string Hex = "0123456789ABCDEF";

    /// <summary>The suffix <c>truncate</c> adds when none is given: a horizontal ellipsis.</summary>
    private static readonly string Ellipsis = new((char)0x2026, 1);

    /// <summary>The encoding a filter gave a value, so the writer does not encode it again.</summary>
    private enum ValueEncoding
    {
        None,
        Html,
        Url,
        Json,
    }

    private static readonly FrozenDictionary<string, FilterSpec> Filters = new Dictionary<string, FilterSpec>(StringComparer.Ordinal)
    {
        ["default"] = new(
            ArgumentsFit(1, 1, Anything),
            (value, filter, _) => IsEmpty(value) ? new Value([new MergeItem(filter.Arguments[0].Text)], false, ValueEncoding.None) : value),
        ["upper"] = new(None, (value, _, _) => Each(value, item => new MergeItem(TextSemantics.Upper(item.Text)))),
        ["lower"] = new(None, (value, _, _) => Each(value, item => new MergeItem(TextSemantics.Lower(item.Text)))),
        ["truncate"] = new(
            ArgumentsFit(1, 2, argument => IsWhole(argument, MaxOutput), AString),
            (value, filter, _) =>
            {
                var count = int.Parse(filter.Arguments[0].Text, NumberStyles.None, CultureInfo.InvariantCulture);
                var suffix = filter.Arguments.Count > 1 ? filter.Arguments[1].Text : Ellipsis;
                return Each(value, item => new MergeItem(Truncated(item.Text, count, suffix)));
            }),
        ["date"] = new(
            ArgumentsFit(0, 1, argument => argument.IsString && CultureFormatting.DateFormats.Contains(argument.Text)),
            (value, filter, culture) =>
            {
                var format = filter.Arguments.Count > 0 ? filter.Arguments[0].Text : "short";
                return Each(value, item => Formatted(CultureFormatting.FormatCalendar(item.Date ?? item.Text, format, culture), item));
            }),
        ["number"] = new(
            ArgumentsFit(0, 1, argument => IsWhole(argument, 20)),
            (value, filter, culture) =>
            {
                var places = Places(filter, 0);
                return Each(value, item => Formatted(CultureFormatting.FormatNumber(item.Number ?? item.Text, places, culture), item));
            }),
        ["currency"] = new(
            ArgumentsFit(1, 2, IsCurrencyCode, argument => IsWhole(argument, 20)),
            (value, filter, culture) =>
            {
                var code = filter.Arguments[0].Text;
                var places = Places(filter, 1);
                return Each(
                    value,
                    item => Formatted(CultureFormatting.FormatCurrency(item.Number ?? item.Text, code, places, culture), item));
            }),
        ["join"] = new(
            ArgumentsFit(0, 1, AString),
            (value, filter, _) =>
            {
                if (!value.IsList)
                {
                    return value;
                }

                var separator = filter.Arguments.Count > 0 ? filter.Arguments[0].Text : ", ";
                return new Value([new MergeItem(string.Join(separator, value.Items.Select(item => item.Text)))], false, ValueEncoding.None);
            }),
        ["html"] = new(None, (value, _, _) => Each(value, item => new MergeItem(HtmlText(item.Text)), ValueEncoding.Html), Encodes: true),
        ["url"] = new(None, (value, _, _) => Each(value, item => new MergeItem(Percent(item.Text, form: false)), ValueEncoding.Url), Encodes: true),
        ["json"] = new(
            None,
            (value, _, _) => new Value([new MergeItem(JsonOf(value))], false, ValueEncoding.Json),
            Encodes: true),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>A value on its way through the filters: its items, whether it is a list, and the encoding a filter gave it.</summary>
    private sealed record Value(IReadOnlyList<MergeItem> Items, bool IsList, ValueEncoding Encoded);

    /// <summary>
    /// A filter: where its arguments do not fit (the filter for a wrong count, otherwise the argument; null when they
    /// fit), what it does to a value, and whether it encodes, so it comes last.
    /// </summary>
    private sealed record FilterSpec(Func<TemplateFilter, int?> Check, Func<Value, TemplateFilter, CultureFormats, Value> Apply, bool Encodes = false);

    /// <summary>The check of a filter without arguments. A method, not a field, so the table above may use it.</summary>
    private static int? None(TemplateFilter filter) => filter.Arguments.Count == 0 ? null : filter.At;

    private static bool Anything(TemplateArgument argument) => true;

    private static bool AString(TemplateArgument argument) => argument.IsString;

    /// <summary>
    /// The check of a filter's arguments: between <paramref name="low"/> and <paramref name="high"/> of them, each
    /// fitting its rule in turn (none for any further one).
    /// </summary>
    private static Func<TemplateFilter, int?> ArgumentsFit(int low, int high, params Func<TemplateArgument, bool>[] rules) =>
        filter =>
        {
            if (filter.Arguments.Count < low || filter.Arguments.Count > high)
            {
                return filter.At;
            }

            for (var k = 0; k < filter.Arguments.Count && k < rules.Length; k++)
            {
                if (!rules[k](filter.Arguments[k]))
                {
                    return filter.Arguments[k].At;
                }
            }

            return null;
        };

    /// <summary>A whole number from 0 to <paramref name="high"/>, written as a number.</summary>
    private static bool IsWhole(TemplateArgument argument, int high)
    {
        if (argument.IsString || argument.Text.Length is 0 or > 6)
        {
            return false;
        }

        foreach (var c in argument.Text)
        {
            if (!IsDigit(c))
            {
                return false;
            }
        }

        return int.Parse(argument.Text, NumberStyles.None, CultureInfo.InvariantCulture) <= high;
    }

    /// <summary>Three capital letters, as ISO 4217 writes a currency.</summary>
    private static bool IsCurrencyCode(TemplateArgument argument) =>
        argument.IsString && argument.Text.Length == 3 && argument.Text.All(c => c is >= 'A' and <= 'Z');

    private static int? Places(TemplateFilter filter, int index) =>
        filter.Arguments.Count > index
            ? int.Parse(filter.Arguments[index].Text, NumberStyles.None, CultureInfo.InvariantCulture)
            : null;

    /// <summary>A formatted item, or the item as it was when it is not what the filter reads.</summary>
    private static MergeItem Formatted(string? text, MergeItem item) => text is null ? item : new MergeItem(text);

    private static Value Each(Value value, Func<MergeItem, MergeItem> map) => Each(value, map, value.Encoded);

    private static Value Each(Value value, Func<MergeItem, MergeItem> map, ValueEncoding encoded) =>
        new([.. value.Items.Select(map)], value.IsList, encoded);

    private static bool IsEmpty(Value value) => value.Items.All(item => item.Text.Length == 0);

    /// <summary>The first <paramref name="count"/> code points and, when it cut, the kept part without trailing spaces and the suffix.</summary>
    private static string Truncated(string text, int count, string suffix)
    {
        var i = 0;
        for (var kept = 0; i < text.Length && kept < count; kept++)
        {
            i += IsPair(text, i) ? 2 : 1;
        }

        if (i >= text.Length)
        {
            return text;
        }

        var end = i;
        while (end > 0 && TextSemantics.IsTrimmed(text[end - 1]))
        {
            end--;
        }

        return string.Concat(text.AsSpan(0, end), suffix);
    }

    /// <summary>The problems of a tag's filters that do not depend on its value, from left to right.</summary>
    private static List<TemplateProblem> FilterProblems(IReadOnlyList<TemplateFilter> filters, TemplateWriter writer)
    {
        var problems = new List<TemplateProblem>();
        var encoded = false;
        var reported = false;
        foreach (var filter in filters)
        {
            if (encoded && !reported)
            {
                problems.Add(new TemplateProblem(TemplateProblems.FilterAfterEncoding, filter.At));
                reported = true;
            }

            if (!Filters.TryGetValue(filter.Name, out var spec))
            {
                problems.Add(new TemplateProblem(TemplateProblems.UnknownFilter, filter.At));
                continue;
            }

            if ((filter.Name == "html" && writer != TemplateWriter.Html) || (filter.Name == "json" && writer == TemplateWriter.JsonString))
            {
                problems.Add(new TemplateProblem(TemplateProblems.FilterNotForContext, filter.At));
            }

            if (spec.Check(filter) is { } wrong)
            {
                problems.Add(new TemplateProblem(TemplateProblems.InvalidArgument, wrong));
            }

            encoded |= spec.Encodes;
        }

        return problems;
    }

    /// <summary>A value through a tag's filters, written as its place writes it.</summary>
    private static string Write(MergeValue source, TagPart tag, CultureFormats culture)
    {
        var value = new Value(source.Items, source.IsList, ValueEncoding.None);
        foreach (var filter in tag.Filters)
        {
            if (Filters.TryGetValue(filter.Name, out var spec))
            {
                value = spec.Apply(value, filter, culture);
            }
        }

        if (tag.Writer == TemplateWriter.JsonValue)
        {
            return value.Encoded == ValueEncoding.Json ? FirstText(value) : JsonOf(value);
        }

        // A list's separator is encoded as its items were, so `url` in an address leaves no raw space.
        var separator = value.Encoded == ValueEncoding.Url ? Percent(", ", form: false) : ", ";
        var text = value.IsList ? string.Join(separator, value.Items.Select(item => item.Text)) : FirstText(value);
        if ((value.Encoded == ValueEncoding.Html && tag.Writer == TemplateWriter.Html) || (value.Encoded == ValueEncoding.Url && tag.Writer == TemplateWriter.Url))
        {
            return text;
        }

        return tag.Writer switch
        {
            TemplateWriter.Html => EscapeHtml(text),
            TemplateWriter.Xml => EscapeXml(text),
            TemplateWriter.Header => HeaderValue(text),
            TemplateWriter.Url => Percent(text, form: false),
            TemplateWriter.Form => Percent(text, form: true),
            TemplateWriter.JsonString => JsonContent(text),
            _ => text,
        };
    }

    private static string FirstText(Value value) => value.Items.Count > 0 ? value.Items[0].Text : string.Empty;

    /// <summary>A value as a complete JSON value: an array for a list, otherwise its one item.</summary>
    private static string JsonOf(Value value) =>
        value.IsList
            ? "[" + string.Join(",", value.Items.Select(JsonItem)) + "]"
            : JsonItem(value.Items.Count > 0 ? value.Items[0] : new MergeItem(string.Empty));

    /// <summary>An item as a complete JSON value: a number for a wire number behind it, otherwise its text as a string.</summary>
    private static string JsonItem(MergeItem item) =>
        item.Number is { } number && ExactDecimal.Parse(number) is { } parsed
            ? parsed.ToString()
            : "\"" + JsonContent(item.Text) + "\"";

    private static bool IsPair(string text, int i) =>
        char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]);

    /// <summary>A character as markup writes it: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c> and <c>'</c> as references, <paramref name="apostrophe"/> being the one for <c>'</c>.</summary>
    private static void AppendMarkup(StringBuilder builder, char c, string apostrophe)
    {
        switch (c)
        {
            case '&':
                builder.Append("&amp;");
                break;
            case '<':
                builder.Append("&lt;");
                break;
            case '>':
                builder.Append("&gt;");
                break;
            case '"':
                builder.Append("&quot;");
                break;
            case (char)39:
                builder.Append(apostrophe);
                break;
            default:
                builder.Append(c);
                break;
        }
    }

    private static string EscapeHtml(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            AppendMarkup(builder, c, "&#39;");
        }

        return builder.ToString();
    }

    /// <summary>The <c>html</c> filter: escaped, with each line break (CRLF, CR or LF) as <c>&lt;br&gt;</c>.</summary>
    private static string HtmlText(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is (char)13 or (char)10)
            {
                builder.Append("<br>");
                if (c == (char)13 && i + 1 < text.Length && text[i + 1] == (char)10)
                {
                    i++;
                }
            }
            else
            {
                AppendMarkup(builder, c, "&#39;");
            }
        }

        return builder.ToString();
    }

    /// <summary>Escaped for XML, with the characters XML 1.0 cannot carry (most controls, U+FFFE, U+FFFF, lone surrogates) as U+FFFD.</summary>
    private static string EscapeXml(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsPair(text, i))
            {
                builder.Append(c).Append(text[++i]);
            }
            else if (c is (char)9 or (char)10 or (char)13 || (c is >= (char)0x20 and <= (char)0xFFFD && !char.IsSurrogate(c)))
            {
                AppendMarkup(builder, c, "&apos;");
            }
            else
            {
                builder.Append(Replacement);
            }
        }

        return builder.ToString();
    }

    /// <summary>A header's value: a CRLF, each other line break and each control character a space, then trimmed.</summary>
    private static string HeaderValue(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == (char)13 && i + 1 < text.Length && text[i + 1] == (char)10)
            {
                builder.Append(' ');
                i++;
            }
            else
            {
                builder.Append(c < (char)0x20 || c is >= (char)0x7F and <= (char)0x9F or (char)0x2028 or (char)0x2029 ? ' ' : c);
            }
        }

        var start = 0;
        var end = builder.Length;
        while (start < end && TextSemantics.IsTrimmed(builder[start]))
        {
            start++;
        }

        while (end > start && TextSemantics.IsTrimmed(builder[end - 1]))
        {
            end--;
        }

        return builder.ToString(start, end - start);
    }

    /// <summary>
    /// Percent-encoded UTF-8, lone surrogates as U+FFFD: as one URI component (all but <c>A-Z a-z 0-9 - . _ ~</c>), or
    /// for a form body (all but <c>A-Z a-z 0-9 * - . _</c>, a space as <c>+</c>).
    /// </summary>
    private static string Percent(string text, bool form)
    {
        var builder = new StringBuilder(text.Length);
        Span<byte> bytes = stackalloc byte[4];
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (IsPair(text, i))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                codePoint = Replacement;
            }

            if (IsLetter(codePoint) || IsDigit(codePoint) || codePoint is '-' or '.' or '_' || codePoint == (form ? '*' : '~'))
            {
                builder.Append((char)codePoint);
            }
            else if (form && codePoint == ' ')
            {
                builder.Append('+');
            }
            else
            {
                var length = new Rune(codePoint).EncodeToUtf8(bytes);
                foreach (var b in bytes[..length])
                {
                    builder.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 15]);
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Escaped as JSON string content, lone surrogates as U+FFFD; <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, U+2028 and U+2029 as <c>\u</c> escapes too.</summary>
    private static string JsonContent(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsPair(text, i))
            {
                builder.Append(c).Append(text[++i]);
                continue;
            }

            if (char.IsSurrogate(c))
            {
                builder.Append(Replacement);
                continue;
            }

            switch (c)
            {
                case '"':
                    builder.Append(Backslash).Append('"');
                    break;
                case Backslash:
                    builder.Append(Backslash).Append(Backslash);
                    break;
                case (char)8:
                    builder.Append(Backslash).Append('b');
                    break;
                case (char)12:
                    builder.Append(Backslash).Append('f');
                    break;
                case (char)10:
                    builder.Append(Backslash).Append('n');
                    break;
                case (char)13:
                    builder.Append(Backslash).Append('r');
                    break;
                case Tab:
                    builder.Append(Backslash).Append('t');
                    break;
                default:
                    if (c < (char)0x20 || c is '<' or '>' or '&' or (char)0x2028 or (char)0x2029)
                    {
                        builder.Append(Backslash).Append('u').Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
