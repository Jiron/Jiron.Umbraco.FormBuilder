using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The culture data the server prints with, by culture name: the formats merge tags and both runtimes print with,
/// written once per culture from .NET's data (<see cref="DotNetCultureFormats"/>), and the countries' names and their
/// order (<see cref="CountryNames"/>). A name .NET does not know gets the invariant culture's formats. The data is the
/// operating system's (its ICU), the same on every server of a site, so each keeps its own copy; another system may
/// write it differently (ADR-0263).
/// </summary>
internal sealed class MergeTagCultures
{
    /// <summary>The most cultures kept; a site has a few, and any further one is written each time it is asked for.</summary>
    private const int Kept = 256;

    private readonly ConcurrentDictionary<string, CultureFormats> _formats = new(StringComparer.OrdinalIgnoreCase);

    // The formats given by culture name, and the cultures made from them as they are asked for.
    private readonly Dictionary<string, CultureFormats> _given = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CultureInfo> _made = new(StringComparer.OrdinalIgnoreCase);

    // Countries given by language, each with its names by code and the place of each name in the language's order.
    private readonly Dictionary<string, (Dictionary<string, string> Names, Dictionary<string, int> Places)> _countries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The formats of every culture from .NET's data.</summary>
    public MergeTagCultures()
    {
    }

    /// <summary>
    /// The formats given for some cultures, and .NET's for the others: the shared vectors print with cultures written as
    /// data, which pin the formatter rather than a platform's culture data (ADR-0064). The countries given for some
    /// languages, in their order, name and order the countries in those languages; .NET's data does for the others: the
    /// rendering fixtures render with culture data written as data, the same on every system (ADR-0263).
    /// </summary>
    /// <param name="given">The formats by culture name.</param>
    /// <param name="countries">The countries by language (<c>en</c>, <c>de</c>), each as its code and name, in the language's order.</param>
    internal MergeTagCultures(IEnumerable<KeyValuePair<string, CultureFormats>> given, IEnumerable<KeyValuePair<string, IReadOnlyList<KeyValuePair<string, string>>>>? countries = null)
    {
        ArgumentNullException.ThrowIfNull(given);
        foreach (var (name, formats) in given)
        {
            _formats[name] = formats;
            _given[name] = formats;
        }

        foreach (var (language, list) in countries ?? [])
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var places = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (code, name) in list)
            {
                names[code] = name;
                places.TryAdd(name, places.Count);
            }

            _countries[language] = (names, places);
        }
    }

    /// <summary>
    /// The culture the server formats its own texts with besides the merge tags (a date in a notice, a count, a file's
    /// size): .NET's culture of the name, or for given formats a copy of it with those formats' dates, names and numbers.
    /// </summary>
    /// <param name="name">A culture name such as <c>de-CH</c>, as <see cref="CultureInfo.GetCultureInfo(string)"/> takes it.</param>
    public CultureInfo Info(string name)
    {
        var system = CultureInfo.GetCultureInfo(name);
        return _given.TryGetValue(name, out var formats) ? _made.GetOrAdd(name, _ => Made(system, formats)) : system;
    }

    /// <summary>The name of a country by its ISO 3166 alpha-2 code in a culture's language; the code when there is none.</summary>
    /// <param name="code">The code, such as <c>CH</c>.</param>
    /// <param name="culture">The culture, such as <c>de-CH</c>; English for the invariant culture or one .NET does not know.</param>
    public string CountryName(string code, string culture)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _countries.Count > 0 && _countries.TryGetValue(CountryNames.Language(culture), out var given)
            ? given.Names.GetValueOrDefault(code.ToUpperInvariant(), code)
            : CountryNames.Name(code, culture);
    }

    /// <summary>
    /// The order of countries' names in a culture, letter case aside: the culture's own, from .NET's data, or for given
    /// countries the order they were given in, any other name after them in ordinal order.
    /// </summary>
    /// <param name="culture">The culture, such as <c>de-CH</c>.</param>
    public IComparer<string?> CountryOrder(string culture)
    {
        if (_countries.Count > 0 && _countries.TryGetValue(CountryNames.Language(culture), out var given))
        {
            var places = given.Places;
            return Comparer<string?>.Create((a, b) => (Place(a, out var x), Place(b, out var y)) switch
            {
                (true, true) => x.CompareTo(y),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            });

            bool Place(string? name, out int place)
            {
                place = 0;
                return name is not null && places.TryGetValue(name, out place);
            }
        }

        return StringComparer.Create(Culture(culture), ignoreCase: true);
    }

    /// <summary>The formats of a culture.</summary>
    /// <param name="name">A culture name such as <c>de-CH</c>; empty for the invariant culture.</param>
    public CultureFormats For(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (_formats.TryGetValue(name, out var formats))
        {
            return formats;
        }

        formats = DotNetCultureFormats.From(Culture(name));
        if (_formats.Count < Kept)
        {
            _formats.TryAdd(name, formats);
        }

        return formats;
    }

    // A read-only copy of a culture with the given formats' date patterns, names and numbers: what the server formats
    // with outside the merge tags reads only these of it.
    private static CultureInfo Made(CultureInfo system, CultureFormats formats)
    {
        var culture = (CultureInfo)system.Clone();
        var dates = culture.DateTimeFormat;
        if (dates.Calendar is not GregorianCalendar)
        {
            dates.Calendar = new GregorianCalendar();
        }

        dates.ShortDatePattern = formats.Dates.Short;
        dates.LongDatePattern = formats.Dates.Long;
        dates.ShortTimePattern = formats.Dates.Time;
        dates.MonthNames = [.. formats.Months, string.Empty];
        dates.AbbreviatedMonthNames = [.. formats.MonthsShort, string.Empty];
        dates.MonthGenitiveNames = [.. formats.MonthsGenitive, string.Empty];
        dates.AbbreviatedMonthGenitiveNames = [.. formats.MonthsShortGenitive, string.Empty];
        dates.DayNames = [.. formats.Days];
        dates.AbbreviatedDayNames = [.. formats.DaysShort];
        dates.AMDesignator = formats.Am;
        dates.PMDesignator = formats.Pm;
        var numbers = culture.NumberFormat;
        numbers.NegativeSign = formats.Minus;
        numbers.NumberDecimalSeparator = formats.Number.Decimal;
        numbers.NumberGroupSeparator = formats.Number.Group;
        numbers.NumberGroupSizes = [.. formats.Number.Sizes];
        return CultureInfo.ReadOnly(culture);
    }

    /// <summary>
    /// The culture of a name, or the invariant culture for a name .NET does not know. Only the cultures .NET has data for
    /// count: a well-formed name of none would otherwise get made-up formats.
    /// </summary>
    public static CultureInfo Culture(string name)
    {
        try
        {
            return CultureInfo.GetCultureInfo(name, predefinedOnly: true);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>
    /// Writes a culture's formats as the browser's runtime takes them (<c>CultureFormats</c> in
    /// <c>packages/logic/src/culture.ts</c>, the shape the shared vectors write), so the browser prints what the server
    /// prints (ADR-0064, ADR-0085).
    /// </summary>
    /// <param name="writer">Where the JSON object goes.</param>
    /// <param name="formats">The formats.</param>
    public static void Write(Utf8JsonWriter writer, CultureFormats formats)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formats);
        writer.WriteStartObject();
        writer.WriteStartObject("dates");
        writer.WriteString("short", formats.Dates.Short);
        writer.WriteString("medium", formats.Dates.Medium);
        writer.WriteString("long", formats.Dates.Long);
        writer.WriteString("time", formats.Dates.Time);
        writer.WriteString("dateTime", formats.Dates.DateTime);
        writer.WriteEndObject();
        Strings(writer, "months", formats.Months);
        Strings(writer, "monthsShort", formats.MonthsShort);
        Strings(writer, "monthsGenitive", formats.MonthsGenitive);
        Strings(writer, "monthsShortGenitive", formats.MonthsShortGenitive);
        Strings(writer, "days", formats.Days);
        Strings(writer, "daysShort", formats.DaysShort);
        writer.WriteString("am", formats.Am);
        writer.WriteString("pm", formats.Pm);
        writer.WriteString("minus", formats.Minus);
        writer.WriteStartObject("number");
        writer.WriteString("decimal", formats.Number.Decimal);
        writer.WriteString("group", formats.Number.Group);
        Sizes(writer, formats.Number.Sizes);
        writer.WriteString("negative", formats.Number.Negative);
        writer.WriteEndObject();
        writer.WriteStartObject("currency");
        writer.WriteString("code", formats.Currency.Code);
        writer.WriteString("symbol", formats.Currency.Symbol);
        writer.WriteString("decimal", formats.Currency.Decimal);
        writer.WriteString("group", formats.Currency.Group);
        Sizes(writer, formats.Currency.Sizes);
        writer.WriteString("positive", formats.Currency.Positive);
        writer.WriteString("negative", formats.Currency.Negative);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void Strings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static void Sizes(Utf8JsonWriter writer, IReadOnlyList<int> sizes)
    {
        writer.WriteStartArray("sizes");
        foreach (var size in sizes)
        {
            writer.WriteNumberValue(size);
        }

        writer.WriteEndArray();
    }
}
