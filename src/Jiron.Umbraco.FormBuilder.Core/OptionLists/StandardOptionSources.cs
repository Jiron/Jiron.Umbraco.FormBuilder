using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>The standard lists (ADR-0073), the option sources every site starts with.</summary>
internal static class StandardOptionSources
{
    /// <summary>Their types, in the order the backoffice offers them.</summary>
    public static readonly ImmutableArray<Type> All =
    [
        typeof(MonthsOptionSource),
        typeof(WeekdaysOptionSource),
        typeof(AgreementOptionSource),
        typeof(SatisfactionOptionSource),
        typeof(YesNoNotSureOptionSource),
    ];

    /// <summary>A name from culture data with its first letter in capitals, as a label starts a line.</summary>
    public static string Capitalised(string name, CultureInfo culture) =>
        name.Length == 0 ? name : string.Concat(culture.TextInfo.ToUpper(name[0]).ToString(), name.AsSpan(1));

    /// <summary>Options of the values with the culture's words, in order.</summary>
    public static ValueTask<IReadOnlyList<FormBuilderOption>> Of(IReadOnlyList<string> values, IReadOnlyList<string> words) =>
        ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([.. values.Select((value, i) => new FormBuilderOption(value, words[i]))]);
}

/// <summary>The months (<c>months</c>, ADR-0073): <c>1</c> to <c>12</c>, named from .NET's culture data over the Gregorian calendar.</summary>
public sealed class MonthsOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "months";

    /// <inheritdoc />
    public string Label => "#jfbOptionSources_months";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var culture = MergeTagCultures.Culture(request.Culture);
        var names = DotNetCultureFormats.Gregorian(culture).MonthNames;
        return ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
            [.. Enumerable.Range(1, 12).Select(month => new FormBuilderOption(month.ToString(CultureInfo.InvariantCulture), StandardOptionSources.Capitalised(names[month - 1], culture)))]);
    }
}

/// <summary>
/// The days of the week (<c>weekdays</c>, ADR-0073): <c>1</c> (Monday) to <c>7</c> (Sunday) as ISO 8601 numbers them,
/// from the culture's first day of the week, named from .NET's culture data.
/// </summary>
public sealed class WeekdaysOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "weekdays";

    /// <inheritdoc />
    public string Label => "#jfbOptionSources_weekdays";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var culture = MergeTagCultures.Culture(request.Culture);
        var dates = DotNetCultureFormats.Gregorian(culture);
        var first = (int)dates.FirstDayOfWeek;
        return ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
        [
            .. Enumerable.Range(0, 7).Select(offset =>
            {
                var day = (first + offset) % 7;
                var iso = day == 0 ? 7 : day;
                return new FormBuilderOption(iso.ToString(CultureInfo.InvariantCulture), StandardOptionSources.Capitalised(dates.DayNames[day], culture));
            }),
        ]);
    }
}

/// <summary>An agreement scale (<c>agreement</c>, ADR-0073): <c>1</c> (strongly disagree) to <c>5</c> (strongly agree).</summary>
public sealed class AgreementOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "agreement";

    /// <inheritdoc />
    public string Label => "#jfbOptionSources_agreement";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StandardOptionSources.Of(["1", "2", "3", "4", "5"], StandardOptionWords.Of(request.Culture).Agreement);
    }
}

/// <summary>A satisfaction scale (<c>satisfaction</c>, ADR-0073): <c>1</c> (very dissatisfied) to <c>5</c> (very satisfied).</summary>
public sealed class SatisfactionOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "satisfaction";

    /// <inheritdoc />
    public string Label => "#jfbOptionSources_satisfaction";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StandardOptionSources.Of(["1", "2", "3", "4", "5"], StandardOptionWords.Of(request.Culture).Satisfaction);
    }
}

/// <summary>Yes, no and not sure (<c>yesNoNotSure</c>, ADR-0073): <c>yes</c>, <c>no</c> and <c>notSure</c>.</summary>
public sealed class YesNoNotSureOptionSource : IFormBuilderOptionSource
{
    /// <inheritdoc />
    public string Name => "yesNoNotSure";

    /// <inheritdoc />
    public string Label => "#jfbOptionSources_yesNoNotSure";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormBuilderOption>> GetOptionsAsync(FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StandardOptionSources.Of(["yes", "no", "notSure"], StandardOptionWords.Of(request.Culture).YesNoNotSure);
    }
}

/// <summary>
/// The words of the standard lists (ADR-0073) from the language tables in <c>OptionLists/lang/</c>, in the seven
/// languages of the visitor texts (ADR-0230). A culture without its own words takes its more general culture's,
/// then English. The tables are read once, as the assembly holds them.
/// </summary>
internal sealed record StandardOptionWords(ImmutableArray<string> Agreement, ImmutableArray<string> Satisfaction, ImmutableArray<string> YesNoNotSure)
{
    private const string ResourcePrefix = "Jiron.Umbraco.FormBuilder.Core.OptionLists.lang.";

    private static readonly FrozenDictionary<string, StandardOptionWords> Tables = Read();

    /// <summary>The languages with words of their own.</summary>
    public static IReadOnlyCollection<string> Cultures => Tables.Keys;

    /// <summary>The words for a culture.</summary>
    public static StandardOptionWords Of(string? culture)
    {
        var candidate = culture ?? string.Empty;
        while (candidate.Length > 0)
        {
            if (Tables.TryGetValue(candidate, out var words))
            {
                return words;
            }

            var dash = candidate.LastIndexOf('-');
            candidate = dash < 0 ? string.Empty : candidate[..dash];
        }

        return Tables["en"];
    }

    private static FrozenDictionary<string, StandardOptionWords> Read()
    {
        var assembly = typeof(StandardOptionWords).Assembly;
        var tables = new Dictionary<string, StandardOptionWords>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            tables[name[ResourcePrefix.Length..^".json".Length]] = new StandardOptionWords(
                Words(root, "agreement", 5, name),
                Words(root, "satisfaction", 5, name),
                Words(root, "yesNoNotSure", 3, name));
        }

        return tables.ContainsKey("en")
            ? tables.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
            : throw new InvalidOperationException("The standard lists have no English words.");
    }

    private static ImmutableArray<string> Words(JsonElement root, string list, int count, string table)
    {
        var words = root.TryGetProperty(list, out var found) && found.ValueKind == JsonValueKind.Array
            ? [.. found.EnumerateArray().Select(word => word.ValueKind == JsonValueKind.String ? word.GetString()! : string.Empty)]
            : ImmutableArray<string>.Empty;
        return words.Length == count && words.All(word => word.Trim().Length > 0)
            ? words
            : throw new InvalidOperationException($"The language table {table} gives '{list}' not as {count} words.");
    }
}
