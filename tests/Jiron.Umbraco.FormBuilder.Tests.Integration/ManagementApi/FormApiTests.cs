using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The form endpoints of the management API (ADR-0044, ADR-0051) on a real site, as the personas: what each answers,
/// what it changes, and the rules that are the API's own: the definition as its document, a search of what the user
/// sees, <c>manageActions</c> for a save that changes the actions, <c>design</c> on a move's new folder and
/// <c>managePermissions</c> with a confirmation for a move or a restore that widens access, and <c>deleteEntries</c> for
/// a delete for good. Three extra personas: the drafter holds <c>design</c> alone globally, the cleaner <c>design</c>,
/// <c>deleteEntries</c> and the <c>viewEntries</c> it needs, and the contractor <c>design</c> and <c>viewEntries</c> on
/// the restricted folder Contracts only.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormApiTests : ManagementApiTestBase
{
    private const string Drafters = "jfbDrafters";
    private const string Cleaners = "jfbCleaners";
    private const string Contractors = "jfbContractors";

    private ManagementApiCaller _api = null!;
    private Guid _events;
    private Guid _hr;
    private Guid _contracts;
    private Guid _spring;
    private Guid _payroll;
    private Guid _contact;

    // Events holds Spring, HR (restricted, for the HR team) Payroll, Contracts (restricted, for the designers and the
    // contractors) nothing; Contact is at the root.
    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        _events = await FolderAsync("Events");
        _hr = await FolderAsync("HR");
        await RestrictAsync(_hr, "hr", FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries);
        _contracts = await FolderAsync("Contracts");
        await RestrictAsync(_contracts, "designer", FormBuilderPermissions.Design);
        await using (var scope = Services.CreateAsyncScope())
        {
            var people = ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider);
            var drafters = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = Drafters, Name = "Drafters", Sections = { AccessPersonas.Section } });
            var cleaners = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = Cleaners, Name = "Cleaners", Sections = { AccessPersonas.Section } });
            var contractors = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = Contractors, Name = "Contractors", Sections = { AccessPersonas.Section } });
            await GrantAsync(drafters.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.Design);
            await GrantAsync(cleaners.Key, PermissionTarget.Global, Guid.Empty, FormBuilderLibraryService.ToDelete | FormBuilderPermissions.ViewEntries);
            await GrantAsync(contractors.Key, PermissionTarget.Folder, _contracts, FormBuilderPermissions.Design | FormBuilderPermissions.ViewEntries);
        }

        _api = new ManagementApiCaller(Client, Services, role => role switch
        {
            "drafter" => [Drafters],
            "cleaner" => [Cleaners],
            "contractor" => [Contractors],
            _ => [AccessPersonas.GroupOf(role)],
        });
        _spring = await FormAsync("Spring", _events);
        _payroll = await FormAsync("Payroll", _hr);
        _contact = await FormAsync("Contact");
    }

    [Test]
    public async Task Search_FindsTheFormsTheUserSees_ByNameOrAlias_InAnyLetterCase_OutsideTheTrash()
    {
        await Forms.CreateAsync(new NewFormRequest("Autumn fair", "springFair", _events));
        await TrashFormAsync(await FormAsync("Spring archive"));

        var admin = await _api.CallAsync("admin", HttpMethod.Get, "form/search?text=SPRING");
        var first = await _api.CallAsync("admin", HttpMethod.Get, "form/search?text=spring&take=1");
        var second = await _api.CallAsync("admin", HttpMethod.Get, "form/search?text=spring&skip=1&take=1");

        Assert.Multiple(async () =>
        {
            Assert.That(Names(admin), Is.EqualTo(new[] { "Autumn fair", "Spring" }), "by name; the alias counts, the trash does not");
            Assert.That(admin.Body!["items"]![1]!["parentKey"]!.GetValue<Guid>(), Is.EqualTo(_events));
            Assert.That((Names(first), first.Body!["total"]!.GetValue<long>(), Names(second)), Is.EqualTo((new List<string> { "Autumn fair" }, 2L, new List<string> { "Spring" })).Using<(List<string>, long, List<string>)>((a, b) => a.Item1.SequenceEqual(b.Item1) && a.Item2 == b.Item2 && a.Item3.SequenceEqual(b.Item3)));
            Assert.That(Names(await _api.CallAsync("admin", HttpMethod.Get, "form/search?text=pay")), Is.EqualTo(new[] { "Payroll" }));
            Assert.That(Names(await _api.CallAsync("viewer", HttpMethod.Get, "form/search?text=pay")), Is.Empty, "the viewer does not see HR");
            var hr = await _api.CallAsync("hr", HttpMethod.Get, "form/search?text=pay");
            Assert.That(Permissions(hr.Body!["items"]![0]!), Is.EqualTo(new[] { "viewEntries", "editEntries" }));
            Assert.That((await _api.CallAsync("admin", HttpMethod.Get, "form/search?text=%20")).Outcome, Is.EqualTo("400 invalid_request"), "a blank text");
            Assert.That((await _api.CallAsync("admin", HttpMethod.Get, "form/search")).Outcome, Is.EqualTo("400 invalid_request"));
            Assert.That((await _api.CallAsync("admin", HttpMethod.Get, $"form/search?text={new string('a', FormController.MaxSearchLength + 1)}")).Outcome, Is.EqualTo("400 invalid_request"), "a text too long");
            var administrator = new AccessUser(Guid.NewGuid(), new HashSet<Guid>(), IsAdministrator: true, HasSection: true);
            Assert.ThrowsAsync<ArgumentException>(() => GetRequiredService<IFormBuilderLibraryService>().SearchFormsAsync(administrator, " ", PageRequest.First(10)), "the service refuses a blank text too");
        });
    }

    [Test]
    public async Task Get_AnswersTheFormToAnyoneWhoSeesIt_AndTheDraftOnlyWithDesign()
    {
        var viewer = await _api.CallAsync("viewer", HttpMethod.Get, $"form/{_spring}");
        var draft = await _api.CallAsync("designer", HttpMethod.Get, $"form/{_spring}/draft");

        Assert.Multiple(async () =>
        {
            Assert.That(viewer.Status, Is.EqualTo(HttpStatusCode.OK), viewer.ToString());
            Assert.That((viewer.Body!["name"]!.GetValue<string>(), viewer.Body["alias"]!.GetValue<string>(), viewer.Body["folderKey"]!.GetValue<Guid>(), viewer.Body["isPublished"]!.GetValue<bool>()), Is.EqualTo(("Spring", "spring", _events, false)));
            Assert.That(viewer.Body!["concurrencyVersion"]!.GetValue<int>(), Is.EqualTo((await Forms.GetAsync(_spring))!.ConcurrencyVersion));
            Assert.That(Permissions(viewer.Body!), Is.EqualTo(new[] { "viewEntries" }));
            Assert.That(Permissions((await _api.CallAsync("hr", HttpMethod.Get, $"form/{_payroll}")).Body!), Is.EqualTo(new[] { "viewEntries", "editEntries" }));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Get, $"form/{_payroll}")).Outcome, Is.EqualTo("404 form_not_found"), "the designer does not see HR");
            var refused = await _api.CallAsync("viewer", HttpMethod.Get, $"form/{_spring}/draft");
            Assert.That((refused.Outcome, refused.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")));
            Assert.That(draft.Status, Is.EqualTo(HttpStatusCode.OK), draft.ToString());
            Assert.That(draft.Body!["definition"]!["key"]!.GetValue<Guid>(), Is.EqualTo(_spring));
            Assert.That(draft.Body!["definition"]!["schemaVersion"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(draft.Body!["definition"]!["pages"]!.AsArray(), Has.Count.EqualTo(1), "a blank form's page");
            Assert.That(draft.Body!["problems"]!.AsArray(), Is.Empty);
            Assert.That((draft.Body!["draftVersion"]!.GetValue<int>(), draft.Body["publishedVersion"], draft.Body["hasUnpublishedChanges"]!.GetValue<bool>()), Is.EqualTo((1, (JsonNode?)null, true)));
            Assert.That(Permissions(draft.Body!["form"]!), Is.EqualTo(new[] { "design", "manageActions" }));
        });
    }

    [Test]
    public async Task Create_InAFolder_AnswersCreatedWithTheKey_AndRecordsTheUser()
    {
        var archive = await FolderAsync("Archive");
        await TrashFolderAsync(archive);

        var created = await _api.CallAsync("designer", HttpMethod.Post, "form", new { name = "Survey", folderKey = _events });
        var stored = await Forms.GetAsync(Guid.Parse(created.Created!));

        Assert.Multiple(async () =>
        {
            Assert.That(created.Status, Is.EqualTo(HttpStatusCode.Created), created.ToString());
            Assert.That(created.Created, Is.EqualTo(created.Body!["key"]!.GetValue<string>()));
            Assert.That((created.Body!["name"]!.GetValue<string>(), created.Body["alias"]!.GetValue<string>(), created.Body["folderKey"]!.GetValue<Guid>()), Is.EqualTo(("Survey", "survey", _events)));
            Assert.That(stored!.CreatedByUserKey, Is.EqualTo(GetRequiredService<IUserService>().GetByEmail("api-designer@jfb.test")!.Key), "the user who signed in");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Post, "form", new { name = "Again", alias = "spring" })).Outcome, Is.EqualTo("409 alias_taken"));
            Assert.That((await _api.CallAsync("viewer", HttpMethod.Post, "form", new { name = "X" })).Outcome, Is.EqualTo("403 forbidden"), "no design at the root");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Post, "form", new { name = "X", folderKey = _hr })).Outcome, Is.EqualTo("404 folder_not_found"));
            Assert.That((await _api.CallAsync("admin", HttpMethod.Post, "form", new { name = "X", folderKey = archive })).Outcome, Is.EqualTo("409 folder_missing"), "nothing lives in the trash");
        });
    }

    [Test]
    public async Task SaveDraft_StoresTheDocument_AnsweringWithTheVersion_AndRefusesWhatCannotBeRead()
    {
        var (version, definition) = await DraftAsync(_spring);
        Fields(definition).Add(Field("name", "Name"));

        var saved = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = version, definition, comment = "A name field" });
        var reread = await DraftAsync(_spring);
        var unchanged = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = reread.Version, definition = reread.Definition });
        var stale = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = version, definition });
        var wrongShape = Clone(reread.Definition);
        wrongShape["pages"] = "none";
        var unreadable = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = reread.Version, definition = wrongShape });
        var otherKey = Clone(reread.Definition);
        otherKey["key"] = Guid.NewGuid().ToString();
        var elsewhere = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = reread.Version, definition = otherKey });
        var versions = (await Forms.ListVersionsAsync(_spring, includeDrafts: true, PageRequest.First(5)))!.Items.Select(entry => entry.Version).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(saved.Status, Is.EqualTo(HttpStatusCode.OK), saved.ToString());
            Assert.That((versions[0].Number, versions[0].Comment, versions[0].CreatedByUserKey), Is.EqualTo((2, "A name field", (Guid?)UserKey("designer"))), "the version with its comment and who saved it");
            Assert.That(saved.Body!["version"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(saved.Body!["form"]!["concurrencyVersion"]!.GetValue<int>(), Is.GreaterThan(version));
            Assert.That(Fields(reread.Definition).Select(field => field!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "name" }), "the document came back");
            Assert.That((unchanged.Status, unchanged.Body?["version"]), Is.EqualTo((HttpStatusCode.OK, (JsonNode?)null)), "nothing changed, nothing written");
            Assert.That(stale.Outcome, Is.EqualTo("409 concurrency_conflict"));
            Assert.That(unreadable.Status, Is.EqualTo(HttpStatusCode.BadRequest), unreadable.ToString());
            Assert.That(unreadable.Body?["errors"]?.AsArray().Select(error => error!["path"]!.GetValue<string>()), Has.Some.StartsWith("$.pages"), "a path into the document, from the reader");
            Assert.That((elsewhere.Outcome, elsewhere.Body!["errors"]![0]!["path"]!.GetValue<string>()), Is.EqualTo(("400 invalid_value", "$.key")));
        });
    }

    [Test]
    public async Task ASaveThatChangesTheActions_NeedsManageActions()
    {
        var (version, definition) = await DraftAsync(_spring);
        var withAction = Clone(definition);
        withAction["actions"] = new JsonArray(new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["type"] = "email", ["name"] = "Notify" });

        var drafter = await _api.CallAsync("drafter", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = version, definition = withAction });
        var staleDrafter = await _api.CallAsync("drafter", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = version - 1, definition = withAction });
        var designer = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = version, definition = withAction });
        var (afterDesigner, saved) = await DraftAsync(_spring);
        Fields(saved).Add(Field("email", "Email"));
        var drafterFields = await _api.CallAsync("drafter", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = afterDesigner, definition = saved });

        Assert.Multiple(async () =>
        {
            Assert.That((drafter.Outcome, drafter.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")), drafter.ToString());
            Assert.That(staleDrafter.Outcome, Is.EqualTo("409 concurrency_conflict"), "a stale save names no permission");
            Assert.That(designer.Status, Is.EqualTo(HttpStatusCode.OK), designer.ToString());
            Assert.That(drafterFields.Status, Is.EqualTo(HttpStatusCode.OK), "the drafter changes everything but the actions");
            Assert.That((await DraftAsync(_spring)).Definition["actions"]!.AsArray().Select(action => action!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Notify" }));
        });
    }

    [Test]
    public async Task PublishAndUnpublish_ChangeWhatVisitorsGet()
    {
        var published = await _api.CallAsync("designer", HttpMethod.Post, $"form/{_contact}/publish", new { concurrencyVersion = await VersionOfAsync(_contact), comment = "Live" });
        var again = await _api.CallAsync("designer", HttpMethod.Post, $"form/{_contact}/publish", new { concurrencyVersion = await VersionOfAsync(_contact) });
        var unpublished = await _api.CallAsync("drafter", HttpMethod.Post, $"form/{_contact}/unpublish", new { concurrencyVersion = await VersionOfAsync(_contact) });
        var live = (await Forms.ListVersionsAsync(_contact, includeDrafts: false, PageRequest.First(5)))!.Items.Single().Version;

        Assert.Multiple(async () =>
        {
            Assert.That(published.Status, Is.EqualTo(HttpStatusCode.OK), published.ToString());
            Assert.That((published.Body!["version"]!.GetValue<int>(), published.Body["form"]!["isPublished"]!.GetValue<bool>(), published.Body["form"]!["publicRevision"]!.GetValue<int>()), Is.EqualTo((2, true, 1)));
            Assert.That((again.Status, again.Body?["version"]), Is.EqualTo((HttpStatusCode.OK, (JsonNode?)null)), "the draft is live already");
            Assert.That(unpublished.Status, Is.EqualTo(HttpStatusCode.OK), unpublished.ToString());
            Assert.That(unpublished.Body!["isPublished"]!.GetValue<bool>(), Is.False);
            Assert.That((live.Comment, live.PublishedByUserKey), Is.EqualTo(("Live", (Guid?)UserKey("designer"))), "the publish with its comment and who published");
            Assert.That((await Forms.GetAsync(_contact))!.UpdatedByUserKey, Is.EqualTo(UserKey("drafter")), "who took it offline");
            Assert.That((await _api.CallAsync("viewer", HttpMethod.Post, $"form/{_contact}/publish", new { concurrencyVersion = await VersionOfAsync(_contact) })).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Post, $"form/{_contact}/publish", new { concurrencyVersion = 1 })).Outcome, Is.EqualTo("409 concurrency_conflict"));
        });
    }

    [Test]
    public async Task Rename_ChangesTheNameAndTheAlias()
    {
        var renamed = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}", new { concurrencyVersion = await VersionOfAsync(_spring), name = "Spring 2027", alias = "spring2027" });

        Assert.Multiple(async () =>
        {
            Assert.That(renamed.Status, Is.EqualTo(HttpStatusCode.OK), renamed.ToString());
            Assert.That((renamed.Body!["name"]!.GetValue<string>(), renamed.Body["alias"]!.GetValue<string>()), Is.EqualTo(("Spring 2027", "spring2027")));
            Assert.That(((await Forms.GetAsync(_spring))!.Name, (await Forms.GetAsync(_spring))!.UpdatedByUserKey), Is.EqualTo(("Spring 2027", (Guid?)UserKey("designer"))));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Put, $"form/{_spring}", new { concurrencyVersion = await VersionOfAsync(_spring), name = "X", alias = "contact" })).Outcome, Is.EqualTo("409 alias_taken"));
        });
    }

    [Test]
    public async Task Move_NeedsDesignOnTheNewFolder_ThatTheUserSees()
    {
        var locked = await FolderAsync("Locked");
        await RestrictAsync(locked, "designer", FormBuilderPermissions.ViewEntries);

        var intoEvents = await _api.CallAsync("designer", HttpMethod.Put, $"form/{_contact}/move", new { concurrencyVersion = await VersionOfAsync(_contact), folderKey = _events });
        var movedBy = (await Forms.GetAsync(_contact))!.UpdatedByUserKey;

        Assert.Multiple(async () =>
        {
            Assert.That(intoEvents.Status, Is.EqualTo(HttpStatusCode.OK), intoEvents.ToString());
            Assert.That(intoEvents.Body!["folderKey"]!.GetValue<Guid>(), Is.EqualTo(_events));
            Assert.That(movedBy, Is.EqualTo(UserKey("designer")));
            Assert.That((await Move("designer", _contact, _hr)).Outcome, Is.EqualTo("404 folder_not_found"), "HR is hidden from the designer");
            var intoLocked = await Move("designer", _contact, locked);
            Assert.That((intoLocked.Outcome, intoLocked.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")), "seen, but not to be changed");
            Assert.That((await Move("admin", _contact, Guid.NewGuid())).Outcome, Is.EqualTo("404 folder_not_found"));
            Assert.That((await Forms.GetAsync(_contact))!.FolderKey, Is.EqualTo(_events), "the refused moves changed nothing");
        });
    }

    [Test]
    public async Task AMoveThatGivesGroupsMoreAccess_NeedsManagePermissions_AndAConfirmation()
    {
        // Offer is in Contracts, which only the designers and the contractors reach; at the root every group's global
        // grants would reach it too.
        var offer = await FormAsync("Offer", _contracts);

        var designer = await Move("designer", offer, null);
        var unconfirmed = await Move("admin", offer, null);
        var confirmed = await _api.CallAsync("admin", HttpMethod.Put, $"form/{offer}/move", new { concurrencyVersion = await VersionOfAsync(offer), folderKey = Guid.Empty, widenAccess = true });

        Assert.Multiple(() =>
        {
            Assert.That((designer.Outcome, designer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "managePermissions")), designer.ToString());
            Assert.That(unconfirmed.Outcome, Is.EqualTo("409 widens_access"), unconfirmed.ToString());
            Assert.That(Gains(unconfirmed)[People.GroupKeys["viewer"]], Is.EqualTo("viewEntries"));
            Assert.That(Gains(designer), Is.EqualTo(Gains(unconfirmed)));
            Assert.That(confirmed.Status, Is.EqualTo(HttpStatusCode.OK), confirmed.ToString());
            Assert.That(confirmed.Body!["folderKey"], Is.Null, "an empty key is the root");
        });
    }

    [Test]
    public async Task TrashAndRestore_TakeTheFormOutOfTheLibraryAndBackIntoItsFolder()
    {
        var trashed = await _api.CallAsync("drafter", HttpMethod.Post, $"form/{_spring}/trash", new { concurrencyVersion = await VersionOfAsync(_spring) });
        var trashedBy = (await Forms.GetAsync(_spring))!.UpdatedByUserKey;
        var restored = await _api.CallAsync("designer", HttpMethod.Post, $"form/{_spring}/restore", new { concurrencyVersion = await VersionOfAsync(_spring) });

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.Status, Is.EqualTo(HttpStatusCode.OK), trashed.ToString());
            Assert.That(trashed.Body!["trashedUtc"], Is.Not.Null);
            Assert.That(restored.Status, Is.EqualTo(HttpStatusCode.OK), restored.ToString());
            Assert.That((trashedBy, (await Forms.GetAsync(_spring))!.UpdatedByUserKey), Is.EqualTo(((Guid?)UserKey("drafter"), (Guid?)UserKey("designer"))), "who trashed it, then who restored it");
            Assert.That((restored.Body!["trashedUtc"], restored.Body["folderKey"]!.GetValue<Guid>()), Is.EqualTo(((JsonNode?)null, _events)));
            Assert.That((await _api.CallAsync("viewer", HttpMethod.Post, $"form/{_spring}/trash", new { concurrencyVersion = await VersionOfAsync(_spring) })).Outcome, Is.EqualTo("403 forbidden"));
        });
    }

    [Test]
    public async Task ARestoreToTheRoot_BecauseItsFolderIsInTheTrash_NeedsWhatAMoveThereNeeds()
    {
        // Offer and Draft went to the trash on their own, then Contracts after them: a restore takes them to the root.
        var offer = await FormAsync("Offer", _contracts);
        var draft = await FormAsync("Draft", _contracts);
        var note = await FormAsync("Note", _contracts);
        await TrashFormAsync(offer);
        await TrashFormAsync(draft);
        await TrashFormAsync(note);
        var contractor = await Restore("contractor", note);
        await TrashFolderAsync(_contracts);

        var contractorAtRoot = await Restore("contractor", offer);
        var designer = await Restore("designer", offer);
        var unconfirmed = await Restore("admin", offer);
        var confirmed = await _api.CallAsync("admin", HttpMethod.Post, $"form/{offer}/restore", new { concurrencyVersion = await VersionOfAsync(offer), widenAccess = true });

        Assert.Multiple(() =>
        {
            Assert.That(contractor.Status, Is.EqualTo(HttpStatusCode.OK), "back into its folder asks nothing more");
            Assert.That((contractorAtRoot.Outcome, contractorAtRoot.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")), "no design at the root");
            Assert.That((designer.Outcome, designer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "managePermissions")), designer.ToString());
            Assert.That(unconfirmed.Outcome, Is.EqualTo("409 widens_access"));
            Assert.That(Gains(unconfirmed)[People.GroupKeys["viewer"]], Is.EqualTo("viewEntries"));
            Assert.That(confirmed.Status, Is.EqualTo(HttpStatusCode.OK), confirmed.ToString());
            Assert.That((confirmed.Body!["folderKey"], confirmed.Body["trashedUtc"]), Is.EqualTo(((JsonNode?)null, (JsonNode?)null)));
        });
    }

    [Test]
    public async Task ADeleteForGood_NeedsTheTrash_AndDesignAndDeleteEntries()
    {
        var live = await _api.CallAsync("admin", HttpMethod.Delete, $"form/{_contact}?concurrencyVersion={await VersionOfAsync(_contact)}");
        await TrashFormAsync(_contact);
        var version = await VersionOfAsync(_contact);

        var designer = await _api.CallAsync("designer", HttpMethod.Delete, $"form/{_contact}?concurrencyVersion={version}");
        var withoutVersion = await _api.CallAsync("cleaner", HttpMethod.Delete, $"form/{_contact}");
        var stale = await _api.CallAsync("cleaner", HttpMethod.Delete, $"form/{_contact}?concurrencyVersion={version - 1}");
        var cleaner = await _api.CallAsync("cleaner", HttpMethod.Delete, $"form/{_contact}?concurrencyVersion={version}");

        Assert.Multiple(async () =>
        {
            Assert.That(live.Outcome, Is.EqualTo("409 not_trashed"));
            Assert.That((designer.Outcome, designer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "deleteEntries")));
            Assert.That(withoutVersion.Outcome, Is.EqualTo("400 invalid_request"));
            Assert.That(stale.Outcome, Is.EqualTo("409 concurrency_conflict"));
            Assert.That(cleaner.Status, Is.EqualTo(HttpStatusCode.OK), cleaner.ToString());
            Assert.That(Counts(cleaner), Is.EqualTo("0 folders, 1 forms, 0 entries, 0 files"));
            Assert.That(await Forms.GetAsync(_contact), Is.Null);
        });
    }

    private IFormBuilderFormService Forms => GetRequiredService<IFormBuilderFormService>();

    private static List<string> Names(ApiAnswer page)
    {
        Assert.That(page.Status, Is.EqualTo(HttpStatusCode.OK), page.ToString());
        return [.. page.Body!["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>())];
    }

    private static List<string> Permissions(JsonNode item) => [.. item["permissions"]!.AsArray().Select(permission => permission!.GetValue<string>())];

    private static Dictionary<Guid, string> Gains(ApiAnswer answer) =>
        answer.Body!["gains"]!.AsArray().ToDictionary(gain => gain!["userGroupKey"]!.GetValue<Guid>(), gain => string.Join(",", gain!["permissions"]!.AsArray().Select(permission => permission!.GetValue<string>())));

    private static string Counts(ApiAnswer answer) =>
        $"{answer.Body!["folders"]} folders, {answer.Body["forms"]} forms, {answer.Body["entries"]} entries, {answer.Body["files"]} files";

    private static JsonObject Clone(JsonObject node) => node.DeepClone().AsObject();

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

    private static JsonObject Field(string alias, string label) =>
        new() { ["key"] = Guid.NewGuid().ToString(), ["type"] = "text", ["alias"] = alias, ["label"] = new JsonObject { [string.Empty] = label } };

    private async Task<int> VersionOfAsync(Guid form) => (await Forms.GetAsync(form))!.ConcurrencyVersion;

    // The API user who calls as the role, once it has signed in.
    private Guid UserKey(string role) => GetRequiredService<IUserService>().GetByEmail($"api-{role}@jfb.test")!.Key;

    // The form's concurrency version and its draft's document, as the designer reads them.
    private async Task<(int Version, JsonObject Definition)> DraftAsync(Guid form)
    {
        var draft = await _api.CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        Assert.That(draft.Status, Is.EqualTo(HttpStatusCode.OK), draft.ToString());
        return (draft.Body!["form"]!["concurrencyVersion"]!.GetValue<int>(), Clone(draft.Body["definition"]!.AsObject()));
    }

    private async Task<ApiAnswer> Move(string role, Guid form, Guid? folderKey) =>
        await _api.CallAsync(role, HttpMethod.Put, $"form/{form}/move", new { concurrencyVersion = await VersionOfAsync(form), folderKey });

    private async Task<ApiAnswer> Restore(string role, Guid form) =>
        await _api.CallAsync(role, HttpMethod.Post, $"form/{form}/restore", new { concurrencyVersion = await VersionOfAsync(form) });
}
