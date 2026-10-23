using System.Security.Cryptography;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Testing.People;

/// <summary>
/// Creates backoffice user groups, users, member groups and members for tests (ADR-0011): integration tests call it
/// directly, and a test host's <see cref="TestPeopleSeeder"/> calls it with the configured people. Each method leaves
/// what exists as it is, so a host that starts again changes nothing. A failure throws
/// <see cref="InvalidOperationException"/> with Umbraco's status, never with a password.
/// </summary>
public sealed class TestPeople(
    IUserService users,
    IUserGroupService userGroups,
    IShortStringHelper shortStrings,
    IMemberService members,
    IMemberGroupService memberGroups,
    IMemberTypeService memberTypes,
    IMemberEditingService memberEditing)
{
    /// <summary>
    /// A new random password that any Umbraco password policy accepts: 32 random letters and digits, plus one capital,
    /// one small letter, one digit and one symbol.
    /// </summary>
    public static string NewPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=') + "Aa1!";

    /// <summary>Creates the user group unless its alias exists; returns the group either way.</summary>
    public async Task<IUserGroup> EnsureUserGroupAsync(TestUserGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (await userGroups.GetAsync(group.Alias) is { } existing)
        {
            return existing;
        }

        var userGroup = new UserGroup(shortStrings)
        {
            Alias = group.Alias,
            Name = string.IsNullOrWhiteSpace(group.Name) ? group.Alias : group.Name,
            Icon = "icon-users",
            HasAccessToAllLanguages = true,
        };
        foreach (var section in group.Sections)
        {
            userGroup.AddAllowedSection(section);
        }

        var created = await userGroups.CreateAsync(userGroup, Constants.Security.SuperUserKey);
        if (!created.Success || created.Result is null)
        {
            throw new InvalidOperationException($"Could not create the test user group '{group.Alias}': {created.Status}.");
        }

        return created.Result;
    }

    /// <summary>Creates the user, approved, with its password and groups, unless its email address exists.</summary>
    public async Task<IUser> EnsureUserAsync(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrWhiteSpace(user.Email) || string.IsNullOrEmpty(user.Password))
        {
            throw new InvalidOperationException("Every test user needs an email address and a password.");
        }

        if (users.GetByEmail(user.Email) is { } existing)
        {
            return existing;
        }

        var groupKeys = new HashSet<Guid>();
        foreach (var alias in user.Groups)
        {
            var group = await userGroups.GetAsync(alias)
                ?? throw new InvalidOperationException($"The test user {user.Email} names the unknown group '{alias}'.");
            groupKeys.Add(group.Key);
        }

        var created = await users.CreateAsync(
            Constants.Security.SuperUserKey,
            new UserCreateModel
            {
                Email = user.Email,
                UserName = user.Email,
                Name = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
                Kind = UserKind.Default,
                UserGroupKeys = groupKeys,
            },
            approveUser: true);
        var createdUser = created.Result.CreatedUser;
        if (!created.Success || createdUser is null)
        {
            throw new InvalidOperationException($"Could not create the test user {user.Email}: {created.Status}.");
        }

        var changed = await users.ChangePasswordAsync(
            Constants.Security.SuperUserKey,
            new ChangeUserPasswordModel { UserKey = createdUser.Key, NewPassword = user.Password });
        if (!changed.Success)
        {
            throw new InvalidOperationException($"Could not set the password of the test user {user.Email}: {changed.Status}.");
        }

        return createdUser;
    }

    /// <summary>Creates the member group unless its name exists; returns the group either way.</summary>
    public async Task<IMemberGroup> EnsureMemberGroupAsync(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (await memberGroups.GetByNameAsync(name) is { } existing)
        {
            return existing;
        }

        var created = await memberGroups.CreateAsync(new MemberGroup { Name = name });
        if (!created.Success || created.Result is null)
        {
            throw new InvalidOperationException($"Could not create the test member group '{name}': {created.Status}.");
        }

        return created.Result;
    }

    /// <summary>
    /// Creates the member, approved, of Umbraco's default member type, with its password and groups (missing groups
    /// are created), unless its email address exists.
    /// </summary>
    public async Task<IMember> EnsureMemberAsync(TestMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (string.IsNullOrWhiteSpace(member.Email) || string.IsNullOrEmpty(member.Password))
        {
            throw new InvalidOperationException("Every test member needs an email address and a password.");
        }

        if (members.GetByEmail(member.Email) is { } existing)
        {
            return existing;
        }

        var groupKeys = new List<Guid>();
        foreach (var name in member.Groups)
        {
            groupKeys.Add((await EnsureMemberGroupAsync(name)).Key);
        }

        var memberType = memberTypes.Get(Constants.Conventions.MemberTypes.DefaultAlias)
            ?? throw new InvalidOperationException("Umbraco's default member type is missing.");
        var superUser = await users.GetAsync(Constants.Security.SuperUserKey)
            ?? throw new InvalidOperationException("Umbraco's super user is missing.");
        var created = await memberEditing.CreateAsync(
            new MemberCreateModel
            {
                Email = member.Email,
                Username = member.Email,
                Password = member.Password,
                ContentTypeKey = memberType.Key,
                IsApproved = true,
                Roles = groupKeys,
                Variants = [new VariantModel { Name = string.IsNullOrWhiteSpace(member.Name) ? member.Email : member.Name }],
            },
            superUser);
        if (!created.Success)
        {
            throw new InvalidOperationException($"Could not create the test member {member.Email}: {created.Status}.");
        }

        return members.GetByEmail(member.Email)
            ?? throw new InvalidOperationException($"The test member {member.Email} was created but cannot be found.");
    }
}
