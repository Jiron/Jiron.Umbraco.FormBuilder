using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// The user and member factories and the host seeder of the Testing project, against a real Umbraco with Jiron Form
/// Builder's tables, where the seeder also gives the groups their grants and seeds the restricted folders.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class TestPeopleTests : FormBuilderMigrationTestBase
{
    private const string Designer = "designer@people.jfb.test";
    private const string Member = "member@people.jfb.test";

    private TestPeople People => ActivatorUtilities.CreateInstance<TestPeople>(Services);

    [SetUp]
    public async Task MigrateAsync()
    {
        var result = await RunAsync(new FormBuilderMigrationPlan());
        Assert.That(result.Successful, Is.True, result.Exception?.ToString());
    }

    [Test]
    public async Task EnsureUserAsync_WithAGroup_CreatesAnApprovedUserWhoCanSignIn()
    {
        var password = TestPeople.NewPassword();
        await People.EnsureUserGroupAsync(DesignersGroup());

        await People.EnsureUserAsync(User(password, "jfbDesigners", "editor"));

        var user = GetRequiredService<IUserService>().GetByEmail(Designer);
        var group = await GetRequiredService<IUserGroupService>().GetAsync("jfbDesigners");
        var signsIn = await SignsInAsync(Designer, password);
        Assert.Multiple(() =>
        {
            Assert.That(user, Is.Not.Null);
            Assert.That(user!.Name, Is.EqualTo("Dana Designer"));
            Assert.That(user.IsApproved, Is.True);
            Assert.That(user.Groups.Select(g => g.Alias), Is.EquivalentTo(new[] { "jfbDesigners", "editor" }));
            Assert.That(group!.AllowedSections, Is.EquivalentTo(new[] { "content" }));
            Assert.That(signsIn, Is.True);
        });
    }

    [Test]
    public async Task EnsureUserAsync_RunTwice_LeavesTheUserAsItIs()
    {
        var password = TestPeople.NewPassword();
        await People.EnsureUserAsync(User(password, "editor"));

        await People.EnsureUserAsync(User(TestPeople.NewPassword(), "editor"));

        Assert.That(await SignsInAsync(Designer, password), Is.True);
    }

    [Test]
    public void EnsureUserAsync_UnknownGroup_FailsWithoutCreatingTheUser()
    {
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => People.EnsureUserAsync(User(TestPeople.NewPassword(), "noSuchGroup")));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("noSuchGroup"));
            Assert.That(GetRequiredService<IUserService>().GetByEmail(Designer), Is.Null);
        });
    }

    [Test]
    public void EnsureUserAsync_WithoutPassword_Fails()
    {
        Assert.ThrowsAsync<InvalidOperationException>(() => People.EnsureUserAsync(User(string.Empty, "editor")));
    }

    [Test]
    public async Task EnsureMemberAsync_WithAGroup_CreatesTheGroupAndAnApprovedMemberWhoCanSignIn()
    {
        var password = TestPeople.NewPassword();

        var member = await People.EnsureMemberAsync(MemberOf(password, "Library members"));

        var group = await GetRequiredService<IMemberGroupService>().GetByNameAsync("Library members");
        var signsIn = await MemberSignsInAsync(Member, password);
        Assert.Multiple(() =>
        {
            Assert.That(member.Email, Is.EqualTo(Member));
            Assert.That(member.Name, Is.EqualTo("Mo Member"));
            Assert.That(member.IsApproved, Is.True);
            Assert.That(group, Is.Not.Null);
            Assert.That(GetRequiredService<IMemberService>().GetAllRoles(member.Id), Is.EquivalentTo(new[] { "Library members" }));
            Assert.That(signsIn, Is.True);
        });
    }

    [Test]
    public async Task EnsureMemberAsync_RunTwice_LeavesTheMemberAsItIs()
    {
        var password = TestPeople.NewPassword();
        var first = await People.EnsureMemberAsync(MemberOf(password, "Library members"));

        var second = await People.EnsureMemberAsync(MemberOf(TestPeople.NewPassword()));

        var signsIn = await MemberSignsInAsync(Member, password);
        Assert.Multiple(() =>
        {
            Assert.That(second.Key, Is.EqualTo(first.Key));
            Assert.That(signsIn, Is.True);
        });
    }

    [Test]
    public async Task Seeder_WithPeopleConfigured_CreatesThemAndSaysSo()
    {
        var settings = new FormBuilderTestingOptions();
        settings.UserGroups.Add(DesignersGroup());
        settings.Users.Add(User(TestPeople.NewPassword(), "jfbDesigners"));
        settings.Members.Add(MemberOf(TestPeople.NewPassword(), "Library members"));
        var seeder = ActivatorUtilities.CreateInstance<TestPeopleSeeder>(Services, Options.Create(settings), People);

        await seeder.HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        var logged = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(TestPeopleSeeder).FullName).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<IUserService>().GetByEmail(Designer), Is.Not.Null);
            Assert.That(GetRequiredService<IMemberService>().GetByEmail(Member), Is.Not.Null);
            Assert.That(logged.Select(entry => entry.Message), Is.EqualTo(new[] { "Jiron Form Builder test people ready (1 groups, 0 restricted folders, 1 users, 1 members)" }));
            Assert.That(logged.Any(entry => entry.Message.Contains(settings.Users[0].Password, StringComparison.Ordinal)), Is.False);
        });
    }

    [Test]
    public void Seeder_WithABrokenPerson_LogsWhyAndFails()
    {
        var settings = new FormBuilderTestingOptions();
        settings.Users.Add(User(TestPeople.NewPassword(), "noSuchGroup"));
        var seeder = ActivatorUtilities.CreateInstance<TestPeopleSeeder>(Services, Options.Create(settings), People);

        Assert.ThrowsAsync<InvalidOperationException>(() => seeder.HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None));

        var logged = GetRequiredService<LogCapture>().Entries.Single(entry => entry.Category == typeof(TestPeopleSeeder).FullName);
        Assert.Multiple(() =>
        {
            Assert.That(logged.Level, Is.EqualTo(LogLevel.Critical));
            Assert.That(logged.Message, Does.StartWith("Jiron Form Builder test people failed:").And.Contain("noSuchGroup"));
        });
    }

    [Test]
    public async Task Seeder_WithNothingConfigured_DoesNothing()
    {
        var seeder = ActivatorUtilities.CreateInstance<TestPeopleSeeder>(Services, Options.Create(new FormBuilderTestingOptions()), People);

        await seeder.HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        Assert.That(GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(TestPeopleSeeder).FullName), Is.Empty);
    }

    [Test]
    public async Task Seeder_GivesEachGroupItsGlobalGrant_AndSeedsTheRestrictedFolders()
    {
        var settings = AccessSettings();

        await Seeder(settings).HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        var logged = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(TestPeopleSeeder).FullName).Select(entry => entry.Message);
        var designers = await GroupKeyAsync("jfbDesigners");
        var hrTeam = await GroupKeyAsync("jfbHrTeam");
        var folders = await GetRequiredService<IFormBuilderFolderService>().ListAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(await GrantAsync(designers, PermissionTarget.Global, Guid.Empty), Is.EqualTo(FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions), "names in any case");
            Assert.That(await Grants.GetAsync(hrTeam, PermissionTarget.Global, Guid.Empty), Is.Null, "no permission, no grant");
            Assert.That(folders.Select(folder => (folder.Name, folder.ParentKey, folder.IsRestricted)), Is.EqualTo(new[] { ("HR", (Guid?)null, true) }));
            Assert.That(await GrantAsync(hrTeam, PermissionTarget.Folder, folders[0].Key), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries));
            Assert.That(await Grants.GetAsync(designers, PermissionTarget.Folder, folders[0].Key), Is.Null);
            Assert.That(logged, Is.EqualTo(new[] { "Jiron Form Builder test people ready (2 groups, 1 restricted folders, 0 users, 0 members)" }));
        });
    }

    [Test]
    public async Task Seeder_RunAgain_BringsEveryGrantBack_AndKeepsOneFolder()
    {
        var settings = AccessSettings();
        await Seeder(settings).SeedAsync(settings);
        var access = GetRequiredService<IFormBuilderAccessService>();
        var designers = await GroupKeyAsync("jfbDesigners");
        var hrTeam = await GroupKeyAsync("jfbHrTeam");
        var hr = (await GetRequiredService<IFormBuilderFolderService>().ListAsync()).Single();

        // Someone changed them since the last start.
        await access.ChangeGrantAsync(new AccessGrantChange(designers, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions, FormBuilderPermissions.ManageLibrary, null));
        await access.ChangeGrantAsync(new AccessGrantChange(hrTeam, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, null));
        await access.ChangeGrantAsync(new AccessGrantChange(hrTeam, PermissionTarget.Folder, hr.Key, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries, FormBuilderPermissions.ViewEntries, null));
        await access.RestrictFolderAsync(hr.Key, false, null);
        await Seeder(settings).SeedAsync(settings);

        var folders = await GetRequiredService<IFormBuilderFolderService>().ListAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(await GrantAsync(designers, PermissionTarget.Global, Guid.Empty), Is.EqualTo(FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions));
            Assert.That(await Grants.GetAsync(hrTeam, PermissionTarget.Global, Guid.Empty), Is.Null, "a grant the settings do not name goes");
            Assert.That(folders.Select(folder => (folder.Key, folder.IsRestricted)), Is.EqualTo(new[] { (hr.Key, true) }), "the same folder, restricted again");
            Assert.That(await GrantAsync(hrTeam, PermissionTarget.Folder, hr.Key), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries));
        });
    }

    [Test]
    public async Task Seeder_MakesANewFolder_WhenTheOneOfThatNameIsInTheTrash()
    {
        var settings = AccessSettings();
        await Seeder(settings).SeedAsync(settings);
        var folderService = GetRequiredService<IFormBuilderFolderService>();
        var old = (await folderService.ListAsync()).Single();
        await folderService.TrashAsync(old.Key, null);

        await Seeder(settings).SeedAsync(settings);

        var live = (await folderService.ListAsync()).Where(folder => folder.TrashedUtc is null).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(live.Select(folder => (folder.Name, folder.IsRestricted)), Is.EqualTo(new[] { ("HR", true) }));
            Assert.That(live[0].Key, Is.Not.EqualTo(old.Key));
        });
    }

    [Test]
    public async Task Seeder_MakesTheFolderAtTheRoot_WhenOneOfThatNameIsInsideAnother()
    {
        var settings = AccessSettings();
        var folderService = GetRequiredService<IFormBuilderFolderService>();
        var archive = await folderService.CreateAsync(new NewFolderRequest("Archive"));
        var nested = await folderService.CreateAsync(new NewFolderRequest("HR", archive.Key));

        await Seeder(settings).SeedAsync(settings);

        var folders = await folderService.ListAsync();
        var hr = folders.Single(folder => folder is { Name: "HR", ParentKey: null });
        var hrTeam = await GroupKeyAsync("jfbHrTeam");
        Assert.Multiple(async () =>
        {
            Assert.That(hr.Key, Is.Not.EqualTo(nested.Key));
            Assert.That(hr.IsRestricted, Is.True);
            Assert.That(folders.Single(folder => folder.Key == nested.Key).IsRestricted, Is.False, "the folder inside Archive stays as it was");
            Assert.That(await Grants.GetAsync(hrTeam, PermissionTarget.Folder, nested.Key), Is.Null);
        });
    }

    [Test]
    public async Task Seeder_WithOnlyARestrictedFolder_SeedsIt_ForOneOfUmbracosGroups()
    {
        var settings = new FormBuilderTestingOptions();
        var reports = new TestRestrictedFolder { Name = "Reports", Group = "editor" };
        reports.Permissions.Add("viewEntries");
        settings.RestrictedFolders.Add(reports);

        await Seeder(settings).HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None);

        var folder = (await GetRequiredService<IFormBuilderFolderService>().ListAsync()).Single();
        var logged = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(TestPeopleSeeder).FullName).Select(entry => entry.Message);
        Assert.Multiple(async () =>
        {
            Assert.That((folder.Name, folder.IsRestricted), Is.EqualTo(("Reports", true)));
            Assert.That(await GrantAsync(await GroupKeyAsync("editor"), PermissionTarget.Folder, folder.Key), Is.EqualTo(FormBuilderPermissions.ViewEntries));
            Assert.That(logged, Is.EqualTo(new[] { "Jiron Form Builder test people ready (0 groups, 1 restricted folders, 0 users, 0 members)" }));
        });
    }

    [TestCase("viewEntry", "jfbHrTeam", "The test permission 'viewEntry' of 'jfbDesigners' is not a permission.")]
    [TestCase("export", "jfbHrTeam", "The test grant was refused: 'export' needs 'viewEntries'.")]
    [TestCase("design", "jfbNobody", "The test user group 'jfbNobody' does not exist.")]
    public void Seeder_WithABrokenGrant_LogsWhyAndFails(string permission, string folderGroup, string reason)
    {
        var settings = AccessSettings();
        settings.UserGroups[0].Permissions.Add(permission);
        settings.RestrictedFolders[0].Group = folderGroup;

        Assert.ThrowsAsync<InvalidOperationException>(() => Seeder(settings).HandleAsync(new UmbracoApplicationStartedNotification(false), CancellationToken.None));

        var logged = GetRequiredService<LogCapture>().Entries.Single(entry => entry.Category == typeof(TestPeopleSeeder).FullName);
        Assert.That(logged.Message, Is.EqualTo("Jiron Form Builder test people failed: " + reason));
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    // Designers with two global permissions (one in another letter case), the HR team without any, and the HR folder
    // granted to the HR team.
    private static FormBuilderTestingOptions AccessSettings()
    {
        var settings = new FormBuilderTestingOptions();
        var designers = DesignersGroup();
        designers.Permissions.Add("design");
        designers.Permissions.Add("MANAGEACTIONS");
        settings.UserGroups.Add(designers);
        settings.UserGroups.Add(new TestUserGroup { Alias = "jfbHrTeam", Name = "HR team" });
        var hr = new TestRestrictedFolder { Name = "HR", Group = "jfbHrTeam" };
        hr.Permissions.Add("viewEntries");
        hr.Permissions.Add("editEntries");
        settings.RestrictedFolders.Add(hr);
        return settings;
    }

    private static TestUserGroup DesignersGroup()
    {
        var group = new TestUserGroup { Alias = "jfbDesigners", Name = "Form designers" };
        group.Sections.Add("content");
        return group;
    }

    private static TestUser User(string password, params string[] groups)
    {
        var user = new TestUser { Email = Designer, Name = "Dana Designer", Password = password };
        foreach (var group in groups)
        {
            user.Groups.Add(group);
        }

        return user;
    }

    private static TestMember MemberOf(string password, params string[] groups)
    {
        var member = new TestMember { Email = Member, Name = "Mo Member", Password = password };
        foreach (var group in groups)
        {
            member.Groups.Add(group);
        }

        return member;
    }

    private IPermissionRepository Grants => GetRequiredService<IPermissionRepository>();

    private TestPeopleSeeder Seeder(FormBuilderTestingOptions settings) =>
        ActivatorUtilities.CreateInstance<TestPeopleSeeder>(Services, Options.Create(settings), People);

    private async Task<Guid> GroupKeyAsync(string alias) => (await GetRequiredService<IUserGroupService>().GetAsync(alias))!.Key;

    private async Task<FormBuilderPermissions> GrantAsync(Guid group, PermissionTarget target, Guid key) =>
        PermissionRules.FromStored((await Grants.GetAsync(group, target, key))?.Permissions ?? 0);

    private async Task<bool> SignsInAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IBackOfficeUserManager>().ValidateCredentialsAsync(email, password);
    }

    private async Task<bool> MemberSignsInAsync(string email, string password)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IMemberManager>().ValidateCredentialsAsync(email, password);
    }
}
