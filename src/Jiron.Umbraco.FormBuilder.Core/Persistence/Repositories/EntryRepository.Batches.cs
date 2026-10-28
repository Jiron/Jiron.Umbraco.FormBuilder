using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The batches of a form's permanent delete (ADR-0047): the ids of its entries, their files and their delete, and the
// uploads that wait for an entry.
internal sealed partial class EntryRepository
{
    /// <inheritdoc />
    /// <remarks>By number, which the index on the form and the number serves without a sort.</remarks>
    public Task<IReadOnlyList<long>> ListIdsAsync(int formId, int take, CancellationToken cancellationToken = default)
    {
        CheckTake(take);
        return database.ReadAsync<IReadOnlyList<long>>(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.FetchListAsync<long>(
                    $"SELECT {n.C("id")} FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("formId")} = @0 ORDER BY {n.C("number")} {n.Page(0, take)}",
                    formId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryFile>> ListFilesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        var ids = Ids(entryIds);
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredEntryFile>>([]);
        }

        return database.ReadAsync<IReadOnlyList<StoredEntryFile>>(
            async db =>
            {
                var n = new SqlNames(db);
                var files = new List<StoredEntryFile>();
                foreach (var chunk in ids.Chunk(SqlArgs.MaxListLength))
                {
                    var rows = await db.FetchListAsync<EntryFileDto>(
                        $"SELECT * FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("entryId")} IN ({SqlArgs.Placeholders(0, chunk.Length)}) ORDER BY {n.C("entryId")}, {n.C("createdUtc")}, {n.C("id")}",
                        [.. chunk.Cast<object?>()]);
                    files.AddRange(rows.Select(FileFromDto));
                }

                return files;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The database's cascades remove the values, files, events, notes and action runs; the service deletes the unsent
    /// mail first (<see cref="IEmailRepository.DeleteUnsentAsync(IReadOnlyCollection{long}, CancellationToken)"/>).
    /// </remarks>
    public Task<int> DeleteAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        var ids = Ids(entryIds);
        if (ids.Length == 0)
        {
            return Task.FromResult(0);
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var deleted = 0;
                foreach (var chunk in ids.Chunk(SqlArgs.MaxListLength))
                {
                    await LockFormsAsync(db, n, chunk);
                    await AdjustPlacesAsync(db, n, chunk, add: false, holdingOnly: true);
                    deleted += await db.ExecAsync(
                        $"DELETE FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("id")} IN ({SqlArgs.Placeholders(0, chunk.Length)})",
                        [.. chunk.Cast<object?>()]);
                }

                return deleted;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryFile>> ListUploadsAsync(int formId, int take, CancellationToken cancellationToken = default)
    {
        CheckTake(take);
        return database.ReadAsync<IReadOnlyList<StoredEntryFile>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EntryFileDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("entryId")} IS NULL AND {n.C("formId")} = @0 ORDER BY {n.C("id")} {n.Page(0, take)}",
                    formId);
                return rows.Select(FileFromDto).ToList();
            },
            cancellationToken);
    }

    private static long[] Ids(IReadOnlyCollection<long> entryIds)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        return entryIds.Distinct().Order().ToArray();
    }

    private static void CheckTake(int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, SqlArgs.MaxListLength);
    }
}
