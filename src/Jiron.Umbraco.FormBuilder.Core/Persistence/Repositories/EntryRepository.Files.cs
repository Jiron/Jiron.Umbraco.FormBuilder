using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The rows of uploaded files (ADR-0006). A file is recorded when it is uploaded, waits without an entry, and is taken by
// the entry that names it; the bytes are the file store's (4.3f).
internal sealed partial class EntryRepository
{
    /// <summary>The longest original file name the column holds.</summary>
    public const int MaxOriginalNameLength = 255;

    /// <summary>The longest content type the column holds.</summary>
    public const int MaxContentTypeLength = 255;

    /// <summary>The longest scan state the column holds.</summary>
    public const int MaxScanStateLength = 16;

    /// <summary>The most orphans one list returns.</summary>
    public const int MaxOrphansPerList = 1000;

    /// <inheritdoc />
    public Task<StoredEntryFile?> AddFileAsync(NewEntryFile file, CancellationToken cancellationToken = default)
    {
        CheckFile(file);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.EntryFile);
                int inserted;
                try
                {
                    inserted = await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("key")}, {n.C("entryId")}, {n.C("formId")}, {n.C("fieldKey")}, {n.C("originalName")}, {n.C("storedName")}, {n.C("contentType")}, {n.C("sizeBytes")}, {n.C("fileHash")}, {n.C("scanState")}, {n.C("createdUtc")})
                        SELECT @0, NULL, @1, @2, @3, @4, @5, @6, @7, @8, @9
                        WHERE EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)}{n.LockingRead} WHERE {n.C("id")} = @1)
                        AND NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("key")} = @0)
                        """,
                        file.Key,
                        file.FormId,
                        file.FieldKey,
                        file.OriginalName,
                        file.StoredName,
                        file.ContentType,
                        file.SizeBytes,
                        file.FileHash,
                        file.ScanState,
                        database.UtcNow);
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "Another file already has this key.", exception);
                }

                if (inserted == 1)
                {
                    return await FindFileAsync(db, n, $"{n.C("key")} = @0", file.Key);
                }

                // Nothing inserted: the form does not exist, or another file has the key.
                return await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {n.T(FormBuilderTables.Form)} WHERE {n.C("id")} = @0", file.FormId) == 0
                    ? null
                    : throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "Another file already has this key.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEntryFile?> GetFileAsync(Guid key, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return FindFileAsync(db, n, $"{n.C("key")} = @0", key);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryFile>> ListFilesAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredEntryFile>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EntryFileDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("entryId")} = @0 ORDER BY {n.C("createdUtc")}, {n.C("id")}",
                    entryId);
                return rows.Select(FileFromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryFile>> ListOrphanFilesAsync(DateTimeOffset createdBefore, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxOrphansPerList);
        return database.ReadAsync<IReadOnlyList<StoredEntryFile>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EntryFileDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("entryId")} IS NULL AND {n.C("createdUtc")} < @0 ORDER BY {n.C("createdUtc")}, {n.C("id")} {n.Page(0, take)}",
                    FormBuilderDbTime.Write(createdBefore));
                return rows.Select(FileFromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEntryFile?> ChangeScanStateAsync(int fileId, string expected, string scanState, CancellationToken cancellationToken = default)
    {
        CheckScanState(expected, nameof(expected));
        CheckScanState(scanState, nameof(scanState));
        if (string.Equals(expected, scanState, StringComparison.Ordinal))
        {
            // Nothing to change, so nothing to write: the file as it is, while its scan is still where it was seen.
            return database.ReadAsync(
                async db =>
                {
                    var n = new SqlNames(db);
                    var current = await FindFileAsync(db, n, $"{n.C("id")} = @0", fileId);
                    CheckScanState(current, expected);
                    return current;
                },
                cancellationToken);
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var changed = await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.EntryFile)} SET {n.C("scanState")} = @0 WHERE {n.C("id")} = @1 AND {n.C("scanState")} = @2",
                    scanState,
                    fileId,
                    expected);
                var current = await FindFileAsync(db, n, $"{n.C("id")} = @0", fileId);
                if (changed == 0 && current is not null)
                {
                    // Not where it was seen when the update ran, whatever it is now: it may have come back since.
                    throw new FormBuilderConcurrencyException($"The file's scan moved on meanwhile (it was no longer {expected}).");
                }

                return current;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteFileAsync(int fileId, long? entryId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.EntryFile);
                var owner = entryId is null ? $"{n.C("entryId")} IS NULL" : $"{n.C("entryId")} = @1";
                object?[] args = entryId is null ? [fileId] : [fileId, entryId];
                var deleted = await db.ExecAsync(
                    $"DELETE FROM {table} WHERE {n.C("id")} = @0 AND {owner} AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.EntryValue)} v{n.LockingRead} WHERE v.{n.C("fileId")} = @0)",
                    args);
                if (deleted == 1)
                {
                    return true;
                }

                var current = await FindFileAsync(db, n, $"{n.C("id")} = @0", fileId);
                if (current is null)
                {
                    return false;
                }

                return current.EntryId != entryId
                    ? throw new FormBuilderConcurrencyException(entryId is null ? "An entry took the file meanwhile." : "The file belongs to another entry.")
                    : throw new FormBuilderConflictException(FormBuilderConflict.InUse, "An entry value still names this file.");
            },
            cancellationToken);

    internal static StoredEntryFile FileFromDto(EntryFileDto d) => new(
        d.Id,
        d.Key,
        d.EntryId,
        d.FormId,
        d.FieldKey,
        d.OriginalName,
        d.StoredName,
        d.ContentType,
        d.SizeBytes,
        d.FileHash,
        d.ScanState,
        FormBuilderDbTime.Read(d.CreatedUtc));

    private static async Task<StoredEntryFile?> FindFileAsync(IUmbracoDatabase db, SqlNames n, string where, params object?[] args)
    {
        var row = await db.FirstOrNullAsync<EntryFileDto>($"SELECT * FROM {n.T(FormBuilderTables.EntryFile)} WHERE {where}", args);
        return row is null ? null : FileFromDto(row);
    }

    /// <summary>
    /// Gives the new entry its files: only uploads of its form that no entry took yet. Refused with
    /// <see cref="FormBuilderConflict.MissingFile"/> when any of them is not one.
    /// </summary>
    private static async Task TakeFilesAsync(IUmbracoDatabase db, SqlNames n, long entryId, int formId, IReadOnlyList<int> fileIds)
    {
        var taken = 0;
        foreach (var chunk in fileIds.Chunk(SqlArgs.MaxListLength))
        {
            taken += await db.ExecAsync(
                $"UPDATE {n.T(FormBuilderTables.EntryFile)} SET {n.C("entryId")} = @0 WHERE {n.C("formId")} = @1 AND {n.C("entryId")} IS NULL AND {n.C("id")} IN ({SqlArgs.Placeholders(2, chunk.Length)})",
                [entryId, formId, .. chunk.Cast<object?>()]);
        }

        if (taken != fileIds.Count)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.MissingFile, "A file of the entry does not exist, belongs to another form, or belongs to another entry already.");
        }
    }

    private static void CheckScanState(StoredEntryFile? current, string expected)
    {
        if (current is not null && !string.Equals(current.ScanState, expected, StringComparison.Ordinal))
        {
            throw new FormBuilderConcurrencyException($"The file's scan moved on meanwhile (it is {current.ScanState}, not {expected}).");
        }
    }

    private static void CheckScanState(string scanState, string parameter)
    {
        ArgumentNullException.ThrowIfNull(scanState, parameter);
        if (scanState.Length is 0 or > MaxScanStateLength || !scanState.All(char.IsAsciiLetter))
        {
            throw new ArgumentException($"A scan state is 1 to {MaxScanStateLength} ASCII letters.", parameter);
        }
    }

    private static void CheckFile(NewEntryFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Require(file.OriginalName is not null && file.OriginalName.Length is > 0 and <= MaxOriginalNameLength, $"A file's original name holds 1 to {MaxOriginalNameLength} characters.");
        Require(file.StoredName is not null && IsLowerHex(file.StoredName, 32), "A file's stored name is 32 lowercase hex characters.");
        Require(file.ContentType is not null && file.ContentType.Length is > 0 and <= MaxContentTypeLength, $"A file's content type holds 1 to {MaxContentTypeLength} characters.");
        Require(file.SizeBytes >= 0, "A file's size is 0 bytes or more.");
        Require(file.FileHash is not null && IsLowerHex(file.FileHash, 64), "A file's SHA-256 is 64 lowercase hex characters.");
        CheckScanState(file.ScanState, nameof(file));

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(file));
            }
        }
    }
}
