using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// The culture formats the server writes from .NET's data (ADR-0065): for every culture .NET knows, the dates, times,
/// numbers and amounts both runtimes print with them are what .NET prints with the culture's own patterns, and a pattern
/// with a part outside ADR-0064's subset takes the invariant culture's, for exactly the cultures whose data has one.
/// </summary>
public sealed class DotNetCultureFormatsTests
{
    // Instants that tell the parts apart: a one-digit day, month and hour; two digits of each in the afternoon; midnight
    // and noon on the twelve-hour clock; and a year that needs its leading zero.
    private static readonly DateTime[] Instants =
    [
        new(2026, 3, 7, 9, 5, 3, DateTimeKind.Unspecified),
        new(2026, 11, 23, 17, 45, 59, DateTimeKind.Unspecified),
        new(2005, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
        new(1999, 12, 31, 12, 30, 0, DateTimeKind.Unspecified),
        new(987, 6, 15, 23, 59, 1, DateTimeKind.Unspecified),
    ];

    // Amounts with groups, a sign, a half that rounds away from zero, and more digits than a double holds.
    private static readonly decimal[] Amounts = [1234567.891m, -1234567.891m, 0.5m, 2.675m, -42.125m, 999m, 12345678901234.5m];

    private static readonly CultureFormats Invariant = DotNetCultureFormats.From(CultureInfo.InvariantCulture);

    [Test]
    public void EveryCulture_PrintsDatesAndTimes_AsDotNetPrintsThem()
    {
        var mismatches = new List<string>();
        var outside = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var formats = DotNetCultureFormats.From(culture);
            var dates = DotNetCultureFormats.Gregorian(culture);

            // Each named format with .NET's standard format of the same pattern and the invariant culture's pattern,
            // which a pattern outside the subset takes.
            (string Name, string Written, string? Standard, string? DotNetPattern, string Fallback)[] patterns =
            [
                ("short", formats.Dates.Short, "d", dates.ShortDatePattern, Invariant.Dates.Short),
                ("long", formats.Dates.Long, "D", dates.LongDatePattern, Invariant.Dates.Long),
                ("time", formats.Dates.Time, "t", dates.ShortTimePattern, Invariant.Dates.Time),
                ("dateTime", formats.Dates.DateTime, "g", dates.ShortDatePattern + " " + dates.ShortTimePattern, Invariant.Dates.DateTime),
                ("medium", formats.Dates.Medium, null, null, Invariant.Dates.Medium),
            ];

            foreach (var (name, written, standard, dotNetPattern, fallback) in patterns)
            {
                var isOutside = dotNetPattern is not null && OutsideSubset(dotNetPattern);
                if (isOutside)
                {
                    if (!outside.TryGetValue(Named(culture), out var names))
                    {
                        outside[Named(culture)] = names = [];
                    }

                    names.Add(name);
                    if (written != fallback)
                    {
                        mismatches.Add($"{Named(culture)} {name}: '{written}' is outside the subset in .NET ('{dotNetPattern}') but is not the invariant culture's '{fallback}'");
                    }
                }

                foreach (var instant in Instants)
                {
                    var printed = CultureFormatting.FormatCalendar(Wire(instant), name, formats);

                    // The written pattern prints in .NET what it prints in both runtimes, and, inside the subset, what the
                    // culture's own standard format prints.
                    var dotNet = instant.ToString(written, dates);
                    var own = standard is not null && !isOutside ? instant.ToString(standard, dates) : dotNet;
                    if (printed != dotNet || printed != own)
                    {
                        mismatches.Add($"{Named(culture)} {name} {Wire(instant)}: '{printed}' with '{written}', .NET '{own}'");
                    }
                }
            }
        }

        var listed = string.Join(", ", outside.Select(culture => $"{culture.Key} ({string.Join(", ", culture.Value)})"));
        TestContext.Out.WriteLine($"Cultures with a date pattern outside the subset ({outside.Count}): {listed}");
        Assert.That(mismatches, Is.Empty, $"{mismatches.Count} mismatches; cultures outside the subset: {listed}");
    }

    [Test]
    public void EveryCulture_PrintsNumbersAndItsOwnAmounts_AsDotNetPrintsThem()
    {
        var mismatches = new List<string>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var formats = DotNetCultureFormats.From(culture);
            foreach (var amount in Amounts)
            {
                var wire = amount.ToString(CultureInfo.InvariantCulture);
                var number = CultureFormatting.FormatNumber(wire, 2, formats);
                var money = CultureFormatting.FormatCurrency(wire, formats.Currency.Code, 2, formats);
                if (number != amount.ToString("N2", culture))
                {
                    mismatches.Add($"{Named(culture)} N2 {wire}: '{number}', .NET '{amount.ToString("N2", culture)}'");
                }

                if (money != amount.ToString("C2", culture))
                {
                    mismatches.Add($"{Named(culture)} C2 {wire}: '{money}', .NET '{amount.ToString("C2", culture)}'");
                }
            }
        }

        Assert.That(mismatches, Is.Empty, $"{mismatches.Count} mismatches");
    }

    [Test]
    public void EveryCulture_HasAMediumDate_WithoutTheDaysName_AndWithTheMonthsShortName()
    {
        var mismatches = new List<string>();
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures))
        {
            var medium = DotNetCultureFormats.From(culture).Dates.Medium;
            var runs = Runs(medium);
            if (runs.Any(run => run is { Letter: 'd', Length: >= 3 } or { Letter: 'M', Length: >= 4 }))
            {
                mismatches.Add($"{Named(culture)}: '{medium}'");
            }
        }

        Assert.That(mismatches, Is.Empty);
    }

    [TestCase("de-CH", "d. MMM yyyy")]
    [TestCase("de-DE", "d. MMM yyyy")]
    [TestCase("en-US", "MMM d, yyyy")]
    [TestCase("en-GB", "d MMM yyyy")]
    [TestCase("fr-FR", "d MMM yyyy")]
    public void TheMediumDate_IsTheLongDate_WithoutTheDaysNameAndItsSeparators(string culture, string medium)
    {
        Assert.That(DotNetCultureFormats.From(CultureInfo.GetCultureInfo(culture)).Dates.Medium, Is.EqualTo(medium));
    }

    [Test]
    public void TheSeparators_AreWrittenIn_AndEscapedForDotNet()
    {
        var formats = DotNetCultureFormats.From(CultureInfo.GetCultureInfo("en-US"));

        Assert.Multiple(() =>
        {
            Assert.That(formats.Dates.Short, Is.EqualTo(@"M\/d\/yyyy"));
            Assert.That(formats.Dates.Time, Is.EqualTo(@"h\:mm tt"));
            Assert.That(CultureFormatting.FormatCalendar("2026-03-07T09:05:03", "short", formats), Is.EqualTo("3/7/2026"));
        });
    }

    [Test]
    public void ACultureWithAnotherCalendar_FormatsOnTheGregorianCalendar()
    {
        // Thai uses the Buddhist calendar by default, whose year is 543 years ahead; wire dates are Gregorian.
        var thai = CultureInfo.GetCultureInfo("th-TH");
        var formats = DotNetCultureFormats.From(thai);

        Assert.Multiple(() =>
        {
            Assert.That(thai.DateTimeFormat.Calendar, Is.Not.InstanceOf<GregorianCalendar>());
            Assert.That(CultureFormatting.FormatCalendar("2026-03-07", "short", formats), Does.Contain("2026"));
        });
    }

    [Test]
    public void TheCurrency_IsTheRegionsOwn_AndNoneForACultureWithoutARegion()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DotNetCultureFormats.From(CultureInfo.GetCultureInfo("de-CH")).Currency.Code, Is.EqualTo("CHF"));
            Assert.That(DotNetCultureFormats.From(CultureInfo.GetCultureInfo("da-DK")).Currency.Code, Is.EqualTo("DKK"));
            Assert.That(DotNetCultureFormats.From(CultureInfo.GetCultureInfo("de")).Currency.Code, Is.Empty);
            Assert.That(Invariant.Currency.Code, Is.Empty);
        });
    }

    [Test]
    public void ANameDotNetDoesNotKnow_FormatsAsTheInvariantCulture_AndEachCultureIsWrittenOnce()
    {
        var cultures = new MergeTagCultures();

        Assert.Multiple(() =>
        {
            Assert.That(cultures.For("xx-not-a-culture-name-at-all").Dates, Is.EqualTo(Invariant.Dates));
            Assert.That(cultures.For("xx-not-a-culture-name-at-all").Currency, Is.EqualTo(Invariant.Currency).Using<CurrencyFormat>(SameCurrency));
            Assert.That(cultures.For("not a name"), Is.Not.Null);
            Assert.That(cultures.For("de-CH"), Is.SameAs(cultures.For("DE-ch")));
            Assert.That(cultures.For(string.Empty).Dates, Is.EqualTo(Invariant.Dates));
        });
    }

    private static bool SameCurrency(CurrencyFormat a, CurrencyFormat b) =>
        a.Code == b.Code && a.Symbol == b.Symbol && a.Decimal == b.Decimal && a.Group == b.Group && a.Sizes.SequenceEqual(b.Sizes) && a.Positive == b.Positive && a.Negative == b.Negative;

    /// <summary>Whether a .NET pattern has, outside quotes and escapes, a letter ADR-0064's subset lacks.</summary>
    private static bool OutsideSubset(string pattern)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c is '\'' or '"')
            {
                var close = pattern.IndexOf(c, i + 1);
                i = close < 0 ? pattern.Length : close;
            }
            else if (c == '\\')
            {
                i++;
            }
            else if ("fFgKz".Contains(c, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The runs of pattern letters in a written pattern, whose literals are escaped one by one.</summary>
    private static List<(char Letter, int Length)> Runs(string pattern)
    {
        var runs = new List<(char Letter, int Length)>();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\')
            {
                i++;
                continue;
            }

            if (!"dMyhHmst".Contains(pattern[i], StringComparison.Ordinal))
            {
                continue;
            }

            var start = i;
            while (i + 1 < pattern.Length && pattern[i + 1] == pattern[start])
            {
                i++;
            }

            runs.Add((pattern[start], i - start + 1));
        }

        return runs;
    }

    private static string Wire(DateTime instant) => instant.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss", CultureInfo.InvariantCulture);

    private static string Named(CultureInfo culture) => culture.Name.Length == 0 ? "(invariant)" : culture.Name;
}
