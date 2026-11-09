using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// What retention reads and writes of the entries (ADR-0222): the time each one goes, the entries whose time has come, the
// legal holds, the counts of the dry run, and which stored file names a row still names.
internal sealed partial class EntryRepository
{
    /// <summary>The longest reason of a legal hold the column holds, as a hold takes it (ADR-0222).</summary>
    public const int MaxHoldReasonLength = 500;

    /// <summary>The most entries one statement gives their times, so it stays within SQL Server's 2,100 parameters.</summary>
    private const int TimesPerStatement = 300;

    /// <inheritdoc />
    /// <remarks>One statement for every <see cref="TimesPerStatement"/> entries, each time picked by the entry's id.</remarks>
    public Task<int> SetPurgeAfterAsync(IReadOnlyCollection<EntryPurgeTime> times, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(times);
        if (times.Count == 0)
        {
            return Task.FromResult(0);
        }

        CheckTake(times.Count);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var changed = 0;
                foreach (var chunk in times.DistinctBy(time => time.EntryId).Chunk(TimesPerStatement))
                {
                    var a = new SqlArgs();
                    var cases = string.Join(" ", chunk.Select(time => $"WHEN {a.Add(time.EntryId)} THEN {a.Add(FormBuilderDbTime.Write(time.PurgeAfterUtc))}"));
                    changed += await db.ExecAsync(
                        $"UPDATE {n.T(FormBuilderTables.Entry)} SET {n.C("purgeAfterUtc")} = CASE {n.C("id")} {cases} END WHERE {n.C("id")} IN ({a.List(chunk.Select(time => (object?)time.EntryId))})",
                        a.Values);
                }

                return changed;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>On the index of the times, soonest first; a hold that lasts past <paramref name="now"/> keeps its entry out.</remarks>
    public Task<IReadOnlyList<EntryRetentionRow>> ListDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default)
    {
        CheckTake(take);
        return database.ReadAsync<IReadOnlyList<EntryRetentionRow>>(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var at = a.Add(FormBuilderDbTime.Write(now));
                var rows = await db.FetchListAsync<RetentionDto>(
                    $"SELECT {RetentionColumns(n)} FROM {n.T(FormBuilderTables.Entry)} "
                    + $"WHERE {n.C("purgeAfterUtc")} <= {at} AND ({n.C("holdUntilUtc")} IS NULL OR {n.C("holdUntilUtc")} <= {at}) AND {KeptStates(n, a)} "
                    + $"ORDER BY {n.C("purgeAfterUtc")}, {n.C("id")} {n.Page(0, take)}",
                    a.Values);
                return rows.Select(RetentionFromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntryRetentionRow>> ListForRetentionAsync(int formId, long afterId, int take, CancellationToken cancellationToken = default)
    {
        CheckTake(take);
        return database.ReadAsync<IReadOnlyList<EntryRetentionRow>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<RetentionDto>(
                    $"SELECT {RetentionColumns(n)} FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("formId")} = @0 AND {n.C("id")} > @1 ORDER BY {n.C("id")} {n.Page(0, take)}",
                    formId,
                    afterId);
                return rows.Select(RetentionFromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EntryRetentionRow>> ListForRetentionAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        var ids = Ids(entryIds);
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<EntryRetentionRow>>([]);
        }

        CheckTake(ids.Length);
        return database.ReadAsync<IReadOnlyList<EntryRetentionRow>>(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var rows = await db.FetchListAsync<RetentionDto>(
                    $"SELECT {RetentionColumns(n)} FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("id")} IN ({a.List(ids.Cast<object?>())}) ORDER BY {n.C("id")}",
                    a.Values);
                return rows.Select(RetentionFromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<long> CountOnHoldAsync(int formId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecuteScalarAsync<long>(
                    $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("formId")} = @0 AND {n.C("holdUntilUtc")} > @1",
                    formId,
                    FormBuilderDbTime.Write(now));
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, RetentionCount>> CountDueAsync(IReadOnlyCollection<int> formIds, RetentionCutoffs cutoffs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formIds);
        ArgumentNullException.ThrowIfNull(cutoffs);
        var counts = new Dictionary<int, RetentionCount>();
        foreach (var chunk in formIds.Distinct().Chunk(SqlArgs.MaxListLength))
        {
            var (rows, files) = await database.ReadAsync(
                async db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    var held = $"{n.C("holdUntilUtc")} > {a.Add(FormBuilderDbTime.Write(cutoffs.Horizon))}";
                    var reached = $"{n.C("formId")} IN ({a.List(chunk.Cast<object?>())}) AND {KeptStates(n, a)} AND ({Reached(n, a, cutoffs)})";
                    var entries = await db.FetchListAsync<DueCount>(
                        $"SELECT {n.C("formId")} AS formId, SUM(CASE WHEN {held} THEN 0 ELSE 1 END) AS due, SUM(CASE WHEN {held} THEN 1 ELSE 0 END) AS held "
                        + $"FROM {n.T(FormBuilderTables.Entry)} WHERE {reached} GROUP BY {n.C("formId")}",
                        a.Values);

                    // The files of the entries that go: those no hold keeps.
                    var going = $"SELECT {n.C("id")} FROM {n.T(FormBuilderTables.Entry)} WHERE {reached} AND ({n.C("holdUntilUtc")} IS NULL OR NOT ({held}))";
                    var stored = await db.FetchListAsync<FormCount>(
                        $"SELECT {n.C("formId")} AS formId, COUNT(*) AS total FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("entryId")} IN ({going}) GROUP BY {n.C("formId")}",
                        a.Values);
                    return (entries, stored.ToDictionary(row => row.FormId, row => row.Total));
                },
                cancellationToken);
            foreach (var row in rows)
            {
                counts[row.FormId] = new RetentionCount(row.Due, row.Held, files.GetValueOrDefault(row.FormId));
            }
        }

        return counts;
    }

    /// <inheritdoc />
    public Task<bool> SetHoldAsync(long entryId, DateTimeOffset? until, string? reason, CancellationToken cancellationToken = default)
    {
        if ((until is null) != (reason is null))
        {
            throw new ArgumentException("A hold has an end and a reason, and its release neither.", nameof(reason));
        }

        if (reason is not null && (reason.Length is 0 or > MaxHoldReasonLength))
        {
            throw new ArgumentException($"A hold's reason has 1 to {MaxHoldReasonLength} characters.", nameof(reason));
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Entry)} SET {n.C("holdUntilUtc")} = @0, {n.C("holdReason")} = @1 WHERE {n.C("id")} = @2",
                    FormBuilderDbTime.Write(until),
                    reason,
                    entryId) == 1;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>On the index of the stored names (ADR-0222), one statement for every <see cref="SqlArgs.MaxListLength"/> names.</remarks>
    public async Task<IReadOnlySet<string>> ListStoredNamesAsync(IReadOnlyCollection<string> storedNames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storedNames);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in storedNames.Distinct(StringComparer.Ordinal).Chunk(SqlArgs.MaxListLength))
        {
            found.UnionWith(await database.ReadAsync(
                db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    return db.FetchListAsync<string>(
                        $"SELECT {n.C("storedName")} FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("storedName")} IN ({a.List(chunk.Cast<object?>())})",
                        a.Values);
                },
                cancellationToken));
        }

        return found;
    }

    // The columns retention reads of an entry, never its values or search text.
    private static readonly IReadOnlyList<string> RetentionColumnNames = ["id", "key", "formId", "number", "state", "isTest", "submittedUtc", "stateChangedUtc", "holdUntilUtc", "holdReason", "purgeAfterUtc"];

    private static string RetentionColumns(SqlNames n) => string.Join(", ", RetentionColumnNames.Select(n.C));

    // The states retention deletes, the others having times of their own.
    private static string KeptStates(SqlNames n, SqlArgs a) =>
        $"{n.C("state")} IN ({a.List(RetentionStates.Kept.Select(state => (object?)StoredNames.Of(state)))})";

    // Whether an entry's time has come by the dry run's horizon: its state's days since its last change of state, a test
    // entry's days, or the form's days since its submission.
    private static string Reached(SqlNames n, SqlArgs a, RetentionCutoffs cutoffs)
    {
        var real = new List<string>();
        foreach (var (state, cutoff) in new[] { (EntryState.Submitted, cutoffs.Submitted), (EntryState.Approved, cutoffs.Approved), (EntryState.Rejected, cutoffs.Rejected), (EntryState.Spam, cutoffs.Spam) })
        {
            if (cutoff is { } before)
            {
                real.Add($"({n.C("state")} = {a.Add(StoredNames.Of(state))} AND {n.C("stateChangedUtc")} <= {a.Add(FormBuilderDbTime.Write(before))})");
            }
        }

        var reached = new List<string>
        {
            $"({n.C("isTest")} = {a.Add(true)} AND {n.C("stateChangedUtc")} <= {a.Add(FormBuilderDbTime.Write(cutoffs.Test))})",
        };
        if (real.Count > 0)
        {
            reached.Add($"({n.C("isTest")} = {a.Add(false)} AND ({string.Join(" OR ", real)}))");
        }

        if (cutoffs.Submission is { } submitted)
        {
            reached.Add($"{n.C("submittedUtc")} <= {a.Add(FormBuilderDbTime.Write(submitted))}");
        }

        return string.Join(" OR ", reached);
    }

    private static EntryRetentionRow RetentionFromDto(RetentionDto d) => new(
        d.Id,
        d.Key,
        d.FormId,
        d.Number,
        StoredNames.Parse<EntryState>(d.State),
        d.IsTest,
        FormBuilderDbTime.Read(d.SubmittedUtc),
        FormBuilderDbTime.Read(d.StateChangedUtc),
        FormBuilderDbTime.Read(d.HoldUntilUtc),
        d.HoldReason,
        FormBuilderDbTime.Read(d.PurgeAfterUtc));

    [ExplicitColumns]
    private sealed class RetentionDto
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("key")]
        public Guid Key { get; set; }

        [Column("formId")]
        public int FormId { get; set; }

        [Column("number")]
        public int Number { get; set; }

        [Column("state")]
        public string State { get; set; } = string.Empty;

        [Column("isTest")]
        public bool IsTest { get; set; }

        [Column("submittedUtc")]
        public DateTime SubmittedUtc { get; set; }

        [Column("stateChangedUtc")]
        public DateTime StateChangedUtc { get; set; }

        [Column("holdUntilUtc")]
        public DateTime? HoldUntilUtc { get; set; }

        [Column("holdReason")]
        public string? HoldReason { get; set; }

        [Column("purgeAfterUtc")]
        public DateTime? PurgeAfterUtc { get; set; }
    }

    [ExplicitColumns]
    private sealed class DueCount
    {
        [Column("formId")]
        public int FormId { get; set; }

        [Column("due")]
        public long Due { get; set; }

        [Column("held")]
        public long Held { get; set; }
    }
}
