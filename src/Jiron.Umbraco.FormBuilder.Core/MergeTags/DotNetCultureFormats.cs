using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// A culture's formats as the server writes them from .NET's culture data (ADR-0064, ADR-0065), for both runtimes to
/// format with: the date patterns in ADR-0064's subset with the date and time separators written in, over the Gregorian
/// calendar that wire dates use; the names; and the number and currency patterns. A date pattern with a part outside the
/// subset (fractions of a second, an era, an offset) takes the invariant culture's pattern instead.
/// </summary>
internal static class DotNetCultureFormats
{
    private const string SubsetLetters = "dMyhHmst";
    private const string UnsupportedLetters = "fFgKz";

    /// <summary>.NET's negative number patterns, by <see cref="NumberFormatInfo.NumberNegativePattern"/>.</summary>
    private static readonly ImmutableArray<string> NegativeNumbers = ["(n)", "-n", "- n", "n-", "n -"];

    /// <summary>.NET's positive currency patterns, by <see cref="NumberFormatInfo.CurrencyPositivePattern"/>.</summary>
    private static readonly ImmutableArray<string> PositiveAmounts = ["$n", "n$", "$ n", "n $"];

    /// <summary>.NET's negative currency patterns, by <see cref="NumberFormatInfo.CurrencyNegativePattern"/>.</summary>
    private static readonly ImmutableArray<string> NegativeAmounts =
    [
        "($n)", "-$n", "$-n", "$n-", "(n$)", "-n$", "n-$", "n$-", "-n $", "-$ n", "n $-", "$ n-", "$ -n", "n- $", "($ n)", "(n $)", "$- n",
    ];

    /// <summary>The culture's formats.</summary>
    public static CultureFormats From(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var dates = Gregorian(culture);
        var invariant = CultureInfo.InvariantCulture.DateTimeFormat;
        var numbers = culture.NumberFormat;
        var longTokens = Tokens(dates.LongDatePattern, dates) ?? Tokens(invariant.LongDatePattern, invariant)!;
        var patterns = new DatePatterns(
            Short: Subset(dates.ShortDatePattern, dates) ?? Subset(invariant.ShortDatePattern, invariant)!,
            Medium: Write(Medium(longTokens)),
            Long: Write(longTokens),
            Time: Subset(dates.ShortTimePattern, dates) ?? Subset(invariant.ShortTimePattern, invariant)!,
            DateTime: Subset(dates.ShortDatePattern + " " + dates.ShortTimePattern, dates)
                ?? Subset(invariant.ShortDatePattern + " " + invariant.ShortTimePattern, invariant)!);
        return new CultureFormats(
            patterns,
            Months: [.. dates.MonthNames.Take(12)],
            MonthsShort: [.. dates.AbbreviatedMonthNames.Take(12)],
            MonthsGenitive: [.. dates.MonthGenitiveNames.Take(12)],
            MonthsShortGenitive: [.. dates.AbbreviatedMonthGenitiveNames.Take(12)],
            Days: [.. dates.DayNames],
            DaysShort: [.. dates.AbbreviatedDayNames],
            Am: dates.AMDesignator,
            Pm: dates.PMDesignator,
            Minus: numbers.NegativeSign,
            Number: new NumberFormat(
                numbers.NumberDecimalSeparator,
                numbers.NumberGroupSeparator,
                [.. numbers.NumberGroupSizes],
                Pick(NegativeNumbers, numbers.NumberNegativePattern, "-n")),
            Currency: new CurrencyFormat(
                CurrencyCode(culture),
                numbers.CurrencySymbol,
                numbers.CurrencyDecimalSeparator,
                numbers.CurrencyGroupSeparator,
                [.. numbers.CurrencyGroupSizes],
                Pick(PositiveAmounts, numbers.CurrencyPositivePattern, "$n"),
                Pick(NegativeAmounts, numbers.CurrencyNegativePattern, "-$n")));
    }

    /// <summary>
    /// The culture's date formats over the Gregorian calendar, which wire dates use, whatever its default calendar is:
    /// its own Gregorian calendar when it has one, otherwise the invariant culture's formats.
    /// </summary>
    public static DateTimeFormatInfo Gregorian(CultureInfo culture)
    {
        if (culture.DateTimeFormat.Calendar is GregorianCalendar)
        {
            return culture.DateTimeFormat;
        }

        var gregorian = culture.OptionalCalendars.OfType<GregorianCalendar>().OrderBy(calendar => calendar.CalendarType == GregorianCalendarTypes.Localized ? 0 : 1).FirstOrDefault();
        if (gregorian is null)
        {
            return CultureInfo.InvariantCulture.DateTimeFormat;
        }

        var dates = (DateTimeFormatInfo)culture.DateTimeFormat.Clone();
        dates.Calendar = gregorian;
        return dates;
    }

    /// <summary>A .NET pattern in the subset, or null when it has a part outside it.</summary>
    private static string? Subset(string pattern, DateTimeFormatInfo dates) => Tokens(pattern, dates) is { } tokens ? Write(tokens) : null;

    /// <summary>
    /// The medium pattern, which .NET does not have: the long pattern without the day's name and the separators that tie
    /// it to the rest, and with the month's short name, so "dddd, d. MMMM yyyy" becomes "d. MMM yyyy".
    /// </summary>
    private static List<Token> Medium(List<Token> tokens)
    {
        var medium = new List<Token>(tokens);
        for (var i = 0; i < medium.Count; i++)
        {
            if (medium[i] is not { Letter: 'd', Run: >= 3 })
            {
                continue;
            }

            medium.RemoveAt(i);
            var hasLater = medium.Skip(i).Any(token => token.Letter is not null);
            if (hasLater)
            {
                while (i < medium.Count && medium[i].IsSeparator)
                {
                    medium.RemoveAt(i);
                }
            }
            else
            {
                while (i > 0 && medium[i - 1].IsSeparator)
                {
                    medium.RemoveAt(--i);
                }
            }

            i--;
        }

        return [.. medium.Select(token => token is { Letter: 'M', Run: >= 4 } ? token with { Run = 3 } : token)];
    }

    /// <summary>
    /// A .NET custom pattern as tokens: runs of the subset's letters, and literal text, with the date separator for
    /// <c>/</c> and the time separator for <c>:</c>; null when the pattern has a part outside the subset.
    /// </summary>
    private static List<Token>? Tokens(string pattern, DateTimeFormatInfo dates)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            if (c is '\'' or '"')
            {
                var text = new StringBuilder();
                var j = i + 1;
                while (j < pattern.Length && pattern[j] != c)
                {
                    if (pattern[j] == '\\' && j + 1 < pattern.Length)
                    {
                        j++;
                    }

                    text.Append(pattern[j]);
                    j++;
                }

                tokens.Add(new Token(null, 0, text.ToString(), Quoted: true));
                i = j + 1;
            }
            else if (c == '\\')
            {
                tokens.Add(new Token(null, 0, i + 1 < pattern.Length ? pattern.Substring(i + 1, 1) : string.Empty, Quoted: true));
                i += 2;
            }
            else if (c == '%' && i + 1 < pattern.Length && SubsetLetters.Contains(pattern[i + 1], StringComparison.Ordinal))
            {
                // A single-letter token marked as custom.
                i++;
            }
            else if (SubsetLetters.Contains(c, StringComparison.Ordinal))
            {
                var j = i;
                while (j < pattern.Length && pattern[j] == c)
                {
                    j++;
                }

                tokens.Add(new Token(c, j - i, string.Empty, Quoted: false));
                i = j;
            }
            else if (UnsupportedLetters.Contains(c, StringComparison.Ordinal))
            {
                return null;
            }
            else
            {
                var text = c switch
                {
                    '/' => dates.DateSeparator,
                    ':' => dates.TimeSeparator,
                    _ => c.ToString(),
                };
                tokens.Add(new Token(null, 0, text, Quoted: c is '/' or ':'));
                i++;
            }
        }

        return tokens;
    }

    /// <summary>
    /// Tokens as a pattern of the subset, each literal character that the subset or .NET would read escaped, so that the
    /// pattern prints the same in .NET as in both runtimes.
    /// </summary>
    private static string Write(List<Token> tokens)
    {
        var pattern = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.Letter is { } letter)
            {
                pattern.Append(letter, token.Run);
                continue;
            }

            foreach (var c in token.Text)
            {
                if (SubsetLetters.Contains(c, StringComparison.Ordinal) || UnsupportedLetters.Contains(c, StringComparison.Ordinal) || c is '\'' or '"' or '\\' or '/' or ':' or '%')
                {
                    pattern.Append('\\');
                }

                pattern.Append(c);
            }
        }

        return pattern.ToString();
    }

    private static string Pick(ImmutableArray<string> patterns, int index, string fallback) => index >= 0 && index < patterns.Length ? patterns[index] : fallback;

    /// <summary>The ISO 4217 code of the culture's own currency, or empty for a culture without a region.</summary>
    private static string CurrencyCode(CultureInfo culture)
    {
        if (culture.IsNeutralCulture || culture.Name.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return new RegionInfo(culture.Name).ISOCurrencySymbol;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>A token of a date pattern: a letter's run, or literal text, quoted or as written.</summary>
    private sealed record Token(char? Letter, int Run, string Text, bool Quoted)
    {
        /// <summary>Whether the token is unquoted literal text without letters, such as ", " or ". ".</summary>
        public bool IsSeparator => Letter is null && !Quoted && Text.All(c => !char.IsLetter(c));
    }
}
