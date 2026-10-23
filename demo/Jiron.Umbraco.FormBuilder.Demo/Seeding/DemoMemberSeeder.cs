using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Demo.Seeding;

/// <summary>
/// The members area (ROADMAP 3.3): the member group "Library members", the generated members from
/// <see cref="DemoOptions"/>, and public access on the members page with the sign-in page for visitors who are not
/// signed in. Idempotent, like the rest of the seeding. Members are found by email address; their passwords are never
/// logged.
/// </summary>
internal sealed partial class DemoMemberSeeder(
    IMemberGroupService memberGroups,
    IMemberTypeService memberTypes,
    IMemberService memberService,
    IMemberEditingService memberEditing,
    IUserService users,
    IContentService contentService,
    IPublicAccessService publicAccess,
    IOptions<DemoOptions> options,
    ILogger<DemoMemberSeeder> logger)
{
    public const string GroupName = "Library members";

    /// <summary>Creates what is missing; true when anything was created.</summary>
    public async Task<bool> SeedAsync(DemoContent content)
    {
        var created = false;
        var group = await memberGroups.GetByNameAsync(GroupName);
        if (group is null)
        {
            var attempt = await memberGroups.CreateAsync(new MemberGroup { Name = GroupName });
            DemoSeeder.Check(attempt, $"the member group {GroupName}");
            group = attempt.Result!;
            created = true;
        }

        var generated = options.Value.Members.Where(member => member.Email.Length > 0 && member.Password.Length > 0).ToList();
        if (generated.Count == 0)
        {
            LogNoMembers(logger);
        }

        var memberType = memberTypes.Get(Constants.Conventions.MemberTypes.DefaultAlias)
            ?? throw new InvalidOperationException("Demo seeding: Umbraco's default member type is missing.");
        var superUser = await users.GetAsync(Constants.Security.SuperUserKey)
            ?? throw new InvalidOperationException("Demo seeding: the super user is missing.");
        foreach (var member in generated.Where(member => memberService.GetByEmail(member.Email) is null))
        {
            var model = new MemberCreateModel
            {
                Email = member.Email,
                Username = member.Email,
                Password = member.Password,
                ContentTypeKey = memberType.Key,
                IsApproved = true,
                Roles = [group.Key],
                Variants = [new VariantModel { Name = member.Name }],
            };
            var attempt = await memberEditing.CreateAsync(model, superUser);
            DemoSeeder.Check(attempt, $"the member {member.Name}");
            created = true;
        }

        var membersPage = contentService.GetById(content.PageOfType(DemoContent.MembersType).Key)
            ?? throw new InvalidOperationException("Demo seeding: the members page is missing.");
        if (publicAccess.GetEntryForContent(membersPage) is null)
        {
            var signIn = content.PageOfType(DemoContent.LoginType).Key;
            var entry = new PublicAccessEntrySlim
            {
                ContentId = membersPage.Key,
                MemberGroupNames = [GroupName],
                MemberUserNames = [],
                LoginPageId = signIn,
                ErrorPageId = signIn,
            };
            DemoSeeder.Check(await publicAccess.CreateAsync(entry), "the members page's public access");
            created = true;
        }

        return created;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo seeding: no members were passed in; start the demo with scripts/demo.ps1")]
    private static partial void LogNoMembers(ILogger logger);
}
