using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Umbraco.Cms.Core;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Settings, Access through the management API (ROADMAP 12.4a, ADR-0013, ADR-0218): every user group's global grant
/// with the administrators first and holding everything, the permissions in three parts and what each needs, and the
/// folders and forms with access of their own; a save of several groups' grants in one unit of work, refused whole when
/// one grant changed meanwhile; a permission without its dependency, a change of the administrators' or a global-only
/// permission on a folder refused; a folder's and a form's grants and restriction, a form's at the version read; and the
/// right to manage permissions for each.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AccessApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    [Test]
    public async Task Access_AnswersEveryGroupsGlobalGrant_ThePartsWhatEachNeeds_AndTheFoldersAndFormsWithAccessOfTheirOwn()
    {
        var folder = await FolderAsync("HR");
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);
        var form = await FormAsync("Quote request");
        await GrantAsync(People.GroupKeys["viewer"], PermissionTarget.Form, form, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries);

        var answer = await Api.CallAsync(Admin, HttpMethod.Get, "access");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            var groups = answer.Body!["groups"]!.AsArray();
            Assert.That(groups[0]!["key"]!.GetValue<Guid>(), Is.EqualTo(Constants.Security.AdminGroupKey), "the administrators first");
            Assert.That(groups[0]!["permissions"]!.AsArray(), Has.Count.EqualTo(PermissionRules.Each.Count), "holding every permission");
            var designers = groups.Single(group => group!["key"]!.GetValue<Guid>() == People.GroupKeys["designer"])!;
            Assert.That(designers["permissions"]!.AsArray().Select(name => name!.GetValue<string>()), Is.EqualTo(new[] { "design", "manageActions", "manageLibrary" }));
            Assert.That(designers["hasSection"]!.GetValue<bool>(), Is.True);
            Assert.That(answer.Body["parts"]!.AsArray().Select(part => part!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "forms", "entries", "library" }));
            Assert.That(answer.Body["needs"]!.AsArray().Select(need => (need!["permission"]!.GetValue<string>(), need["needs"]!.GetValue<string>())), Does.Contain(("editEntries", "viewEntries")));
            Assert.That(answer.Body["lock"], Is.Null);
            var items = answer.Body["items"]!.AsArray();
            Assert.That(items.Select(item => (item!["type"]!.GetValue<string>(), item["name"]!.GetValue<string>(), item["isRestricted"]!.GetValue<bool>())), Is.EqualTo(new[] { ("folder", "HR", true), ("form", "Quote request", false) }));
            Assert.That(JsonNode.DeepEquals(items[1]!["grants"], JsonNode.Parse($$"""[{"group":"{{People.GroupKeys["viewer"]}}","permissions":["viewEntries","editEntries"]}]""")), Is.True, items[1]!.ToJsonString());
        });
    }

    [Test]
    public async Task ASave_ChangesSeveralGroupsInOneUnit_AndAGrantChangedMeanwhile_RefusesAllOfIt()
    {
        var viewers = People.GroupKeys["viewer"];
        var hr = People.GroupKeys["hr"];

        var saved = await Api.CallAsync(Admin, HttpMethod.Put, "access", new
        {
            grants = new object[]
            {
                new { group = viewers, expected = new[] { "viewEntries" }, permissions = new[] { "viewEntries", "export" } },
                new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "viewEntries", "editEntries" } },
            },
        });
        var stale = await Api.CallAsync(Admin, HttpMethod.Put, "access", new
        {
            grants = new object[]
            {
                new { group = hr, expected = new[] { "viewEntries", "editEntries" }, permissions = Array.Empty<string>() },
                new { group = viewers, expected = new[] { "viewEntries" }, permissions = Array.Empty<string>() },
            },
        });
        var snapshot = await GetRequiredService<IFormBuilderAccessService>().ReadSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(stale.Outcome, Is.EqualTo("409 concurrency_conflict"), stale.ToString());
            Assert.That(snapshot.OwnOf(viewers, PermissionTarget.Global, Guid.Empty), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.Export));
            Assert.That(snapshot.OwnOf(hr, PermissionTarget.Global, Guid.Empty), Is.EqualTo(FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries), "nothing of the refused save stays");
        });
    }

    [Test]
    public async Task APermissionWithoutItsDependency_TheAdministratorsGrant_OrAnUnknownName_IsRefused_AndNothingIsSaved()
    {
        var hr = People.GroupKeys["hr"];
        var needs = await Api.CallAsync(Admin, HttpMethod.Put, "access", new { grants = new[] { new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "editEntries" } } } });
        var admins = await Api.CallAsync(Admin, HttpMethod.Put, "access", new { grants = new[] { new { group = Constants.Security.AdminGroupKey, expected = Array.Empty<string>(), permissions = new[] { "design" } } } });
        var unknown = await Api.CallAsync(Admin, HttpMethod.Put, "access", new { grants = new[] { new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "fly" } } } });
        var twice = await Api.CallAsync(Admin, HttpMethod.Put, "access", new { grants = new[] { new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "viewEntries" } }, new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "export", "viewEntries" } } } });

        Assert.Multiple(async () =>
        {
            Assert.That(needs.Outcome, Is.EqualTo("400 validation_failed"), needs.ToString());
            Assert.That(admins.Outcome, Is.EqualTo("400 validation_failed"), admins.ToString());
            Assert.That(Codes(admins), Is.EqualTo(new[] { AccessController.AdministratorsCode }));
            Assert.That((twice.Outcome, Codes(twice)), Is.EqualTo(("400 validation_failed", new[] { FormBuilderAccessService.DuplicateGroupCode })), "a group named twice");
            Assert.That(unknown.Outcome, Is.EqualTo("400 validation_failed"), unknown.ToString());
            Assert.That((await GetRequiredService<IFormBuilderAccessService>().ReadSnapshotAsync()).OwnOf(hr, PermissionTarget.Global, Guid.Empty), Is.EqualTo(FormBuilderPermissions.None));
        });
    }

    [Test]
    public async Task AFoldersGrantsAndRestriction_SaveTogether_AndAGlobalOnlyPermissionThereIsRefused()
    {
        var folder = await FolderAsync("Events");
        var hr = People.GroupKeys["hr"];

        var before = await Api.CallAsync(Admin, HttpMethod.Get, $"access/folder/{folder}");
        var saved = await Api.CallAsync(Admin, HttpMethod.Put, $"access/folder/{folder}", new
        {
            grants = new[] { new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "design", "viewEntries" } } },
            isRestricted = true,
        });
        var globalOnly = await Api.CallAsync(Admin, HttpMethod.Put, $"access/folder/{folder}", new
        {
            grants = new[] { new { group = hr, expected = new[] { "design", "viewEntries" }, permissions = new[] { "design", "viewEntries", "manageSettings" } } },
            isRestricted = true,
        });
        var missing = await Api.CallAsync(Admin, HttpMethod.Get, $"access/folder/{Guid.NewGuid()}");
        var stored = await GetRequiredService<IFolderRepository>().GetAsync(folder);

        Assert.Multiple(() =>
        {
            Assert.That(before.Outcome, Is.EqualTo("200"), before.ToString());
            Assert.That((before.Body!["name"]!.GetValue<string>(), before.Body["isRestricted"]!.GetValue<bool>(), before.Body["version"]), Is.EqualTo(("Events", false, (JsonNode?)null)));
            Assert.That(before.Body["groups"]!.AsArray().Any(group => group!["key"]!.GetValue<Guid>() == Constants.Security.AdminGroupKey), Is.False, "the administrators hold everything here too");
            Assert.That(before.Body["permissions"]!.AsArray(), Has.Count.EqualTo(7), "a folder takes the folder-level permissions");
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["isRestricted"]!.GetValue<bool>(), Is.True);
            var hrGroup = saved.Body["groups"]!.AsArray().Single(group => group!["key"]!.GetValue<Guid>() == hr)!;
            Assert.That(hrGroup["permissions"]!.AsArray().Select(name => name!.GetValue<string>()), Is.EqualTo(new[] { "design", "viewEntries" }));
            Assert.That(stored!.IsRestricted, Is.True);
            Assert.That(globalOnly.Outcome, Is.EqualTo("400 validation_failed"), globalOnly.ToString());
            Assert.That(missing.Outcome, Is.EqualTo("404 folder_not_found"), missing.ToString());
        });
    }

    [Test]
    public async Task AFormsRestriction_SavesAtTheVersionRead_AndOneMadeMeanwhile_RefusesTheWholeSave()
    {
        var form = await FormAsync("Applications");
        var hr = People.GroupKeys["hr"];
        var read = await Api.CallAsync(Admin, HttpMethod.Get, $"access/form/{form}");
        var version = read.Body!["version"]!.GetValue<int>();

        var saved = await Api.CallAsync(Admin, HttpMethod.Put, $"access/form/{form}", new
        {
            grants = new[] { new { group = hr, expected = Array.Empty<string>(), permissions = new[] { "viewEntries" } } },
            isRestricted = true,
            version,
        });
        var stale = await Api.CallAsync(Admin, HttpMethod.Put, $"access/form/{form}", new
        {
            grants = new[] { new { group = hr, expected = new[] { "viewEntries" }, permissions = Array.Empty<string>() } },
            isRestricted = false,
            version,
        });
        var snapshot = await GetRequiredService<IFormBuilderAccessService>().ReadSnapshotAsync();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That((saved.Body!["isRestricted"]!.GetValue<bool>(), saved.Body["version"]!.GetValue<int>()), Is.EqualTo((true, version + 1)));
            Assert.That(stale.Outcome, Is.EqualTo("409 concurrency_conflict"), stale.ToString());
            Assert.That(snapshot.OwnOf(hr, PermissionTarget.Form, form), Is.EqualTo(FormBuilderPermissions.ViewEntries), "the grant of the refused save stays as it was");
        });
    }

    [Test]
    public async Task Access_NeedsTheRightToManagePermissions()
    {
        var folder = await FolderAsync("Private");
        var form = await FormAsync("Private form");

        var calls = new[]
        {
            await Api.CallAsync("designer", HttpMethod.Get, "access"),
            await Api.CallAsync("designer", HttpMethod.Put, "access", new { grants = Array.Empty<object>() }),
            await Api.CallAsync("designer", HttpMethod.Get, $"access/folder/{folder}"),
            await Api.CallAsync("designer", HttpMethod.Put, $"access/folder/{folder}", new { grants = Array.Empty<object>(), isRestricted = true }),
            await Api.CallAsync("designer", HttpMethod.Get, $"access/form/{form}"),
            await Api.CallAsync("designer", HttpMethod.Put, $"access/form/{form}", new { grants = Array.Empty<object>(), isRestricted = true, version = 1 }),
        };

        Assert.That(calls.Select(call => call.Outcome), Is.All.EqualTo("403 forbidden"));
    }

    private static string[] Codes(ApiAnswer answer) => [.. answer.Body?["errors"]?.AsArray().Select(error => error!["code"]!.GetValue<string>()) ?? []];
}
