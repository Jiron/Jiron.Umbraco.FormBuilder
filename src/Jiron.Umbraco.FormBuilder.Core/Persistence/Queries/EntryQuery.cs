using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>Which test entries a list holds (section 4.4: preview entries stay out unless the editor asks).</summary>
internal enum EntryTestFilter
{
    /// <summary>Real entries only; the default.</summary>
    Exclude,

    /// <summary>Real and test entries.</summary>
    Include,

    /// <summary>Test entries only.</summary>
    Only,
}

/// <summary>Which entries a list holds by their action runs (ADR-0207).</summary>
internal enum EntryActionFilter
{
    /// <summary>Every entry, whatever its runs.</summary>
    Any,

    /// <summary>An entry with a run that failed for good.</summary>
    Failed,

    /// <summary>An entry with a run whose outcome is uncertain.</summary>
    Uncertain,

    /// <summary>An entry with runs, every one of them delivered or skipped.</summary>
    Delivered,
}

/// <summary>The column a list is sorted by (ADR-0039).</summary>
internal enum EntrySortColumn
{
    /// <summary>The submission time, then the entry id.</summary>
    Submitted,

    /// <summary>A field's text key: the text cleaned and folded as rules compare it (ADR-0038).</summary>
    Text,

    /// <summary>A field's number.</summary>
    Number,

    /// <summary>A field's date.</summary>
    Date,
}

/// <summary>
/// How a list is sorted: by submission, or by a field's value, every entry once at its first answer, with the entries
/// that have no value last, newest first (ADR-0039).
/// </summary>
/// <param name="Column">The column.</param>
/// <param name="Descending">Whether the largest value comes first.</param>
/// <param name="FieldId">The field's catalogue id for a value sort; 0 for the submission.</param>
internal sealed record EntrySort(EntrySortColumn Column, bool Descending, int FieldId = 0)
{
    /// <summary>Newest first: the default.</summary>
    public static EntrySort Newest { get; } = new(EntrySortColumn.Submitted, Descending: true);

    /// <summary>Oldest first.</summary>
    public static EntrySort Oldest { get; } = new(EntrySortColumn.Submitted, Descending: false);

    /// <summary>A sort by a field's text key, number or date.</summary>
    public static EntrySort ByValue(int fieldId, EntrySortColumn column, bool descending) => new(column, descending, fieldId);

    /// <summary>Whether the sort walks a field's values.</summary>
    public bool ByField => Column != EntrySortColumn.Submitted;
}

/// <summary>How a text filter compares (ADR-0039).</summary>
internal enum EntryTextMatch
{
    /// <summary>Some answer equals one of the values.</summary>
    OneOf,

    /// <summary>No answer equals any of the values: the exact opposite of <see cref="OneOf"/>.</summary>
    NoneOf,

    /// <summary>Some answer starts with the value.</summary>
    StartsWith,
}

/// <summary>
/// A filter on one field's values. An entry matches when any of its answers does (ADR-0039). The field is a row of the
/// form's catalogue; a field of another form matches no entry of this form.
/// </summary>
/// <param name="FieldId">The field's catalogue id.</param>
internal abstract record EntryFieldFilter(int FieldId);

/// <summary>A text answer compared by its key, as rules compare text (ADR-0005, ADR-0038).</summary>
/// <param name="FieldId">The field's catalogue id.</param>
/// <param name="Match">How it compares.</param>
/// <param name="Values">The values: 1 to <see cref="EntryQuerySql.MaxValuesPerFilter"/>, one for a prefix.</param>
internal sealed record EntryTextFilter(int FieldId, EntryTextMatch Match, IReadOnlyList<string> Values) : EntryFieldFilter(FieldId);

/// <summary>A number answer in a range, open at either end.</summary>
/// <param name="FieldId">The field's catalogue id.</param>
/// <param name="Min">The lower bound, or none.</param>
/// <param name="MinInclusive">Whether the lower bound itself matches.</param>
/// <param name="Max">The upper bound, or none.</param>
/// <param name="MaxInclusive">Whether the upper bound itself matches.</param>
internal sealed record EntryNumberFilter(int FieldId, decimal? Min, bool MinInclusive, decimal? Max, bool MaxInclusive) : EntryFieldFilter(FieldId);

/// <summary>A date answer from <paramref name="From"/> (inclusive) to before <paramref name="Before"/>, in UTC.</summary>
/// <param name="FieldId">The field's catalogue id.</param>
/// <param name="From">The first instant that matches, or none.</param>
/// <param name="Before">The first instant that no longer matches, or none.</param>
internal sealed record EntryDateFilter(int FieldId, DateTime? From, DateTime? Before) : EntryFieldFilter(FieldId);

/// <summary>A checkbox checked, or not: an entry without the answer is not checked.</summary>
/// <param name="FieldId">The field's catalogue id.</param>
/// <param name="Checked">Whether it is checked.</param>
internal sealed record EntryCheckedFilter(int FieldId, bool Checked) : EntryFieldFilter(FieldId);

/// <summary>A field answered (it has a value, a file included), or not.</summary>
/// <param name="FieldId">The field's catalogue id.</param>
/// <param name="Answered">Whether it is answered.</param>
internal sealed record EntryAnsweredFilter(int FieldId, bool Answered) : EntryFieldFilter(FieldId);

/// <summary>Which entries of one form a list holds, and in which order (ADR-0039).</summary>
/// <param name="FormId">The form.</param>
internal sealed record EntryQuery(int FormId)
{
    /// <summary>The states; none means every state.</summary>
    public IReadOnlyCollection<EntryState> States { get; init; } = [];

    /// <summary>Which test entries.</summary>
    public EntryTestFilter Tests { get; init; } = EntryTestFilter.Exclude;

    /// <summary>The first submission time that matches, in UTC, or none.</summary>
    public DateTime? SubmittedFrom { get; init; }

    /// <summary>The first submission time that no longer matches, in UTC, or none.</summary>
    public DateTime? SubmittedBefore { get; init; }

    /// <summary>The field filters, all of which an entry matches.</summary>
    public IReadOnlyList<EntryFieldFilter> Filters { get; init; } = [];

    /// <summary>Words to look for in the entry's search text, or none.</summary>
    public string? Search { get; init; }

    /// <summary>
    /// The entry numbers a search also finds: an entry whose number is one of them matches as its search text would
    /// (ADR-0207). Only with a search; at most <see cref="EntryQuerySql.MaxSearchNumbers"/>.
    /// </summary>
    public IReadOnlyList<int> SearchNumbers { get; init; } = [];

    /// <summary>Which entries by their action runs.</summary>
    public EntryActionFilter Actions { get; init; } = EntryActionFilter.Any;

    /// <summary>The order.</summary>
    public EntrySort Sort { get; init; } = EntrySort.Newest;
}

/// <summary>Where the walk of a value sort stood: the value row and its value, for a row deleted meanwhile.</summary>
/// <param name="ValueId">The value row.</param>
/// <param name="Text">Its text key, for a text sort.</param>
/// <param name="Number">Its number, for a number sort.</param>
/// <param name="Date">Its date, for a date sort.</param>
internal sealed record EntryValuePosition(long ValueId, string? Text, double? Number, DateTime? Date);

/// <summary>
/// The place after the last entry of a page, where the next page starts (ADR-0039). It names a position only; every
/// page still filters by its form.
/// </summary>
/// <param name="Sort">The sort the cursor belongs to.</param>
/// <param name="EntryId">The last entry.</param>
/// <param name="SubmittedUtc">Its submission time as stored, for an entry deleted meanwhile.</param>
/// <param name="Value">In the walk of a value sort, where it stood; none by submission and in the tail.</param>
internal sealed record EntryCursor(EntrySort Sort, long EntryId, DateTime SubmittedUtc, EntryValuePosition? Value);

/// <summary>How many entries match: exact, or at least <see cref="Value"/> when <see cref="More"/> (a search).</summary>
/// <param name="Value">The count, at most 1,000 for a search.</param>
/// <param name="More">Whether more than <see cref="Value"/> match.</param>
internal readonly record struct EntryCount(int Value, bool More);

/// <summary>One page of entries.</summary>
/// <param name="Entries">The page's entries, in the list's order.</param>
/// <param name="Next">Where the next page starts, or none on the last page.</param>
/// <param name="Count">How many entries match, when asked for.</param>
internal sealed record EntryPage(IReadOnlyList<StoredEntry> Entries, EntryCursor? Next, EntryCount? Count);
