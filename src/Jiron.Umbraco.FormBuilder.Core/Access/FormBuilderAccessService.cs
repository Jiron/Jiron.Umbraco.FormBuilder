using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Services;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>
/// Who may do what in the Forms section (ADR-0013, ADR-0048). It evaluates a user's permissions globally, on a folder
/// and on a form, scopes their lists, says what a move gives each group, and changes grants and restrictions under the
/// host's locks, each with an audit entry. Every check reads one snapshot of the grants and the folders per server,
/// which every committed change drops. The policies of the management API (4.6b) and its endpoints (4.7) build on it.
/// </summary>
internal interface IFormBuilderAccessService
{
    /// <summary>The user's permissions globally, for what belongs to no folder or form.</summary>
    Task<FormBuilderPermissions> GetGlobalAsync(AccessUser user, CancellationToken cancellationToken = default);

    /// <summary>The user's permissions on a folder, for what happens in it; null for the root.</summary>
    Task<FormBuilderPermissions> GetForFolderAsync(AccessUser user, Guid? folderKey, CancellationToken cancellationToken = default);

    /// <summary>The user's permissions on a form.</summary>
    Task<FormBuilderPermissions> GetForFormAsync(AccessUser user, FormAccessTarget form, CancellationToken cancellationToken = default);

    /// <summary>
    /// The forms the user's lists hold, for <see cref="FormFilter.Access"/>: those where they hold one of
    /// <paramref name="anyOf"/>, by default any permission.
    /// </summary>
    Task<AccessScope> GetFormScopeAsync(AccessUser user, FormBuilderPermissions anyOf = PermissionRules.FolderLevel, CancellationToken cancellationToken = default);

    /// <summary>The folders the user's folder lists hold: those that lead to a folder or form they hold something on.</summary>
    Task<FolderVisibility> GetFolderVisibilityAsync(AccessUser user, CancellationToken cancellationToken = default);

    /// <summary>The groups a move gives more access to the item, with what each gains.</summary>
    Task<IReadOnlyList<AccessGain>> GetMoveGainsAsync(AccessMove move, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes a group's grant from what the editor saw to new permissions, none to remove it, with an audit entry. Null
    /// when the folder or form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderSettingLockedException">The host locks the grant (<c>setting_locked</c>).</exception>
    /// <exception cref="FormBuilderSettingsValidationException">
    /// A permission without its dependency, a global-only one on a folder or form, or an unknown group (<c>validation_failed</c>).
    /// </exception>
    /// <exception cref="FormBuilderConcurrencyException">The grant changed since the editor saw it.</exception>
    Task<AccessGrant?> ChangeGrantAsync(AccessGrantChange change, CancellationToken cancellationToken = default);

    /// <summary>Restricts a folder outside the trash, or lifts its restriction, with an audit entry. Null when it does not exist.</summary>
    /// <exception cref="FormBuilderSettingLockedException">The host locks the access section.</exception>
    /// <exception cref="FormBuilderConflictException">The folder is in the trash (<c>trashed</c>).</exception>
    Task<AccessRestriction?> RestrictFolderAsync(Guid folderKey, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restricts a form outside the trash, or lifts its restriction, at the concurrency version the caller read, with an
    /// audit entry. Null when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderSettingLockedException">The host locks the access section.</exception>
    /// <exception cref="FormBuilderConflictException">The form is in the trash (<c>trashed</c>).</exception>
    /// <exception cref="FormBuilderConcurrencyException">The form changed since the caller read it.</exception>
    Task<AccessRestriction?> RestrictFormAsync(Guid formKey, int expectedConcurrencyVersion, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>Removes every grant of a user group Umbraco deleted, with an audit entry when it had any; returns how many.</summary>
    Task<int> RemoveGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed partial class FormBuilderAccessService(
    IPermissionRepository permissions,
    IFolderRepository folders,
    IFormRepository forms,
    IAuditRepository audit,
    FormBuilderDatabase database,
    IUserGroupService userGroups,
    IOptions<FormBuilderOptions> options,
    AppCaches appCaches,
    FormBuilderAccessCache cache,
    ILogger<FormBuilderAccessService> logger)
    : IFormBuilderAccessService
{
    /// <summary>The audit action of a grant's change: the target, the group, the permissions before and after.</summary>
    public const string GrantChangedAction = "access.grantChanged";

    /// <summary>The audit action of a folder or form restricted.</summary>
    public const string RestrictedAction = "access.restricted";

    /// <summary>The audit action of a folder's or form's restriction lifted.</summary>
    public const string UnrestrictedAction = "access.unrestricted";

    /// <summary>The audit action of the grants a deleted user group took with it.</summary>
    public const string GroupRemovedAction = "access.groupRemoved";

    /// <summary>The audit entity type of a global grant, which names no item.</summary>
    public const string GlobalEntityType = "access";

    /// <summary>The audit entity type of a user group; the item is the group's key.</summary>
    public const string UserGroupEntityType = "userGroup";

    /// <summary>The settings field of a grant's permissions, for its errors.</summary>
    public const string PermissionsField = FormBuilderSettingsSections.Access + ".permissions";

    /// <summary>The settings field of a grant's group, for its errors.</summary>
    public const string GroupField = FormBuilderSettingsSections.Access + ".group";

    /// <summary>The settings field of the global grants, which the host can lock.</summary>
    public const string GrantsField = FormBuilderSettingsSections.Access + ".grants";

    /// <summary>The error of a permission granted without the one it needs.</summary>
    public const string NeedsCode = "needs";

    /// <summary>The error of a global-only permission granted on a folder or form.</summary>
    public const string GlobalOnlyCode = "globalOnly";

    /// <summary>The error of a value that is no permission.</summary>
    public const string UnknownCode = "unknown";

    /// <summary>The error of a group Umbraco does not have.</summary>
    public const string UnknownGroupCode = "unknownGroup";

    /// <summary>The host configuration key of the global grants.</summary>
    public const string HostGrantsKey = FormBuilderOptions.SectionName + ":Access:Grants";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetGlobalAsync(AccessUser user, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Global(user);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetForFolderAsync(AccessUser user, Guid? folderKey, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Folder(user, folderKey);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetForFormAsync(AccessUser user, FormAccessTarget form, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Form(user, form);

    /// <inheritdoc />
    public async Task<AccessScope> GetFormScopeAsync(AccessUser user, FormBuilderPermissions anyOf = PermissionRules.FolderLevel, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Scope(user, anyOf);

    /// <inheritdoc />
    public async Task<FolderVisibility> GetFolderVisibilityAsync(AccessUser user, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var scope = snapshot.Scope(user, PermissionRules.FolderLevel);
        if (scope.IsEverything)
        {
            return FolderVisibility.Everything;
        }

        var granted = await permissions.ListFoldersOfGrantedFormsAsync(scope.GroupKeys, (int)scope.AnyOf, cancellationToken);
        return snapshot.Visibility(scope, granted);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccessGain>> GetMoveGainsAsync(AccessMove move, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).MoveGains(move);

    /// <inheritdoc />
    public async Task<AccessGrant?> ChangeGrantAsync(AccessGrantChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        EnsureOwnUnit();
        CheckLocks(change.TargetType);
        await CheckAsync(change);
        if (await TargetMissingAsync(change.TargetType, change.TargetKey, cancellationToken))
        {
            return null;
        }

        if (change.Expected == change.Permissions)
        {
            // Nothing to change: the grant still has to be what the editor saw.
            var current = await permissions.GetAsync(change.UserGroupKey, change.TargetType, change.TargetKey, cancellationToken);
            return (current?.Permissions ?? 0) == (int)change.Expected
                ? Grant(change, current)
                : throw new FormBuilderConcurrencyException("The grant changed meanwhile.");
        }

        var (gone, stored) = await database.WriteAsync(
            async _ =>
            {
                var grant = await permissions.ChangeAsync(
                    new GrantChange(change.UserGroupKey, change.TargetType, change.TargetKey, (int)change.Expected, (int)change.Permissions),
                    cancellationToken);
                if (grant is null && change.Permissions != FormBuilderPermissions.None)
                {
                    // The folder or form went at the same moment; the statement wrote nothing.
                    return (true, grant);
                }

                var details = new AuditDetails()
                    .AddIdentifier("target", StoredNames.Of(change.TargetType))
                    .Add("group", change.UserGroupKey)
                    .AddIdentifiers("before", PermissionRules.NamesOf(change.Expected))
                    .AddIdentifiers("after", PermissionRules.NamesOf(change.Permissions));
                var item = change.TargetType == PermissionTarget.Global ? (Guid?)null : change.TargetKey;
                await audit.AddAsync(new NewAuditEntry(GrantChangedAction, EntityTypeOf(change.TargetType), item, change.UserKey, details), cancellationToken);
                return (false, grant);
            },
            cancellationToken);
        if (gone)
        {
            return null;
        }

        cache.Changed();
        return Grant(change, stored);
    }

    /// <inheritdoc />
    public async Task<AccessRestriction?> RestrictFolderAsync(Guid folderKey, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        CheckLocks(PermissionTarget.Folder);
        if (await folders.GetAsync(folderKey, cancellationToken) is not { } folder)
        {
            return null;
        }

        CheckOutsideTrash(folder.TrashedUtc, "folder");
        if (folder.IsRestricted == isRestricted)
        {
            return new AccessRestriction(folder.Key, isRestricted, null);
        }

        var changed = await database.WriteAsync(
            async _ =>
            {
                if (await folders.RestrictAsync(folder.Id, isRestricted, userKey, cancellationToken) is not { } stored)
                {
                    return null;
                }

                await audit.AddAsync(new NewAuditEntry(RestrictionAction(isRestricted), FormBuilderFolderService.EntityType, folder.Key, userKey), cancellationToken);
                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        cache.Changed();
        return new AccessRestriction(changed.Key, changed.IsRestricted, null);
    }

    /// <inheritdoc />
    public async Task<AccessRestriction?> RestrictFormAsync(Guid formKey, int expectedConcurrencyVersion, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        CheckLocks(PermissionTarget.Form);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckOutsideTrash(form.TrashedUtc, "form");
        if (form.ConcurrencyVersion != expectedConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException("The form changed since it was read.");
        }

        if (form.IsRestricted == isRestricted)
        {
            return new AccessRestriction(form.Key, isRestricted, form.ConcurrencyVersion);
        }

        var changed = await database.WriteAsync(
            async _ =>
            {
                var update = new FormUpdate(form.Id, expectedConcurrencyVersion, form.Alias, form.Name, form.FolderId, isRestricted, userKey);
                if (await forms.UpdateAsync(update, cancellationToken) is not { } stored)
                {
                    return null;
                }

                await audit.AddAsync(new NewAuditEntry(RestrictionAction(isRestricted), FormBuilderFormService.EntityType, form.Key, userKey), cancellationToken);
                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        cache.Changed();
        return new AccessRestriction(changed.Key, changed.IsRestricted, changed.ConcurrencyVersion);
    }

    /// <inheritdoc />
    public async Task<int> RemoveGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default)
    {
        var removed = await database.WriteAsync(
            async _ =>
            {
                var count = await permissions.DeleteForGroupAsync(userGroupKey, cancellationToken);
                if (count > 0)
                {
                    await audit.AddAsync(new NewAuditEntry(GroupRemovedAction, UserGroupEntityType, userGroupKey, null, new AuditDetails().Add("grants", count)), cancellationToken);
                }

                return count;
            },
            cancellationToken);

        // A host grant may name the group, so the snapshot goes even when it held no grant here.
        cache.Changed();
        return removed;
    }

    /// <summary>
    /// The snapshot, from this server's cache or read now. A change clears the cached copy and its loading token; when
    /// the token is gone or replaced after the read, the rows may be older than the change, so the snapshot answers this
    /// call but is not kept (ADR-0043's pattern).
    /// </summary>
    internal async Task<AccessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var runtime = appCaches.RuntimeCache;
        if (runtime.GetCacheItem<AccessSnapshot>(AccessCacheKeys.Snapshot) is { } hit)
        {
            return hit;
        }

        var token = new object();
        runtime.Insert(AccessCacheKeys.Loading, () => token, CacheDuration);
        var grants = await permissions.ListAsync(cancellationToken);
        var tree = await folders.ListAllAsync(cancellationToken);
        var snapshot = AccessSnapshot.Create(
            grants,
            tree.Select(folder => new AccessFolder(folder.Id, folder.Key, folder.ParentId, folder.IsRestricted)),
            await HostGrantsAsync());
        if (ReferenceEquals(runtime.Get(AccessCacheKeys.Loading), token))
        {
            runtime.Insert(AccessCacheKeys.Snapshot, () => snapshot, CacheDuration);
        }

        return snapshot;
    }

    private static string EntityTypeOf(PermissionTarget target) => target switch
    {
        PermissionTarget.Folder => FormBuilderFolderService.EntityType,
        PermissionTarget.Form => FormBuilderFormService.EntityType,
        _ => GlobalEntityType,
    };

    private static string RestrictionAction(bool isRestricted) => isRestricted ? RestrictedAction : UnrestrictedAction;

    private static AccessGrant Grant(AccessGrantChange change, StoredGrant? stored) =>
        new(change.UserGroupKey, change.TargetType, change.TargetKey, PermissionRules.FromStored(stored?.Permissions ?? 0));

    private static void CheckOutsideTrash(DateTimeOffset? trashedUtc, string item)
    {
        if (trashedUtc is not null)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.Trashed, $"The {item} is in the trash; restore it first.");
        }
    }

    private async Task<bool> TargetMissingAsync(PermissionTarget target, Guid key, CancellationToken cancellationToken) => target switch
    {
        PermissionTarget.Folder => await folders.GetAsync(key, cancellationToken) is null,
        PermissionTarget.Form => await forms.GetAsync(key, cancellationToken) is null,
        _ => false,
    };

    // The global grants of the host, by group key, or null when the host sets none. A group the site does not have
    // grants nothing, and each load says so.
    private async Task<IReadOnlyDictionary<Guid, FormBuilderPermissions>?> HostGrantsAsync()
    {
        var configured = options.Value.Access.Grants;
        if (configured.Count == 0)
        {
            return null;
        }

        var grants = new Dictionary<Guid, FormBuilderPermissions>();
        foreach (var grant in configured)
        {
            if (string.IsNullOrWhiteSpace(grant.Group) || await userGroups.GetAsync(grant.Group) is not { } group)
            {
                LogUnknownHostGroup(logger, grant.Group);
                continue;
            }

            var granted = FormBuilderPermissions.None;
            foreach (var name in grant.Permissions)
            {
                granted |= PermissionRules.TryParse(name, out var permission) ? permission : FormBuilderPermissions.None;
            }

            grants[group.Key] = grants.GetValueOrDefault(group.Key) | granted;
        }

        return grants;
    }

    // The host's locks first: the access section, then the global grants it sets itself (ADR-0048).
    private void CheckLocks(PermissionTarget target)
    {
        var host = options.Value;
        for (var i = 0; i < host.LockedSections.Count; i++)
        {
            if (string.Equals(host.LockedSections[i], FormBuilderSettingsSections.Access, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormBuilderSettingLockedException(FormBuilderSettingsSections.Access, $"{SettingsHostRules.LockedSectionsKey}:{i}");
            }
        }

        if (target == PermissionTarget.Global && host.Access.Grants.Count > 0)
        {
            throw new FormBuilderSettingLockedException(GrantsField, HostGrantsKey);
        }
    }

    // What the model allows, all problems at once; a grant that gives something needs a group Umbraco has.
    private async Task CheckAsync(AccessGrantChange change)
    {
        var errors = new List<SettingsError>();
        if ((change.Permissions & ~PermissionRules.All) != 0 || (change.Expected & ~PermissionRules.All) != 0)
        {
            errors.Add(new SettingsError(PermissionsField, UnknownCode, "Holds a value that is no permission."));
        }

        if (PermissionRules.NotGrantable(change.TargetType, change.Permissions) is var globalOnly and not FormBuilderPermissions.None)
        {
            errors.Add(new SettingsError(PermissionsField, GlobalOnlyCode, $"{string.Join(", ", PermissionRules.NamesOf(globalOnly))} can be granted globally only."));
        }

        foreach (var missing in PermissionRules.MissingDependencies(change.Permissions))
        {
            errors.Add(new SettingsError(PermissionsField, NeedsCode, $"'{PermissionRules.NameOf(missing.Permission)}' needs '{PermissionRules.NameOf(missing.Needs)}'."));
        }

        if (change.Permissions != FormBuilderPermissions.None && await userGroups.GetAsync(change.UserGroupKey) is null)
        {
            errors.Add(new SettingsError(GroupField, UnknownGroupCode, "No user group has this key."));
        }

        if (errors.Count > 0)
        {
            throw new FormBuilderSettingsValidationException(errors);
        }
    }

    private void EnsureOwnUnit()
    {
        if (database.InUnitOfWork)
        {
            throw new InvalidOperationException("A change of access is a unit of work of its own: it drops the cached snapshot once it has committed (ADR-0048).");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder's host configuration grants permissions to the user group '{Group}', which the site does not have; it grants nothing")]
    private static partial void LogUnknownHostGroup(ILogger logger, string? group);
}
