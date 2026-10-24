using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
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

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
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
