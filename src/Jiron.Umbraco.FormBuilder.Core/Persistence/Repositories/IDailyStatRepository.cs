namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>One count to add to a form's value of today (section 4.5, ADR-0041).</summary>
/// <param name="Metric">What is counted: one camelCase word of ASCII letters and digits, at most 64 characters, such as <c>views</c>.</param>
/// <param name="Dimension">
/// What it is counted by: empty for nothing, or at most 128 of ASCII letters, digits, <c>_</c>, <c>.</c>, <c>:</c> and
/// <c>-</c>: a key, an hour, or a field key and an error kind. Never anything a visitor typed.
/// </param>
/// <param name="By">How much to add, from 0 to <see cref="int.MaxValue"/>.</param>
internal sealed record DailyStatCount(string Metric, string Dimension = "", long By = 1);

/// <summary>A form's value of one metric and dimension on one day.</summary>
/// <param name="Day">The UTC day.</param>
/// <param name="FormId">The form.</param>
/// <param name="Metric">What was counted.</param>
/// <param name="Dimension">What it was counted by; empty for nothing.</param>
/// <param name="Value">The sum of the counts.</param>
internal sealed record DailyStat(DateOnly Day, int FormId, string Metric, string Dimension, long Value);

/// <summary>A form's sum of one metric without a dimension over some days.</summary>
/// <param name="FormId">The form.</param>
/// <param name="Metric">What was counted.</param>
/// <param name="Value">The sum.</param>
internal sealed record DailyStatSum(int FormId, string Metric, long Value);

/// <summary>
/// The daily counters of forms in <c>jfbDailyStat</c> (section 4.5, ADR-0041): per UTC day, form, metric and dimension,
/// right however many servers count at once, and never personal. New metrics need no migration. Only this repository
/// writes the table, and every write of a form's values holds the form's row first, as the unit that deletes the form
/// does (4.5): a count never reaches a form that is being deleted, and two units never wait for each other in a circle.
/// SQL Server compares metrics and dimensions ignoring case, so no two may differ in case only.
/// </summary>
internal interface IDailyStatRepository
{
    /// <summary>
    /// Adds <paramref name="counts"/> to the form's values of today (the UTC day of <see cref="FormBuilderDatabase.UtcNow"/>),
    /// in the caller's unit of work when there is one. Counts of one metric and dimension are added up first. False when
    /// the form does not exist, counting nothing, even where values of it are left; true when there was nothing to add.
    /// </summary>
    Task<bool> IncrementAsync(int formId, IReadOnlyList<DailyStatCount> counts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="counts"/> to the form's values of <paramref name="day"/>, a UTC day from 1753 to today, as
    /// <see cref="IncrementAsync"/> adds them to today's: for sums that waited, such as a server's beacons (ADR-0211), which
    /// count on the day they came even when they are written after midnight.
    /// </summary>
    Task<bool> IncrementOnAsync(int formId, DateOnly day, IReadOnlyList<DailyStatCount> counts, CancellationToken cancellationToken = default);

    /// <summary>
    /// A form's values from <paramref name="from"/> to <paramref name="to"/>, both included, by day, metric and dimension:
    /// at most 366 days, from 1753 on.
    /// </summary>
    Task<IReadOnlyList<DailyStat>> ListAsync(int formId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// The sums of these forms' values of these metrics without a dimension from <paramref name="from"/> to
    /// <paramref name="to"/>, both included, by form and metric, as the site's table of forms reads them (ADR-0211): at
    /// most 366 days, from 1753 on. A form and metric without a value is left out.
    /// </summary>
    Task<IReadOnlyList<DailyStatSum>> SumAsync(IReadOnlyCollection<int> formIds, IReadOnlyCollection<string> metrics, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes all of a form's values a day at a time, in the unit that deletes the form (4.5), after it deleted the form's
    /// row; how many went. It holds the form's row itself when the row is still there, so no count comes in between.
    /// </summary>
    Task<int> DeleteFormAsync(int formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one form's values of the earliest day that lies more than <paramref name="keep"/> before today (retention,
    /// 13.1); how many went. The job repeats it until it returns 0, a form and a day at a time, so no statement locks more
    /// than one form's values of one day.
    /// </summary>
    Task<int> PurgeAsync(TimeSpan keep, CancellationToken cancellationToken = default);
}
