using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// Every unit that writes a form's values holds the form's row first (<see cref="RowHolds.HoldFormAsync"/>), as the
/// unit that deletes the form does: no count reaches a form that is being deleted, units of one form take turns, and none
/// waits for a form's row while it holds a value. Values are deleted a form and a day at a time, each a seek on the key.
/// </remarks>
internal sealed class DailyStatRepository(FormBuilderDatabase database) : IDailyStatRepository
{
    /// <summary>The longest metric the column holds.</summary>
    public const int MaxMetricLength = 64;

    /// <summary>The longest dimension the column holds.</summary>
    public const int MaxDimensionLength = 128;

    /// <summary>The most counts one call adds.</summary>
    public const int MaxCountsPerCall = 200;

    /// <summary>The most one count adds, so the counts of a call can never add up past what a value holds.</summary>
    public const long MaxBy = int.MaxValue;

    /// <summary>The most days one list covers: a year, leap or not.</summary>
    public const int MaxListDays = 366;

    /// <summary>The longest time statistics can be kept: ten years.</summary>
    public static readonly TimeSpan MaxKeep = TimeSpan.FromDays(3653);

    /// <inheritdoc />
    public Task<bool> IncrementAsync(int formId, IReadOnlyList<DailyStatCount> counts, CancellationToken cancellationToken = default) =>
        CountAsync(formId, null, counts, cancellationToken);

    /// <inheritdoc />
    public Task<bool> IncrementOnAsync(int formId, DateOnly day, IReadOnlyList<DailyStatCount> counts, CancellationToken cancellationToken = default)
    {
        if (Midnight(day) < FormBuilderDbTime.MinStorable)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, $"The day lies in {FormBuilderDbTime.MinStorable.Year} or later.");
        }

        if (Midnight(day) > database.UtcNow.Date)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "The day is today or before.");
        }

        return CountAsync(formId, day, counts, cancellationToken);
    }

    // Adds the counts to the values of the day, today when it is null.
    private Task<bool> CountAsync(int formId, DateOnly? on, IReadOnlyList<DailyStatCount> counts, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(formId, 1);
        var merged = Merge(counts);
        if (merged.Count == 0)
        {
            return Task.FromResult(true);
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await RowHolds.HoldFormAsync(db, n, formId))
                {
                    return false;
                }

                var day = on is { } given ? Midnight(given) : database.UtcNow.Date;
                foreach (var (metric, dimension, by) in merged)
                {
                    await AddAsync(db, n, day, formId, metric, dimension, by);
                }

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DailyStat>> ListAsync(int formId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        CheckDays(from, to);
        return database.ReadAsync<IReadOnlyList<DailyStat>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<DailyStatDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.DailyStat)} WHERE {n.C("formId")} = @0 AND {n.C("day")} >= @1 AND {n.C("day")} <= @2 ORDER BY {n.C("day")}, {n.C("metric")}{n.BinaryCollation}, {n.C("dimension")}{n.BinaryCollation}",
                    formId,
                    Midnight(from),
                    Midnight(to));
                return rows.Select(r => new DailyStat(DateOnly.FromDateTime(r.Day), r.FormId, r.Metric, r.Dimension, r.Value)).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailyStatSum>> SumAsync(IReadOnlyCollection<int> formIds, IReadOnlyCollection<string> metrics, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formIds);
        ArgumentNullException.ThrowIfNull(metrics);
        CheckDays(from, to);
        if (metrics.Count is 0 or > SqlArgs.MaxListLength / 2 || metrics.Any(metric => !IsMetric(metric)))
        {
            throw new ArgumentException($"A sum names 1 to {SqlArgs.MaxListLength / 2} metrics, each one camelCase word.", nameof(metrics));
        }

        var sums = new List<DailyStatSum>();
        foreach (var chunk in formIds.Distinct().Chunk(SqlArgs.MaxListLength / 2))
        {
            sums.AddRange(await database.ReadAsync(
                async db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    var rows = await db.FetchListAsync<SumRow>(
                        $"SELECT {n.C("formId")} AS formId, {n.C("metric")} AS metric, SUM({n.C("value")}) AS total FROM {n.T(FormBuilderTables.DailyStat)} "
                        + $"WHERE {n.C("formId")} IN ({a.List(chunk.Cast<object?>())}) AND {n.C("day")} >= {a.Add(Midnight(from))} AND {n.C("day")} <= {a.Add(Midnight(to))} "
                        + $"AND {n.C("metric")} IN ({a.List(metrics.Distinct(StringComparer.Ordinal).Cast<object?>())}) AND {n.C("dimension")} = {a.Add(string.Empty)} "
                        + $"GROUP BY {n.C("formId")}, {n.C("metric")}",
                        a.Values);
                    return rows.Select(row => new DailyStatSum(row.FormId, row.Metric, row.Total));
                },
                cancellationToken));
        }

        return sums;
    }

    /// <inheritdoc />
    public Task<int> DeleteFormAsync(int formId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(formId, 1);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.DailyStat);

                // A unit that deleted the form's row holds it already, and finds nothing more to hold.
                await RowHolds.HoldFormAsync(db, n, formId);
                var days = await db.FetchListAsync<DailyStatDto>(
                    $"SELECT {n.C("day")} FROM {table} WHERE {n.C("formId")} = @0 GROUP BY {n.C("day")}",
                    formId);
                var deleted = 0;
                foreach (var row in days)
                {
                    deleted += await db.ExecAsync(
                        $"DELETE FROM {table} WHERE {n.C("day")} = @0 AND {n.C("formId")} = @1",
                        DateTime.SpecifyKind(row.Day, DateTimeKind.Utc),
                        formId);
                }

                return deleted;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(TimeSpan keep, CancellationToken cancellationToken = default)
    {
        if (keep <= TimeSpan.Zero || keep > MaxKeep)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, $"Statistics are kept longer than 0 and at most {MaxKeep.TotalDays:0} days.");
        }

        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.DailyStat);
                var earliest = $"(SELECT MIN(q.{n.C("day")}) FROM {table} q WHERE q.{n.C("day")} < @0)";
                return db.ExecAsync(
                    $"DELETE FROM {table} WHERE {n.C("day")} = {earliest} AND {n.C("formId")} = (SELECT MIN(p.{n.C("formId")}) FROM {table} p WHERE p.{n.C("day")} = {earliest})",
                    (database.UtcNow - keep).Date);
            },
            cancellationToken);
    }

    /// <summary>Whether <paramref name="metric"/> is one camelCase word of ASCII letters and digits, at most 64 characters.</summary>
    internal static bool IsMetric(string? metric) =>
        metric is { Length: > 0 and <= MaxMetricLength } && char.IsAsciiLetterLower(metric[0]) && metric.All(char.IsAsciiLetterOrDigit);

    /// <summary>Whether <paramref name="dimension"/> is empty, or at most 128 of ASCII letters, digits, <c>_</c>, <c>.</c>, <c>:</c> and <c>-</c>.</summary>
    internal static bool IsDimension(string? dimension) =>
        dimension is { Length: <= MaxDimensionLength } && dimension.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or ':' or '-');

    // One count: added to the day's row, or the day's first row of the metric and dimension. The unit holds the form's
    // row, so no other unit adds a row of the form in between. Days are midnights, which both databases store exactly,
    // so the key compares them for equality.
    private static async Task AddAsync(IUmbracoDatabase db, SqlNames n, DateTime day, int formId, string metric, string dimension, long by)
    {
        var table = n.T(FormBuilderTables.DailyStat);
        var updated = await db.ExecAsync(
            $"UPDATE {table} SET {n.C("value")} = {n.C("value")} + @0 WHERE {n.C("day")} = @1 AND {n.C("formId")} = @2 AND {n.C("metric")} = @3 AND {n.C("dimension")} = @4",
            by,
            day,
            formId,
            metric,
            dimension);
        if (updated == 0)
        {
            await db.ExecAsync(
                $"INSERT INTO {table} ({n.C("day")}, {n.C("formId")}, {n.C("metric")}, {n.C("dimension")}, {n.C("value")}) VALUES (@0, @1, @2, @3, @4)",
                day,
                formId,
                metric,
                dimension,
                by);
        }
    }

    // The counts added up per metric and dimension, without the zeros, in ordinal order, so every unit writes one form's
    // rows in the same order.
    private static List<(string Metric, string Dimension, long By)> Merge(IReadOnlyList<DailyStatCount> counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        Require(counts.Count <= MaxCountsPerCall, $"A call adds at most {MaxCountsPerCall} counts.");
        var sums = new SortedDictionary<(string Metric, string Dimension), long>(Comparer<(string Metric, string Dimension)>.Create(
            (a, b) => string.CompareOrdinal(a.Metric, b.Metric) is var metric and not 0 ? metric : string.CompareOrdinal(a.Dimension, b.Dimension)));
        foreach (var count in counts)
        {
            Require(count is not null, "A count is not null.");
            Require(IsMetric(count!.Metric), $"A metric is one camelCase word of ASCII letters and digits, at most {MaxMetricLength} characters.");
            Require(IsDimension(count.Dimension), $"A dimension is empty, or at most {MaxDimensionLength} of ASCII letters, digits, '_', '.', ':' and '-'.");
            Require(count.By is >= 0 and <= MaxBy, $"A count adds 0 to {MaxBy}.");
            var key = (count.Metric, count.Dimension);
            sums[key] = sums.GetValueOrDefault(key) + count.By;
        }

        return sums.Where(sum => sum.Value > 0).Select(sum => (sum.Key.Metric, sum.Key.Dimension, sum.Value)).ToList();

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(counts));
            }
        }
    }

    private static DateTime Midnight(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    // Days a read covers: the last on or after the first, at most 366 of them, from 1753 on.
    private static void CheckDays(DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, "The last day comes on or after the first.");
        }

        if (to.DayNumber - from.DayNumber >= MaxListDays)
        {
            throw new ArgumentOutOfRangeException(nameof(to), to, $"A list covers at most {MaxListDays} days.");
        }

        if (Midnight(from) < FormBuilderDbTime.MinStorable)
        {
            throw new ArgumentOutOfRangeException(nameof(from), from, $"The first day lies in {FormBuilderDbTime.MinStorable.Year} or later.");
        }
    }

    [NPoco.ExplicitColumns]
    private sealed class SumRow
    {
        [NPoco.Column("formId")]
        public int FormId { get; set; }

        [NPoco.Column("metric")]
        public string Metric { get; set; } = string.Empty;

        [NPoco.Column("total")]
        public long Total { get; set; }
    }
}
