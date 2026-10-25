using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The folder endpoints of the management API (ADR-0047, ADR-0050) on a real site, as the personas: what each answers,
/// what it changes, and the rules that are the API's own: <c>design</c> on a move's new parent, <c>managePermissions</c>
/// and a confirmation for a move that widens access, <c>design</c> and <c>deleteEntries</c> on everything a permanent
/// delete removes, and an emptied trash that keeps what the user may not delete. The cleaner is an extra persona, granted
/// <c>design</c>, <c>deleteEntries</c> and the <c>viewEntries</c> it needs globally.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FolderApiTests : ManagementApiTestBase
{
    private const string Cleaners = "jfbCleaners";

    private ManagementApiCaller _api = null!;
    private Guid _events;
    private Guid _hr;

    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var group = await ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider)
                .EnsureUserGroupAsync(new TestUserGroup { Alias = Cleaners, Name = "Cleaners", Sections = { AccessPersonas.Section } });
            await GrantAsync(group.Key, PermissionTarget.Global, Guid.Empty, FormBuilderLibraryService.ToDelete | FormBuilderPermissions.ViewEntries);
        }

        _api = new ManagementApiCaller(Client, Services, role => role == "cleaner" ? [Cleaners] : [AccessPersonas.GroupOf(role)]);
        _events = await FolderAsync("Events");
        _hr = await FolderAsync("HR");
        await RestrictAsync(_hr, "hr", FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries);
    }

    [Test]
    public async Task Get_AnswersTheFolder_WithTheUsersPermissionsOnIt()
    {
        var events = await _api.CallAsync("viewer", HttpMethod.Get, $"folder/{_events}");
        var hr = await _api.CallAsync("hr", HttpMethod.Get, $"folder/{_hr}");

        Assert.Multiple(() =>
        {
            Assert.That(events.Status, Is.EqualTo(HttpStatusCode.OK), events.ToString());
            Assert.That((events.Body!["key"]!.GetValue<Guid>(), events.Body["name"]!.GetValue<string>(), events.Body["isRestricted"]!.GetValue<bool>()), Is.EqualTo((_events, "Events", false)));
            Assert.That(Permissions(events), Is.EqualTo(new[] { "viewEntries" }));
            Assert.That(events.Body!["trashedUtc"], Is.Null);
            Assert.That(Permissions(hr), Is.EqualTo(new[] { "viewEntries", "editEntries" }));
            Assert.That(hr.Body!["isRestricted"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public async Task Create_AtTheRootOrInAFolder_AnswersCreatedWithTheKey_AndRecordsTheUser()
    {
        var atRoot = await _api.CallAsync("designer", HttpMethod.Post, "folder", new { name = "  Campaigns " });
        var inEvents = await _api.CallAsync("designer", HttpMethod.Post, "folder", new { name = "Spring", parentKey = _events });
        var stored = await GetRequiredService<IFormBuilderFolderService>().GetAsync(Guid.Parse(inEvents.Created!));

        Assert.Multiple(() =>
        {
            Assert.That(atRoot.Status, Is.EqualTo(HttpStatusCode.Created), atRoot.ToString());
            Assert.That(atRoot.Created, Is.EqualTo(atRoot.Body!["key"]!.GetValue<string>()));
            Assert.That((atRoot.Body!["name"]!.GetValue<string>(), atRoot.Body["parentKey"]), Is.EqualTo(("Campaigns", (JsonNode?)null)), "the name trimmed");
            Assert.That(Permissions(atRoot), Is.EqualTo(new[] { "design", "manageActions" }), "the folder-level part of the designer's grants");
            Assert.That(inEvents.Body!["parentKey"]!.GetValue<Guid>(), Is.EqualTo(_events));
            Assert.That(stored!.CreatedByUserKey, Is.EqualTo(GetRequiredService<IUserService>().GetByEmail("api-designer@jfb.test")!.Key), "the user who signed in");
        });
    }

    [Test]
    public async Task Create_InAFolderTheUserCannotChange_OrInTheTrash_IsRefused()
    {
        var archive = await FolderAsync("Archive");
        await TrashFolderAsync(archive);

        Assert.Multiple(async () =>
        {
            Assert.That((await _api.CallAsync("viewer", HttpMethod.Post, "folder", new { name = "X", parentKey = _events })).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That((await _api.CallAsync("viewer", HttpMethod.Post, "folder", new { parentKey = _events })).Outcome, Is.EqualTo("400 invalid_request"), "a body that cannot be read names no folder to check");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Post, "folder", new { name = "X", parentKey = _hr })).Outcome, Is.EqualTo("404 folder_not_found"), "the designer does not see HR");
            Assert.That((await _api.CallAsync("admin", HttpMethod.Post, "folder", new { name = "X", parentKey = archive })).Outcome, Is.EqualTo("409 folder_missing"), "nothing lives in the trash");
        });
    }

    [Test]
    public async Task Rename_ChangesTheName_AndChecksThePermissionBeforeTheRequestsValues()
    {
        var unread = await _api.CallAsync("viewer", HttpMethod.Put, $"folder/{_events}", new { });
        var unnamed = await _api.CallAsync("designer", HttpMethod.Put, $"folder/{_events}", new { });
        var renamed = await _api.CallAsync("designer", HttpMethod.Put, $"folder/{_events}", new { name = "Events 2027" });

        Assert.Multiple(async () =>
        {
            Assert.That(renamed.Status, Is.EqualTo(HttpStatusCode.OK), renamed.ToString());
            Assert.That(renamed.Body!["name"]!.GetValue<string>(), Is.EqualTo("Events 2027"));
            Assert.That((await GetRequiredService<IFormBuilderFolderService>().GetAsync(_events))!.Name, Is.EqualTo("Events 2027"));
            Assert.That(unread.Outcome, Is.EqualTo("403 forbidden"), "the permission comes before the request's values");
            Assert.That(unnamed.Outcome, Is.EqualTo("400 invalid_request"), unnamed.ToString());
        });
    }

    [Test]
    public async Task Move_NeedsDesignOnTheNewParent_ThatTheUserSees()
    {
        var spare = await FolderAsync("Spare");
        var locked = await FolderAsync("Locked");
        await RestrictAsync(locked, "designer", FormBuilderPermissions.ViewEntries);

        var intoEvents = await _api.CallAsync("designer", HttpMethod.Put, $"folder/{spare}/move", new { parentKey = _events });

        Assert.Multiple(async () =>
        {
            Assert.That(intoEvents.Status, Is.EqualTo(HttpStatusCode.OK), intoEvents.ToString());
            Assert.That(intoEvents.Body!["parentKey"]!.GetValue<Guid>(), Is.EqualTo(_events));
            Assert.That((await _api.CallAsync("designer", HttpMethod.Put, $"folder/{spare}/move", new { parentKey = _hr })).Outcome, Is.EqualTo("404 folder_not_found"), "HR is hidden from the designer");
            var intoLocked = await _api.CallAsync("designer", HttpMethod.Put, $"folder/{spare}/move", new { parentKey = locked });
            Assert.That((intoLocked.Outcome, intoLocked.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")), "seen, but not to be changed");
            Assert.That((await _api.CallAsync("designer", HttpMethod.Put, $"folder/{spare}/move", new { parentKey = Guid.NewGuid() })).Outcome, Is.EqualTo("404 folder_not_found"));
            Assert.That((await _api.CallAsync("admin", HttpMethod.Put, $"folder/{spare}/move", new { parentKey = Guid.NewGuid() })).Outcome, Is.EqualTo("404 folder_not_found"), "for anyone");
            Assert.That((await _api.CallAsync("admin", HttpMethod.Put, $"folder/{_events}/move", new { parentKey = spare })).Outcome, Is.EqualTo("409 folder_into_itself"));
            Assert.That((await GetRequiredService<IFormBuilderFolderService>().GetAsync(spare))!.ParentKey, Is.EqualTo(_events), "the refused moves changed nothing");
        });
    }

    [Test]
    public async Task AMoveThatGivesGroupsMoreAccess_NeedsManagePermissions_AndAConfirmation()
    {
        // Contracts is restricted and granted to the designers; Drafts inside it holds only their grant. At the root it
        // would hold the global grants of every group too.
        var contracts = await FolderAsync("Contracts");
        await RestrictAsync(contracts, "designer", FormBuilderPermissions.Design);
        var drafts = await FolderAsync("Drafts", contracts);

        var designer = await _api.CallAsync("designer", HttpMethod.Put, $"folder/{drafts}/move", new { parentKey = (Guid?)null });
        var unconfirmed = await _api.CallAsync("admin", HttpMethod.Put, $"folder/{drafts}/move", new { parentKey = (Guid?)null });
        var gainsBeforeMove = Gains(unconfirmed);
        var confirmed = await _api.CallAsync("admin", HttpMethod.Put, $"folder/{drafts}/move", new { parentKey = Guid.Empty, widenAccess = true });

        Assert.Multiple(() =>
        {
            Assert.That((designer.Outcome, designer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "managePermissions")), designer.ToString());
            Assert.That(Gains(designer), Is.EqualTo(gainsBeforeMove));
            Assert.That(unconfirmed.Outcome, Is.EqualTo("409 widens_access"), unconfirmed.ToString());
            Assert.That(gainsBeforeMove[People.GroupKeys["viewer"]], Is.EqualTo("viewEntries"));
            Assert.That(gainsBeforeMove[People.GroupKeys["designer"]], Is.EqualTo("manageActions"), "the designers held only design inside Contracts");
            Assert.That(gainsBeforeMove.ContainsKey(People.GroupKeys["noAccess"]), Is.False, "a group without grants gains nothing");
            Assert.That(confirmed.Status, Is.EqualTo(HttpStatusCode.OK), confirmed.ToString());
            Assert.That(confirmed.Body!["parentKey"], Is.Null, "an empty key is the root");
        });
    }

    [Test]
    public async Task TrashAndRestore_TakeTheFolderWithWhatIsBelowIt()
    {
        var archive = await FolderAsync("Archive");
        var old = await FolderAsync("Old", archive);
        var notes = await FormAsync("Notes", old);

        var trashed = await _api.CallAsync("designer", HttpMethod.Post, $"folder/{archive}/trash");
        var inTrash = await _api.CallAsync("designer", HttpMethod.Get, $"folder/{archive}");
        var restored = await _api.CallAsync("designer", HttpMethod.Post, $"folder/{archive}/restore");

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.Status, Is.EqualTo(HttpStatusCode.OK), trashed.ToString());
            Assert.That(Keys(trashed, "folders"), Is.EqualTo(new[] { archive, old }));
            Assert.That(Keys(trashed, "forms"), Is.EqualTo(new[] { notes }));
            Assert.That(inTrash.Body!["trashedUtc"], Is.Not.Null);
            Assert.That(Keys(restored, "folders"), Is.EqualTo(new[] { archive, old }));
            Assert.That(Keys(restored, "forms"), Is.EqualTo(new[] { notes }));
            Assert.That((await GetRequiredService<IFormBuilderFolderService>().GetAsync(old))!.TrashedUtc, Is.Null);
            Assert.That((await _api.CallAsync("viewer", HttpMethod.Post, $"folder/{archive}/trash")).Outcome, Is.EqualTo("403 forbidden"));
        });
    }

    [Test]
    public async Task ADeleteForGood_NeedsTheTrash_AndDesignAndDeleteEntriesOnEverythingItRemoves()
    {
        // Archive holds the restricted Personnel, which only the HR team sees, and a form.
        var archive = await FolderAsync("Archive");
        var personnel = await FolderAsync("Personnel", archive);
        await RestrictAsync(personnel, "hr", FormBuilderPermissions.ViewEntries);
        await FormAsync("Notes", archive);
        var live = await _api.CallAsync("admin", HttpMethod.Delete, $"folder/{archive}");
        await TrashFolderAsync(archive);

        var designer = await _api.CallAsync("designer", HttpMethod.Delete, $"folder/{archive}");
        var cleaner = await _api.CallAsync("cleaner", HttpMethod.Delete, $"folder/{archive}");
        var stillThere = await GetRequiredService<IFormBuilderFolderService>().GetAsync(personnel);
        var admin = await _api.CallAsync("admin", HttpMethod.Delete, $"folder/{archive}");

        Assert.Multiple(async () =>
        {
            Assert.That(live.Outcome, Is.EqualTo("409 not_trashed"));
            Assert.That((designer.Outcome, designer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "deleteEntries")));
            Assert.That((cleaner.Outcome, cleaner.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")), "Personnel below Archive is not the cleaner's");
            Assert.That(stillThere, Is.Not.Null, "a refused delete removes nothing");
            Assert.That(admin.Status, Is.EqualTo(HttpStatusCode.OK), admin.ToString());
            Assert.That(Counts(admin), Is.EqualTo("2 folders, 1 forms, 0 entries, 0 files"));
            Assert.That(await GetRequiredService<IFormBuilderFolderService>().GetAsync(personnel), Is.Null);
        });
    }

    [Test]
    public async Task ADeleteForGood_IsRefused_ForAFolderThatHoldsARestrictedFormTheUserMayNotDelete()
    {
        var records = await FolderAsync("Records");
        var salaries = await FormAsync("Salaries", records);
        var form = (await GetRequiredService<IFormBuilderFormService>().GetAsync(salaries))!;
        await GetRequiredService<IFormBuilderAccessService>().RestrictFormAsync(salaries, form.ConcurrencyVersion, true, null);
        await GrantAsync(People.GroupKeys["hr"], PermissionTarget.Form, salaries, FormBuilderPermissions.ViewEntries);
        await TrashFolderAsync(records);

        var cleaner = await _api.CallAsync("cleaner", HttpMethod.Delete, $"folder/{records}");

        Assert.Multiple(async () =>
        {
            Assert.That((cleaner.Outcome, cleaner.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")));
            Assert.That(await GetRequiredService<IFormBuilderFormService>().GetAsync(salaries), Is.Not.Null);
        });
    }

    [Test]
    public async Task EmptyingTheTrash_RemovesWhatTheUserMayDeleteForGood_AndKeepsTheRest()
    {
        var archive = await FolderAsync("Archive");
        var personnel = await FolderAsync("Personnel", archive);
        await RestrictAsync(personnel, "hr", FormBuilderPermissions.ViewEntries);
        var spare = await FolderAsync("Spare");
        var old = await FormAsync("Old");
        await TrashFolderAsync(archive);
        await TrashFolderAsync(spare);
        await TrashFormAsync(old);

        var cleaner = await _api.CallAsync("cleaner", HttpMethod.Delete, "trash");
        var left = await _api.CallAsync("admin", HttpMethod.Get, "trash");
        var admin = await _api.CallAsync("admin", HttpMethod.Delete, "trash");
        var viewer = await _api.CallAsync("viewer", HttpMethod.Delete, "trash");

        Assert.Multiple(() =>
        {
            Assert.That(cleaner.Status, Is.EqualTo(HttpStatusCode.OK), cleaner.ToString());
            Assert.That((Counts(cleaner, "deleted"), cleaner.Body!["left"]!.GetValue<int>()), Is.EqualTo(("1 folders, 1 forms, 0 entries, 0 files", 1)), "Spare and Old went; Archive, with Personnel, stayed");
            Assert.That(left.Body!["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Archive" }));
            Assert.That((Counts(admin, "deleted"), admin.Body!["left"]!.GetValue<int>()), Is.EqualTo(("2 folders, 0 forms, 0 entries, 0 files", 0)));
            Assert.That((Counts(viewer, "deleted"), viewer.Body!["left"]!.GetValue<int>()), Is.EqualTo(("0 folders, 0 forms, 0 entries, 0 files", 0)), "an empty trash");
        });
    }

    private static List<string> Permissions(ApiAnswer answer) => [.. answer.Body!["permissions"]!.AsArray().Select(permission => permission!.GetValue<string>())];

    private static List<Guid> Keys(ApiAnswer answer, string list) => [.. answer.Body![list]!.AsArray().Select(key => key!.GetValue<Guid>())];

    private static Dictionary<Guid, string> Gains(ApiAnswer answer) =>
        answer.Body!["gains"]!.AsArray().ToDictionary(gain => gain!["userGroupKey"]!.GetValue<Guid>(), gain => string.Join(",", gain!["permissions"]!.AsArray().Select(permission => permission!.GetValue<string>())));

    private static string Counts(ApiAnswer answer, string? part = null)
    {
        var counts = part is null ? answer.Body! : answer.Body![part]!;
        return $"{counts["folders"]} folders, {counts["forms"]} forms, {counts["entries"]} entries, {counts["files"]} files";
    }
}
