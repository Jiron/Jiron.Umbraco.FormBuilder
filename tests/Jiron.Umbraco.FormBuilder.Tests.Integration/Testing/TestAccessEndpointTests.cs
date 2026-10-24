using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// <c>GET /jfb-testing/access</c> on a real test host in Development (ADR-0049): each backoffice user's effective
/// permissions globally, at the root and on each root folder outside the trash, as the evaluation gives them, which the browser tests
/// read to see that the personas have their grants.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TestAccessEndpointTests : FormBuilderTestServerTestBase
{
    [Test]
    public async Task Access_ListsEveryUser_WithTheirPermissions_GloballyAtTheRootAndInEachLiveRootFolder()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var people = ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider);
            var access = ActivatorUtilities.CreateInstance<TestAccess>(scope.ServiceProvider);
            var folders = scope.ServiceProvider.GetRequiredService<IFormBuilderFolderService>();
            await people.EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbViewers", Name = "Viewers", Sections = { AccessPersonas.Section } });
            await people.EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbHrTeam", Name = "HR team", Sections = { AccessPersonas.Section } });
            await access.GrantGloballyAsync("jfbViewers", ["viewEntries", "manageLibrary"]);
            await folders.CreateAsync(new NewFolderRequest("Events"));
            var hr = await access.EnsureRestrictedFolderAsync(new TestRestrictedFolder { Name = "HR", Group = "jfbHrTeam", Permissions = { "viewEntries", "editEntries" } });
            await folders.CreateAsync(new NewFolderRequest("Payroll", hr));
            await folders.TrashAsync((await folders.CreateAsync(new NewFolderRequest("Old"))).Key, null);
            await people.EnsureUserAsync(new TestUser { Email = "viewer@access.jfb.test", Password = TestPeople.NewPassword(), Groups = { "jfbViewers" } });
            await people.EnsureUserAsync(new TestUser { Email = "hr@access.jfb.test", Password = TestPeople.NewPassword(), Groups = { "jfbHrTeam" } });
            await people.EnsureUserAsync(new TestUser { Email = "writer@access.jfb.test", Password = TestPeople.NewPassword(), Groups = { "writer" } });
        }

        var context = await TestEndpointsHost.FromAsync(Factory.Server, "/jfb-testing/access", IPAddress.Loopback);
        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
        using var body = await JsonDocument.ParseAsync(context.Response.Body);
        var users = body.RootElement.GetProperty("users").EnumerateArray().ToDictionary(user => user.GetProperty("email").GetString()!);

        Assert.Multiple(() =>
        {
            Assert.That(users.Keys.Where(email => email.EndsWith("@access.jfb.test", StringComparison.Ordinal)), Is.EqualTo(new[] { "hr@access.jfb.test", "viewer@access.jfb.test", "writer@access.jfb.test" }), "in the order of their addresses");
            Assert.That(Describe(users["viewer@access.jfb.test"]), Is.EqualTo("section; global viewEntries, manageLibrary; root viewEntries; Events viewEntries; HR (restricted)"), "the root folders in the library's order, without the one in the trash or the one inside HR");
            Assert.That(Describe(users["hr@access.jfb.test"]), Is.EqualTo("section; global ; root ; Events; HR (restricted) viewEntries, editEntries"));
            Assert.That(Describe(users["writer@access.jfb.test"]), Is.EqualTo("no section; global ; root ; Events; HR (restricted)"));
            Assert.That(users.Values.Count(user => user.GetProperty("global").GetArrayLength() == 11), Is.EqualTo(1), "the site's administrator");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration) => TestEndpointsHost.SwitchOn(configuration);

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => TestEndpointsHost.UseDevelopment(builder);

    private static string Describe(JsonElement user)
    {
        static string Names(JsonElement list) => string.Join(", ", list.EnumerateArray().Select(name => name.GetString()));
        var folders = user.GetProperty("folders").EnumerateArray()
            .Select(folder => $"{folder.GetProperty("name").GetString()}{(folder.GetProperty("restricted").GetBoolean() ? " (restricted)" : string.Empty)} {Names(folder.GetProperty("permissions"))}".TrimEnd());
        return $"{(user.GetProperty("section").GetBoolean() ? "section" : "no section")}; global {Names(user.GetProperty("global"))}; root {Names(user.GetProperty("root"))}; {string.Join("; ", folders)}";
    }
}
