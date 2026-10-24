using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Testing.People;

/// <summary>
/// Gives a test host's user groups their Jiron Form Builder grants and seeds its restricted folders, through the access
/// and folder services as an editor would (ADR-0013, ADR-0049). Every run brings each grant to what the settings say,
/// whatever it held, so a host that restarts on its database ends where a fresh one does. A problem throws
/// <see cref="InvalidOperationException"/> naming what to fix.
/// </summary>
internal sealed class TestAccess(IFormBuilderAccessService access, IFormBuilderFolderService folders, IPermissionRepository grants, IUserGroupService userGroups)
{
    /// <summary>Sets the group's global grant to exactly these permissions.</summary>
    public async Task GrantGloballyAsync(string groupAlias, IEnumerable<string> permissions) =>
        await GrantAsync(await GroupKeyAsync(groupAlias), PermissionTarget.Global, Guid.Empty, Parse(permissions, groupAlias));

    /// <summary>The root folder of that name outside the trash, made if missing, restricted, with its grant to its group.</summary>
    public async Task<Guid> EnsureRestrictedFolderAsync(TestRestrictedFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var groupKey = await GroupKeyAsync(folder.Group);
        var permissions = Parse(folder.Permissions, folder.Name);
        var existing = (await folders.ListAsync()).FirstOrDefault(found => found is { ParentKey: null, TrashedUtc: null } && found.Name == folder.Name);
        var key = existing?.Key ?? (await folders.CreateAsync(new NewFolderRequest(folder.Name))).Key;
        await access.RestrictFolderAsync(key, true, null);
        await GrantAsync(groupKey, PermissionTarget.Folder, key, permissions);
        return key;
    }

    private static FormBuilderPermissions Parse(IEnumerable<string> names, string owner)
    {
        ArgumentNullException.ThrowIfNull(names);
        var permissions = FormBuilderPermissions.None;
        foreach (var name in names)
        {
            permissions |= PermissionRules.TryParse(name, out var permission)
                ? permission
                : throw new InvalidOperationException($"The test permission '{name}' of '{owner}' is not a permission.");
        }

        return permissions;
    }

    private async Task<Guid> GroupKeyAsync(string alias) =>
        (await userGroups.GetAsync(alias))?.Key ?? throw new InvalidOperationException($"The test user group '{alias}' does not exist.");

    private async Task GrantAsync(Guid groupKey, PermissionTarget target, Guid targetKey, FormBuilderPermissions permissions)
    {
        var held = PermissionRules.FromStored((await grants.GetAsync(groupKey, target, targetKey))?.Permissions ?? 0);
        try
        {
            await access.ChangeGrantAsync(new AccessGrantChange(groupKey, target, targetKey, held, permissions, null));
        }
        catch (FormBuilderSettingsValidationException refused)
        {
            throw new InvalidOperationException($"The test grant was refused: {string.Join(" ", refused.Errors.Select(error => error.Message))}", refused);
        }
    }
}
