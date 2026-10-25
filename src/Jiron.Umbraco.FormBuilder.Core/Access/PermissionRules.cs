using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>A permission granted without the one it needs, such as <c>export</c> without <c>viewEntries</c>.</summary>
/// <param name="Permission">The permission granted.</param>
/// <param name="Needs">The permission it needs, which the grant lacks.</param>
internal sealed record MissingDependency(FormBuilderPermissions Permission, FormBuilderPermissions Needs);

/// <summary>
/// What the permissions mean together (ADR-0013, ADR-0048): their names, which ones a folder or form can be granted,
/// and which one each needs.
/// </summary>
internal static class PermissionRules
{
    /// <summary>The permissions that can be granted on folders and forms; only these flow down the tree.</summary>
    public const FormBuilderPermissions FolderLevel =
        FormBuilderPermissions.Design
        | FormBuilderPermissions.ManageActions
        | FormBuilderPermissions.ViewEntries
        | FormBuilderPermissions.ViewSensitive
        | FormBuilderPermissions.EditEntries
        | FormBuilderPermissions.DeleteEntries
        | FormBuilderPermissions.Export;

    /// <summary>The permissions granted only globally.</summary>
    public const FormBuilderPermissions GlobalOnly =
        FormBuilderPermissions.ManageLibrary
        | FormBuilderPermissions.ManageSettings
        | FormBuilderPermissions.ManagePermissions
        | FormBuilderPermissions.PrivacyTools;

    /// <summary>Every permission: what an administrator holds everywhere.</summary>
    public const FormBuilderPermissions All = FolderLevel | GlobalOnly;

    // Each permission with its name, in the order of ADR-0013's table.
    private static readonly ImmutableArray<(FormBuilderPermissions Permission, string Name)> Named =
    [
        (FormBuilderPermissions.Design, "design"),
        (FormBuilderPermissions.ManageActions, "manageActions"),
        (FormBuilderPermissions.ViewEntries, "viewEntries"),
        (FormBuilderPermissions.ViewSensitive, "viewSensitive"),
        (FormBuilderPermissions.EditEntries, "editEntries"),
        (FormBuilderPermissions.DeleteEntries, "deleteEntries"),
        (FormBuilderPermissions.Export, "export"),
        (FormBuilderPermissions.ManageLibrary, "manageLibrary"),
        (FormBuilderPermissions.ManageSettings, "manageSettings"),
        (FormBuilderPermissions.ManagePermissions, "managePermissions"),
        (FormBuilderPermissions.PrivacyTools, "privacyTools"),
    ];

    /// <summary>Every permission on its own, in the order of ADR-0013's table.</summary>
    public static IReadOnlyList<FormBuilderPermissions> Each { get; } = [.. Named.Select(named => named.Permission)];

    /// <summary>The name of one permission, such as <c>viewEntries</c>.</summary>
    public static string NameOf(FormBuilderPermissions permission)
    {
        foreach (var (single, name) in Named)
        {
            if (single == permission)
            {
                return name;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(permission), permission, "Not one permission.");
    }

    /// <summary>The names of the permissions in a set, in the order of ADR-0013's table.</summary>
    public static IReadOnlyList<string> NamesOf(FormBuilderPermissions permissions) =>
        [.. Named.Where(named => (permissions & named.Permission) != 0).Select(named => named.Name)];

    /// <summary>The permission a name stands for, in any letter case; false for a name that is none.</summary>
    public static bool TryParse(string? name, out FormBuilderPermissions permission)
    {
        foreach (var (single, known) in Named)
        {
            if (string.Equals(known, name, StringComparison.OrdinalIgnoreCase))
            {
                permission = single;
                return true;
            }
        }

        permission = FormBuilderPermissions.None;
        return false;
    }

    /// <summary>
    /// The permissions a stored value holds. A value that a later version adds, and that this one does not know, is
    /// left out, so it grants nothing here.
    /// </summary>
    public static FormBuilderPermissions FromStored(int stored) => (FormBuilderPermissions)stored & All;

    /// <summary>The permission that <paramref name="permission"/> needs, or none.</summary>
    public static FormBuilderPermissions Needs(FormBuilderPermissions permission) => permission switch
    {
        FormBuilderPermissions.ViewSensitive or FormBuilderPermissions.EditEntries or FormBuilderPermissions.DeleteEntries or FormBuilderPermissions.Export => FormBuilderPermissions.ViewEntries,
        FormBuilderPermissions.ManageActions => FormBuilderPermissions.Design,
        _ => FormBuilderPermissions.None,
    };

    /// <summary>Each permission in the set whose dependency the set lacks, in the order of ADR-0013's table.</summary>
    public static IReadOnlyList<MissingDependency> MissingDependencies(FormBuilderPermissions permissions) =>
        [.. Each.Where(permission => (permissions & permission) != 0)
            .Select(permission => new MissingDependency(permission, Needs(permission)))
            .Where(missing => missing.Needs != FormBuilderPermissions.None && (permissions & missing.Needs) == 0)];

    /// <summary>The permissions a target cannot be granted: the global-only ones on a folder or form.</summary>
    public static FormBuilderPermissions NotGrantable(PermissionTarget target, FormBuilderPermissions permissions) =>
        target == PermissionTarget.Global ? FormBuilderPermissions.None : permissions & GlobalOnly;
}
