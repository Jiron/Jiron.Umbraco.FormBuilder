using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// What the form library says of the entries of its forms (ADR-0166): the last days' entries in a folder's list, the
// tree's mark for unseen entries, and what a permanent delete takes. Each reads its forms in chunks the database takes.
internal sealed partial class EntryRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, long>> CountSinceAsync(IReadOnlyCollection<int> formIds, DateTimeOffset since, CancellationToken cancellationToken = default)
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
                    return db.FetchListAsync<FormCount>(
                        $"SELECT {n.C("formId")} AS formId, COUNT(*) AS total FROM {n.T(FormBuilderTables.Entry)} "
                        + $"WHERE {n.C("formId")} IN ({a.List(chunk.Cast<object?>())}) AND {Arrived(n, a)} AND {n.C("submittedUtc")} >= {a.Add(FormBuilderDbTime.Write(since))} "
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
}
