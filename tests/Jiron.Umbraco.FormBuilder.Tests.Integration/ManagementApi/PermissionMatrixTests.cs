using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The permission matrix (ADR-0013, ADR-0050): it finds every endpoint of the management API through the API explorer,
/// checks its declaration against <see cref="Declared"/>, written out by hand from ADR-0050's table, and calls each as
/// every persona that must be refused: without the Forms section, seeing the folder but lacking the permission, and
/// holding nothing on the folder or form. A new endpoint is called by itself, and fails until the table names it. Two
/// reviewed exceptions: the form picker's endpoints, authorised by content access (ADR-0013, ADR-0133), each carrying that
/// declaration with its own access tests (<c>PickerApiTests</c>); and the download of a background export's file, which a
/// plain link calls with a ticket instead of a token (ADR-0210), so it is anonymous, declares its ticket, and finds
/// nothing for anyone without one; its ticket's own tests are <c>EntryExportApiTests</c>.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PermissionMatrixTests : ManagementApiTestBase
{
    /// <summary>What each endpoint needs: its permissions (none: any user of the section) and where, with the key's name.</summary>
    private static readonly Dictionary<string, (FormBuilderPermissions Needs, FormBuilderAccessResourceKind On, string? Key)> Declared = new()
    {
        ["GET me"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET setup/checklist"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET setup/health"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET entry/recent"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET insights/glance"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["PUT insights/tables"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET tree/root"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET tree/children"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Folder, "parentKey"),
        ["GET folder/{key}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Folder, "key"),
        ["POST folder"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "parentKey"),
        ["PUT folder/{key}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["PUT folder/{key}/move"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["POST folder/{key}/trash"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["POST folder/{key}/restore"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["DELETE folder/{key}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "key"),
        ["GET trash"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET trash/holdings"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["DELETE trash"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET library/items"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Folder, "folderKey"),
        ["GET library/trash"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET template"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["POST template/form/{formKey}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "formKey"),
        ["POST template/group"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["PUT template/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["DELETE template/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET form/search"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET form/search/field"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET form/{key}/holdings"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/duplicate"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/copy-actions"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/from-template"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "folderKey"),
        ["POST form/import/check"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "folderKey"),
        ["POST form/import"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "folderKey"),
        ["GET form/{key}/export"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/draft"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Folder, "folderKey"),
        ["PUT form/{key}/draft"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/publish"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/unpublish"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/move"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/trash"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/restore"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["DELETE form/{key}"] = (FormBuilderPermissions.Design | FormBuilderPermissions.DeleteEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/referenced-by"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/files/{fileKey}"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/files/archive"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entries/export"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/insights"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/insights/answers"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/insights/answers/{field}/texts"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries/export/plan"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries/export/file"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries/export/jobs"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entries/export/jobs/{exportKey}"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries/export/jobs/{exportKey}/ticket"] = (FormBuilderPermissions.Export, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/versions"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/versions/{number}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/versions/compare"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/versions/{number}/compare"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/versions/{number}/restore"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/versions/{number}/pin"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/versions/{number}/comment"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET field-type"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET field-type/{alias}/changes"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET action-type"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET action-type/content/document-type/{alias}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET canvas"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET option-source"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET option-list"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET option-list/library"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET option-list/editor"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET option-list/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET option-list/{key}/used-by"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST option-list"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["PUT option-list/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["DELETE option-list/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST option-list/{key}/duplicate"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST option-list/preview"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST option-list/read-file"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET option-list/{key}/fetched"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST option-list/{key}/fetch"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET theme/library"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET theme/editor"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET theme/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET theme/{key}/used-by"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST theme"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["PUT theme/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["DELETE theme/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST theme/{key}/duplicate"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET theme/{key}/export"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST theme/import"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST theme/preview"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET theme/sample"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET email-layout/library"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET email-layout/editor"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET email-layout/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET email-layout/{key}/used-by"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST email-layout"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["PUT email-layout/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["DELETE email-layout/{key}"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST email-layout/{key}/duplicate"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST email-layout/preview"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["POST email-layout/{key}/test"] = (FormBuilderPermissions.ManageLibrary, FormBuilderAccessResourceKind.Global, null),
        ["GET captcha-provider"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET form/{key}/setting-choices"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/options"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/merge-tag-paths"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/check"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/outcome-preview"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/email-preview"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/preview"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/preview/ticket"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/preview/entries/{submissionId}"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/preview/entries/{submissionId}/actions"] = (FormBuilderPermissions.ManageActions, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/action/{actionKey}/test"] = (FormBuilderPermissions.ManageActions, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/action/last-run"] = (FormBuilderPermissions.Design, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entry-state"] = (FormBuilderPermissions.EditEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entry-state/undo"] = (FormBuilderPermissions.EditEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entries/view"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/entries/columns"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries/delete"] = (FormBuilderPermissions.DeleteEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entries/run-actions"] = (FormBuilderPermissions.ManageActions, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT entry/filter-set/{setKey}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["DELETE entry/filter-set/{setKey}"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET entries/view"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["POST entries/inbox"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["POST entries/spam"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET entries/forms"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET entries/unseen"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["POST entries/spam/empty"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET entries/digest"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["PUT entries/digest"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET entries/digest/preview"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["POST entries/digest/test"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["GET form/{key}/entry/{entryKey}/action-run"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entry/{entryKey}/action-run/{runId}/retry"] = (FormBuilderPermissions.ManageActions, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entry/{entryKey}"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entry/{entryKey}/reveal"] = (FormBuilderPermissions.ViewSensitive, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entry/{entryKey}/timeline"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entry/{entryKey}/notes"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entry/{entryKey}/notes"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/entry/{entryKey}/notes/{noteId}"] = (FormBuilderPermissions.ViewEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["PUT form/{key}/entry/{entryKey}/answers"] = (FormBuilderPermissions.EditEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET form/{key}/entry/{entryKey}/forward"] = (FormBuilderPermissions.EditEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["POST form/{key}/entry/{entryKey}/forward"] = (FormBuilderPermissions.EditEntries, FormBuilderAccessResourceKind.Form, "key"),
        ["GET entry/{entryKey}/form"] = (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null),
        ["POST setup/form-block"] = (FormBuilderPermissions.ManageSettings, FormBuilderAccessResourceKind.Global, null),
        ["POST email/test"] = (FormBuilderPermissions.ManageSettings, FormBuilderAccessResourceKind.Global, null),
        ["GET settings/{section}"] = (FormBuilderPermissions.ManageSettings, FormBuilderAccessResourceKind.Global, null),
        ["PUT settings/{section}"] = (FormBuilderPermissions.ManageSettings, FormBuilderAccessResourceKind.Global, null),
        ["GET access"] = (FormBuilderPermissions.ManagePermissions, FormBuilderAccessResourceKind.Global, null),
        ["PUT access"] = (FormBuilderPermissions.ManagePermissions, FormBuilderAccessResourceKind.Global, null),
        ["GET access/folder/{key}"] = (FormBuilderPermissions.ManagePermissions, FormBuilderAccessResourceKind.Global, null),
        ["PUT access/folder/{key}"] = (FormBuilderPermissions.ManagePermissions, FormBuilderAccessResourceKind.Global, null),
        ["GET access/form/{key}"] = (FormBuilderPermissions.ManagePermissions, FormBuilderAccessResourceKind.Global, null),
        ["PUT access/form/{key}"] = (FormBuilderPermissions.ManagePermissions, FormBuilderAccessResourceKind.Global, null),
    };

    /// <summary>
    /// The reviewed exception list: the endpoints authorised by content access instead of Jiron Form Builder's permissions.
    /// Only the form picker's may be on it (6.7e).
    /// </summary>
    private static readonly string[] Exceptions = ["GET picker/children", "GET picker/search", "GET picker/forms", "GET picker/folders", "GET picker/themes"];

    /// <summary>The reviewed endpoints a ticket authorises instead of a signed-in user: only the download of an export's file (11.4).</summary>
    private static readonly string[] Tickets = ["GET form/{key}/entries/export/jobs/{exportKey}/file"];

    // The personas who may stand for "sees it but lacks the permission", in the order the test tries them.
    private static readonly string[] Seers = ["viewer", "designer", "entryHandler", "privacyOfficer", "noAccess"];

    private Guid _events;
    private Guid _hr;
    private Guid _spring;
    private Guid _payroll;

    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        var folders = GetRequiredService<IFormBuilderFolderService>();
        var access = GetRequiredService<IFormBuilderAccessService>();
        _events = (await folders.CreateAsync(new NewFolderRequest("Events"))).Key;
        _hr = (await folders.CreateAsync(new NewFolderRequest("HR"))).Key;
        await access.RestrictFolderAsync(_hr, true, null);
        await access.ChangeGrantAsync(new AccessGrantChange(People.GroupKeys["hr"], PermissionTarget.Folder, _hr, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.EditEntries, null));
        var forms = GetRequiredService<IFormBuilderFormService>();
        _spring = (await forms.CreateAsync(new NewFormRequest("Spring", FolderKey: _events))).Key;
        _payroll = (await forms.CreateAsync(new NewFormRequest("Payroll", FolderKey: _hr))).Key;
        // The document type a content action's table reads.
        var matrix = new global::Umbraco.Cms.Core.Models.ContentType(GetRequiredService<IShortStringHelper>(), Constants.System.Root) { Alias = ManagementApiEndpoint.DocumentTypeAlias, Name = "Matrix" };
        await GetRequiredService<IContentTypeService>().CreateAsync(matrix, Constants.Security.SuperUserKey);
    }

    /// <summary>What the table's declarations cannot say: lowering a field's sensitivity also takes <c>viewSensitive</c> (ADR-0150).</summary>
    [Test]
    public async Task PublishingOrRestoringALowerSensitivity_NeedsViewSensitive()
    {
        var versions = GetRequiredService<IFormBuilderFormVersionService>();
        var fieldKey = Guid.NewGuid().ToString();
        async Task<ApiAnswer> SaveAsync(bool sensitive)
        {
            var draft = await CallAsync("designer", HttpMethod.Get, $"form/{_spring}/draft");
            var definition = draft.Body!["definition"]!.DeepClone().AsObject();
            definition["pages"]![0]!["groups"]![0]!["fields"] = new JsonArray(new JsonObject
            {
                ["key"] = fieldKey,
                ["type"] = "text",
                ["alias"] = "email",
                ["label"] = new JsonObject { [string.Empty] = "Email" },
                ["sensitive"] = sensitive,
            });
            return await CallAsync("designer", HttpMethod.Put, $"form/{_spring}/draft", new { concurrencyVersion = draft.Body["form"]!["concurrencyVersion"]!.GetValue<int>(), definition });
        }

        async Task<int> ConcurrencyAsync() => (await GetRequiredService<IFormBuilderFormService>().GetAsync(_spring))!.ConcurrencyVersion;
        async Task<ApiAnswer> PublishAsync(string role) => await CallAsync(role, HttpMethod.Post, $"form/{_spring}/publish", new { concurrencyVersion = await ConcurrencyAsync() });

        await SaveAsync(false);
        var plain = await PublishAsync("designer");
        var plainVersion = (await versions.ListVersionsAsync(_spring, includeDrafts: true, PageRequest.First(1)))!.Items[0].Version.Number;
        await SaveAsync(true);
        var raised = await PublishAsync("designer");
        var savedLower = await SaveAsync(false);
        var lowered = await PublishAsync("designer");
        var stale = await CallAsync("designer", HttpMethod.Post, $"form/{_spring}/publish", new { concurrencyVersion = await ConcurrencyAsync() - 1 });
        var byAdmin = await PublishAsync("admin");
        await SaveAsync(true);
        await PublishAsync("designer");
        var restored = await CallAsync("designer", HttpMethod.Post, $"form/{_spring}/versions/{plainVersion}/restore", new { concurrencyVersion = await ConcurrencyAsync() });
        var restoredByAdmin = await CallAsync("admin", HttpMethod.Post, $"form/{_spring}/versions/{plainVersion}/restore", new { concurrencyVersion = await ConcurrencyAsync() });

        Assert.Multiple(() =>
        {
            Assert.That(plain.Status, Is.EqualTo(HttpStatusCode.OK), plain.ToString());
            Assert.That(raised.Status, Is.EqualTo(HttpStatusCode.OK), raised.ToString());
            Assert.That(savedLower.Status, Is.EqualTo(HttpStatusCode.OK), "a draft may hold it: designing is not reading");
            Assert.That((lowered.Outcome, lowered.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "viewSensitive")), lowered.ToString());
            Assert.That(stale.Outcome, Is.EqualTo("409 concurrency_conflict"), "a stale publish names no permission");
            Assert.That(byAdmin.Status, Is.EqualTo(HttpStatusCode.OK), byAdmin.ToString());
            Assert.That((restored.Outcome, restored.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "viewSensitive")), restored.ToString());
            Assert.That(restoredByAdmin.Status, Is.EqualTo(HttpStatusCode.OK), restoredByAdmin.ToString());
        });
    }

    [Test]
    public void EveryEndpoint_DeclaresItsAccess_AsTheTableSays()
    {
        var endpoints = Endpoints();

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Is.Not.Empty, "the API explorer lists the management API");
            Assert.That(endpoints.Select(endpoint => endpoint.Name), Is.EquivalentTo(Declared.Keys.Concat(Exceptions).Concat(Tickets)), "every endpoint is in the table, and the table names no endpoint that is gone");
            Assert.That(Exceptions, Has.All.StartsWith("GET picker/"), "the reviewed exception list holds the form picker's endpoints only");
            foreach (var endpoint in endpoints.Where(endpoint => Exceptions.Contains(endpoint.Name)))
            {
                Assert.That(endpoint.Declarations.Select(declaration => declaration.GetType()), Is.EqualTo(new[] { typeof(FormBuilderContentAccessAttribute) }), $"{endpoint.Name} declares content access");
            }

            foreach (var endpoint in endpoints)
            {
                var ticket = endpoint.Declarations.Any(declaration => declaration is FormBuilderTicketAccessAttribute);
                Assert.That((ticket, endpoint.AllowsAnonymous), Is.EqualTo((Tickets.Contains(endpoint.Name), Tickets.Contains(endpoint.Name))), $"{endpoint.Name}: anonymous exactly when it declares a ticket, and only a reviewed one does");
            }

            foreach (var endpoint in endpoints.Where(endpoint => !Exceptions.Contains(endpoint.Name) && !Tickets.Contains(endpoint.Name)))
            {
                Assert.That(endpoint.Declarations, Has.Count.EqualTo(1), $"{endpoint.Name} carries exactly one declaration");
                if (endpoint.Declarations.Count == 1 && Declared.TryGetValue(endpoint.Name, out var expected))
                {
                    var declaration = endpoint.Declarations[0];
                    Assert.That((declaration.Permissions, declaration.On, declaration.Key), Is.EqualTo(expected), endpoint.Name);
                    Assert.That(declaration is FormBuilderSectionAttribute, Is.EqualTo(expected.Needs == FormBuilderPermissions.None), $"{endpoint.Name}: a section declaration exactly when it needs no permission");
                    if (declaration.Key is { } key)
                    {
                        Assert.That(endpoint.KeySource(key), Is.Not.Null, $"{endpoint.Name}: the key {key} names a parameter or a property of the body");
                    }
                }
            }
        });
    }

    [Test]
    public async Task EachEndpoint_RefusesAUserWithoutTheFormsSection()
    {
        var answers = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => !Exceptions.Contains(endpoint.Name) && !Tickets.Contains(endpoint.Name)))
        {
            answers.Add($"{endpoint.Name}: {(await CallAsync("outsider", endpoint, Visible(endpoint))).Outcome}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.All.EndsWith(": 403 no_section"), string.Join("\n", answers));
    }

    [Test]
    public async Task EachTicketsEndpoint_FindsNothing_ForAnyoneWithoutATicket()
    {
        var answers = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Tickets.Contains(endpoint.Name)))
        {
            foreach (var role in new[] { "admin", "outsider", "noAccess" })
            {
                answers.Add($"{endpoint.Name} as {role}: {(await CallAsync(role, endpoint, _spring)).Outcome}");
            }

            answers.Add($"{endpoint.Name} without a token: {(await Api.CallAsync(null, endpoint, _spring)).Outcome}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Has.Count.EqualTo(Tickets.Length * 4));
        Assert.That(answers, Is.All.EndsWith(": 404 export_not_found"), string.Join("\n", answers));
    }

    [Test]
    public async Task EachEndpointThatNeedsAPermission_RefusesAUserWhoSeesTheItemButLacksIt_NamingThePermission()
    {
        var answers = new List<string>();
        var expected = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Declared.TryGetValue(endpoint.Name, out var declared) && declared.Needs != FormBuilderPermissions.None))
        {
            var needs = Declared[endpoint.Name].Needs;
            var on = Declared[endpoint.Name].On;
            var role = Seers.First(seer => Sees(seer, on) && (Held(seer, on) & needs) != needs);
            var missing = PermissionRules.Each.First(permission => (needs & permission) != FormBuilderPermissions.None && (Held(role, on) & permission) == FormBuilderPermissions.None);
            var answer = await CallAsync(role, endpoint, Visible(endpoint));
            answers.Add($"{endpoint.Name} as {role}: {answer.Outcome} {answer.Body?["permission"]?.GetValue<string>()}");
            expected.Add($"{endpoint.Name} as {role}: 403 forbidden {PermissionRules.NameOf(missing)}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.Not.Empty);
        Assert.That(answers, Is.EqualTo(expected));
    }

    [Test]
    public async Task EachEndpointOnAFolderOrForm_AnswersNotFound_ToAUserWhoHoldsNothingOnIt_AndForAKeyThatIsNone()
    {
        var answers = new List<string>();
        var expected = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Declared.TryGetValue(endpoint.Name, out var declared) && declared.On != FormBuilderAccessResourceKind.Global))
        {
            var code = Declared[endpoint.Name].On == FormBuilderAccessResourceKind.Form ? "form_not_found" : "folder_not_found";
            answers.Add($"{endpoint.Name} as designer on HR: {(await CallAsync("designer", endpoint, Declared[endpoint.Name].On == FormBuilderAccessResourceKind.Form ? _payroll : _hr)).Outcome}");
            expected.Add($"{endpoint.Name} as designer on HR: 404 {code}");
            answers.Add($"{endpoint.Name} as admin on none: {(await CallAsync("admin", endpoint, Guid.NewGuid())).Outcome}");
            expected.Add($"{endpoint.Name} as admin on none: 404 {code}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.Not.Empty);
        Assert.That(answers, Is.EqualTo(expected));
    }

    [Test]
    public async Task EachEndpointForTheWholeSection_AnswersAUserOfTheSectionWithoutGrants()
    {
        var answers = new List<string>();
        foreach (var endpoint in Endpoints().Where(endpoint => Declared.TryGetValue(endpoint.Name, out var declared) && declared == (FormBuilderPermissions.None, FormBuilderAccessResourceKind.Global, null)))
        {
            answers.Add($"{endpoint.Name}: {(await CallAsync("noAccess", endpoint, _events)).Outcome}");
        }

        await WriteAsync(answers);
        Assert.That(answers, Is.Not.Empty);
        Assert.That(answers, Is.All.EndsWith(": 200"), string.Join("\n", answers));
    }

    // What a persona holds globally (its own group's grants, ADR-0013) or on Events, an unrestricted folder at the root,
    // which holds the global grants that work in folders.
    private static FormBuilderPermissions Held(string role, FormBuilderAccessResourceKind on)
    {
        var grants = AccessPersonas.Groups.FirstOrDefault(group => group.Role == role).Grants;
        return on == FormBuilderAccessResourceKind.Global ? grants : grants & PermissionRules.FolderLevel;
    }

    private static bool Sees(string role, FormBuilderAccessResourceKind on) => on == FormBuilderAccessResourceKind.Global || Held(role, on) != FormBuilderPermissions.None;

    // Names each endpoint the test called, with its answer, in the test's output.
    private static async Task WriteAsync(List<string> answers)
    {
        foreach (var answer in answers)
        {
            await TestContext.Out.WriteLineAsync(answer);
        }
    }

    private Task<ApiAnswer> CallAsync(string role, ManagementApiEndpoint endpoint, Guid key) => Api.CallAsync(role, endpoint, key);

    // The key that Events stands for: Events itself, or its form Spring for an endpoint on a form.
    private Guid Visible(ManagementApiEndpoint endpoint) =>
        Declared.TryGetValue(endpoint.Name, out var declared) && declared.On == FormBuilderAccessResourceKind.Form ? _spring : _events;

    private List<ManagementApiEndpoint> Endpoints() => ManagementApiEndpoints.All(Services);
}
