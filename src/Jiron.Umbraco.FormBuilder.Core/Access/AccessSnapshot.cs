using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>A folder as the snapshot holds it.</summary>
/// <param name="Id">The folder's row id.</param>
/// <param name="Key">Its key.</param>
/// <param name="ParentId">The folder it is in, or null at the root.</param>
/// <param name="IsRestricted">Whether only the grants on it and below count below it.</param>
internal sealed record AccessFolder(int Id, Guid Key, int? ParentId, bool IsRestricted);

/// <summary>
/// The inputs of every permission check, read once per server (ADR-0048): every grant, the folder tree with the trash,
/// and the host's global grants. It evaluates in memory: a user's permissions globally, at the root, on a folder and on
/// a form, the scope of their lists, and what a move gives each group. Forms are not in it; a check of a form brings
/// the form's own row.
/// </summary>
internal sealed class AccessSnapshot
{
    private readonly Dictionary<Guid, GroupGrants> _groups;
    private readonly Dictionary<int, AccessFolder> _byId;
    private readonly Dictionary<Guid, AccessFolder> _byKey;

    private AccessSnapshot(Dictionary<Guid, GroupGrants> groups, Dictionary<int, AccessFolder> byId)
    {
        _groups = groups;
        _byId = byId;
        _byKey = byId.Values.ToDictionary(folder => folder.Key);
    }

    /// <summary>Whether the host configuration sets the global grants, so the stored ones count for nothing.</summary>
    public bool HostGrants { get; private init; }

    /// <summary>The folders, by row id.</summary>
    public IReadOnlyDictionary<int, AccessFolder> Folders => _byId;

    /// <summary>
    /// A snapshot of stored grants and folders. With <paramref name="hostGrants"/>, the global grants are the host's
    /// (by group key) and the stored global grants are left out.
    /// </summary>
    public static AccessSnapshot Create(IEnumerable<StoredGrant> grants, IEnumerable<AccessFolder> folders, IReadOnlyDictionary<Guid, FormBuilderPermissions>? hostGrants = null)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(folders);
        var groups = new Dictionary<Guid, GroupGrants>();
        GroupGrants Of(Guid groupKey) => groups.TryGetValue(groupKey, out var found) ? found : groups[groupKey] = new GroupGrants();

        foreach (var grant in grants)
        {
            var permissions = PermissionRules.FromStored(grant.Permissions);
            switch (grant.TargetType)
            {
                case PermissionTarget.Global when hostGrants is null:
                    Of(grant.UserGroupKey).Global |= permissions;
                    break;
                case PermissionTarget.Folder:
                    Of(grant.UserGroupKey).Folders[grant.TargetKey] = permissions & PermissionRules.FolderLevel;
                    break;
                case PermissionTarget.Form:
                    Of(grant.UserGroupKey).Forms[grant.TargetKey] = permissions & PermissionRules.FolderLevel;
                    break;
            }
        }

        foreach (var (groupKey, permissions) in hostGrants ?? new Dictionary<Guid, FormBuilderPermissions>())
        {
            Of(groupKey).Global |= permissions & PermissionRules.All;
        }

        return new AccessSnapshot(groups, folders.ToDictionary(folder => folder.Id)) { HostGrants = hostGrants is not null };
    }

    /// <summary>
    /// The user's permissions globally: every permission for an administrator, otherwise the union of their groups'
    /// global grants; nothing without the Forms section.
    /// </summary>
    public FormBuilderPermissions Global(AccessUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Evaluate(user, PermissionRules.All, groups => OwnGlobal(groups));
    }

    /// <summary>
    /// The user's permissions on a folder (null: the root), for what happens in it: the folder's own grants, plus what
    /// its parent gives unless it is restricted. A folder the snapshot does not know gives only its own grants.
    /// </summary>
    public FormBuilderPermissions Folder(AccessUser user, Guid? folderKey)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Evaluate(user, PermissionRules.FolderLevel, groups => FolderOf(groups, folderKey));
    }

    /// <summary>The user's permissions on a form: its own grants, plus what its folder gives unless it is restricted.</summary>
    public FormBuilderPermissions Form(AccessUser user, FormAccessTarget form)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(form);
        return Evaluate(user, PermissionRules.FolderLevel, groups => FormOf(groups, form));
    }

    /// <summary>The forms a list holds for the user: those where the user holds one of <paramref name="anyOf"/>.</summary>
    public FormVisibility FormVisibilityOf(AccessUser user, FormBuilderPermissions anyOf)
    {
        ArgumentNullException.ThrowIfNull(user);
        anyOf &= PermissionRules.FolderLevel;
        if (anyOf == FormBuilderPermissions.None)
        {
            throw new ArgumentException("A list holds forms by the permissions granted on folders and forms.", nameof(anyOf));
        }

        if (!user.HasSection)
        {
            return FormVisibility.Nothing;
        }

        if (user.IsAdministrator)
        {
            return FormVisibility.Everything;
        }

        var groups = GroupsOf(user);
        var memo = new Dictionary<int, FormBuilderPermissions>();
        return new FormVisibility
        {
            Root = (Root(groups) & anyOf) != 0,
            FolderIds = [.. _byId.Values.Where(folder => (FolderOf(groups, folder, memo) & anyOf) != 0).Select(folder => folder.Id).Order()],
            GroupKeys = [.. user.GroupKeys.Where(key => _groups.TryGetValue(key, out var grants) && grants.Forms.Values.Any(permissions => (permissions & anyOf) != 0)).Order()],
            AnyOf = anyOf,
        };
    }

    /// <summary>
    /// The folders a folder list holds for the user of <paramref name="forms"/>: those whose forms it holds, the folders
    /// that hold forms with their own grant (<paramref name="grantedFormFolderIds"/>), and every folder above them.
    /// </summary>
    public FolderVisibility FolderVisibilityOf(FormVisibility forms, IEnumerable<int> grantedFormFolderIds)
    {
        ArgumentNullException.ThrowIfNull(forms);
        ArgumentNullException.ThrowIfNull(grantedFormFolderIds);
        if (forms.IsEverything)
        {
            return FolderVisibility.Everything;
        }

        var keys = new HashSet<Guid>();
        foreach (var id in forms.FolderIds.Concat(grantedFormFolderIds))
        {
            // Up to the root, or to a folder already reached; a parent the snapshot does not know ends the walk.
            for (int? current = id; current is { } at && _byId.TryGetValue(at, out var folder) && keys.Add(folder.Key);)
            {
                current = folder.ParentId;
            }
        }

        return new FolderVisibility(false, keys);
    }

    /// <summary>
    /// The groups a move gives more access to the item, and what each gains: what the new place gives and the old one
    /// did not, apart from what the item's own grants give anyway. A restricted item gains nothing. Umbraco's
    /// administrators hold everything already and are left out.
    /// </summary>
    public IReadOnlyList<AccessGain> MoveGains(AccessMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (move.IsRestricted)
        {
            return [];
        }

        var gains = new List<AccessGain>();
        foreach (var (groupKey, grants) in _groups.OrderBy(pair => pair.Key))
        {
            if (groupKey == Constants.Security.AdminGroupKey)
            {
                continue;
            }

            GroupGrants[] one = [grants];
            var own = (move.ItemType == PermissionTarget.Folder ? grants.Folders : grants.Forms).GetValueOrDefault(move.ItemKey);
            var gained = FolderOf(one, move.ToFolderKey) & ~FolderOf(one, move.FromFolderKey) & ~own;
            if (gained != FormBuilderPermissions.None)
            {
                gains.Add(new AccessGain(groupKey, gained));
            }
        }

        return gains;
    }

    private static FormBuilderPermissions OwnGlobal(IReadOnlyList<GroupGrants> groups) =>
        groups.Aggregate(FormBuilderPermissions.None, (permissions, grants) => permissions | grants.Global);

    private static FormBuilderPermissions Own(IReadOnlyList<GroupGrants> groups, Func<GroupGrants, Dictionary<Guid, FormBuilderPermissions>> targets, Guid key) =>
        groups.Aggregate(FormBuilderPermissions.None, (permissions, grants) => permissions | targets(grants).GetValueOrDefault(key));

    private static FormBuilderPermissions Root(IReadOnlyList<GroupGrants> groups) => OwnGlobal(groups) & PermissionRules.FolderLevel;

    // Nothing without the section, everything of the kind for an administrator, otherwise the rule for the user's groups.
    private FormBuilderPermissions Evaluate(AccessUser user, FormBuilderPermissions everything, Func<IReadOnlyList<GroupGrants>, FormBuilderPermissions> rule) =>
        !user.HasSection ? FormBuilderPermissions.None
        : user.IsAdministrator ? everything
        : rule(GroupsOf(user));

    private List<GroupGrants> GroupsOf(AccessUser user) =>
        [.. user.GroupKeys.Where(_groups.ContainsKey).Select(key => _groups[key])];

    private FormBuilderPermissions FormOf(IReadOnlyList<GroupGrants> groups, FormAccessTarget form) =>
        Own(groups, grants => grants.Forms, form.Key) | (form.IsRestricted ? FormBuilderPermissions.None : FolderOf(groups, form.FolderKey));

    private FormBuilderPermissions FolderOf(IReadOnlyList<GroupGrants> groups, Guid? folderKey)
    {
        if (folderKey is not { } key)
        {
            return Root(groups);
        }

        return _byKey.TryGetValue(key, out var folder)
            ? FolderOf(groups, folder, new Dictionary<int, FormBuilderPermissions>())
            : Own(groups, grants => grants.Folders, key);
    }

    // A folder's permissions: up the tree to the nearest folder already evaluated, a restricted one (which inherits
    // nothing), the root, or a parent the snapshot does not know (nothing comes from there), then down again, keeping
    // each folder's result.
    private FormBuilderPermissions FolderOf(IReadOnlyList<GroupGrants> groups, AccessFolder start, Dictionary<int, FormBuilderPermissions> memo)
    {
        var chain = new List<AccessFolder>();
        var inherited = FormBuilderPermissions.None;
        for (var current = start; ;)
        {
            if (memo.TryGetValue(current.Id, out var known))
            {
                inherited = known;
                break;
            }

            chain.Add(current);

            // A walk longer than the tree has folders meets a cycle, which no folder may have: it fails closed.
            if (current.IsRestricted || chain.Count > _byId.Count)
            {
                break;
            }

            if (current.ParentId is not { } parentId)
            {
                inherited = Root(groups);
                break;
            }

            if (!_byId.TryGetValue(parentId, out var parent))
            {
                break;
            }

            current = parent;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var folder = chain[i];
            inherited = Own(groups, grants => grants.Folders, folder.Key) | inherited;
            memo[folder.Id] = inherited;
        }

        return memo[start.Id];
    }

    // One group's grants, gathered while the snapshot is made and never changed after.
    private sealed class GroupGrants
    {
        public FormBuilderPermissions Global { get; set; }

        public Dictionary<Guid, FormBuilderPermissions> Folders { get; } = new();

        public Dictionary<Guid, FormBuilderPermissions> Forms { get; } = new();
    }
}
