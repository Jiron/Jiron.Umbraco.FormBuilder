using System.Collections.Frozen;
using System.Globalization;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>The patterns of the named date formats, in the subset of .NET's custom date patterns ADR-0064 lists.</summary>
internal sealed record DatePatterns(string Short, string Medium, string Long, string Time, string DateTime);

/// <summary>How a culture writes a number: <see cref="Negative"/> holds <c>n</c> for the digits and <c>-</c> for the minus sign.</summary>
/// <param name="Decimal">The decimal separator.</param>
/// <param name="Group">The group separator.</param>
/// <param name="Sizes">From the point leftwards, the last size repeating; a 0 ends the grouping (as .NET reads them).</param>
/// <param name="Negative">The pattern of a negative number.</param>
internal sealed record NumberFormat(string Decimal, string Group, IReadOnlyList<int> Sizes, string Negative);

/// <summary>How a culture writes an amount: its own currency, and patterns with <c>n</c>, <c>-</c> and <c>$</c> for the symbol.</summary>
internal sealed record CurrencyFormat(
    string Code,
    string Symbol,
    string Decimal,
    string Group,
    IReadOnlyList<int> Sizes,
    string Positive,
    string Negative);

/// <summary>A culture's formats as the server writes them (ADR-0064). Names are listed January and Sunday first.</summary>
internal sealed record CultureFormats(
    DatePatterns Dates,
    IReadOnlyList<string> Months,
    IReadOnlyList<string> MonthsShort,
    IReadOnlyList<string> MonthsGenitive,
    IReadOnlyList<string> MonthsShortGenitive,
    IReadOnlyList<string> Days,
    IReadOnlyList<string> DaysShort,
    string Am,
    string Pm,
    string Minus,
    NumberFormat Number,
    CurrencyFormat Currency);

/// <summary>
/// How both runtimes print dates, numbers and amounts with a culture's formats (ADR-0064), the same in TypeScript
/// (<c>packages/logic/src/culture.ts</c>). Neither runtime uses its own culture data, so the browser prints what the server
/// prints.
/// </summary>
internal static class CultureFormatting
{
    /// <summary>The formats of the <c>date</c> filter.</summary>
    public static readonly FrozenSet<string> DateFormats =
        new[] { "short", "medium", "long", "time", "dateTime", "iso" }.ToFrozenSet(StringComparer.Ordinal);

    private const string PatternLetters = "dMyhHmst";
    private const char NoBreakSpace = (char)0xA0;

    /// <summary>The day a time of day alone is formatted on, as TypeScript's day 0.</summary>
    private static readonly int UnixEpochDay = new DateOnly(1970, 1, 1).DayNumber;

    /// <summary>The places of a currency's minor unit where ISO 4217 gives other than 2.</summary>
    private static readonly FrozenDictionary<string, int> MinorUnitsByCode = new[]
        {
            "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "UYI", "VND", "VUV", "XAF", "XOF", "XPF",
        }
        .Select(code => KeyValuePair.Create(code, 0))
        .Concat(new[] { "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND" }.Select(code => KeyValuePair.Create(code, 3)))
        .Concat(new[] { "CLF", "UYW" }.Select(code => KeyValuePair.Create(code, 4)))
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The places of a currency's minor unit: 2 unless ISO 4217 gives others.</summary>
    public static int MinorUnits(string code) => MinorUnitsByCode.GetValueOrDefault(code, 2);

    /// <summary>
    /// A date, a time or a date-time in the wire form, in one of the <c>date</c> filter's formats; null when the text is
    /// none of them, or lacks what the format needs (a time for <c>time</c>, a date for the date formats, both for
    /// <c>dateTime</c>).
    /// </summary>
    public static string? FormatCalendar(string text, string format, CultureFormats culture)
    {
        if (!TryReadCalendar(text, out var day, out var seconds))
        {
            return null;
        }

        if (format == "iso")
        {
            if (day is not { } date)
            {
                return seconds is { } time ? IsoTime(time) : null;
            }

            return seconds is { } at ? CalendarValues.FormatDate(date) + "T" + IsoTime(at) : CalendarValues.FormatDate(date);
        }

        var lacks = format == "time" ? seconds is null : day is null || (format == "dateTime" && seconds is null);
        if (lacks)
        {
            return null;
        }

        var pattern = format switch
        {
            "short" => culture.Dates.Short,
            "medium" => culture.Dates.Medium,
            "long" => culture.Dates.Long,
            "time" => culture.Dates.Time,
            _ => culture.Dates.DateTime,
        };
        return FormatPattern(pattern, day ?? UnixEpochDay, seconds ?? 0, culture);
    }

    /// <summary>
    /// A number in the wire form as the culture writes it, rounded half away from zero and padded to
    /// <paramref name="places"/> when given, with its own decimals otherwise; null when the text is not a number the
    /// filters read.
    /// </summary>
    public static string? FormatNumber(string text, int? places, CultureFormats culture)
    {
        if (ReadAmount(text, places) is not { } value)
        {
            return null;
        }

        var format = culture.Number;
        var digits = DigitsOf(value, places, format.Decimal, format.Group, format.Sizes);
        return value.N.Sign < 0 ? ApplyPattern(format.Negative, digits, culture.Minus, null, spaced: false) : digits;
    }

    /// <summary>
    /// An amount in the wire form in a currency, in the culture's currency pattern: its own symbol for its own currency,
    /// the code for any other; rounded to <paramref name="places"/>, or the currency's minor unit. Null when the text is
    /// not a number the filters read.
    /// </summary>
    public static string? FormatCurrency(string text, string code, int? places, CultureFormats culture)
    {
        var scale = places ?? MinorUnits(code);
        if (ReadAmount(text, scale) is not { } value)
        {
            return null;
        }

        var format = culture.Currency;
        var own = string.Equals(code, format.Code, StringComparison.Ordinal);
        var digits = DigitsOf(value, scale, format.Decimal, format.Group, format.Sizes);
        var pattern = value.N.Sign < 0 ? format.Negative : format.Positive;
        return ApplyPattern(pattern, digits, culture.Minus, own ? format.Symbol : code, spaced: !own);
    }

    private static string Pad(int value, int length) => value.ToString(CultureInfo.InvariantCulture).PadLeft(length, '0');

    /// <summary>A name from a list the server wrote, or the number when the list is too short.</summary>
    private static string Name(IReadOnlyList<string> names, int index) =>
        index < names.Count ? names[index] : (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>A date, a time or both, read from their wire forms (ADR-0005).</summary>
    private static bool TryReadCalendar(string text, out int? day, out int? seconds)
    {
        day = null;
        seconds = null;
        if (text.Length > 10 && text[10] == 'T')
        {
            day = CalendarValues.ParseDate(text[..10]);
            seconds = CalendarValues.ParseTime(text[11..]);
            return day is not null && seconds is not null;
        }

        day = CalendarValues.ParseDate(text);
        if (day is not null)
        {
            return true;
        }

        seconds = CalendarValues.ParseTime(text);
        return seconds is not null;
    }

    /// <summary>The time of day as the wire writes it, with seconds.</summary>
    private static string IsoTime(int seconds) => Pad(seconds / 3600, 2) + ":" + Pad(seconds / 60 % 60, 2) + ":" + Pad(seconds % 60, 2);

    /// <summary>
    /// Walks a pattern: quoted text in <c>'</c> or <c>"</c> and a character after <c>\</c> are literal, and a run of one
    /// pattern letter is a token. <paramref name="visit"/> gets each token's letter and length, or no letter and the
    /// literal text.
    /// </summary>
    private static void Walk(string pattern, Action<char?, int, string> visit)
    {
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c is '\'' or '"')
            {
                var literal = new StringBuilder();
                var j = i + 1;
                while (j < pattern.Length && pattern[j] != c)
                {
                    if (pattern[j] == '\\' && j + 1 < pattern.Length)
                    {
                        j++;
                    }

                    literal.Append(pattern[j]);
                    j++;
                }

                visit(null, 0, literal.ToString());
                i = j + 1;
            }
            else if (c == '\\')
            {
                visit(null, 0, i + 1 < pattern.Length ? pattern.Substring(i + 1, 1) : string.Empty);
                i += 2;
            }
            else if (PatternLetters.Contains(c, StringComparison.Ordinal))
            {
                var j = i;
                while (j < pattern.Length && pattern[j] == c)
                {
                    j++;
                }

                visit(c, j - i, string.Empty);
                i = j;
            }
            else
            {
                visit(null, 0, pattern.Substring(i, 1));
                i++;
            }
        }
    }

    /// <summary>Whether a pattern has the day of the month (<c>d</c> or <c>dd</c>), which makes month names genitive.</summary>
    private static bool HasDayOfMonth(string pattern)
    {
        var found = false;
        Walk(pattern, (letter, run, _) => found |= letter == 'd' && run <= 2);
        return found;
    }

    /// <summary>A day number and a time of day in a pattern of ADR-0064's subset.</summary>
    private static string FormatPattern(string pattern, int day, int seconds, CultureFormats culture)
    {
        var date = DateOnly.FromDayNumber(day);
        var (y, m, d) = (date.Year, date.Month, date.Day);
        var weekday = (int)date.DayOfWeek;
        var hour = seconds / 3600;
        var minute = seconds / 60 % 60;
        var second = seconds % 60;
        var genitive = HasDayOfMonth(pattern);
        var output = new StringBuilder();
        Walk(pattern, (letter, run, literal) =>
        {
            switch (letter)
            {
                case null:
                    output.Append(literal);
                    break;
                case 'd':
                    output.Append(run switch
                    {
                        1 => d.ToString(CultureInfo.InvariantCulture),
                        2 => Pad(d, 2),
                        _ => Name(run == 3 ? culture.DaysShort : culture.Days, weekday),
                    });
                    break;
                case 'M':
                    output.Append(run switch
                    {
                        <= 2 => Pad(m, run),
                        3 => Name(genitive ? culture.MonthsShortGenitive : culture.MonthsShort, m - 1),
                        _ => Name(genitive ? culture.MonthsGenitive : culture.Months, m - 1),
                    });
                    break;
                case 'y':
                    output.Append(run switch
                    {
                        1 => (y % 100).ToString(CultureInfo.InvariantCulture),
                        2 => Pad(y % 100, 2),
                        _ => Pad(y, run),
                    });
                    break;
                case 'h':
                    output.Append(Pad(hour % 12 == 0 ? 12 : hour % 12, Math.Min(run, 2)));
                    break;
                case 'H':
                    output.Append(Pad(hour, Math.Min(run, 2)));
                    break;
                case 'm':
                    output.Append(Pad(minute, Math.Min(run, 2)));
                    break;
                case 's':
                    output.Append(Pad(second, Math.Min(run, 2)));
                    break;
                default:
                    {
                        // 't': the designator, or its first character.
                        var designator = hour < 12 ? culture.Am : culture.Pm;
                        output.Append(run == 1 ? FirstCharacter(designator) : designator);
                        break;
                    }
            }
        });
        return output.ToString();
    }

    /// <summary>The first code point of a text, a surrogate pair whole; empty for the empty text.</summary>
    private static string FirstCharacter(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return text[..(char.IsHighSurrogate(text[0]) && text.Length > 1 && char.IsLowSurrogate(text[1]) ? 2 : 1)];
    }

    /// <summary>The integer digits with a separator between the groups, from the right, as .NET reads group sizes.</summary>
    private static string Grouped(string digits, string separator, IReadOnlyList<int> sizes)
    {
        var groups = new List<string>();
        var end = digits.Length;
        var k = 0;
        var size = sizes.Count > 0 ? sizes[0] : 0;
        while (size > 0 && end > size)
        {
            groups.Add(digits[(end - size)..end]);
            end -= size;
            if (k < sizes.Count - 1)
            {
                k++;
            }

            size = sizes[k];
        }

        groups.Add(digits[..end]);
        groups.Reverse();
        return string.Join(separator, groups);
    }

    /// <summary>A number pattern's text: <c>n</c> the digits, <c>-</c> the minus sign, <c>$</c> the symbol (with a no-break space beside it).</summary>
    private static string ApplyPattern(string pattern, string digits, string minus, string? symbol, bool spaced)
    {
        var output = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            var next = i + 1 < pattern.Length ? pattern[i + 1] : (char)0;
            if (c == 'n')
            {
                output.Append(digits);
                if (spaced && next == '$')
                {
                    output.Append(NoBreakSpace);
                }
            }
            else if (c == '-')
            {
                output.Append(minus);
            }
            else if (c == '$' && symbol is not null)
            {
                output.Append(symbol);
                if (spaced && next == 'n')
                {
                    output.Append(NoBreakSpace);
                }
            }
            else
            {
                output.Append(c);
            }
        }

        return output.ToString();
    }

    /// <summary>A number the filters read: the wire form within 60 digits, rounded to <paramref name="places"/> when given; null otherwise.</summary>
    private static ExactDecimal? ReadAmount(string text, int? places)
    {
        try
        {
            var value = ExactDecimal.ReadNumber(text);
            return value is { } read && places is { } scale ? ExactDecimal.Round(read, scale) : value;
        }
        catch (LogicException)
        {
            return null;
        }
    }

    /// <summary>The digits of a number with a culture's separators and grouping, padded to <paramref name="places"/> when given.</summary>
    private static string DigitsOf(ExactDecimal value, int? places, string decimalSeparator, string group, IReadOnlyList<int> sizes)
    {
        var text = (value.N.Sign < 0 ? ExactDecimal.Negate(value) : value).ToString();
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integer = point < 0 ? text : text[..point];
        var fraction = point < 0 ? string.Empty : text[(point + 1)..];
        if (places is { } scale)
        {
            fraction = fraction.PadRight(scale, '0');
        }

        return Grouped(integer, group, sizes) + (fraction.Length > 0 ? decimalSeparator + fraction : string.Empty);
    }
}
