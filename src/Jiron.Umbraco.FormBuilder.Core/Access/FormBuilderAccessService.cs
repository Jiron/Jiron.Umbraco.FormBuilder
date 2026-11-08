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

    /// <summary>What the user holds anywhere: globally, or on any folder or form (ADR-0165).</summary>
    Task<FormBuilderPermissions> GetAnywhereAsync(AccessUser user, CancellationToken cancellationToken = default);

    /// <summary>What the user holds on the root or on some folder, which creating a form takes; a grant on one form gives none of it.</summary>
    Task<FormBuilderPermissions> GetOnFoldersAsync(AccessUser user, CancellationToken cancellationToken = default);

    /// <summary>The user groups other than the administrators that hold the permission anywhere.</summary>
    Task<IReadOnlyList<Guid>> ListGroupsHoldingAsync(FormBuilderPermissions permission, CancellationToken cancellationToken = default);

    /// <summary>The user's permissions on a form.</summary>
    Task<FormBuilderPermissions> GetForFormAsync(AccessUser user, FormAccessTarget form, CancellationToken cancellationToken = default);

    /// <summary>
    /// The forms the user's lists hold, for <see cref="FormFilter.Visibility"/>: those where they hold one of
    /// <paramref name="anyOf"/>, by default any permission.
    /// </summary>
    Task<FormVisibility> GetFormVisibilityAsync(AccessUser user, FormBuilderPermissions anyOf = PermissionRules.FolderLevel, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Removes every grant of a user group Umbraco deleted, with an audit entry when it had any, in a unit of work of its
    /// own as every change of access is; returns how many.
    /// </summary>
    Task<int> RemoveGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a target's grants and a folder's or form's restriction in one unit of work, each grant from what the editor
    /// saw, with an audit entry for each change (ADR-0218). Null when the folder or form does not exist.
    /// </summary>
    /// <exception cref="FormBuilderSettingLockedException">The host locks the access section, or the global grants (<c>setting_locked</c>).</exception>
    /// <exception cref="FormBuilderSettingsValidationException">
    /// A permission without its dependency, a global-only one on a folder or form, an unknown group or a group named twice
    /// (<c>validation_failed</c>).
    /// </exception>
    /// <exception cref="FormBuilderConflictException">The folder or form is in the trash (<c>trashed</c>).</exception>
    /// <exception cref="FormBuilderConcurrencyException">A grant, or the form, changed since the editor read it; nothing is saved.</exception>
    Task<AccessSaved?> SaveAsync(AccessSave save, CancellationToken cancellationToken = default);

    /// <summary>The host's rule that keeps access on a target from being changed here, or null when none does.</summary>
    AccessHostLock? LockOf(PermissionTarget target);

    /// <summary>The snapshot every check reads (ADR-0048), for a screen that shows each group's grants.</summary>
    Task<AccessSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default);
}

/// <summary>What a save of a target's grants left: each grant it changed, and the folder's or form's restriction.</summary>
/// <param name="Grants">The grants as they are now, of the groups the save changed.</param>
/// <param name="Restriction">The folder's or form's restriction now; null for the global grants.</param>
internal sealed record AccessSaved(IReadOnlyList<AccessGrant> Grants, AccessRestriction? Restriction);

/// <inheritdoc />
internal sealed partial class FormBuilderAccessService(
    IPermissionRepository permissions,
    IFolderRepository folders,
    IFormRepository forms,
    IUserGroupService userGroups,
    IOptions<FormBuilderOptions> options,
    AppCaches appCaches,
    AccessSnapshotInvalidator snapshots,
    FormBuilderChanges<FormBuilderAccessService> changes,
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

    /// <summary>The error of a save that names a group twice.</summary>
    public const string DuplicateGroupCode = "duplicateGroup";

    /// <summary>The host configuration key of the global grants.</summary>
    public const string HostGrantsKey = FormBuilderOptions.SectionName + ":Access:Grants";

    // The refusal of a change of access inside a caller's unit of work or an Umbraco scope.
    private const string OwnUnit = "A change of access is a unit of work of its own: it drops the cached snapshot once it has committed (ADR-0048).";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetGlobalAsync(AccessUser user, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Global(user);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetAnywhereAsync(AccessUser user, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Anywhere(user);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetOnFoldersAsync(AccessUser user, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).OnFolders(user);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListGroupsHoldingAsync(FormBuilderPermissions permission, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).GroupsHolding(permission);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetForFolderAsync(AccessUser user, Guid? folderKey, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Folder(user, folderKey);

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> GetForFormAsync(AccessUser user, FormAccessTarget form, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).Form(user, form);

    /// <inheritdoc />
    public async Task<FormVisibility> GetFormVisibilityAsync(AccessUser user, FormBuilderPermissions anyOf = PermissionRules.FolderLevel, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).FormVisibilityOf(user, anyOf);

    /// <inheritdoc />
    public async Task<FolderVisibility> GetFolderVisibilityAsync(AccessUser user, CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken);
        var forms = snapshot.FormVisibilityOf(user, PermissionRules.FolderLevel);
        if (forms.IsEverything)
        {
            return FolderVisibility.Everything;
        }

        var granted = await permissions.ListFoldersOfGrantedFormsAsync(forms.GroupKeys, (int)forms.AnyOf, cancellationToken);
        return snapshot.FolderVisibilityOf(forms, granted);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccessGain>> GetMoveGainsAsync(AccessMove move, CancellationToken cancellationToken = default) =>
        (await GetSnapshotAsync(cancellationToken)).MoveGains(move);

    /// <inheritdoc />
    public async Task<AccessGrant?> ChangeGrantAsync(AccessGrantChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        changes.EnsureOwnUnit(OwnUnit);
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

        var (gone, stored) = await changes.WriteAsync(
            async () =>
            {
                var grant = await permissions.ChangeAsync(
                    new GrantChange(change.UserGroupKey, change.TargetType, change.TargetKey, (int)change.Expected, (int)change.Permissions),
                    cancellationToken);

                // Nothing written for a grant that remains: the folder or form went at the same moment.
                return (Gone: grant is null && change.Permissions != FormBuilderPermissions.None, Grant: grant);
            },
            written => written.Gone ? [] : [GrantEntry(change)],
            cancellationToken);
        if (gone)
        {
            return null;
        }

        snapshots.Changed();
        return Grant(change, stored);
    }

    /// <inheritdoc />
    public async Task<AccessRestriction?> RestrictFolderAsync(Guid folderKey, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        CheckLocks(PermissionTarget.Folder);
        if (await folders.GetAsync(folderKey, cancellationToken) is not { } folder)
        {
            return null;
        }

        LibraryTrash.CheckOutside(folder.TrashedUtc, "folder");
        if (folder.IsRestricted == isRestricted)
        {
            return new AccessRestriction(folder.Key, isRestricted, null);
        }

        var changed = await changes.WriteAsync(
            () => folders.RestrictAsync(folder.Id, isRestricted, userKey, cancellationToken),
            stored => stored is null ? [] : [new NewAuditEntry(RestrictionAction(isRestricted), FormBuilderFolderService.EntityType, folder.Key, userKey)],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        snapshots.Changed();
        return new AccessRestriction(changed.Key, changed.IsRestricted, null);
    }

    /// <inheritdoc />
    public async Task<AccessRestriction?> RestrictFormAsync(Guid formKey, int expectedConcurrencyVersion, bool isRestricted, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        CheckLocks(PermissionTarget.Form);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        LibraryTrash.CheckOutside(form.TrashedUtc, FormChecks.Item);
        if (form.ConcurrencyVersion != expectedConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException("The form changed since it was read.");
        }

        if (form.IsRestricted == isRestricted)
        {
            return new AccessRestriction(form.Key, isRestricted, form.ConcurrencyVersion);
        }

        var changed = await changes.WriteAsync(
            () => forms.UpdateAsync(new FormUpdate(form.Id, expectedConcurrencyVersion, form.Alias, form.Name, form.FolderId, isRestricted, userKey), cancellationToken),
            stored => stored is null ? [] : [new NewAuditEntry(RestrictionAction(isRestricted), FormAudit.EntityType, form.Key, userKey)],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        snapshots.Changed();
        return new AccessRestriction(changed.Key, changed.IsRestricted, changed.ConcurrencyVersion);
    }

    /// <inheritdoc />
    public async Task<int> RemoveGroupAsync(Guid userGroupKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        var removed = await changes.WriteAsync(
            () => permissions.DeleteForGroupAsync(userGroupKey, cancellationToken),
            count => count == 0 ? [] : [new NewAuditEntry(GroupRemovedAction, UserGroupEntityType, userGroupKey, null, new AuditDetails().Add("grants", count))],
            cancellationToken);

        // A host grant may name the group, so the snapshot goes even when it held no grant here.
        snapshots.Changed();
        return removed;
    }

    /// <inheritdoc />
    public async Task<AccessSaved?> SaveAsync(AccessSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        changes.EnsureOwnUnit(OwnUnit);
        CheckLocks(save.TargetType);
        if (save.TargetType == PermissionTarget.Global && save.IsRestricted is not null)
        {
            throw new ArgumentException("The global grants have no restriction.", nameof(save));
        }

        var errors = new List<SettingsError>();
        if (save.Grants.GroupBy(grant => grant.UserGroupKey).Any(group => group.Count() > 1))
        {
            errors.Add(new SettingsError(GroupField, DuplicateGroupCode, "Names a user group more than once."));
        }

        var edits = save.Grants.Where(grant => grant.Expected != grant.Permissions).ToList();
        foreach (var edit in edits)
        {
            errors.AddRange(await ErrorsAsync(Change(save, edit)));
        }

        if (errors.Count > 0)
        {
            throw new FormBuilderSettingsValidationException([.. errors.Distinct()]);
        }

        // The target as it is now: a folder or a form outside the trash, a form at the version the editor read.
        StoredFolder? folder = null;
        StoredForm? form = null;
        if (save.TargetType == PermissionTarget.Folder)
        {
            if ((folder = await folders.GetAsync(save.TargetKey, cancellationToken)) is null)
            {
                return null;
            }

            LibraryTrash.CheckOutside(folder.TrashedUtc, "folder");
        }
        else if (save.TargetType == PermissionTarget.Form)
        {
            if ((form = await forms.GetAsync(save.TargetKey, cancellationToken)) is null)
            {
                return null;
            }

            LibraryTrash.CheckOutside(form.TrashedUtc, FormChecks.Item);
        }

        var restricted = folder?.IsRestricted ?? form?.IsRestricted;
        var restrict = save.IsRestricted is { } wanted && restricted is { } now && wanted != now;
        if (restrict && form is not null && form.ConcurrencyVersion != save.ExpectedConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException("The form changed since it was read.");
        }

        if (edits.Count == 0 && !restrict)
        {
            return new AccessSaved([], restricted is { } kept ? new AccessRestriction(save.TargetKey, kept, form?.ConcurrencyVersion) : null);
        }

        var saved = await changes.WriteAsync(
            async () =>
            {
                var grants = new List<AccessGrant>();
                foreach (var edit in edits)
                {
                    // A grant changed meanwhile throws, which takes back every change of this unit.
                    var stored = await permissions.ChangeAsync(
                        new GrantChange(edit.UserGroupKey, save.TargetType, save.TargetKey, (int)edit.Expected, (int)edit.Permissions),
                        cancellationToken);
                    if (stored is null && edit.Permissions != FormBuilderPermissions.None)
                    {
                        throw new FormBuilderConcurrencyException("The folder or form went while its access was saved.");
                    }

                    grants.Add(Grant(Change(save, edit), stored));
                }

                AccessRestriction? restriction = restricted is { } kept ? new AccessRestriction(save.TargetKey, kept, form?.ConcurrencyVersion) : null;
                if (restrict && folder is not null)
                {
                    var changed = await folders.RestrictAsync(folder.Id, save.IsRestricted!.Value, save.UserKey, cancellationToken)
                        ?? throw new FormBuilderConcurrencyException("The folder went while its access was saved.");
                    restriction = new AccessRestriction(changed.Key, changed.IsRestricted, null);
                }
                else if (restrict && form is not null)
                {
                    var changed = await forms.UpdateAsync(new FormUpdate(form.Id, form.ConcurrencyVersion, form.Alias, form.Name, form.FolderId, save.IsRestricted!.Value, save.UserKey), cancellationToken)
                        ?? throw new FormBuilderConcurrencyException("The form changed while its access was saved.");
                    restriction = new AccessRestriction(changed.Key, changed.IsRestricted, changed.ConcurrencyVersion);
                }

                return new AccessSaved(grants, restriction);
            },
            written =>
            [
                .. edits.Select(edit => GrantEntry(Change(save, edit))),
                .. restrict && written.Restriction is { } restriction
                    ? [new NewAuditEntry(RestrictionAction(restriction.IsRestricted), EntityTypeOf(save.TargetType), save.TargetKey, save.UserKey)]
                    : Array.Empty<NewAuditEntry>(),
            ],
            cancellationToken);
        snapshots.Changed();
        return saved;
    }

    /// <inheritdoc />
    public AccessHostLock? LockOf(PermissionTarget target)
    {
        var host = options.Value;
        for (var i = 0; i < host.LockedSections.Count; i++)
        {
            if (string.Equals(host.LockedSections[i], FormBuilderSettingsSections.Access, StringComparison.OrdinalIgnoreCase))
            {
                return new AccessHostLock(SettingsLocks.WholeSection, $"{SettingsHostRules.LockedSectionsKey}:{i}");
            }
        }

        return target == PermissionTarget.Global && host.Access.Grants.Count > 0 ? new AccessHostLock("grants", HostGrantsKey) : null;
    }

    /// <inheritdoc />
    public Task<AccessSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default) => GetSnapshotAsync(cancellationToken);

    /// <summary>
    /// The snapshot, from this server's cache or read now, one read for every miss while it runs. A change clears the
    /// cached copy and the read under way; a read that began before the change answers its callers but is not kept
    /// (ADR-0043's pattern, <see cref="RuntimeCacheLoads"/>).
    /// </summary>
    internal Task<AccessSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
        RuntimeCacheLoads.GetAsync(appCaches.RuntimeCache, AccessCacheKeys.Snapshot, AccessCacheKeys.Loading, LoadSnapshotAsync, CacheDuration, cancellationToken);

    // Read without a caller's cancellation: other callers may wait for it.
    private async Task<AccessSnapshot> LoadSnapshotAsync()
    {
        var grants = await permissions.ListAsync(CancellationToken.None);
        var tree = await folders.ListAllAsync(CancellationToken.None);
        return AccessSnapshot.Create(
            grants,
            tree.Select(folder => new AccessFolder(folder.Id, folder.Key, folder.ParentId, folder.IsRestricted)),
            await HostGrantsAsync());
    }

    private static string EntityTypeOf(PermissionTarget target) => target switch
    {
        PermissionTarget.Folder => FormBuilderFolderService.EntityType,
        PermissionTarget.Form => FormAudit.EntityType,
        _ => GlobalEntityType,
    };

    private static string RestrictionAction(bool isRestricted) => isRestricted ? RestrictedAction : UnrestrictedAction;

    private static AccessGrant Grant(AccessGrantChange change, StoredGrant? stored) =>
        new(change.UserGroupKey, change.TargetType, change.TargetKey, PermissionRules.FromStored(stored?.Permissions ?? 0));

    // The audit entry of a grant's change: the target, the group, the permissions before and after.
    private static NewAuditEntry GrantEntry(AccessGrantChange change)
    {
        var details = new AuditDetails()
            .AddIdentifier("target", StoredNames.Of(change.TargetType))
            .Add("group", change.UserGroupKey)
            .AddIdentifiers("before", PermissionRules.NamesOf(change.Expected))
            .AddIdentifiers("after", PermissionRules.NamesOf(change.Permissions));
        var item = change.TargetType == PermissionTarget.Global ? (Guid?)null : change.TargetKey;
        return new NewAuditEntry(GrantChangedAction, EntityTypeOf(change.TargetType), item, change.UserKey, details);
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

    // A group's change within a save of several.
    private static AccessGrantChange Change(AccessSave save, AccessGrantEdit edit) =>
        new(edit.UserGroupKey, save.TargetType, save.TargetKey, edit.Expected, edit.Permissions, save.UserKey);

    // The host's locks first: the access section, then the global grants it sets itself (ADR-0048).
    private void CheckLocks(PermissionTarget target)
    {
        switch (LockOf(target))
        {
            case { Field: SettingsLocks.WholeSection } section:
                throw new FormBuilderSettingLockedException(FormBuilderSettingsSections.Access, section.ConfigurationKey);
            case { } grants:
                throw new FormBuilderSettingLockedException(GrantsField, grants.ConfigurationKey);
        }
    }

    // What the model allows, all problems at once; a grant that gives something needs a group Umbraco has.
    private async Task CheckAsync(AccessGrantChange change)
    {
        var errors = await ErrorsAsync(change);
        if (errors.Count > 0)
        {
            throw new FormBuilderSettingsValidationException(errors);
        }
    }

    // What the model refuses of a change, each problem once.
    private async Task<List<SettingsError>> ErrorsAsync(AccessGrantChange change)
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

        return errors;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder's host configuration grants permissions to the user group '{Group}', which the site does not have; it grants nothing")]
    private static partial void LogUnknownHostGroup(ILogger logger, string? group);
}
