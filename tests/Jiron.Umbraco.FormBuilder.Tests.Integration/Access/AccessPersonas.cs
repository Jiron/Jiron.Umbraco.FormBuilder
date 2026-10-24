using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// The test personas of ADR-0013 on a real site: a user group per persona with its global grants, one user in each,
/// Umbraco's administrators and writers for the administrator and the outsider, and the HR team, a separate group that
/// later gets its own grant on a restricted folder. The administrators have the Forms section from the migration (4.1).
/// </summary>
internal sealed class AccessPersonas(TestPeople people, IUserService users, IFormBuilderAccessService access)
{
    /// <summary>The Forms section's alias, which every persona's group but the outsider's opens.</summary>
    public const string Section = "Jiron.FormBuilder.Section";

    /// <summary>The personas with their own group, its alias and its global grants (ADR-0013).</summary>
    public static readonly (string Role, string Group, FormBuilderPermissions Grants)[] Groups =
    [
        ("designer", "jfbDesigners", FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions | FormBuilderPermissions.ManageLibrary),
        ("entryHandler", "jfbEntryHandlers", FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries | FormBuilderPermissions.Export),
        ("viewer", "jfbViewers", FormBuilderPermissions.ViewEntries),
        ("privacyOfficer", "jfbPrivacyOfficers", FormBuilderPermissions.PrivacyTools | FormBuilderPermissions.ViewEntries | FormBuilderPermissions.ViewSensitive | FormBuilderPermissions.DeleteEntries),
        ("noAccess", "jfbNoAccess", FormBuilderPermissions.None),
        ("hr", "jfbHrTeam", FormBuilderPermissions.None),
    ];

    /// <summary>Every persona: those of <see cref="Groups"/>, the administrator and the outsider.</summary>
    public static readonly string[] Roles = ["admin", .. Groups.Select(group => group.Role), "outsider"];

    /// <summary>Creates the groups with their grants and a user per persona, and reads each user back as the evaluation sees them.</summary>
    public async Task<Personas> CreateAsync()
    {
        var groupKeys = new Dictionary<string, Guid>();
        foreach (var (role, alias, grants) in Groups)
        {
            var group = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = alias, Name = alias, Sections = { Section } });
            groupKeys[role] = group.Key;
            if (grants != FormBuilderPermissions.None)
            {
                await access.ChangeGrantAsync(new AccessGrantChange(group.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, grants, null));
            }
        }

        var memberships = Groups.ToDictionary(group => group.Role, group => group.Group);
        memberships["admin"] = Constants.Security.AdminGroupAlias;
        memberships["outsider"] = "writer";

        var accessUsers = new Dictionary<string, AccessUser>();
        foreach (var role in Roles)
        {
            var created = await people.EnsureUserAsync(new TestUser { Email = $"{role}@jfb.test", Name = role, Password = TestPeople.NewPassword(), Groups = { memberships[role] } });
            var user = await users.GetAsync(created.Key) ?? throw new InvalidOperationException($"The user of {role} is missing.");
            accessUsers[role] = AccessUser.Of(user);
        }

        return new Personas(accessUsers, groupKeys);
    }
}

/// <summary>The personas as the evaluation sees them, and the keys of their own groups.</summary>
/// <param name="Users">Each persona's user, by role.</param>
/// <param name="GroupKeys">The key of each persona's own group, by role.</param>
internal sealed record Personas(IReadOnlyDictionary<string, AccessUser> Users, IReadOnlyDictionary<string, Guid> GroupKeys)
{
    /// <summary>A persona's user.</summary>
    public AccessUser this[string role] => Users[role];
}
