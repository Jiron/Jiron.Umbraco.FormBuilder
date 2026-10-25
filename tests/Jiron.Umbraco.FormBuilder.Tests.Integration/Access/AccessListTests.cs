using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// The "who may see this form" filter on a real database (ADR-0048), as each persona of ADR-0013: the form list filters
/// in its query, before paging and counting, and the folder list keeps the folders on the way to what the persona may
/// see. The library:
/// <code>
/// Welcome, Board (restricted, granted to the designers), Gone (in the trash)
/// Events: Spring, Autumn
/// HR (restricted, granted to the HR team): Payroll
///   Archive: Old
/// </code>
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class AccessListTests : RepositoryTestBase
{
    private Personas _personas = null!;

    private IFormBuilderAccessService Access => GetRequiredService<IFormBuilderAccessService>();

    private IFormBuilderFolderService FolderService => GetRequiredService<IFormBuilderFolderService>();

    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        _personas = await ActivatorUtilities.CreateInstance<AccessPersonas>(Services).CreateAsync();

        await CreateFormAsync("welcome", name: "Welcome");
        var board = await CreateFormAsync("board", name: "Board");
        await Access.RestrictFormAsync(board.Key, board.ConcurrencyVersion, true, Editor);
        await Access.ChangeGrantAsync(new AccessGrantChange(_personas.GroupKeys["designer"], PermissionTarget.Form, board.Key, FormBuilderPermissions.None, FormBuilderPermissions.Design, Editor));
        var gone = await CreateFormAsync("gone", name: "Gone");
        await Forms.TrashAsync(gone.Id, gone.ConcurrencyVersion, Editor);

        var events = await FolderService.CreateAsync(new NewFolderRequest("Events", UserKey: Editor));
        await CreateFormAsync("spring", await IdOf(events.Key), "Spring");
        await CreateFormAsync("autumn", await IdOf(events.Key), "Autumn");

        var hr = await FolderService.CreateAsync(new NewFolderRequest("HR", UserKey: Editor));
        await Access.RestrictFolderAsync(hr.Key, true, Editor);
        await Access.ChangeGrantAsync(new AccessGrantChange(_personas.GroupKeys["hr"], PermissionTarget.Folder, hr.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries, Editor));
        await CreateFormAsync("payroll", await IdOf(hr.Key), "Payroll");
        var archive = await FolderService.CreateAsync(new NewFolderRequest("Archive", hr.Key, Editor));
        await CreateFormAsync("old", await IdOf(archive.Key), "Old");
    }

    [TestCase("admin", new[] { "Autumn", "Board", "Old", "Payroll", "Spring", "Welcome" })]
    [TestCase("designer", new[] { "Autumn", "Board", "Spring", "Welcome" })]
    [TestCase("entryHandler", new[] { "Autumn", "Spring", "Welcome" })]
    [TestCase("viewer", new[] { "Autumn", "Spring", "Welcome" })]
    [TestCase("privacyOfficer", new[] { "Autumn", "Spring", "Welcome" })]
    [TestCase("noAccess", new string[0])]
    [TestCase("hr", new[] { "Old", "Payroll" })]
    [TestCase("outsider", new string[0])]
    public async Task TheFormList_HoldsWhatThePersonaMaySee_CountedBeforePaging(string role, string[] expected)
    {
        var access = await Access.GetFormVisibilityAsync(_personas[role]);

        var all = await Forms.ListAsync(new FormFilter { AllFolders = true, Access = access }, PageRequest.First(50));
        var first = await Forms.ListAsync(new FormFilter { AllFolders = true, Access = access }, PageRequest.First(2));

        Assert.Multiple(() =>
        {
            Assert.That(all.Items.Select(form => form.Name), Is.EqualTo(expected));
            Assert.That(first.Items.Select(form => form.Name), Is.EqualTo(expected.Take(2)), "a page of the filtered list");
            Assert.That(first.Total, Is.EqualTo(expected.Length), "the count is the filtered one");
        });
    }

    [TestCase("admin", new[] { "Autumn", "Board", "Old", "Payroll", "Spring", "Welcome" })]
    [TestCase("designer", new string[0])]
    [TestCase("entryHandler", new[] { "Autumn", "Spring", "Welcome" })]
    [TestCase("hr", new[] { "Old", "Payroll" })]
    public async Task AListByAPermission_HoldsTheFormsWhereThePersonaHoldsIt(string role, string[] expected)
    {
        var viewing = await Access.GetFormVisibilityAsync(_personas[role], FormBuilderPermissions.ViewEntries);

        var forms = await Forms.ListAsync(new FormFilter { AllFolders = true, Access = viewing }, PageRequest.First(50));

        Assert.That(forms.Items.Select(form => form.Name), Is.EqualTo(expected), "the forms whose entries the persona may view");
    }

    [TestCase("admin", new[] { "Archive", "Events", "HR" })]
    [TestCase("designer", new[] { "Events" })]
    [TestCase("entryHandler", new[] { "Events" })]
    [TestCase("viewer", new[] { "Events" })]
    [TestCase("privacyOfficer", new[] { "Events" })]
    [TestCase("noAccess", new string[0])]
    [TestCase("hr", new[] { "Archive", "HR" })]
    [TestCase("outsider", new string[0])]
    public async Task TheFolderList_HoldsTheFoldersOnTheWayToWhatThePersonaMaySee(string role, string[] expected)
    {
        var visibility = await Access.GetFolderVisibilityAsync(_personas[role]);

        var folders = await FolderService.ListAsync(visibility);

        Assert.That(folders.Select(folder => folder.Name).Order(StringComparer.Ordinal), Is.EqualTo(expected));
    }

    [Test]
    public async Task AFormGrantedOnItsOwn_BringsTheFoldersAboveIt_IntoTheFolderList()
    {
        var hr = (await FolderService.ListAsync()).Single(folder => folder.Name == "HR");
        var old = (await Forms.GetByAliasAsync("old"))!;
        await Access.ChangeGrantAsync(new AccessGrantChange(_personas.GroupKeys["noAccess"], PermissionTarget.Form, old.Key, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, Editor));
        var noAccess = _personas["noAccess"];

        var folders = await FolderService.ListAsync(await Access.GetFolderVisibilityAsync(noAccess));
        var forms = await Forms.ListAsync(new FormFilter { AllFolders = true, Access = await Access.GetFormVisibilityAsync(noAccess) }, PageRequest.First(50));

        Assert.Multiple(() =>
        {
            Assert.That(folders.Select(folder => folder.Name).Order(StringComparer.Ordinal), Is.EqualTo(new[] { "Archive", "HR" }), "the way to Old, in a restricted folder");
            Assert.That(forms.Items.Select(form => form.Name), Is.EqualTo(new[] { "Old" }), "but not Payroll beside it");
            Assert.That(hr.IsRestricted, Is.True);
        });
    }

    [Test]
    public async Task TheFilter_CombinesWithTheOtherConditionsOfAList()
    {
        var events = (await FolderService.ListAsync()).Single(folder => folder.Name == "Events");
        var viewer = await Access.GetFormVisibilityAsync(_personas["viewer"]);
        var hr = await Access.GetFormVisibilityAsync(_personas["hr"]);

        Assert.Multiple(async () =>
        {
            Assert.That((await Forms.ListAsync(new FormFilter { Trashed = true, AllFolders = true, Access = viewer }, PageRequest.First(50))).Items.Select(form => form.Name), Is.EqualTo(new[] { "Gone" }), "the trash");
            Assert.That((await Forms.ListAsync(new FormFilter { Trashed = true, AllFolders = true, Access = hr }, PageRequest.First(50))).Items, Is.Empty);
            Assert.That((await Forms.ListAsync(new FormFilter { AllFolders = true, Search = "spring", Access = viewer }, PageRequest.First(50))).Items.Select(form => form.Name), Is.EqualTo(new[] { "Spring" }), "search");
            Assert.That((await Forms.ListAsync(new FormFilter { AllFolders = true, Search = "payroll", Access = viewer }, PageRequest.First(50))).Items, Is.Empty);
            Assert.That((await Forms.ListAsync(new FormFilter { FolderId = await IdOf(events.Key), Access = viewer }, PageRequest.First(50))).Items.Select(form => form.Name), Is.EqualTo(new[] { "Autumn", "Spring" }), "one folder");
            Assert.That((await Forms.ListAsync(new FormFilter { FolderId = null, Access = viewer }, PageRequest.First(50))).Items.Select(form => form.Name), Is.EqualTo(new[] { "Welcome" }), "the root, without Board");
            Assert.That((await Forms.ListAsync(new FormFilter { AllFolders = true, Access = FormVisibility.Nothing }, PageRequest.First(50))).Total, Is.Zero);
            Assert.That((await Forms.ListAsync(new FormFilter { AllFolders = true }, PageRequest.First(50))).Total, Is.EqualTo(6), "no filter: Jiron Form Builder's own work");
        });
    }

    [Test]
    public void EveryPersona_IsWhoTheEvaluationThinks()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_personas["admin"].IsAdministrator, Is.True);
            Assert.That(_personas["admin"].HasSection, Is.True, "the migration gives the administrators the Forms section");
            Assert.That(AccessPersonas.Roles.Where(role => role != "admin").Select(role => _personas[role].IsAdministrator), Is.All.False);
            Assert.That(AccessPersonas.Roles.Where(role => role != "outsider").Select(role => _personas[role].HasSection), Is.All.True);
            Assert.That(_personas["outsider"].HasSection, Is.False);
            Assert.That(_personas["viewer"].GroupKeys, Is.EqualTo(new[] { _personas.GroupKeys["viewer"] }));
        });
    }

    private async Task<int> IdOf(Guid folderKey) => (await Folders.GetAsync(folderKey))!.Id;
}
