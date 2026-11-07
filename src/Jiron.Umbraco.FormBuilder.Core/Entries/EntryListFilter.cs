using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>A span of days the entries list's Received filter names (ADR-0207), in the site's time zone; a week starts on Monday.</summary>
internal enum EntryReceivedPreset
{
    /// <summary>Today.</summary>
    Today,

    /// <summary>This week, from Monday.</summary>
    ThisWeek,

    /// <summary>Today and the six days before it.</summary>
    Last7Days,

    /// <summary>This month.</summary>
    ThisMonth,

    /// <summary>The month before this one.</summary>
    LastMonth,
}

/// <summary>
/// When the entries were received: a preset, or the days from <see cref="From"/> to <see cref="To"/>, both included and
/// either open; in the site's time zone, so that a saved "today" stays today (ADR-0207).
/// </summary>
internal sealed record EntryReceived
{
    /// <summary>The span of days, or none for a range.</summary>
    public EntryReceivedPreset? Preset { get; init; }

    /// <summary>The first day of a range, or none.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last day of a range, or none.</summary>
    public DateOnly? To { get; init; }
}

/// <summary>What a field filter tests (ADR-0207); the field's kind decides which it allows.</summary>
internal enum EntryFieldTest
{
    /// <summary>An answer is one of the values.</summary>
    OneOf,

    /// <summary>No answer is any of the values.</summary>
    NoneOf,

    /// <summary>An answer starts with the value.</summary>
    StartsWith,

    /// <summary>A number from the minimum to the maximum, or a date from the first day to the last, both included and either open.</summary>
    Between,

    /// <summary>Checked.</summary>
    Checked,

    /// <summary>Not checked: unchecked or unanswered.</summary>
    NotChecked,

    /// <summary>Answered.</summary>
    Answered,

    /// <summary>Not answered.</summary>
    NotAnswered,
}

/// <summary>A filter on one of the form's fields, by its key.</summary>
internal sealed record EntryFieldCondition
{
    /// <summary>The field.</summary>
    public Guid Field { get; init; }

    /// <summary>What it tests.</summary>
    public EntryFieldTest Test { get; init; }

    /// <summary>The values of <see cref="EntryFieldTest.OneOf"/> and <see cref="EntryFieldTest.NoneOf"/>, or the one of <see cref="EntryFieldTest.StartsWith"/>.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>The lowest number of a number range, or none.</summary>
    public decimal? Min { get; init; }

    /// <summary>The highest number of a number range, or none.</summary>
    public decimal? Max { get; init; }

    /// <summary>The first day of a date range, or none.</summary>
    public DateOnly? From { get; init; }

    /// <summary>The last day of a date range, or none.</summary>
    public DateOnly? To { get; init; }
}

/// <summary>The order of a list: by when the entries were received, or by a field's answers.</summary>
internal sealed record EntryListSort
{
    /// <summary>The field, or none for when the entries were received.</summary>
    public Guid? Field { get; init; }

    /// <summary>Whether the newest or the largest comes first.</summary>
    public bool Descending { get; init; } = true;
}

/// <summary>
/// Which entries a list holds and in which order (IA 6.1, ADR-0207): what a list asks for, and what a filter set keeps,
/// in one shape.
/// </summary>
internal sealed record EntryListFilter
{
    /// <summary>The states; none means every state a list holds, which leaves out drafts and Spam.</summary>
    public IReadOnlyList<EntryState> States { get; init; } = [];

    /// <summary>When the entries were received, or any time.</summary>
    public EntryReceived? Received { get; init; }

    /// <summary>The entries by their action runs.</summary>
    public EntryActionFilter Actions { get; init; } = EntryActionFilter.Any;

    /// <summary>The field filters, all of which an entry matches.</summary>
    public IReadOnlyList<EntryFieldCondition> Fields { get; init; } = [];

    /// <summary>The words, number or reference to look for, or none.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// The forms of a list across forms (the Inbox and Spam, ADR-0209); none for every form the user may view. A form's own
    /// list names none, or only its form.
    /// </summary>
    public IReadOnlyList<Guid> Forms { get; init; } = [];

    /// <summary>The order; none for the list's own: newest first, or in the Inbox the unseen entries first.</summary>
    public EntryListSort? Sort { get; init; }
}

/// <summary>The JSON a filter set keeps its filter in: the shape the management API reads and writes, names in camelCase.</summary>
internal static class EntryListJson
{
    /// <summary>The options: the web's defaults, enums by their camelCase names only, unknown members refused.</summary>
    public static readonly JsonSerializerOptions Options = FrozenOptions.Of(new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
    });
}
