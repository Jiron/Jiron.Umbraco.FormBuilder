using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The trash and the permanent delete (ADR-0047): a form in the trash carries the key of the trash operation that put
// it there, its own or its folder's, so a folder's restore brings back exactly what went with it.
internal sealed partial class FormRepository
{
    /// <inheritdoc />
    public Task<StoredForm?> TrashAsync(int formId, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return ChangeAsync(
                    db,
                    n,
                    formId,
                    expectedConcurrencyVersion,
                    $"{n.C("trashedUtc")} = @0, {n.C("trashKey")} = @4",
                    $"{n.C("trashedUtc")} IS NULL",
                    userKey,
                    Guid.NewGuid());
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredForm?> RestoreAsync(int formId, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var folders = n.T(FormBuilderTables.Folder);
                var folderLives = $"EXISTS (SELECT 1 FROM {folders} p{n.LockingRead} WHERE p.{n.C("id")} = {n.T(FormBuilderTables.Form)}.{n.C("folderId")} AND p.{n.C("trashedUtc")} IS NULL)";
                return ChangeAsync(
                    db,
                    n,
                    formId,
                    expectedConcurrencyVersion,
                    $"{n.C("trashedUtc")} = NULL, {n.C("trashKey")} = NULL, {n.C("folderId")} = CASE WHEN {folderLives} THEN {n.C("folderId")} ELSE NULL END",
                    $"{n.C("trashedUtc")} IS NOT NULL",
                    userKey);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredForm?> HoldTrashedAsync(int formId, int expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var held = await RowHolds.HoldAsync(
                    db,
                    n,
                    FormBuilderTables.Form,
                    $"{n.C("id")} = @0 AND {n.C("concurrencyVersion")} = @1 AND {n.C("trashedUtc")} IS NOT NULL",
                    formId,
                    expectedConcurrencyVersion);
                var current = await FindAsync(db, "id", formId);
                if (held == 0 && current is not null)
                {
                    CheckFresh(current, expectedConcurrencyVersion);
                    CheckTrashed(current);
                }

                return current;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int formId, int expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Form);
                var noEntries = $"NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Entry)} e{n.LockingRead} WHERE e.{n.C("formId")} = @0)";

                // The form's row points at two of its versions, which the delete cascades to: let go of them first.
                var released = await db.ExecAsync(
                    $"UPDATE {table} SET {n.C("draftVersionId")} = NULL, {n.C("publishedVersionId")} = NULL WHERE {n.C("id")} = @0 AND {n.C("concurrencyVersion")} = @1 AND {n.C("trashedUtc")} IS NOT NULL AND {noEntries}",
                    formId,
                    expectedConcurrencyVersion);
                if (released == 0)
                {
                    if (await FindAsync(db, "id", formId) is not { } current)
                    {
                        return false;
                    }

                    CheckFresh(current, expectedConcurrencyVersion);
                    CheckTrashed(current);
                    throw new FormBuilderConflictException(FormBuilderConflict.InUse, "The form still has entries.");
                }

                // The release holds the form's row and the range of its entries, so the delete finds it. Should it not,
                // the unit rolls back, rather than leave a form in the trash without its versions.
                if (await db.ExecAsync($"DELETE FROM {table} WHERE {n.C("id")} = @0 AND {noEntries}", formId) != 1)
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.InUse, "The form still has entries.");
                }

                return true;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredForm>> TrashInFoldersAsync(IReadOnlyCollection<int> folderIds, Guid trashKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        var ids = FolderIds(folderIds);
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredForm>>([]);
        }

        return database.WriteAsync<IReadOnlyList<StoredForm>>(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Form);
                var now = database.UtcNow;
                var trashed = new List<StoredForm>();
                foreach (var chunk in ids.Chunk(SqlArgs.MaxListLength))
                {
                    await db.ExecAsync(
                        $"UPDATE {table} SET {n.C("trashedUtc")} = @0, {n.C("trashKey")} = @1, {Raise(n)}, {n.C("updatedUtc")} = @0, {n.C("updatedByUserKey")} = @2 WHERE {n.C("trashedUtc")} IS NULL AND {n.C("folderId")} IN ({SqlArgs.Placeholders(3, chunk.Length)})",
                        [now, trashKey, userKey, .. chunk.Cast<object?>()]);

                    // The key is new, so the forms that carry it in these folders are exactly those this statement trashed.
                    var rows = await db.FetchListAsync<FormDto>(
                        $"SELECT * FROM {table} WHERE {n.C("trashKey")} = @0 AND {n.C("folderId")} IN ({SqlArgs.Placeholders(1, chunk.Length)}) ORDER BY {n.C("name")}, {n.C("id")}",
                        [trashKey, .. chunk.Cast<object?>()]);
                    trashed.AddRange(rows.Select(FromDto));
                }

                return trashed;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredForm>> RestoreInFoldersAsync(IReadOnlyCollection<int> folderIds, Guid trashKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        var ids = FolderIds(folderIds);
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredForm>>([]);
        }

        return database.WriteAsync<IReadOnlyList<StoredForm>>(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Form);
                var restored = new List<StoredForm>();
                foreach (var chunk in ids.Chunk(SqlArgs.MaxListLength))
                {
                    // Out of the trash first, with the key kept: outside the trash no other form carries a key, so the
                    // forms with it that are outside the trash now are exactly those this statement brought back.
                    var keyed = $"{n.C("trashKey")} = @0 AND {n.C("folderId")} IN ({SqlArgs.Placeholders(1, chunk.Length)})";
                    object?[] args = [trashKey, .. chunk.Cast<object?>()];
                    await db.ExecAsync(
                        $"UPDATE {table} SET {n.C("trashedUtc")} = NULL, {Raise(n)}, {n.C("updatedUtc")} = @{chunk.Length + 1}, {n.C("updatedByUserKey")} = @{chunk.Length + 2} WHERE {keyed} AND {n.C("trashedUtc")} IS NOT NULL",
                        [.. args, database.UtcNow, userKey]);
                    var rows = await db.FetchListAsync<FormDto>(
                        $"SELECT * FROM {table} WHERE {keyed} AND {n.C("trashedUtc")} IS NULL ORDER BY {n.C("name")}, {n.C("id")}",
                        args);
                    await db.ExecAsync($"UPDATE {table} SET {n.C("trashKey")} = NULL WHERE {keyed} AND {n.C("trashedUtc")} IS NULL", args);
                    restored.AddRange(rows.Select(row => FromDto(row) with { TrashKey = null }));
                }

                return restored;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredForm>> ListInFoldersAsync(IReadOnlyCollection<int> folderIds, CancellationToken cancellationToken = default)
    {
        var ids = FolderIds(folderIds);
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredForm>>([]);
        }

        return database.ReadAsync<IReadOnlyList<StoredForm>>(
            async db =>
            {
                var n = new SqlNames(db);
                var found = new List<StoredForm>();
                foreach (var chunk in ids.Chunk(SqlArgs.MaxListLength))
                {
                    var rows = await db.FetchListAsync<FormDto>(
                        $"SELECT * FROM {n.T(FormBuilderTables.Form)} WHERE {n.C("folderId")} IN ({SqlArgs.Placeholders(0, chunk.Length)}) ORDER BY {n.C("name")}, {n.C("id")}",
                        [.. chunk.Cast<object?>()]);
                    found.AddRange(rows.Select(FromDto));
                }

                return found;
            },
            cancellationToken);
    }

    private static int[] FolderIds(IReadOnlyCollection<int> folderIds)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        return folderIds.Distinct().Order().ToArray();
    }

    private static void CheckTrashed(StoredForm current)
    {
        if (current.TrashedUtc is null)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.NotTrashed, "Only a form in the trash is deleted; put it in the trash first.");
        }
    }
}
