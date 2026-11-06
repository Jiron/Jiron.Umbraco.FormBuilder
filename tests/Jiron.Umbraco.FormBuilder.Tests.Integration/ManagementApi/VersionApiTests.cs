using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The version endpoints of the management API (ADR-0045, ADR-0052) on a real site: the History, a version with its
/// document, a comparison, a restore into a new draft with <c>manageActions</c> when it changes the actions, pins and
/// comments. Spring has four versions: 1 the blank draft, 2 a draft with the field Name, 3 its publish, and 4 the draft
/// with the action Notify added. The drafter is an extra persona with <c>design</c> alone.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class VersionApiTests : ManagementApiTestBase
{
    private const string Drafters = "jfbDrafters";
    private static readonly Guid NameKey = Guid.Parse("00000000-0000-4000-8000-000000000011");

    private ManagementApiCaller _api = null!;
    private Guid _spring;

    [SetUp]
    public async Task CreateTheVersionsAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var drafters = await ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider)
                .EnsureUserGroupAsync(new TestUserGroup { Alias = Drafters, Name = "Drafters", Sections = { AccessPersonas.Section } });
            await GrantAsync(drafters.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.Design);
        }

        _api = new ManagementApiCaller(Client, Services, role => role == "drafter" ? [Drafters] : [AccessPersonas.GroupOf(role)]);
        _spring = await FormAsync("Spring", await FolderAsync("Events"));
        await SaveAsync(definition => Fields(definition).Add(new JsonObject { ["key"] = NameKey.ToString(), ["type"] = "text", ["alias"] = "name", ["label"] = new JsonObject { [string.Empty] = "Name" } }));
        var published = await _api.CallAsync("designer", HttpMethod.Post, $"form/{_spring}/publish", new { concurrencyVersion = await VersionOfAsync() });
        Assert.That(published.Status, Is.EqualTo(HttpStatusCode.OK), published.ToString());
        await SaveAsync(definition => definition["actions"] = new JsonArray(new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["type"] = "email", ["name"] = "Notify" }));
    }

    [Test]
    public async Task TheHistory_ListsThePublishes_OrEveryVersion_NewestFirst_WithWhatChanged()
    {
        var publishes = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions");
        var all = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions?includeDrafts=true");
        var first = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions?includeDrafts=true&take=2");
        var second = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions?includeDrafts=true&skip=2&take=2");

        Assert.Multiple(async () =>
        {
            Assert.That(Numbers(publishes), Is.EqualTo(new[] { "3 published live" }));
            Assert.That(Numbers(all), Is.EqualTo(new[] { "4 draft draft", "3 published live", "2 draft", "1 draft" }));
            Assert.That((Numbers(first), first.Body!["total"]!.GetValue<long>(), Numbers(second)), Is.EqualTo((new List<string> { "4 draft draft", "3 published live" }, 4L, new List<string> { "2 draft", "1 draft" })).Using<(List<string>, long, List<string>)>((a, b) => a.Item1.SequenceEqual(b.Item1) && a.Item2 == b.Item2 && a.Item3.SequenceEqual(b.Item3)));
            var newest = all.Body!["items"]![0]!;
            Assert.That(newest["changes"]!.AsArray().Select(change => $"{change!["kind"]} {change["change"]} {change["label"]}"), Is.EqualTo(new[] { "action added Notify" }));
            Assert.That(all.Body!["items"]![3]!["changes"], Is.Null, "the first version has nothing before it");
            Assert.That(newest["version"]!["createdByUserKey"]!.GetValue<Guid>(), Is.Not.EqualTo(Guid.Empty));
            var saver = await GetRequiredService<IUserService>().GetAsync(newest["version"]!["createdByUserKey"]!.GetValue<Guid>());
            Assert.That(saver?.Name, Is.Not.Null.And.Not.Empty);
            Assert.That(newest["by"]?.GetValue<string>(), Is.EqualTo(saver?.Name), "who saved it, by name (ADR-0196)");
            var publisher = all.Body!["items"]![1]!;
            Assert.That(publisher["by"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty, "who published it");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions?take=51")).Outcome, Is.EqualTo("400 invalid_request"), "at most 50 a page");
            var viewer = await _api.CallAsync("viewer", HttpMethod.Get, $"form/{_spring}/versions");
            Assert.That((viewer.Outcome, viewer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")));
        });
    }

    [Test]
    public async Task AVersion_ComesWithItsDocument_AndAMissingOneIsNotFound()
    {
        var second = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions/2");

        Assert.Multiple(async () =>
        {
            Assert.That(second.Status, Is.EqualTo(HttpStatusCode.OK), second.ToString());
            Assert.That((second.Body!["version"]!["number"]!.GetValue<int>(), second.Body["version"]!["kind"]!.GetValue<string>()), Is.EqualTo((2, "draft")));
            Assert.That(Fields(second.Body!["definition"]!.AsObject()).Select(field => field!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "name" }));
            Assert.That(second.Body!["problems"]!.AsArray(), Is.Empty);
            Assert.That((await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions/99")).Outcome, Is.EqualTo("404 version_not_found"));
        });
    }

    [Test]
    public async Task AComparison_NamesEachChangedElement_WithItsPropertiesBeforeAndAfter()
    {
        await SaveAsync(definition => Fields(definition)[0]!["label"] = new JsonObject { [string.Empty] = "Full name" });

        var added = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions/compare?from=1&to=2");
        var changed = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions/compare?from=2&to=5");

        Assert.Multiple(async () =>
        {
            Assert.That(added.Status, Is.EqualTo(HttpStatusCode.OK), added.ToString());
            Assert.That((added.Body!["fromVersion"]!.GetValue<int>(), added.Body["toVersion"]!.GetValue<int>()), Is.EqualTo((1, 2)));
            var field = added.Body!["changes"]!.AsArray().Single()!;
            Assert.That((field["kind"]!.GetValue<string>(), field["change"]!.GetValue<string>(), field["key"]!.GetValue<Guid>(), field["alias"]!.GetValue<string>(), field["type"]!.GetValue<string>()), Is.EqualTo(("field", "added", NameKey, "name", "text")));
            Assert.That((field["before"], field["after"]!["alias"]!.GetValue<string>(), field["properties"]!.AsArray().Count), Is.EqualTo(((JsonNode?)null, "name", 0)));
            var label = changed.Body!["changes"]!.AsArray().Single(change => change!["kind"]!.GetValue<string>() == "field")!;
            Assert.That(label["change"]!.GetValue<string>(), Is.EqualTo("changed"));
            Assert.That(label["properties"]!.AsArray().Select(property => $"{string.Join("/", property!["path"]!.AsArray().Select(segment => segment!.GetValue<string>()))}: {property["from"]!.ToJsonString()} -> {property["to"]!.ToJsonString()}"), Is.EqualTo(new[] { "label/: \"Name\" -> \"Full name\"" }));
            Assert.That(changed.Body!["changes"]!.AsArray().Select(change => $"{change!["kind"]} {change["change"]}"), Does.Contain("action added"), "and the action of version 4");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions/compare?from=1&to=99")).Outcome, Is.EqualTo("404 version_not_found"));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions/compare?from=1")).Outcome, Is.EqualTo("400 invalid_request"));
        });
    }

    [Test]
    public async Task ADraftNotSavedYet_IsComparedWithAVersion_AndComesBackAsASaveWouldStoreIt()
    {
        var draft = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/draft");
        var definition = draft.Body!["definition"]!.DeepClone().AsObject();
        Fields(definition)[0]!["label"] = new JsonObject { [string.Empty] = "Full name" };
        definition["actions"] = new JsonArray();
        var before = await VersionOfAsync();

        var compared = await _api.CallAsync("designer", HttpMethod.Post, $"form/{_spring}/versions/4/compare", new { definition });
        var bare = definition.DeepClone().AsObject();
        bare.Remove("settings");
        var canonical = await _api.CallAsync("designer", HttpMethod.Post, $"form/{_spring}/versions/4/compare", new { definition = bare });

        Assert.Multiple(async () =>
        {
            Assert.That(compared.Status, Is.EqualTo(HttpStatusCode.OK), compared.ToString());
            Assert.That(compared.Body!["fromVersion"]!.GetValue<int>(), Is.EqualTo(4));
            Assert.That(compared.Body!["changes"]!.AsArray().Select(change => $"{change!["kind"]} {change["change"]}"), Is.EquivalentTo(new[] { "field changed", "action removed" }));
            var label = compared.Body!["changes"]!.AsArray().Single(change => change!["kind"]!.GetValue<string>() == "field")!;
            Assert.That(label["properties"]!.AsArray().Select(property => $"{string.Join("/", property!["path"]!.AsArray().Select(segment => segment!.GetValue<string>()))}: {property["from"]!.ToJsonString()} -> {property["to"]!.ToJsonString()}"), Is.EqualTo(new[] { "label/: \"Name\" -> \"Full name\"" }));
            Assert.That(Fields(compared.Body!["definition"]!.AsObject())[0]!["label"]![string.Empty]!.GetValue<string>(), Is.EqualTo("Full name"), "the draft sent, as a save stores it");
            Assert.That(compared.Body!["definition"]!["schemaVersion"], Is.Not.Null, "in its canonical form");
            Assert.That(canonical.Body?["definition"]?["settings"]?.AsObject().Count, Is.GreaterThan(0), canonical.ToString() + ": the settings a save writes, defaults and all, where none were sent");
            Assert.That(await VersionOfAsync(), Is.EqualTo(before), "a comparison saves nothing");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Post, $"form/{_spring}/versions/99/compare", new { definition })).Outcome, Is.EqualTo("404 version_not_found"));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Post, $"form/{_spring}/versions/4/compare", new { definition = new JsonObject { ["pages"] = "none" } })).Status, Is.EqualTo(HttpStatusCode.BadRequest), "a definition that can't be read");
            var viewer = await _api.CallAsync("viewer", HttpMethod.Post, $"form/{_spring}/versions/4/compare", new { definition });
            Assert.That((viewer.Outcome, viewer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")));
        });
    }

    [Test]
    public async Task ARestore_CopiesTheVersionIntoANewDraft_AndNeedsManageActionsWhenItChangesThem()
    {
        var version = await VersionOfAsync();

        var drafter = await Restore("drafter", 3, version);
        var staleDrafter = await Restore("drafter", 3, version - 1);
        var drafterSame = await Restore("drafter", 4, version);
        var viewer = await Restore("viewer", 2, version);
        var missing = await Restore("designer", 99, version);
        var designer = await Restore("designer", 2, version);
        var draft = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/draft");
        var newest = (await GetRequiredService<IFormBuilderFormVersionService>().ListVersionsAsync(_spring, includeDrafts: true, PageRequest.First(1)))!.Items[0].Version;

        Assert.Multiple(() =>
        {
            Assert.That((drafter.Outcome, drafter.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")), "version 3 has no action");
            Assert.That(staleDrafter.Outcome, Is.EqualTo("409 concurrency_conflict"), "a stale restore names no permission");
            Assert.That((drafterSame.Status, drafterSame.Body?["version"]), Is.EqualTo((HttpStatusCode.OK, (JsonNode?)null)), "the draft's own version keeps the actions and writes nothing");
            Assert.That((viewer.Outcome, viewer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")));
            Assert.That(missing.Outcome, Is.EqualTo("404 version_not_found"));
            Assert.That(designer.Status, Is.EqualTo(HttpStatusCode.OK), designer.ToString());
            Assert.That((designer.Body!["version"]!.GetValue<int>(), designer.Body["form"]!["concurrencyVersion"]!.GetValue<int>() > version), Is.EqualTo((5, true)));
            Assert.That((draft.Body!["draftVersion"]!.GetValue<int>(), draft.Body["definition"]!["actions"]?.AsArray().Count ?? 0), Is.EqualTo((5, 0)), "version 2's definition, without the action");
            Assert.That(Fields(draft.Body!["definition"]!.AsObject()).Select(field => field!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "name" }), "with its field");
            Assert.That((newest.Number, newest.CreatedByUserKey), Is.EqualTo((5, (Guid?)GetRequiredService<IUserService>().GetByEmail("api-designer@jfb.test")!.Key)), "who restored it");
            Assert.That(designer.Body!["form"]!["permissions"]!.AsArray().Select(permission => permission!.GetValue<string>()), Is.EqualTo(new[] { "design", "manageActions" }));
        });
    }

    [Test]
    public async Task PinsAndComments_ChangeTheVersion_AndNotTheForm()
    {
        var before = await VersionOfAsync();

        var pinned = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/2/pin", new { isPinned = true });
        var unpinned = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/3/pin", new { isPinned = false });
        var commented = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/2/comment", new { comment = "Before the launch" });
        var listed = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/versions?includeDrafts=true");
        var cleared = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/2/comment", new { comment = (string?)null });

        Assert.Multiple(async () =>
        {
            Assert.That((pinned.Status, pinned.Body!["number"]!.GetValue<int>(), pinned.Body["isPinned"]!.GetValue<bool>()), Is.EqualTo((HttpStatusCode.OK, 2, true)), pinned.ToString());
            Assert.That(unpinned.Body!["isPinned"]!.GetValue<bool>(), Is.False);
            Assert.That(commented.Body!["comment"]!.GetValue<string>(), Is.EqualTo("Before the launch"));
            var second = listed.Body!["items"]!.AsArray().Single(item => item!["version"]!["number"]!.GetValue<int>() == 2)!["version"]!;
            Assert.That((second["isPinned"]!.GetValue<bool>(), second["comment"]!.GetValue<string>()), Is.EqualTo((true, "Before the launch")), "kept");
            Assert.That(cleared.Body!["comment"], Is.Null);
            Assert.That(await VersionOfAsync(), Is.EqualTo(before), "the form's concurrency version stays");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/2/comment", new { comment = new string('x', 501) })).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/99/pin", new { isPinned = true })).Outcome, Is.EqualTo("404 version_not_found"));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/versions/99/comment", new { comment = "x" })).Outcome, Is.EqualTo("404 version_not_found"));
        });
    }

    // Each version as "number kind", with "live" or "draft" for the version visitors get and the form's draft.
    private static List<string> Numbers(ApiAnswer page)
    {
        Assert.That(page.Status, Is.EqualTo(HttpStatusCode.OK), page.ToString());
        return [.. page.Body!["items"]!.AsArray().Select(item => item!["version"]!).Select(version =>
            $"{version["number"]} {version["kind"]}{(version["isLive"]!.GetValue<bool>() ? " live" : string.Empty)}{(version["isDraft"]!.GetValue<bool>() ? " draft" : string.Empty)}")];
    }

    private static JsonArray Fields(JsonObject definition)
    {
        var group = definition["pages"]![0]!["groups"]![0]!.AsObject();
        if (group["fields"] is not JsonArray fields)
        {
            fields = [];
            group["fields"] = fields;
        }

        return fields;
    }

    private async Task<int> VersionOfAsync() => (await GetRequiredService<IFormBuilderFormService>().GetAsync(_spring))!.ConcurrencyVersion;

    // Saves a new draft of Spring as the designer, changed by the caller.
    private async Task SaveAsync(Action<JsonObject> change)
    {
        var draft = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/draft");
        var definition = draft.Body!["definition"]!.DeepClone().AsObject();
        change(definition);
        var saved = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = draft.Body["form"]!["concurrencyVersion"]!.GetValue<int>(), definition });
        Assert.That(saved.Status, Is.EqualTo(HttpStatusCode.OK), saved.ToString());
    }

    private Task<ApiAnswer> Restore(string role, int number, int concurrencyVersion) =>
        _api.CallAsync(role, HttpMethod.Post, $"form/{_spring}/versions/{number}/restore", new { concurrencyVersion });
}
