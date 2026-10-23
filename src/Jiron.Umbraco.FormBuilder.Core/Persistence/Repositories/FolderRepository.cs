using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A folder of the form library.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">The folder's key.</param>
/// <param name="ParentId">The folder it is in, or null at the root.</param>
/// <param name="Name">Its name.</param>
/// <param name="SortOrder">Its place among its siblings.</param>
/// <param name="IsRestricted">Whether only the grants on it and below count below it (ADR-0013).</param>
/// <param name="TrashedUtc">When it went to the trash, or null.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
/// <param name="CreatedByUserKey">Who created it, or null when Jiron Form Builder did.</param>
/// <param name="UpdatedByUserKey">Who changed it last, or null when Jiron Form Builder did.</param>
internal sealed record StoredFolder(
    int Id,
    Guid Key,
    int? ParentId,
    string Name,
    int SortOrder,
    bool IsRestricted,
    DateTimeOffset? TrashedUtc,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? CreatedByUserKey,
    Guid? UpdatedByUserKey);

/// <summary>A folder to create.</summary>
/// <param name="Key">Its key.</param>
/// <param name="ParentId">The folder to create it in, or null for the root.</param>
/// <param name="Name">Its name, 1 to 255 characters.</param>
/// <param name="SortOrder">Its place among its siblings.</param>
/// <param name="IsRestricted">Whether it is restricted (ADR-0013).</param>
/// <param name="UserKey">Who creates it, or null when Jiron Form Builder does.</param>
internal sealed record NewFolder(Guid Key, int? ParentId, string Name, int SortOrder = 0, bool IsRestricted = false, Guid? UserKey = null);

/// <summary>
/// The folders of the form library in <c>jfbFolder</c>. A change that the tree's state refuses throws a
/// <see cref="FormBuilderConflictException"/> and writes nothing; a missing folder gives null or false.
/// </summary>
internal interface IFolderRepository
{
    /// <summary>
    /// Creates a folder. Refused with <see cref="FormBuilderConflict.DuplicateKey"/>,
    /// <see cref="FormBuilderConflict.MissingFolder"/> (the parent) or <see cref="FormBuilderConflict.FolderTooDeep"/>.
    /// </summary>
    Task<StoredFolder> CreateAsync(NewFolder folder, CancellationToken cancellationToken = default);

    /// <summary>A folder by key, or null.</summary>
    Task<StoredFolder?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>A folder by id, or null.</summary>
    Task<StoredFolder?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>The folders directly in <paramref name="parentId"/> (null: the root), in the trash or not, in sort order.</summary>
    Task<IReadOnlyList<StoredFolder>> ListChildrenAsync(int? parentId, bool trashed, CancellationToken cancellationToken = default);

    /// <summary>Every folder, for the tree and the permission checks (a library holds few folders).</summary>
    Task<IReadOnlyList<StoredFolder>> ListAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Renames, reorders or restricts a folder. Null when it does not exist.</summary>
    Task<StoredFolder?> UpdateAsync(int id, string name, int sortOrder, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a folder into <paramref name="parentId"/> (null: the root), with everything in it. Null when it does not
    /// exist. Refused with <see cref="FormBuilderConflict.MissingFolder"/>, <see cref="FormBuilderConflict.FolderIntoItself"/>
    /// or <see cref="FormBuilderConflict.FolderTooDeep"/>.
    /// </summary>
    Task<StoredFolder?> MoveAsync(int id, int? parentId, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>Puts a folder in the trash; a trashed one stays as it is. Null when it does not exist.</summary>
    Task<StoredFolder?> TrashAsync(int id, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a folder out of the trash, back into its parent, or to the root when the parent is in the trash itself.
    /// A folder outside the trash stays as it is. Null when it does not exist.
    /// </summary>
    Task<StoredFolder?> RestoreAsync(int id, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an empty folder. False when it does not exist. Refused with <see cref="FormBuilderConflict.FolderNotEmpty"/>
    /// while it holds folders or forms.
    /// </summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class FolderRepository(FormBuilderDatabase database) : IFolderRepository
{
    /// <summary>
    /// The most levels the tree may have, a folder at the root being on level 1. It bounds the walks up and down the
    /// tree, so they stay far below SQL Server's limit of 100 recursions.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>The longest name the column holds.</summary>
    public const int MaxNameLength = 255;

    /// <inheritdoc />
    public Task<StoredFolder> CreateAsync(NewFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        CheckName(folder.Name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Folder);
                var now = database.UtcNow;
                var columns = $"{n.C("key")}, {n.C("parentId")}, {n.C("name")}, {n.C("sortOrder")}, {n.C("isRestricted")}, {n.C("createdUtc")}, {n.C("updatedUtc")}, {n.C("createdByUserKey")}, {n.C("updatedByUserKey")}";
                var values = "@0, @1, @2, @3, @4, @5, @5, @6, @6";
                var newKey = $"NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("key")} = @0)";
                object?[] args = [folder.Key, folder.ParentId, folder.Name, folder.SortOrder, folder.IsRestricted, now, folder.UserKey];

                // One statement: the parent must exist and leave room for one more level (ADR-0036).
                var sql = folder.ParentId is null
                    ? $"INSERT INTO {table} ({columns}) SELECT {values} WHERE {newKey}"
                    : $"{n.WithRecursive} {Up(n, "@1")} INSERT INTO {table} ({columns}) SELECT {values} WHERE {newKey} AND EXISTS (SELECT 1 FROM up WHERE id = @1) AND (SELECT MAX(depth) FROM up) < {MaxDepth}";
                if (await db.ExecAsync(sql, args) == 0)
                {
                    throw await RefusedCreateAsync(db, n, folder);
                }

                return FromDto((await db.FirstOrNullAsync<FolderDto>($"SELECT * FROM {table} WHERE {n.C("key")} = @0", folder.Key))!);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredFolder?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, "key", key), cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, "id", id), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredFolder>> ListChildrenAsync(int? parentId, bool trashed, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredFolder>>(
            async db =>
            {
                var n = new SqlNames(db);
                var parent = parentId is null ? $"{n.C("parentId")} IS NULL" : $"{n.C("parentId")} = @0";
                var trash = trashed ? "IS NOT NULL" : "IS NULL";
                var rows = await db.FetchListAsync<FolderDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Folder)} WHERE {parent} AND {n.C("trashedUtc")} {trash} ORDER BY {n.C("sortOrder")}, {n.C("name")}, {n.C("id")}",
                    parentId is null ? [] : [parentId]);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredFolder>> ListAllAsync(CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredFolder>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<FolderDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Folder)} ORDER BY {n.C("sortOrder")}, {n.C("name")}, {n.C("id")}");
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> UpdateAsync(int id, string name, int sortOrder, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default)
    {
        CheckName(name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Folder)} SET {n.C("name")} = @0, {n.C("sortOrder")} = @1, {n.C("isRestricted")} = @2, {n.C("updatedUtc")} = @3, {n.C("updatedByUserKey")} = @4 WHERE {n.C("id")} = @5",
                    name,
                    sortOrder,
                    isRestricted,
                    database.UtcNow,
                    userKey,
                    id);
                return await FindAsync(db, "id", id);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredFolder?> MoveAsync(int id, int? parentId, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Folder);
                var set = $"UPDATE {table} SET {n.C("parentId")} = @0, {n.C("updatedUtc")} = @1, {n.C("updatedByUserKey")} = @2 WHERE {n.C("id")} = @3";

                // One statement (ADR-0036): the parent exists, is neither the folder nor below it, and the folder's
                // deepest level stays within MaxDepth. The walks lock what they read on SQL Server, so two moves that
                // would make a ring together wait for each other instead.
                var sql = parentId is null
                    ? $"{n.WithRecursive} {Down(n)} {set} AND (SELECT MAX(depth) FROM down) <= {MaxDepth}"
                    : $"{n.WithRecursive} {Up(n, "@0")}, {Down(n)} {set} AND EXISTS (SELECT 1 FROM up WHERE id = @0) AND NOT EXISTS (SELECT 1 FROM up WHERE id = @3) AND (SELECT MAX(depth) FROM up) + (SELECT MAX(depth) FROM down) <= {MaxDepth}";
                if (await db.ExecAsync(sql, parentId, database.UtcNow, userKey, id) == 1)
                {
                    return await FindAsync(db, "id", id);
                }

                return await FindAsync(db, "id", id) is null ? null : throw await RefusedMoveAsync(db, n, id, parentId);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> TrashAsync(int id, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Folder)} SET {n.C("trashedUtc")} = @0, {n.C("updatedUtc")} = @0, {n.C("updatedByUserKey")} = @1 WHERE {n.C("id")} = @2 AND {n.C("trashedUtc")} IS NULL",
                    database.UtcNow,
                    userKey,
                    id);
                return await FindAsync(db, "id", id);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> RestoreAsync(int id, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Folder);
                var parentLives = $"EXISTS (SELECT 1 FROM {table} p WHERE p.{n.C("id")} = {table}.{n.C("parentId")} AND p.{n.C("trashedUtc")} IS NULL)";
                await db.ExecAsync(
                    $"UPDATE {table} SET {n.C("trashedUtc")} = NULL, {n.C("parentId")} = CASE WHEN {parentLives} THEN {n.C("parentId")} ELSE NULL END, {n.C("updatedUtc")} = @0, {n.C("updatedByUserKey")} = @1 WHERE {n.C("id")} = @2 AND {n.C("trashedUtc")} IS NOT NULL",
                    database.UtcNow,
                    userKey,
                    id);
                return await FindAsync(db, "id", id);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Folder);
                var deleted = await db.ExecAsync(
                    $"DELETE FROM {table} WHERE {n.C("id")} = @0 AND NOT EXISTS (SELECT 1 FROM {table} c WHERE c.{n.C("parentId")} = @0) AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)} f WHERE f.{n.C("folderId")} = @0)",
                    id);
                if (deleted == 1)
                {
                    return true;
                }

                return await FindAsync(db, "id", id) is null
                    ? false
                    : throw new FormBuilderConflictException(FormBuilderConflict.FolderNotEmpty, "The folder still holds folders or forms.");
            },
            cancellationToken);

    internal static StoredFolder FromDto(FolderDto d) => new(
        d.Id,
        d.Key,
        d.ParentId,
        d.Name,
        d.SortOrder,
        d.IsRestricted,
        FormBuilderDbTime.Read(d.TrashedUtc),
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.UpdatedUtc),
        d.CreatedByUserKey,
        d.UpdatedByUserKey);

    // The parent (the parameter named by parent) and every folder above it, each with its level counted from the
    // parent: the deepest row's depth is the parent's level.
    private static string Up(SqlNames n, string parent) =>
        $"up(id, parentId, depth) AS (SELECT {n.C("id")}, {n.C("parentId")}, 1 FROM {n.T(FormBuilderTables.Folder)}{n.InsertOnceHint} WHERE {n.C("id")} = {parent} "
        + $"UNION ALL SELECT f.{n.C("id")}, f.{n.C("parentId")}, up.depth + 1 FROM {n.T(FormBuilderTables.Folder)} f{n.InsertOnceHint} JOIN up ON f.{n.C("id")} = up.parentId WHERE up.depth <= {MaxDepth})";

    // The moved folder (@3) and every folder below it: the deepest row's depth is the height of the moved subtree.
    private static string Down(SqlNames n) =>
        $"down(id, depth) AS (SELECT {n.C("id")}, 1 FROM {n.T(FormBuilderTables.Folder)}{n.InsertOnceHint} WHERE {n.C("id")} = @3 "
        + $"UNION ALL SELECT f.{n.C("id")}, down.depth + 1 FROM {n.T(FormBuilderTables.Folder)} f{n.InsertOnceHint} JOIN down ON f.{n.C("parentId")} = down.id WHERE down.depth <= {MaxDepth})";

    private static async Task<StoredFolder?> FindAsync(IUmbracoDatabase db, string column, object value)
    {
        var n = new SqlNames(db);
        var row = await db.FirstOrNullAsync<FolderDto>($"SELECT * FROM {n.T(FormBuilderTables.Folder)} WHERE {n.C(column)} = @0", value);
        return row is null ? null : FromDto(row);
    }

    // After a refused write: which condition refused it. A library holds few folders, so the tree is read whole.
    private static async Task<FormBuilderConflictException> RefusedCreateAsync(IUmbracoDatabase db, SqlNames n, NewFolder folder)
    {
        if (await FindAsync(db, "key", folder.Key) is not null)
        {
            return new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "Another folder already has this key.");
        }

        var parents = await ParentsAsync(db, n);
        return parents.ContainsKey(folder.ParentId!.Value)
            ? new FormBuilderConflictException(FormBuilderConflict.FolderTooDeep, $"A folder can be at most {MaxDepth} levels deep.")
            : new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The parent folder does not exist.");
    }

    private static async Task<FormBuilderConflictException> RefusedMoveAsync(IUmbracoDatabase db, SqlNames n, int id, int? parentId)
    {
        var parents = await ParentsAsync(db, n);
        if (parentId is { } parent)
        {
            if (!parents.ContainsKey(parent))
            {
                return new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The target folder does not exist.");
            }

            int? current = parent;
            for (var steps = 0; current is { } at && steps <= MaxDepth; steps++)
            {
                if (at == id)
                {
                    return new FormBuilderConflictException(FormBuilderConflict.FolderIntoItself, "A folder cannot move into itself or into a folder below it.");
                }

                current = parents.GetValueOrDefault(at);
            }
        }

        return new FormBuilderConflictException(FormBuilderConflict.FolderTooDeep, $"A folder can be at most {MaxDepth} levels deep.");
    }

    private static async Task<Dictionary<int, int?>> ParentsAsync(IUmbracoDatabase db, SqlNames n)
    {
        var rows = await db.FetchListAsync<FolderDto>($"SELECT {n.C("id")}, {n.C("parentId")} FROM {n.T(FormBuilderTables.Folder)}");
        return rows.ToDictionary(row => row.Id, row => row.ParentId);
    }

    private static void CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"A folder name holds 1 to {MaxNameLength} characters.", nameof(name));
        }
    }
}
