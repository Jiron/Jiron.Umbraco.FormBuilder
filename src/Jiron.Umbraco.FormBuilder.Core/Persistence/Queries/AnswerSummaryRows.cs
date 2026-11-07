namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>Which fields of the catalogue the summaries read, by what each is summarised by (ADR-0212).</summary>
internal sealed record AnswerSummaryFields
{
    /// <summary>The fields whose answers are counted on their own: those no other statement below reads.</summary>
    public IReadOnlyCollection<int> Answered { get; init; } = [];

    /// <summary>The fields counted per stored key: options, rankings and a grid's rows.</summary>
    public IReadOnlyCollection<int> Options { get; init; } = [];

    /// <summary>The number fields.</summary>
    public IReadOnlyCollection<int> Numbers { get; init; } = [];

    /// <summary>The checkbox fields.</summary>
    public IReadOnlyCollection<int> Ticked { get; init; } = [];

    /// <summary>The free-text fields, whose newest answers are read.</summary>
    public IReadOnlyCollection<int> Texts { get; init; } = [];

    /// <summary>
    /// The grids, each with its rows' fields: a grid's answers are the entries, or rows of a repeatable group, that
    /// answered any of its rows, each once.
    /// </summary>
    public IReadOnlyList<(int Grid, IReadOnlyList<int> Rows)> Grids { get; init; } = [];
}

/// <summary>What the database answers of the summaries (ADR-0212).</summary>
/// <param name="Entries">How many entries match.</param>
/// <param name="Answered">For each field and grid, how many answers: entries, or rows of a repeatable group.</param>
/// <param name="Options">For each field and stored key, how many answers chose it.</param>
/// <param name="Numbers">For each number field, its figures.</param>
/// <param name="Ticked">For each checkbox field, how many ticked it.</param>
/// <param name="Texts">For each free-text field, its newest answers.</param>
internal sealed record AnswerSummaryRows(
    long Entries,
    IReadOnlyDictionary<int, long> Answered,
    IReadOnlyList<AnswerOptionCount> Options,
    IReadOnlyDictionary<int, AnswerNumbers> Numbers,
    IReadOnlyDictionary<int, long> Ticked,
    IReadOnlyDictionary<int, AnswerTextPage> Texts);

/// <summary>How many answers of a field chose a stored key.</summary>
/// <param name="FieldId">The field.</param>
/// <param name="Key">The stored key (<c>textKey</c>).</param>
/// <param name="Count">How many answers chose it.</param>
/// <param name="Positions">The sum of the places it was chosen at, from 0: a ranking's positions less one each.</param>
/// <param name="Sample">One of its stored texts, to name it by.</param>
internal sealed record AnswerOptionCount(int FieldId, string Key, long Count, long Positions, string Sample);

/// <summary>A number field's figures.</summary>
/// <param name="Count">How many answers.</param>
/// <param name="Sum">Their sum.</param>
/// <param name="Lowest">The lowest.</param>
/// <param name="Highest">The highest.</param>
/// <param name="Median">The middle one, or the mean of the two in the middle.</param>
/// <param name="Low">How many are 6 or less, a 0-to-10 scale's bottom group.</param>
/// <param name="Middle">How many are 7 or 8.</param>
/// <param name="High">How many are 9 or more, its top group.</param>
internal sealed record AnswerNumbers(long Count, double Sum, double Lowest, double Highest, double Median, long Low, long Middle, long High);

/// <summary>A free-text answer.</summary>
/// <param name="Entry">Its entry's key.</param>
/// <param name="Received">When its entry was received.</param>
/// <param name="Row">Its row of a repeatable group, 0 for a field outside one.</param>
/// <param name="Text">The text.</param>
internal sealed record AnswerText(Guid Entry, DateTimeOffset Received, int Row, string Text);

/// <summary>A page of free-text answers.</summary>
/// <param name="Texts">The answers, newest entry first.</param>
/// <param name="Next">Where the next page starts; none on the last.</param>
internal sealed record AnswerTextPage(IReadOnlyList<AnswerText> Texts, AnswerTextCursor? Next);

/// <summary>Where a page of free-text answers ended: its last answer's entry, that entry's submission and the answer's row.</summary>
/// <param name="EntryId">The entry's row.</param>
/// <param name="SubmittedUtc">Its submission time, in UTC.</param>
/// <param name="Row">The answer's row.</param>
internal sealed record AnswerTextCursor(long EntryId, DateTime SubmittedUtc, int Row);

/// <summary>A row of a grouped summary statement, read by its columns' aliases.</summary>
internal sealed class AnswerSummaryRow
{
    /// <summary>The field.</summary>
    public int FieldId { get; set; }

    /// <summary>The stored key, for a statement grouped by it.</summary>
    public string? TextKey { get; set; }

    /// <summary>How many answers.</summary>
    public long Total { get; set; }

    /// <summary>How many answers chose the key first, for options.</summary>
    public long? Firsts { get; set; }

    /// <summary>How many answers are ticked, for checkboxes.</summary>
    public long? Ticks { get; set; }

    /// <summary>The sum of the places, for options.</summary>
    public long? Positions { get; set; }

    /// <summary>One of the stored texts, for options.</summary>
    public string? Sample { get; set; }

    /// <summary>The sum, for numbers.</summary>
    public double? Sum { get; set; }

    /// <summary>The lowest, for numbers.</summary>
    public double? Lowest { get; set; }

    /// <summary>The highest, for numbers.</summary>
    public double? Highest { get; set; }

    /// <summary>How many in a 0-to-10 scale's bottom group.</summary>
    public long? Low { get; set; }

    /// <summary>How many in its middle group.</summary>
    public long? Middle { get; set; }

    /// <summary>How many in its top group.</summary>
    public long? High { get; set; }
}

/// <summary>A free-text answer as the statement reads it.</summary>
internal sealed class AnswerTextRow
{
    /// <summary>Its entry's key.</summary>
    public Guid EntryKey { get; set; }

    /// <summary>Its entry's row.</summary>
    public long EntryId { get; set; }

    /// <summary>Its entry's submission time as stored.</summary>
    public DateTime SubmittedUtc { get; set; }

    /// <summary>Its row of a repeatable group.</summary>
    public int RowIndex { get; set; }

    /// <summary>The text.</summary>
    public string? Text { get; set; }
}
