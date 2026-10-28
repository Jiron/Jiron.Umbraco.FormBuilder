using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The culture formats merge tags print with, by culture name, written once per culture from .NET's data
/// (<see cref="DotNetCultureFormats"/>). A name .NET does not know gets the invariant culture's formats. The formats are
/// the same on every server, so each keeps its own copy.
/// </summary>
internal sealed class MergeTagCultures
{
    /// <summary>The most cultures kept; a site has a few, and any further one is written each time it is asked for.</summary>
    private const int Kept = 256;

    private readonly ConcurrentDictionary<string, CultureFormats> _formats = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The formats of every culture from .NET's data.</summary>
    public MergeTagCultures()
    {
    }

    /// <summary>
    /// The formats given for some cultures, and .NET's for the others: the shared vectors print with cultures written as
    /// data, which pin the formatter rather than a platform's culture data (ADR-0064).
    /// </summary>
    /// <param name="given">The formats by culture name.</param>
    internal MergeTagCultures(IEnumerable<KeyValuePair<string, CultureFormats>> given)
    {
        ArgumentNullException.ThrowIfNull(given);
        foreach (var (name, formats) in given)
        {
            _formats[name] = formats;
        }
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
