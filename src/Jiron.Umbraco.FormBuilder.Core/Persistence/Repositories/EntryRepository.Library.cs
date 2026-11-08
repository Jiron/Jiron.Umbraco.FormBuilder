using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// What the form library says of the entries of its forms (ADR-0166): the last days' entries in a folder's list, the
// tree's mark for unseen entries, and what a permanent delete takes; and what the summary email counts (ADR-0209). Each
// reads its forms in chunks the database takes.
internal sealed partial class EntryRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, long>> CountSinceAsync(IReadOnlyCollection<int> formIds, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        CountArrivedAsync(formIds, since, null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, long>> CountBetweenAsync(IReadOnlyCollection<int> formIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        CountArrivedAsync(formIds, from, to, cancellationToken);

    /// <inheritdoc />
    public async Task<long> CountWaitingAsync(IReadOnlyCollection<int> formIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formIds);
        long waiting = 0;
        foreach (var chunk in formIds.Distinct().Chunk(SqlArgs.MaxListLength))
        {
            waiting += await database.ReadAsync(
                db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    return db.ScalarAsync<long>(
                        $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.Entry)} "
                        + $"WHERE {n.C("formId")} IN ({a.List(chunk.Cast<object?>())}) AND {n.C("isTest")} = {a.Add(false)} AND {n.C("state")} = {a.Add(StoredNames.Of(EntryState.Submitted))}",
                        a.Values);
                },
                cancellationToken);
        }

        return waiting;
    }

    /// <inheritdoc />
    public async Task<MemberEntryCount> CountMembersAsync(int formId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var row = await database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                return db.FirstOrNullAsync<MemberCount>(
                    $"SELECT COUNT(DISTINCT {n.C("memberKey")}) AS members, COUNT({n.C("memberKey")}) AS entries FROM {n.T(FormBuilderTables.Entry)} "
                    + $"WHERE {n.C("formId")} = {a.Add(formId)} AND {Arrived(n, a)} AND {n.C("memberKey")} IS NOT NULL "
                    + $"AND {n.C("submittedUtc")} >= {a.Add(FormBuilderDbTime.Write(from))} AND {n.C("submittedUtc")} < {a.Add(FormBuilderDbTime.Write(to))}",
                    a.Values);
            },
            cancellationToken);
        return row is null ? default : new MemberEntryCount(row.Members, row.Entries);
    }

    // The entries that arrived in each form from a moment on, up to another when one is given.
    private async Task<IReadOnlyDictionary<int, long>> CountArrivedAsync(IReadOnlyCollection<int> formIds, DateTimeOffset from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(formIds);
        var counts = new Dictionary<int, long>();
        foreach (var chunk in formIds.Distinct().Chunk(SqlArgs.MaxListLength))
        {
            var rows = await database.ReadAsync(
                db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    var until = to is { } end ? $" AND {n.C("submittedUtc")} < {a.Add(FormBuilderDbTime.Write(end))}" : string.Empty;
                    return db.FetchListAsync<FormCount>(
                        $"SELECT {n.C("formId")} AS formId, COUNT(*) AS total FROM {n.T(FormBuilderTables.Entry)} "
                        + $"WHERE {n.C("formId")} IN ({a.List(chunk.Cast<object?>())}) AND {Arrived(n, a)} AND {n.C("submittedUtc")} >= {a.Add(FormBuilderDbTime.Write(from))}{until} "
                        + $"GROUP BY {n.C("formId")}",
                        a.Values);
                },
                cancellationToken);
            foreach (var row in rows)
            {
                counts[row.FormId] = row.Total;
            }
        }

        return counts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<int>> ListFormIdsWithUnseenAsync(IReadOnlyCollection<int> formIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formIds);
        var found = new HashSet<int>();
        foreach (var chunk in formIds.Distinct().Chunk(SqlArgs.MaxListLength))
        {
            found.UnionWith(await database.ReadAsync(
                db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    return db.FetchListAsync<int>(
                        $"SELECT DISTINCT {n.C("formId")} FROM {n.T(FormBuilderTables.Entry)} "
                        + $"WHERE {n.C("formId")} IN ({a.List(chunk.Cast<object?>())}) AND {Arrived(n, a)} AND {n.C("firstSeenUtc")} IS NULL",
                        a.Values);
                },
                cancellationToken));
        }

        return found;
    }

    /// <inheritdoc />
    /// <remarks>Newest first on the index of the unseen entries, so it reads at most <paramref name="most"/> + 1 of them.</remarks>
    public Task<int> CountUnseenAsync(FormVisibility visibility, int most, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        CheckTake(most);
        return database.ReadAsync(
            db =>
            {
                var statement = EntryQuerySql.CountUnseen(new SqlNames(db), visibility, most);
                return db.ScalarAsync<int>(statement.Text, statement.Args);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EntryHoldings> CountHeldAsync(IReadOnlyCollection<int> formIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(formIds);
        long entries = 0;
        long files = 0;
        foreach (var chunk in formIds.Distinct().Chunk(SqlArgs.MaxListLength))
        {
            (entries, files) = await database.ReadAsync(
                async db =>
                {
                    var n = new SqlNames(db);
                    var list = SqlArgs.Placeholders(0, chunk.Length);
                    var values = chunk.Cast<object>().ToArray();
                    var inEntries = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("formId")} IN ({list})", values);
                    var inFiles = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("formId")} IN ({list})", values);
                    return (entries + inEntries, files + inFiles);
                },
                cancellationToken);
        }

        return new EntryHoldings(entries, files);
    }

    // The entries the recent list holds: they have arrived, and are no test.
    private static string Arrived(SqlNames n, SqlArgs a) =>
        $"{n.C("isTest")} = {a.Add(false)} AND {n.C("state")} IN ({a.Add(StoredNames.Of(EntryState.Submitted))}, {a.Add(StoredNames.Of(EntryState.Approved))}, {a.Add(StoredNames.Of(EntryState.Rejected))})";

    [ExplicitColumns]
    private sealed class FormCount
    {
        [Column("formId")]
        public int FormId { get; set; }

        [Column("total")]
        public long Total { get; set; }
    }

    private sealed class MemberCount
    {
        [Column("members")]
        public long Members { get; set; }

        [Column("entries")]
        public long Entries { get; set; }
    }
}
