using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>A form as the evaluation needs it: its key, its folder and whether it is restricted (ADR-0048).</summary>
/// <param name="Key">The form's key.</param>
/// <param name="FolderKey">Its folder, or null at the root.</param>
/// <param name="IsRestricted">Whether only the grants on it count below its folders.</param>
public sealed record FormAccessTarget(Guid Key, Guid? FolderKey, bool IsRestricted)
{
    /// <summary>The target of a form the form service answered with.</summary>
    public static FormAccessTarget Of(FormInfo form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new FormAccessTarget(form.Key, form.FolderKey, form.IsRestricted);
    }
}

/// <summary>A move of a form or folder, for the groups it gives more access (ADR-0013, ADR-0048).</summary>
/// <param name="ItemType">A form or a folder.</param>
/// <param name="ItemKey">Its key.</param>
/// <param name="IsRestricted">Whether it is restricted, so that a move changes nothing for it.</param>
/// <param name="FromFolderKey">The folder it is in now, or null at the root.</param>
/// <param name="ToFolderKey">The folder it would go to, or null for the root.</param>
internal sealed record AccessMove(PermissionTarget ItemType, Guid ItemKey, bool IsRestricted, Guid? FromFolderKey, Guid? ToFolderKey);

/// <summary>What a user group would gain from a move.</summary>
/// <param name="UserGroupKey">The group.</param>
/// <param name="Gained">The permissions it would hold on the item and did not before.</param>
internal sealed record AccessGain(Guid UserGroupKey, FormBuilderPermissions Gained);

/// <summary>
/// Which forms a list holds for a user (ADR-0048), which the query builder turns into one clause on <c>jfbForm</c>:
/// everything, nothing, or the unrestricted forms in <see cref="FolderIds"/> (and at the root when <see cref="Root"/>)
/// plus the forms with their own grant to one of <see cref="GroupKeys"/>.
/// </summary>
internal sealed record FormVisibility
{
    /// <summary>Every form: an administrator's lists.</summary>
    public static FormVisibility Everything { get; } = new() { IsEverything = true };

    /// <summary>No form.</summary>
    public static FormVisibility Nothing { get; } = new();

    /// <summary>True for every form, whatever the rest says.</summary>
    public bool IsEverything { get; init; }

    /// <summary>Whether the unrestricted forms at the root count.</summary>
    public bool Root { get; init; }

    /// <summary>The folders whose unrestricted forms count.</summary>
    public IReadOnlyCollection<int> FolderIds { get; init; } = [];

    /// <summary>The user's groups that hold a grant of <see cref="AnyOf"/> on a form of their own.</summary>
    public IReadOnlyCollection<Guid> GroupKeys { get; init; } = [];

    /// <summary>The permissions of which a form's own grant must hold one.</summary>
    public FormBuilderPermissions AnyOf { get; init; } = PermissionRules.FolderLevel;

    /// <summary>True when no form can count.</summary>
    public bool IsNothing => !IsEverything && !Root && FolderIds.Count == 0 && GroupKeys.Count == 0;
}

/// <summary>Which folders a folder list holds for a user: those that lead to something the user may see (ADR-0048).</summary>
/// <param name="IsEverything">True for every folder.</param>
/// <param name="FolderKeys">The folders it holds, unless <paramref name="IsEverything"/>.</param>
public sealed record FolderVisibility(bool IsEverything, IReadOnlySet<Guid> FolderKeys)
{
    /// <summary>Every folder: an administrator's lists.</summary>
    public static FolderVisibility Everything { get; } = new(true, FrozenSet<Guid>.Empty);

    /// <summary>Whether the list holds the folder.</summary>
    public bool Allows(Guid folderKey) => IsEverything || FolderKeys.Contains(folderKey);
}

/// <summary>A change of a group's grant, from the permissions the editor saw to the new ones (ADR-0048).</summary>
/// <param name="UserGroupKey">The Umbraco user group.</param>
/// <param name="TargetType">Where it applies.</param>
/// <param name="TargetKey">The folder's or form's key, or <see cref="Guid.Empty"/> for a global grant.</param>
/// <param name="Expected">The permissions the grant holds now as the editor saw it, none for no grant.</param>
/// <param name="Permissions">The permissions it should hold, none to remove it.</param>
/// <param name="UserKey">Who changes it, or null when Jiron Form Builder does.</param>
internal sealed record AccessGrantChange(
    Guid UserGroupKey,
    PermissionTarget TargetType,
    Guid TargetKey,
    FormBuilderPermissions Expected,
    FormBuilderPermissions Permissions,
    Guid? UserKey);

/// <summary>A group's grant on a target after a change.</summary>
/// <param name="UserGroupKey">The Umbraco user group.</param>
/// <param name="TargetType">Where it applies.</param>
/// <param name="TargetKey">The folder's or form's key, or <see cref="Guid.Empty"/> for a global grant.</param>
/// <param name="Permissions">The permissions it holds, none when it was removed.</param>
internal sealed record AccessGrant(Guid UserGroupKey, PermissionTarget TargetType, Guid TargetKey, FormBuilderPermissions Permissions);

/// <summary>A folder's or form's restriction after a change.</summary>
/// <param name="Key">The folder's or form's key.</param>
/// <param name="IsRestricted">Whether it is restricted now.</param>
/// <param name="ConcurrencyVersion">A form's concurrency version after the change; null for a folder.</param>
internal sealed record AccessRestriction(Guid Key, bool IsRestricted, int? ConcurrencyVersion);

/// <summary>One group's grant in a save of several (ADR-0218): the permissions the editor saw and the new ones.</summary>
/// <param name="UserGroupKey">The Umbraco user group.</param>
/// <param name="Expected">The permissions the grant holds now as the editor saw it, none for no grant.</param>
/// <param name="Permissions">The permissions it should hold, none to remove it.</param>
internal sealed record AccessGrantEdit(Guid UserGroupKey, FormBuilderPermissions Expected, FormBuilderPermissions Permissions);

/// <summary>
/// A save of a target's grants, and of a folder's or form's restriction, in one unit of work (ADR-0218): a grant
/// changed meanwhile, or a form changed since it was read, refuses all of it, so nothing is left half saved.
/// </summary>
/// <param name="TargetType">Where the grants apply.</param>
/// <param name="TargetKey">The folder's or form's key, or <see cref="Guid.Empty"/> for the global grants.</param>
/// <param name="Grants">The groups' grants, each group once; one whose permissions stay as they were is left as it is.</param>
/// <param name="IsRestricted">A folder's or form's restriction; null to leave it, and always null for the global grants.</param>
/// <param name="ExpectedConcurrencyVersion">A form's concurrency version as the editor read it, which a change of its restriction needs.</param>
/// <param name="UserKey">Who saves, or null when Jiron Form Builder does.</param>
internal sealed record AccessSave(
    PermissionTarget TargetType,
    Guid TargetKey,
    IReadOnlyList<AccessGrantEdit> Grants,
    bool? IsRestricted,
    int? ExpectedConcurrencyVersion,
    Guid? UserKey);

/// <summary>A host rule that keeps access from being changed here (ADR-0048): what it holds, and its key.</summary>
/// <param name="Field">The access section's field it holds: <c>*</c> for the whole section, <c>grants</c> for the global grants.</param>
/// <param name="ConfigurationKey">The configuration key behind it.</param>
internal sealed record AccessHostLock(string Field, string ConfigurationKey);
