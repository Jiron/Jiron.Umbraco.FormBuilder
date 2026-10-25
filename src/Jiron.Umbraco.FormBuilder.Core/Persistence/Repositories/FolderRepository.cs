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
/// <param name="TrashKey">The trash operation that put it there, the same for everything that went with it (ADR-0047), or null.</param>
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
    Guid? TrashKey,
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

/// <summary>What a folder's trash or restore changed (ADR-0047).</summary>
/// <param name="TrashKey">The trash key: the new one of a trash, or the one a restore brought back (null for a folder trashed without one).</param>
/// <param name="Folders">The folders it changed, as they are now: the folder first, then those below it by sort order and name.</param>
internal sealed record FolderTreeChange(Guid? TrashKey, IReadOnlyList<StoredFolder> Folders);

/// <summary>
/// The folders of the form library in <c>jfbFolder</c>. A change that the tree's state refuses throws a
/// <see cref="FormBuilderConflictException"/> and writes nothing; a missing folder gives null or false. Nothing lives
/// below a folder in the trash (ADR-0047): folders are created and moved only below folders outside it, and a trash
/// takes everything below the folder with it.
/// </summary>
internal interface IFolderRepository
{
    /// <summary>
    /// Creates a folder, at the root or in a folder outside the trash. Refused with
    /// <see cref="FormBuilderConflict.DuplicateKey"/>, <see cref="FormBuilderConflict.MissingFolder"/> (the parent is
    /// missing or in the trash) or <see cref="FormBuilderConflict.FolderTooDeep"/>.
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

    /// <summary>
    /// Renames a folder and leaves the rest as it is, so a rename never undoes a restriction set meanwhile. Null when it
    /// does not exist.
    /// </summary>
    Task<StoredFolder?> RenameAsync(int id, string name, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restricts a folder outside the trash, or lifts its restriction, and leaves the rest as it is (ADR-0048). Null when
    /// it does not exist; refused with <see cref="FormBuilderConflict.Trashed"/> for a folder in the trash.
    /// </summary>
    Task<StoredFolder?> RestrictAsync(int id, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a folder outside the trash into <paramref name="parentId"/> (null: the root), with everything in it. Null
    /// when it does not exist. Refused with <see cref="FormBuilderConflict.Trashed"/> for a folder in the trash, and with
    /// <see cref="FormBuilderConflict.MissingFolder"/> (the target is missing or in the trash),
    /// <see cref="FormBuilderConflict.FolderIntoItself"/> or <see cref="FormBuilderConflict.FolderTooDeep"/>.
    /// </summary>
    Task<StoredFolder?> MoveAsync(int id, int? parentId, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a folder in the trash with every folder below it, all with one new trash key (ADR-0047). The forms in them
    /// are the form repository's to trash with that key, in the same unit of work. Null when the folder does not exist
    /// or is in the trash already.
    /// </summary>
    Task<FolderTreeChange?> TrashAsync(int id, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a folder out of the trash into <paramref name="landingParentId"/>: its parent, which the caller found outside
    /// the trash, or null for the root, which the caller found it goes to as its parent is in the trash; with the folders
    /// below it that went to the trash with it: those with its trash key, reached only through such folders (ADR-0047).
    /// The parent is read with a lock, and a parent found the other way refuses with
    /// <see cref="FormBuilderConflict.LandingChanged"/>. The forms in them are the form repository's to restore, in the
    /// same unit of work. Null when the folder does not exist or is outside the trash.
    /// </summary>
    Task<FolderTreeChange?> RestoreAsync(int id, int? landingParentId, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an empty folder in the trash. False when it does not exist. Refused with
    /// <see cref="FormBuilderConflict.NotTrashed"/> outside the trash, and with <see cref="FormBuilderConflict.FolderNotEmpty"/>
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

                // One statement: the parent must be outside the trash and leave room for one more level (ADR-0036,
                // ADR-0047).
                var sql = folder.ParentId is null
                    ? $"INSERT INTO {table} ({columns}) SELECT {values} WHERE {newKey}"
                    : $"{n.WithRecursive} {Up(n, "@1")} INSERT INTO {table} ({columns}) SELECT {values} WHERE {newKey} AND {Lives(n, "@1")} AND (SELECT MAX(depth) FROM up) < {MaxDepth}";
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
        database.ReadAsync<IReadOnlyList<StoredFolder>>(db => AllAsync(db, new SqlNames(db)), cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> RenameAsync(int id, string name, Guid? userKey, CancellationToken cancellationToken = default)
    {
        CheckName(name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Folder)} SET {n.C("name")} = @0, {n.C("updatedUtc")} = @1, {n.C("updatedByUserKey")} = @2 WHERE {n.C("id")} = @3",
                    name,
                    database.UtcNow,
                    userKey,
                    id);
                return await FindAsync(db, "id", id);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredFolder?> RestrictAsync(int id, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var changed = await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Folder)} SET {n.C("isRestricted")} = @0, {n.C("updatedUtc")} = @1, {n.C("updatedByUserKey")} = @2 WHERE {n.C("id")} = @3 AND {n.C("trashedUtc")} IS NULL",
                    isRestricted,
                    database.UtcNow,
                    userKey,
                    id);
                var current = await FindAsync(db, "id", id);
                return changed == 1 || current is null
                    ? current
                    : throw LibraryTrash.InTrash("folder");
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFolder?> MoveAsync(int id, int? parentId, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Folder);
                var set = $"UPDATE {table} SET {n.C("parentId")} = @0, {n.C("updatedUtc")} = @1, {n.C("updatedByUserKey")} = @2 WHERE {n.C("id")} = @3 AND {n.C("trashedUtc")} IS NULL";

                // One statement (ADR-0036, ADR-0047): the folder is outside the trash, the parent too and neither the
                // folder nor below it, and the folder's deepest level stays within MaxDepth. The walks lock what they
                // read on SQL Server, so two moves that would make a ring together wait for each other instead.
                var sql = parentId is null
                    ? $"{n.WithRecursive} {Down(n)} {set} AND (SELECT MAX(depth) FROM down) <= {MaxDepth}"
                    : $"{n.WithRecursive} {Up(n, "@0")}, {Down(n)} {set} AND {Lives(n, "@0")} AND NOT EXISTS (SELECT 1 FROM up WHERE id = @3) AND (SELECT MAX(depth) FROM up) + (SELECT MAX(depth) FROM down) <= {MaxDepth}";
                if (await db.ExecAsync(sql, parentId, database.UtcNow, userKey, id) == 1)
                {
                    return await FindAsync(db, "id", id);
                }

                return await FindAsync(db, "id", id) is { } current ? throw await RefusedMoveAsync(db, n, current, parentId) : null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<FolderTreeChange?> TrashAsync(int id, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var trashKey = Guid.NewGuid();

                // One statement: the folder and every folder below it, reached only through folders outside the trash
                // (none is below one in it). The walk locks what it reads on SQL Server, so a folder created in the
                // tree or moved into it meanwhile waits for the trash and is then refused.
                var walk = $"live(id, depth) AS (SELECT {n.C("id")}, 1 FROM {n.T(FormBuilderTables.Folder)}{n.LockingRead} WHERE {n.C("id")} = @3 AND {n.C("trashedUtc")} IS NULL "
                    + $"UNION ALL SELECT f.{n.C("id")}, live.depth + 1 FROM {n.T(FormBuilderTables.Folder)} f{n.LockingRead} JOIN live ON f.{n.C("parentId")} = live.id WHERE f.{n.C("trashedUtc")} IS NULL AND live.depth <= {MaxDepth})";
                var trashed = await db.ExecAsync(
                    $"{n.WithRecursive} {walk} UPDATE {n.T(FormBuilderTables.Folder)} SET {n.C("trashedUtc")} = @0, {n.C("trashKey")} = @1, {n.C("updatedUtc")} = @0, {n.C("updatedByUserKey")} = @2 WHERE {n.C("id")} IN (SELECT id FROM live)",
                    database.UtcNow,
                    trashKey,
                    userKey,
                    id);
                if (trashed == 0)
                {
                    return null;
                }

                var rows = await db.FetchListAsync<FolderDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Folder)} WHERE {n.C("trashKey")} = @0 ORDER BY {n.C("sortOrder")}, {n.C("name")}, {n.C("id")}",
                    trashKey);
                return new FolderTreeChange(trashKey, TopFirst(rows.Select(FromDto), id));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<FolderTreeChange?> RestoreAsync(int id, int? landingParentId, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Folder);

                // Held first, so its trash key stays the one read next until the unit ends.
                if (await RowHolds.HoldAsync(db, n, FormBuilderTables.Folder, $"{n.C("id")} = @0 AND {n.C("trashedUtc")} IS NOT NULL", id) == 0)
                {
                    return null;
                }

                var held = (await FindAsync(db, "id", id))!;
                var trashKey = held.TrashKey;

                // The landing the caller checked, read with a lock so it holds until the unit ends: the parent while it
                // lives, else the root.
                var intoParent = held.ParentId is { } parentId
                    && await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {table}{n.LockingRead} WHERE {n.C("id")} = @0 AND {n.C("trashedUtc")} IS NULL", parentId) == 1;
                if ((intoParent ? held.ParentId : null) != landingParentId)
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.LandingChanged, "Where it lands changed since it was checked: its folder went to the trash or came back meanwhile. Check again, then restore it.");
                }

                // What went to the trash with it: the folders below it with its key, reached only through such folders.
                // The walk locks what it reads, and the restore walks the same rows. The leading semicolon keeps NPoco
                // from putting a select of its own before a query that starts with WITH; both databases skip it.
                var walk = $"back(id, depth) AS (SELECT {n.C("id")}, 1 FROM {table}{n.LockingRead} WHERE {n.C("id")} = @0 "
                    + $"UNION ALL SELECT f.{n.C("id")}, back.depth + 1 FROM {table} f{n.LockingRead} JOIN back ON f.{n.C("parentId")} = back.id WHERE f.{n.C("trashKey")} = @1 AND back.depth <= {MaxDepth})";
                var ids = (await db.FetchListAsync<int>($";{n.WithRecursive} {walk} SELECT id FROM back", id, trashKey)).ToHashSet();

                // The folder goes back into its parent while that is outside the trash; those below it keep theirs.
                var parentLives = $"EXISTS (SELECT 1 FROM {table} p{n.LockingRead} WHERE p.{n.C("id")} = {table}.{n.C("parentId")} AND p.{n.C("trashedUtc")} IS NULL)";
                await db.ExecAsync(
                    $"{n.WithRecursive} {walk} UPDATE {table} SET {n.C("trashedUtc")} = NULL, {n.C("trashKey")} = NULL, {n.C("parentId")} = CASE WHEN {table}.{n.C("id")} <> @0 OR {parentLives} THEN {n.C("parentId")} ELSE NULL END, {n.C("updatedUtc")} = @2, {n.C("updatedByUserKey")} = @3 WHERE {n.C("id")} IN (SELECT id FROM back)",
                    id,
                    trashKey,
                    database.UtcNow,
                    userKey);

                // A library holds few folders, so the tree is read whole.
                var restored = (await AllAsync(db, n)).Where(folder => ids.Contains(folder.Id));
                return new FolderTreeChange(trashKey, TopFirst(restored, id));
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
                    $"DELETE FROM {table} WHERE {n.C("id")} = @0 AND {n.C("trashedUtc")} IS NOT NULL AND NOT EXISTS (SELECT 1 FROM {table} c{n.LockingRead} WHERE c.{n.C("parentId")} = @0) AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)} f{n.LockingRead} WHERE f.{n.C("folderId")} = @0)",
                    id);
                if (deleted == 1)
                {
                    return true;
                }

                return await FindAsync(db, "id", id) switch
                {
                    null => false,
                    { TrashedUtc: null } => throw LibraryTrash.OutsideTrash("folder"),
                    _ => throw new FormBuilderConflictException(FormBuilderConflict.FolderNotEmpty, "The folder still holds folders or forms."),
                };
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
        d.TrashKey,
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

    // The folder that the parameter names exists and is outside the trash, read with a lock.
    private static string Lives(SqlNames n, string folder) =>
        $"EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Folder)} p{n.LockingRead} WHERE p.{n.C("id")} = {folder} AND p.{n.C("trashedUtc")} IS NULL)";

    // The folder with this id first, then the others in their order.
    private static List<StoredFolder> TopFirst(IEnumerable<StoredFolder> folders, int id) =>
        [.. folders.Where(folder => folder.Id == id), .. folders.Where(folder => folder.Id != id)];

    private static async Task<IReadOnlyList<StoredFolder>> AllAsync(IUmbracoDatabase db, SqlNames n)
    {
        var rows = await db.FetchListAsync<FolderDto>(
            $"SELECT * FROM {n.T(FormBuilderTables.Folder)} ORDER BY {n.C("sortOrder")}, {n.C("name")}, {n.C("id")}");
        return rows.Select(FromDto).ToList();
    }

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

        var tree = (await AllAsync(db, n)).ToDictionary(row => row.Id);
        return tree.TryGetValue(folder.ParentId!.Value, out var parent) && parent.TrashedUtc is null
            ? new FormBuilderConflictException(FormBuilderConflict.FolderTooDeep, $"A folder can be at most {MaxDepth} levels deep.")
            : new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The parent folder does not exist or is in the trash.");
    }

    private static async Task<FormBuilderConflictException> RefusedMoveAsync(IUmbracoDatabase db, SqlNames n, StoredFolder folder, int? parentId)
    {
        if (folder.TrashedUtc is not null)
        {
            return LibraryTrash.InTrash("folder");
        }

        var tree = (await AllAsync(db, n)).ToDictionary(row => row.Id);
        if (parentId is { } target)
        {
            if (!tree.TryGetValue(target, out var parent) || parent.TrashedUtc is not null)
            {
                return new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The target folder does not exist or is in the trash.");
            }

            int? current = target;
            for (var steps = 0; current is { } at && steps <= MaxDepth; steps++)
            {
                if (at == folder.Id)
                {
                    return new FormBuilderConflictException(FormBuilderConflict.FolderIntoItself, "A folder cannot move into itself or into a folder below it.");
                }

                current = tree.GetValueOrDefault(at)?.ParentId;
            }
        }

        return new FormBuilderConflictException(FormBuilderConflict.FolderTooDeep, $"A folder can be at most {MaxDepth} levels deep.");
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
