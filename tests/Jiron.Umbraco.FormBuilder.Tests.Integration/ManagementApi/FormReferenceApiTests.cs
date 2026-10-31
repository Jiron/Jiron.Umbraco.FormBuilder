using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// "Used on" (ROADMAP 6.7c, ADR-0017, ADR-0131) on a real site: a page that picks a form is listed by
/// <c>GET form/{key}/referenced-by</c> in the shape of Umbraco's document reference, a draft's pick counts, and the page
/// goes once the pick is removed and published; a form picked in a block is listed; a page saved with a form deleted
/// meanwhile writes no reference; a page outside the user's content start nodes is counted but not named; and the
/// rebuild finds every page that picks a form, the recycle bin's included, and drops the rows of deleted content.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormReferenceApiTests : ManagementApiTestBase
{
    private const string PageTypeAlias = "jfbUsedOnPage";

    private IContentService Contents => GetRequiredService<IContentService>();

    [Test]
    public async Task APageThatPicksTheForm_IsListedAsAReference_AndStopsBeingListedAfterThePickIsRemoved()
    {
        var form = await FormAsync("Contact");
        var type = await PageTypeAsync();
        var page = Page(type, "Contact page", form: form, publish: true);
        var draft = Page(type, "Draft page", form: form, publish: false);

        var listed = await ReferencesAsync("admin", form);
        page.SetValue("form", null);
        Contents.Save(page);
        var stillLive = await ReferencesAsync("admin", form);
        Contents.Publish(page, ["*"]);
        var draftOnly = await ReferencesAsync("admin", form);
        draft.SetValue("form", null);
        Contents.Save(draft);
        var none = await ReferencesAsync("admin", form);

        Assert.Multiple(() =>
        {
            Assert.That(listed.Status, Is.EqualTo(HttpStatusCode.OK), listed.Text);
            Assert.That(Names(listed), Is.EqualTo(new[] { "Contact page", "Draft page" }), "by name, a draft's pick too");
            Assert.That((Total(listed), NotShown(listed)), Is.EqualTo((2, 0)));
            var item = listed.Body!["items"]![0]!;
            Assert.That(item["$type"]!.GetValue<string>(), Is.EqualTo("DocumentReferenceResponseModel"), "Umbraco's references box maps by it");
            Assert.That((item["id"]!.GetValue<string>(), item["published"]!.GetValue<bool>()), Is.EqualTo((page.Key.ToString(), true)));
            Assert.That(
                (item["documentType"]!["id"]!.GetValue<string>(), item["documentType"]!["alias"]!.GetValue<string>(), item["documentType"]!["name"]!.GetValue<string>(), item["documentType"]!["icon"]!.GetValue<string>()),
                Is.EqualTo((type.Key.ToString(), PageTypeAlias, "Used-on page", "icon-document")));
            Assert.That(item["variants"]!.AsArray().Select(variant => (variant!["name"]!.GetValue<string>(), variant["state"]!.GetValue<string>())), Is.EqualTo(new[] { ("Contact page", "Published") }));
            Assert.That(listed.Body["items"]![1]!["published"]!.GetValue<bool>(), Is.False, "the draft");
            Assert.That(Names(stillLive), Is.EqualTo(new[] { "Contact page", "Draft page" }), "the published page still shows the form");
            Assert.That(Names(draftOnly), Is.EqualTo(new[] { "Draft page" }), "the page goes once the pick is removed and published");
            Assert.That((Total(none), Names(none)), Is.EqualTo((0, Array.Empty<string>())));
        });
    }

    [Test]
    public async Task APagePickingAFormThatTheLibraryNoLongerHolds_WritesNoReferenceToIt()
    {
        var kept = await FormAsync("Kept");
        var gone = await FormAsync("Gone");
        var type = await PageTypeAsync();
        var page = Page(type, "Both", form: gone, publish: true, blockForm: kept);
        var before = Rows(page.Key);
        await TrashFormAsync(gone);
        var forms = GetRequiredService<IFormBuilderFormService>();
        Assert.That(await forms.DeleteAsync(gone, (await forms.GetAsync(gone))!.ConcurrencyVersion, null), Is.Not.Null);
        var afterDelete = Rows(page.Key);
        page.Name = "Both again";
        Contents.Save(page);
        var afterSave = Rows(page.Key);
        var never = Page(type, "Never", form: Guid.NewGuid(), publish: false);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(2), "a row per form");
            Assert.That(afterDelete, Is.EqualTo(1), "the delete took the gone form's row");
            Assert.That(afterSave, Is.EqualTo(1), "and the save wrote none back");
            Assert.That(Rows(never.Key), Is.Zero, "a form the library never held");
        });
    }

    [Test]
    public async Task AFormPickedInABlock_IsListed_UnderTheBlockListsProperty()
    {
        var form = await FormAsync("Newsletter");
        var type = await PageTypeAsync();
        var page = Page(type, "Blocks page", form: null, publish: true, blockForm: form);

        var listed = await ReferencesAsync("admin", form);
        var rows = await GetRequiredService<IReferenceRepository>().ContentOfAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That(listed.Status, Is.EqualTo(HttpStatusCode.OK), listed.Text);
            Assert.That(Names(listed), Is.EqualTo(new[] { "Blocks page" }));
            Assert.That(rows, Is.EqualTo(new[] { page.Key }));
            Assert.That(Aliases(page.Key), Is.EqualTo(new[] { "blocks" }), "the content's own property");
        });
    }

    [Test]
    public async Task ACopy_IsListed_APageInTheRecycleBinStaysListed_AndADeletedOneGoes()
    {
        var form = await FormAsync("Feedback");
        var type = await PageTypeAsync();
        var page = Page(type, "Original", form: form, publish: true);
        var copy = Contents.Copy(page, Constants.System.Root, relateToOriginal: false)!;
        var copied = await ReferencesAsync("admin", form);
        Assert.That(Contents.MoveToRecycleBin(page).Success, Is.True);
        var trashed = await ReferencesAsync("admin", form);
        Assert.That(Contents.Delete(page).Success, Is.True);
        var deleted = await ReferencesAsync("admin", form);

        Assert.Multiple(() =>
        {
            Assert.That(copied.Body!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()), Is.EquivalentTo(new[] { page.Key.ToString(), copy.Key.ToString() }), "the copy too");
            Assert.That(Total(trashed), Is.EqualTo(2), "a page in the recycle bin still names the form, and a restore would show it");
            Assert.That(deleted.Body!["items"]!.AsArray().Select(item => item!["id"]!.GetValue<string>()), Is.EqualTo(new[] { copy.Key.ToString() }));
            Assert.That(Rows(page.Key), Is.Zero, "the deleted page's rows gone");
        });
    }

    [Test]
    public async Task APageOutsideTheUsersStartNodes_IsCountedButNotNamed()
    {
        var form = await FormAsync("Careers");
        var type = await PageTypeAsync();
        var section = Page(type, "Section", form: null, publish: true);
        Page(type, "Inside", form: form, publish: true, parentId: section.Id);
        Page(type, "Outside", form: form, publish: true);
        await using (var scope = Services.CreateAsyncScope())
        {
            var people = scope.ServiceProvider.GetRequiredService<TestPeople>();
            var group = await people.EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbSectionEditors", Name = "Section editors", Sections = { "content", Integration.Access.AccessPersonas.Section } });
            group.StartContentId = section.Id;
            Assert.That((await scope.ServiceProvider.GetRequiredService<IUserGroupService>().UpdateAsync(group, Constants.Security.SuperUserKey)).Success, Is.True);
            await GrantAsync(group.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.Design);
        }

        var caller = new ManagementApiCaller(Client, Services, _ => ["jfbSectionEditors"]);
        var restricted = await caller.CallAsync("sectionEditor", HttpMethod.Get, $"form/{form}/referenced-by");
        var everyone = await ReferencesAsync("admin", form);
        var paged = await CallAsync("admin", HttpMethod.Get, $"form/{form}/referenced-by?skip=1&take=1");

        Assert.Multiple(() =>
        {
            Assert.That(restricted.Status, Is.EqualTo(HttpStatusCode.OK), restricted.Text);
            Assert.That(Names(restricted), Is.EqualTo(new[] { "Inside" }));
            Assert.That((Total(restricted), NotShown(restricted)), Is.EqualTo((1, 1)), "the other page counted, not named");
            Assert.That(restricted.Text, Does.Not.Contain("Outside"));
            Assert.That((Names(everyone), NotShown(everyone)), Is.EqualTo((new[] { "Inside", "Outside" }, 0)));
            Assert.That((Names(paged), Total(paged)), Is.EqualTo((new[] { "Outside" }, 2)), "a page of them");
        });
    }

    [Test]
    public async Task TheEndpoint_AnswersAFormTheUserCannotSeeOrThatIsMissing_WithNotFound_AndAnOddPageWithABadRequest()
    {
        var form = await FormAsync("Private");

        var missing = await ReferencesAsync("admin", Guid.NewGuid());
        var unseen = await ReferencesAsync("noAccess", form);
        var odd = await CallAsync("admin", HttpMethod.Get, $"form/{form}/referenced-by?take=0");

        Assert.Multiple(() =>
        {
            Assert.That(missing.Status, Is.EqualTo(HttpStatusCode.NotFound), missing.Text);
            Assert.That(unseen.Status, Is.EqualTo(HttpStatusCode.NotFound), unseen.Text);
            Assert.That(odd.Status, Is.EqualTo(HttpStatusCode.BadRequest), odd.Text);
        });
    }

    [Test]
    public async Task TheRebuild_FindsEveryPageThatPicksAForm_AndDropsTheRowsOfDeletedContent()
    {
        var form = await FormAsync("Everywhere");
        var other = await FormAsync("Elsewhere");
        var type = await PageTypeAsync();
        var published = Page(type, "Published", form: form, publish: true);
        var draft = Page(type, "Draft", form: form, publish: false);
        var blocks = Page(type, "Blocks", form: null, publish: true, blockForm: other);
        var trashed = Page(type, "Trashed", form: other, publish: true);
        Assert.That(Contents.MoveToRecycleBin(trashed).Success, Is.True);
        Page(type, "Nothing", form: null, publish: true);
        var references = GetRequiredService<IReferenceRepository>();
        var orphan = Guid.NewGuid();
        await references.ReplaceContentAsync(orphan, [new ContentFormReference(form, "form", string.Empty)]);
        Write($"DELETE FROM {{0}} WHERE {{1}} <> @0", orphan);
        var emptied = await references.ContentOfAsync(form);

        var rebuild = GetRequiredService<FormReferenceRebuild>();
        await rebuild.RequestAsync();
        var firstPart = await rebuild.ContinueAsync(DateTimeOffset.MinValue);
        var done = await rebuild.ContinueAsync(DateTimeOffset.MaxValue);

        Assert.Multiple(async () =>
        {
            Assert.That(emptied, Is.EqualTo(new[] { orphan }), "only a deleted page's row left before the rebuild");
            Assert.That(firstPart, Is.False, "a part past its deadline stops after a batch");
            Assert.That(done, Is.True);
            Assert.That(await references.ContentOfAsync(form), Is.EquivalentTo(new[] { published.Key, draft.Key }), "the deleted page's row gone");
            Assert.That(await references.ContentOfAsync(other), Is.EquivalentTo(new[] { blocks.Key, trashed.Key }), "in a block and in the recycle bin");
            Assert.That(await rebuild.IsDueAsync(), Is.False, "the request done");
            Assert.That(await rebuild.ContinueAsync(DateTimeOffset.MaxValue), Is.True, "nothing left to do");
        });
    }

    [Test]
    public async Task TheMigrations_AskForARebuild_AndTheJobDoesIt()
    {
        var form = await FormAsync("Installed");
        var type = await PageTypeAsync();
        var page = Page(type, "Before", form: form, publish: true);
        Write($"DELETE FROM {{0}} WHERE {{1}} = @0", page.Key);
        var requested = await GetRequiredService<IKeyValueRepository>().GetAsync(Core.Persistence.Migrations.RequestReferenceRebuildV1.RebuildKey);
        var job = GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().OfType<FormReferenceRebuildJob>().Single();

        await job.ExecuteAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(requested, Is.Not.Null, "a fresh install asks for one");
            Assert.That((job.Name, job.Period), Is.EqualTo((FormReferenceRebuildJob.JobName, TimeSpan.FromMinutes(1))));
            Assert.That(await GetRequiredService<IReferenceRepository>().ContentOfAsync(form), Is.EqualTo(new[] { page.Key }));
            Assert.That(await GetRequiredService<FormReferenceRebuild>().IsDueAsync(), Is.False);
        });
    }

    // The element type and the picker's data type from setup, a block list that allows the element type, and a page
    // type with a picker property "form" and a block list property "blocks".
    private async Task<IContentType> PageTypeAsync()
    {
        var blocks = await GetRequiredService<FormBlockTypes>().CreateAsync(Constants.Security.SuperUserKey);
        var dataTypes = GetRequiredService<IDataTypeService>();
        var editors = GetRequiredService<PropertyEditorCollection>();
        var blockList = new DataType(editors[Constants.PropertyEditors.Aliases.BlockList]!, GetRequiredService<IConfigurationEditorJsonSerializer>())
        {
            Name = "Used-on blocks",
            DatabaseType = ValueStorageType.Ntext,
            ConfigurationData = new Dictionary<string, object> { ["blocks"] = new[] { new Dictionary<string, object> { ["contentElementTypeKey"] = blocks.ElementType.Key } } },
        };
        var createdList = await dataTypes.CreateAsync(blockList, Constants.Security.SuperUserKey);
        Assert.That(createdList.Success, Is.True, createdList.Status.ToString());
        var shortStrings = GetRequiredService<IShortStringHelper>();
        var type = new ContentType(shortStrings, -1) { Alias = PageTypeAlias, Name = "Used-on page", Icon = "icon-document", AllowedAsRoot = true };
        type.AddPropertyType(new PropertyType(shortStrings, (await dataTypes.GetAsync(FormBlockTypes.DataTypeKey))!, "form") { Name = "Form" });
        type.AddPropertyType(new PropertyType(shortStrings, createdList.Result!, "blocks") { Name = "Blocks" });
        var types = GetRequiredService<IContentTypeService>();
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        type.AllowedContentTypes = [new ContentTypeSort(type.Key, 0, type.Alias)];
        Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        return type;
    }

    // A page that picks a form in its picker property, in a block of its block list, both or neither.
    private IContent Page(IContentType type, string name, Guid? form, bool publish, Guid? blockForm = null, int parentId = Constants.System.Root)
    {
        var page = Contents.Create(name, parentId, type.Alias);
        if (form is { } picked)
        {
            page.SetValue("form", picked.ToString("D"));
        }

        if (blockForm is { } inBlock)
        {
            var element = GetRequiredService<IContentTypeService>().Get(FormBlockTypes.ElementTypeAlias)!;
            var block = Guid.NewGuid();
            page.SetValue("blocks", new JsonObject
            {
                ["layout"] = new JsonObject { [Constants.PropertyEditors.Aliases.BlockList] = new JsonArray(new JsonObject { ["contentKey"] = block.ToString("D") }) },
                ["contentData"] = new JsonArray(new JsonObject
                {
                    ["contentTypeKey"] = element.Key.ToString("D"),
                    ["key"] = block.ToString("D"),
                    ["values"] = new JsonArray(new JsonObject { ["alias"] = FormBlockTypes.PropertyAlias, ["value"] = inBlock.ToString("D") }),
                }),
                ["settingsData"] = new JsonArray(),
                ["expose"] = new JsonArray(new JsonObject { ["contentKey"] = block.ToString("D"), ["culture"] = null, ["segment"] = null }),
            }.ToJsonString());
        }

        Assert.That(Contents.Save(page).Success, Is.True);
        if (publish)
        {
            Assert.That(Contents.Publish(page, ["*"]).Success, Is.True);
        }

        return page;
    }

    private Task<ApiAnswer> ReferencesAsync(string role, Guid form) => CallAsync(role, HttpMethod.Get, $"form/{form}/referenced-by");

    private static string[] Names(ApiAnswer answer) => [.. answer.Body?["items"]?.AsArray().Select(item => item!["name"]!.GetValue<string>()) ?? []];

    private static long Total(ApiAnswer answer) => answer.Body!["total"]!.GetValue<long>();

    private static int NotShown(ApiAnswer answer) => answer.Body!["notShown"]!.GetValue<int>();

    // How many rows the index holds for a content item.
    private int Rows(Guid contentKey) => Query(db => db.ExecuteScalar<int>(Sql($"SELECT COUNT(*) FROM {{0}} WHERE {{1}} = @0", db), contentKey));

    // The property aliases of a content item's rows.
    private string[] Aliases(Guid contentKey) => [.. Query(db => db.Fetch<string>(Sql($"SELECT {db.SqlContext.SqlSyntax.GetQuotedColumnName("propertyAlias")} FROM {{0}} WHERE {{1}} = @0", db), contentKey))];

    private void Write(string sql, Guid contentKey)
    {
        using var scope = GetRequiredService<IScopeProvider>().CreateScope();
        scope.Database.Execute(Sql(sql, scope.Database), contentKey);
        scope.Complete();
    }

    private T Query<T>(Func<IUmbracoDatabase, T> read)
    {
        using var scope = GetRequiredService<IScopeProvider>().CreateScope(autoComplete: true);
        return read(scope.Database);
    }

    // The statement with {0} the reference table and {1} its content key column, quoted for the database.
    private static string Sql(string sql, IUmbracoDatabase db) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, db.SqlContext.SqlSyntax.GetQuotedTableName(FormBuilderTables.Reference), db.SqlContext.SqlSyntax.GetQuotedColumnName("contentKey"));
}
