using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>Where a grant applies (ADR-0013).</summary>
internal enum PermissionTarget
{
    /// <summary>Everywhere; the target key is <see cref="Guid.Empty"/>.</summary>
    Global,

    /// <summary>A folder and what is in it, named by the folder's key.</summary>
    Folder,

    /// <summary>One form, named by its key.</summary>
    Form,
}

/// <summary>A user group's grant on one target: the permissions as flags (ADR-0013, 4.6).</summary>
/// <param name="Id">The row's id.</param>
/// <param name="UserGroupKey">The Umbraco user group.</param>
/// <param name="TargetType">Where it applies.</param>
/// <param name="TargetKey">The folder's or form's key, or <see cref="Guid.Empty"/> for a global grant.</param>
/// <param name="Permissions">The permissions as flags; never 0, as no grant has no row.</param>
internal sealed record StoredGrant(int Id, Guid UserGroupKey, PermissionTarget TargetType, Guid TargetKey, int Permissions);

/// <summary>
/// A change to a group's grant on a target, from the permissions the editor saw to new ones, 0 standing for no grant on
/// either side. The change happens only while the grant still holds <paramref name="Expected"/>, so two editors never
/// overwrite each other unseen and the audit records exactly what changed.
/// </summary>
/// <param name="UserGroupKey">The Umbraco user group.</param>
/// <param name="TargetType">Where it applies.</param>
/// <param name="TargetKey">The folder's or form's key, or <see cref="Guid.Empty"/> for a global grant.</param>
/// <param name="Expected">The permissions the grant holds now, or 0 when there is none.</param>
/// <param name="Permissions">The permissions it should hold, or 0 to remove it.</param>
internal sealed record GrantChange(Guid UserGroupKey, PermissionTarget TargetType, Guid TargetKey, int Expected, int Permissions);

/// <summary>
/// The grants of Jiron Form Builder's permissions to user groups in <c>jfbPermission</c> (ADR-0013, ADR-0037): one row
/// per group and target. What the flags mean, which may be granted where and how they combine is the permission
/// model's (4.6); this stores them.
/// </summary>
internal interface IPermissionRepository
{
    /// <summary>A group's grant on a target, or null when it has none.</summary>
    Task<StoredGrant?> GetAsync(Guid userGroupKey, PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default);

    /// <summary>Every grant, in the order they were made.</summary>
    Task<IReadOnlyList<StoredGrant>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The grants of these groups everywhere, in the order they were made: what a user's evaluation reads.</summary>
    Task<IReadOnlyList<StoredGrant>> ListForGroupsAsync(IReadOnlyCollection<Guid> userGroupKeys, CancellationToken cancellationToken = default);

    /// <summary>The grants of every group on one target, in the order they were made.</summary>
    Task<IReadOnlyList<StoredGrant>> ListForTargetAsync(PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants, changes or removes a group's permissions on a target in one conditional statement, and returns the grant
    /// as it is now (null when there is none). Refused with <see cref="FormBuilderConcurrencyException"/>, changing
    /// nothing, when the grant no longer holds <see cref="GrantChange.Expected"/>.
    /// </summary>
    Task<StoredGrant?> ChangeAsync(GrantChange change, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every grant on a folder or form deleted for good (4.5). Returns how many. Global grants are removed one by
    /// one through <see cref="ChangeAsync"/>, never all at once.
    /// </summary>
    Task<int> DeleteForTargetAsync(PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default);

    /// <summary>Removes every grant of a group, for a user group Umbraco deleted (4.6). Returns how many.</summary>
    Task<int> DeleteForGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class PermissionRepository(FormBuilderDatabase database) : IPermissionRepository
{
    /// <summary>The most group keys one statement names; SQL Server takes at most 2,100 parameters.</summary>
    public const int GroupsPerStatement = 1000;

    /// <inheritdoc />
    public Task<StoredGrant?> GetAsync(Guid userGroupKey, PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default)
    {
        CheckTarget(userGroupKey, targetType, targetKey);
        return database.ReadAsync(db => FindAsync(db, new SqlNames(db), userGroupKey, targetType, targetKey), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredGrant>> ListAsync(CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredGrant>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<PermissionDto>($"SELECT * FROM {n.T(FormBuilderTables.Permission)} ORDER BY {n.C("id")}");
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredGrant>> ListForGroupsAsync(IReadOnlyCollection<Guid> userGroupKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userGroupKeys);
        var keys = userGroupKeys.Distinct().ToArray();
        if (keys.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredGrant>>([]);
        }

        return database.ReadAsync<IReadOnlyList<StoredGrant>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = new List<PermissionDto>();
                foreach (var chunk in keys.Chunk(GroupsPerStatement))
                {
                    var list = string.Join(", ", chunk.Select((_, i) => "@" + i.ToString(CultureInfo.InvariantCulture)));
                    rows.AddRange(await db.FetchListAsync<PermissionDto>(
                        $"SELECT * FROM {n.T(FormBuilderTables.Permission)} WHERE {n.C("userGroupKey")} IN ({list})",
                        [.. chunk.Cast<object?>()]));
                }

                return rows.OrderBy(row => row.Id).Select(FromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredGrant>> ListForTargetAsync(PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default)
    {
        CheckTarget(targetType, targetKey);
        return database.ReadAsync<IReadOnlyList<StoredGrant>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<PermissionDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Permission)} WHERE {n.C("targetType")} = @0 AND {n.C("targetKey")} = @1 ORDER BY {n.C("id")}",
                    targetType.ToString(),
                    targetKey);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredGrant?> ChangeAsync(GrantChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        CheckTarget(change.UserGroupKey, change.TargetType, change.TargetKey);
        var (group, type, key) = (change.UserGroupKey, change.TargetType.ToString(), change.TargetKey);
        if (change is { Expected: 0, Permissions: 0 })
        {
            // Nothing to write: a read says whether the group still holds no grant.
            return database.ReadAsync(
                async db =>
                {
                    var current = await FindAsync(db, new SqlNames(db), group, change.TargetType, key);
                    if (current is not null)
                    {
                        throw Stale(change, current.Permissions);
                    }

                    return current;
                },
                cancellationToken);
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Permission);
                var row = $"{n.C("userGroupKey")} = @0 AND {n.C("targetType")} = @1 AND {n.C("targetKey")} = @2";
                int changed;
                try
                {
                    changed = change switch
                    {
                        { Expected: 0 } => await db.ExecAsync(
                            $"""
                            INSERT INTO {table} ({n.C("userGroupKey")}, {n.C("targetType")}, {n.C("targetKey")}, {n.C("permissions")})
                            SELECT @0, @1, @2, @3
                            WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {row})
                            """,
                            group,
                            type,
                            key,
                            change.Permissions),
                        { Permissions: 0 } => await db.ExecAsync($"DELETE FROM {table} WHERE {row} AND {n.C("permissions")} = @3", group, type, key, change.Expected),
                        _ => await db.ExecAsync($"UPDATE {table} SET {n.C("permissions")} = @3 WHERE {row} AND {n.C("permissions")} = @4", group, type, key, change.Permissions, change.Expected),
                    };
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    // A grant made at the same moment that the database let through: the editor did not see it.
                    throw new FormBuilderConcurrencyException("The group was given a grant on this target meanwhile.", exception);
                }

                var current = await FindAsync(db, n, group, change.TargetType, key);
                return changed == 1 ? current : throw Stale(change, current?.Permissions ?? 0);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteForTargetAsync(PermissionTarget targetType, Guid targetKey, CancellationToken cancellationToken = default)
    {
        CheckTarget(targetType, targetKey);
        if (targetType == PermissionTarget.Global)
        {
            throw new ArgumentException("Grants are removed together only for a folder or a form.", nameof(targetType));
        }

        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Permission)} WHERE {n.C("targetType")} = @0 AND {n.C("targetKey")} = @1", targetType.ToString(), targetKey);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteForGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default)
    {
        CheckGroup(userGroupKey);
        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Permission)} WHERE {n.C("userGroupKey")} = @0", userGroupKey);
            },
            cancellationToken);
    }

    private static void CheckTarget(Guid userGroupKey, PermissionTarget targetType, Guid targetKey)
    {
        CheckGroup(userGroupKey);
        CheckTarget(targetType, targetKey);
    }

    private static void CheckGroup(Guid userGroupKey)
    {
        if (userGroupKey == Guid.Empty)
        {
            throw new ArgumentException("A grant names a user group.", nameof(userGroupKey));
        }
    }

    // Keys never hold NULL, so a global grant's target is Guid.Empty, and only a global grant's (section 4.5).
    private static void CheckTarget(PermissionTarget targetType, Guid targetKey)
    {
        if (!Enum.IsDefined(targetType))
        {
            throw new ArgumentOutOfRangeException(nameof(targetType), targetType, "A grant applies globally, to a folder or to a form.");
        }

        if ((targetType == PermissionTarget.Global) != (targetKey == Guid.Empty))
        {
            throw new ArgumentException("A global grant's target key is Guid.Empty, and a folder's or form's grant names its key.", nameof(targetKey));
        }
    }

    private static FormBuilderConcurrencyException Stale(GrantChange change, int current) =>
        new($"The grant changed meanwhile (it holds {current.ToString(CultureInfo.InvariantCulture)}, not {change.Expected.ToString(CultureInfo.InvariantCulture)}).");

    private static async Task<StoredGrant?> FindAsync(IUmbracoDatabase db, SqlNames n, Guid userGroupKey, PermissionTarget targetType, Guid targetKey)
    {
        var row = await db.FirstOrNullAsync<PermissionDto>(
            $"SELECT * FROM {n.T(FormBuilderTables.Permission)} WHERE {n.C("userGroupKey")} = @0 AND {n.C("targetType")} = @1 AND {n.C("targetKey")} = @2",
            userGroupKey,
            targetType.ToString(),
            targetKey);
        return row is null ? null : FromDto(row);
    }

    private static StoredGrant FromDto(PermissionDto d) => new(d.Id, d.UserGroupKey, Enum.Parse<PermissionTarget>(d.TargetType), d.TargetKey, d.Permissions);
}
