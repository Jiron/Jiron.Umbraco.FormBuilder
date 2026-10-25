using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The library tree and the trash of the management API (ADR-0048, ADR-0050) as the personas see them. Each item reads
/// <c>kind name (restricted) +: permissions</c>, where <c>+</c> marks a folder with something the user sees in it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TreeApiTests : ManagementApiTestBase
{
    private const string AllInFolders = "design,manageActions,viewEntries,viewSensitive,editEntries,deleteEntries,export";

    private Guid _contact;
    private Guid _events;
    private Guid _hr;
    private Guid _payroll;

    // Events holds the form Spring, HR (restricted, for the HR team) the form Payroll, Empty nothing; Contact is at the root.
    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        _events = await FolderAsync("Events");
        await FormAsync("Spring", _events);
        _hr = await FolderAsync("HR");
        await RestrictAsync(_hr, "hr", FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries);
        _payroll = await FormAsync("Payroll", _hr);
        await FolderAsync("Empty");
        _contact = await FormAsync("Contact");
    }

    [Test]
    public async Task TheRoot_ListsTheFoldersFirst_ThenTheForms_EachWithTheUsersPermissions()
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        await forms.PublishAsync(_contact, (await forms.GetAsync(_contact))!.ConcurrencyVersion, null, null);

        var root = await CallAsync("admin", HttpMethod.Get, "tree/root");

        Assert.Multiple(() =>
        {
            Assert.That(root.Status, Is.EqualTo(HttpStatusCode.OK), root.ToString());
            Assert.That(Items(root), Is.EqualTo(new[]
            {
                $"folder Empty: {AllInFolders}",
                $"folder Events +: {AllInFolders}",
                $"folder HR (restricted) +: {AllInFolders}",
                $"form Contact: {AllInFolders}",
            }), "folders of the same place in the order of their names");
            Assert.That(root.Body!["total"]!.GetValue<long>(), Is.EqualTo(4));
            Assert.That(root.Body!["items"]![3]!["alias"]!.GetValue<string>(), Is.EqualTo("contact"));
            Assert.That(root.Body!["items"]!.AsArray().Select(item => item!["isPublished"]!.GetValue<bool>()), Is.EqualTo(new[] { false, false, false, true }), "the published Contact");
            Assert.That(root.Body!["items"]!.AsArray().Select(item => item!["parentKey"]), Is.All.Null);
        });
    }

    [Test]
    public async Task AFoldersChildren_WithoutAFolder_AreTheRoots()
    {
        var root = await CallAsync("admin", HttpMethod.Get, "tree/root");
        var noKey = await CallAsync("admin", HttpMethod.Get, "tree/children");
        var emptyKey = await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={Guid.Empty}");

        Assert.Multiple(() =>
        {
            Assert.That(Items(noKey), Is.EqualTo(Items(root)));
            Assert.That(Items(emptyKey), Is.EqualTo(Items(root)));
        });
    }

    [Test]
    public async Task AFolderHasChildren_OnlyForWhatTheUserSees_InItsOwnStateOfTheTrash()
    {
        var memo = await FormAsync("Memo", await FolderAsync("Hidden"));
        var forms = GetRequiredService<IFormBuilderFormService>();
        await GetRequiredService<IFormBuilderAccessService>().RestrictFormAsync(memo, (await forms.GetAsync(memo))!.ConcurrencyVersion, true, null);
        var shared = await FolderAsync("Shared");
        await RestrictAsync(await FolderAsync("Secret", shared), "hr", FormBuilderPermissions.ViewEntries);
        var spare = await FolderAsync("Spare");
        await TrashFolderAsync(await FolderAsync("Gone", spare));
        await TrashFormAsync(await FormAsync("Dropped", spare));

        static IEnumerable<string> Picked(List<string> items) => items.Where(item => item.Split(':')[0].TrimEnd(' ', '+') is "folder Hidden" or "folder Shared" or "folder Spare");

        Assert.Multiple(async () =>
        {
            Assert.That(Picked(Items(await CallAsync("admin", HttpMethod.Get, "tree/root"))), Is.EqualTo(new[] { $"folder Hidden +: {AllInFolders}", $"folder Shared +: {AllInFolders}", $"folder Spare: {AllInFolders}" }), "Spare holds only what is in the trash");
            Assert.That(Picked(Items(await CallAsync("viewer", HttpMethod.Get, "tree/root"))), Is.EqualTo(new[] { "folder Hidden: viewEntries", "folder Shared: viewEntries", "folder Spare: viewEntries" }), "the viewer sees neither Memo nor Secret");
        });
    }

    [Test]
    public async Task TheLibrary_ListsNothing_ForAFolderThatIsNone()
    {
        var administrator = new AccessUser(Guid.NewGuid(), new HashSet<Guid>(), IsAdministrator: true, HasSection: true);

        Assert.That(await GetRequiredService<IFormBuilderLibraryService>().ListChildrenAsync(administrator, Guid.NewGuid(), PageRequest.First(10)), Is.Null, "a folder deleted after the access check answers 404 too");
    }

    [Test]
    public async Task TheTree_HoldsWhatEachPersonaSees()
    {
        await GrantAsync(People.GroupKeys["entryHandler"], PermissionTarget.Form, _payroll, FormBuilderPermissions.ViewEntries);

        Assert.Multiple(async () =>
        {
            Assert.That(Items(await CallAsync("viewer", HttpMethod.Get, "tree/root")), Is.EqualTo(new[] { "folder Empty: viewEntries", "folder Events +: viewEntries", "form Contact: viewEntries" }), "not the restricted HR");
            Assert.That(Items(await CallAsync("hr", HttpMethod.Get, "tree/root")), Is.EqualTo(new[] { "folder HR (restricted) +: viewEntries,editEntries" }), "only HR");
            Assert.That(Items(await CallAsync("hr", HttpMethod.Get, $"tree/children?parentKey={_hr}")), Is.EqualTo(new[] { "form Payroll: viewEntries,editEntries" }));
            Assert.That(Items(await CallAsync("noAccess", HttpMethod.Get, "tree/root")), Is.Empty, "nothing granted, nothing shown");
            Assert.That(Items(await CallAsync("entryHandler", HttpMethod.Get, $"tree/children?parentKey={_hr}")), Is.EqualTo(new[] { "form Payroll: viewEntries" }), "the form granted on its own");
            Assert.That((await CallAsync("entryHandler", HttpMethod.Get, "tree/root")).Body!["items"]!.AsArray().Select(Item), Does.Contain("folder HR (restricted) +: "), "HR shows on the way to it, with nothing held");
            Assert.That((await CallAsync("viewer", HttpMethod.Get, $"tree/children?parentKey={_hr}")).Outcome, Is.EqualTo("404 folder_not_found"));
        });
    }

    [Test]
    public async Task AFoldersChildren_PageAcrossItsFoldersAndForms_FoldersFirst()
    {
        var big = await FolderAsync("Big");
        await FolderAsync("B1", big);
        await FolderAsync("B2", big);
        foreach (var name in new[] { "F3", "F1", "F2" })
        {
            await FormAsync(name, big);
        }

        var first = await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={big}&take=2");
        var across = await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={big}&skip=1&take=2");
        var last = await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={big}&skip=3&take=5");
        var past = await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={big}&skip=9");

        Assert.Multiple(() =>
        {
            Assert.That(Names(first), Is.EqualTo(new[] { "B1", "B2" }));
            Assert.That(Names(across), Is.EqualTo(new[] { "B2", "F1" }));
            Assert.That(Names(last), Is.EqualTo(new[] { "F2", "F3" }), "forms by name");
            Assert.That(Names(past), Is.Empty);
            Assert.That(new[] { first, across, last, past }.Select(page => page.Body!["total"]!.GetValue<long>()), Is.All.EqualTo(5));
            Assert.That(first.Body!["items"]![0]!["parentKey"]!.GetValue<Guid>(), Is.EqualTo(big));
        });
    }

    [Test]
    public async Task TheRootAndTheTrash_PageToo()
    {
        await TrashFolderAsync(await FolderAsync("Archive"));
        await TrashFormAsync(await FormAsync("Old"));

        var root = await CallAsync("admin", HttpMethod.Get, "tree/root?skip=1&take=2");
        var trash = await CallAsync("admin", HttpMethod.Get, "trash?skip=1&take=1");

        Assert.Multiple(() =>
        {
            Assert.That(Names(root), Is.EqualTo(new[] { "Events", "HR" }), root.ToString());
            Assert.That(root.Body!["total"]!.GetValue<long>(), Is.EqualTo(4));
            Assert.That(Names(trash), Is.EqualTo(new[] { "Old" }), trash.ToString());
            Assert.That(trash.Body!["total"]!.GetValue<long>(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TheTree_LeavesTheTrashOut_AndAFolderInTheTrashHoldsWhatWentWithIt()
    {
        var archive = await FolderAsync("Archive");
        var old = await FolderAsync("Old", archive);
        await FormAsync("Notes", old);
        await TrashFolderAsync(archive);

        var root = await CallAsync("admin", HttpMethod.Get, "tree/root");
        var inTrashed = await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={archive}");

        Assert.Multiple(() =>
        {
            Assert.That(Names(root), Does.Not.Contain("Archive"));
            Assert.That(Items(inTrashed), Is.EqualTo(new[] { $"folder Old +: {AllInFolders}" }));
            Assert.That(inTrashed.Body!["items"]![0]!["isTrashed"]!.GetValue<bool>(), Is.True);
        });
    }

    [Test]
    public async Task TheTrash_ListsItsTopItems_ThatTheUserSees_FoldersFirst()
    {
        var archive = await FolderAsync("Archive");
        await FolderAsync("Old", archive);
        await FormAsync("Minutes", archive);
        await TrashFolderAsync(archive);
        var spring = (await CallAsync("admin", HttpMethod.Get, $"tree/children?parentKey={_events}")).Body!["items"]![0]!["key"]!.GetValue<Guid>();
        await TrashFormAsync(spring);
        await TrashFormAsync(_payroll);

        var admin = await CallAsync("admin", HttpMethod.Get, "trash");
        var viewer = await CallAsync("viewer", HttpMethod.Get, "trash");
        var hr = await CallAsync("hr", HttpMethod.Get, "trash");

        Assert.Multiple(() =>
        {
            Assert.That(Items(admin), Is.EqualTo(new[] { $"folder Archive +: {AllInFolders}", $"form Payroll: {AllInFolders}", $"form Spring: {AllInFolders}" }), "not Old or Minutes, which went with Archive");
            Assert.That(admin.Body!["items"]!.AsArray().Select(item => item!["isTrashed"]!.GetValue<bool>()), Is.All.True);
            Assert.That(Items(viewer), Is.EqualTo(new[] { "folder Archive +: viewEntries", "form Spring: viewEntries" }));
            Assert.That(Items(hr), Is.EqualTo(new[] { "form Payroll: viewEntries,editEntries" }));
        });
    }

    [TestCase("tree/root?take=0")]
    [TestCase("tree/root?take=501")]
    [TestCase("tree/root?skip=-1")]
    [TestCase("trash?take=0")]
    public async Task APageOutsideTheLimits_IsRefused(string path)
    {
        var answer = await CallAsync("admin", HttpMethod.Get, path);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("400 invalid_request"), answer.ToString());
            Assert.That(answer.Body!["errors"]!.AsArray().Select(error => error!["message"]!.GetValue<string>()), Is.All.EqualTo("This value is missing or not valid."), "never the server's words");
        });
    }

    private static string Item(JsonNode? item) =>
        $"{item!["kind"]} {item["name"]}{(item["isRestricted"]!.GetValue<bool>() ? " (restricted)" : string.Empty)}{(item["hasChildren"]!.GetValue<bool>() ? " +" : string.Empty)}: {string.Join(",", item["permissions"]!.AsArray().Select(permission => permission!.GetValue<string>()))}";

    private static List<string> Items(ApiAnswer page)
    {
        Assert.That(page.Status, Is.EqualTo(HttpStatusCode.OK), page.ToString());
        return [.. page.Body!["items"]!.AsArray().Select(Item)];
    }

    private static List<string> Names(ApiAnswer page) => [.. page.Body!["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>())];
}
