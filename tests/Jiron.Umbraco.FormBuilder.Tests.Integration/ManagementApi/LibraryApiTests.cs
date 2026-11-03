using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The form library's endpoints (ROADMAP 9.2, ADR-0166): duplicate, copy actions, the template gallery and a form from a
/// template, the import's check and the import, export, the field search, a folder's list and the trash's, what a delete
/// takes, whether a draft's form has entries (ROADMAP 9.4a), the tree's mark for unseen entries, and deleting an empty
/// folder at once.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class LibraryApiTests : ManagementApiTestBase
{
    private const string Designer = "designer";

    private Guid _events;
    private Guid _hr;

    private IFormBuilderFormService Forms => GetRequiredService<IFormBuilderFormService>();

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    // Events is an ordinary folder; HR is restricted to the HR team, who may design there but not manage actions.
    [SetUp]
    public async Task CreateTheLibraryAsync()
    {
        _events = await FolderAsync("Events");
        _hr = await FolderAsync("HR");
        await RestrictAsync(_hr, "hr", FormBuilderPermissions.Design);
    }

    [Test]
    public async Task ADuplicate_LandsInTheFormsFolder_WithOrWithoutActions_AndTakesTheFoldersAccess()
    {
        var secret = await FormAsync("Secret", _events);
        await DesignAsync(secret, [Text("name", "Name")], [Mail()]);
        await GetRequiredService<IFormBuilderAccessService>().RestrictFormAsync(secret, await VersionAsync(secret), true, null);
        await GrantAsync(People.GroupKeys[Designer], PermissionTarget.Form, secret, FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions);
        var payroll = await FormAsync("Payroll", _hr);
        await DesignAsync(payroll, [Text("name", "Name")], [Mail()]);

        var copy = await CallAsync(Designer, HttpMethod.Post, $"form/{secret}/duplicate", new { name = "Secret (copy)", includeActions = true });
        var bare = await CallAsync(Designer, HttpMethod.Post, $"form/{secret}/duplicate", new { name = "Secret (bare)", includeActions = false });
        var withActions = await CallAsync("hr", HttpMethod.Post, $"form/{payroll}/duplicate", new { name = "Payroll (copy)", includeActions = true });
        var withoutActions = await CallAsync("hr", HttpMethod.Post, $"form/{payroll}/duplicate", new { name = "Payroll (copy)", includeActions = false });

        Assert.Multiple(async () =>
        {
            Assert.That(copy.Status, Is.EqualTo(HttpStatusCode.Created), copy.ToString());
            Assert.That(copy.Body!["folderKey"]!.GetValue<Guid>(), Is.EqualTo(_events), "in the form's folder");
            Assert.That(copy.Body["isRestricted"]!.GetValue<bool>(), Is.False, "the copy takes its folder's access");
            Assert.That(copy.Created, Is.EqualTo(copy.Body["key"]!.GetValue<Guid>().ToString()));
            Assert.That(await ActionCountAsync(copy.Body["key"]!.GetValue<Guid>()), Is.EqualTo(1));
            Assert.That(await ActionCountAsync(bare.Body!["key"]!.GetValue<Guid>()), Is.Zero);
            Assert.That((withActions.Outcome, withActions.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")), "actions need manageActions where the copy lands");
            Assert.That(withoutActions.Status, Is.EqualTo(HttpStatusCode.Created), withoutActions.ToString());
        });
    }

    [Test]
    public async Task CopiedActions_MapTheirFieldsByAlias_AndTheAnswerListsWhatTheTargetLacks()
    {
        var spring = await FormAsync("Spring", _events);
        var email = Text("email", "Email");
        var topic = Text("topic", "Topic");
        await DesignAsync(spring, [email, topic], [Mail(topic.Key, email.Key)]);
        var autumn = await FormAsync("Autumn", _events);
        var autumnEmail = Text("email", "Email");
        await DesignAsync(autumn, [autumnEmail]);
        var payroll = await FormAsync("Payroll", _hr);
        var bonus = await FormAsync("Bonus", _hr);
        await DesignAsync(payroll, [Text("name", "Name")], [Mail()]);

        var copied = await CallAsync(Designer, HttpMethod.Post, $"form/{spring}/copy-actions", new { targetKey = autumn, concurrencyVersion = await VersionAsync(autumn) });
        var hidden = await CallAsync(Designer, HttpMethod.Post, $"form/{spring}/copy-actions", new { targetKey = payroll, concurrencyVersion = await VersionAsync(payroll) });
        var notManaged = await CallAsync("hr", HttpMethod.Post, $"form/{payroll}/copy-actions", new { targetKey = bonus, concurrencyVersion = await VersionAsync(bonus) });
        var draft = (await Forms.GetDraftAsync(autumn))!.Definition!;

        Assert.Multiple(() =>
        {
            Assert.That(copied.Status, Is.EqualTo(HttpStatusCode.OK), copied.ToString());
            var action = copied.Body!["actions"]![0]!;
            Assert.That(action["unmapped"]!.AsArray().Select(reference => reference!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "topic" }), "the rule's field the target lacks");
            Assert.That(action["mergeTags"]!.AsArray().Select(tag => tag!["tag"]!.GetValue<string>()), Is.EqualTo(new[] { "field.topic" }), "the subject's tag the target lacks");
            Assert.That(copied.Body["target"]!["version"]!.GetValue<int>(), Is.GreaterThan(1), "a new draft");
            Assert.That(draft.Actions.Select(copy => copy.Key), Is.EqualTo(new[] { action["key"]!.GetValue<Guid>() }));
            Assert.That(draft.Actions[0].Settings!.TryGetProperty("replyToField", out var replyTo) ? replyTo.GetGuid() : Guid.Empty, Is.EqualTo(autumnEmail.Key), "the email field maps by its alias");
            Assert.That(hidden.Outcome, Is.EqualTo("404 form_not_found"), "a target the user does not see does not exist for them");
            Assert.That((notManaged.Outcome, notManaged.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")));
        });
    }

    [Test]
    public async Task TheGallery_ListsContactFirst_AndAFormFromItLandsInTheFolder()
    {
        var gallery = await CallAsync("noAccess", HttpMethod.Get, "template");
        var created = await CallAsync(Designer, HttpMethod.Post, "form/from-template", new { templateKey = BuiltInFormTemplates.ContactKey, folderKey = _events });
        var named = await CallAsync(Designer, HttpMethod.Post, "form/from-template", new { templateKey = BuiltInFormTemplates.ContactKey, name = "Enquiries" });
        var missing = await CallAsync(Designer, HttpMethod.Post, "form/from-template", new { templateKey = Guid.NewGuid() });

        Assert.Multiple(() =>
        {
            Assert.That(gallery.Status, Is.EqualTo(HttpStatusCode.OK), gallery.ToString());
            var contact = gallery.Body!["items"]![0]!;
            Assert.That((contact["key"]!.GetValue<Guid>(), contact["builtIn"]!.GetValue<bool>(), contact["alias"]!.GetValue<string>()), Is.EqualTo((BuiltInFormTemplates.ContactKey, true, "contact")));
            Assert.That((contact["holds"]!["pages"]!.GetValue<int>(), contact["holds"]!["fields"]!.GetValue<int>()), Is.EqualTo((1, 3)));
            Assert.That(contact["definition"]!["pages"]!.AsArray(), Has.Count.EqualTo(1), "the definition, for the preview");
            Assert.That(created.Status, Is.EqualTo(HttpStatusCode.Created), created.ToString());
            Assert.That((created.Body!["name"]!.GetValue<string>(), created.Body["folderKey"]!.GetValue<Guid>()), Is.EqualTo(("Contact", _events)));
            Assert.That(named.Body!["name"]!.GetValue<string>(), Is.EqualTo("Enquiries"));
            Assert.That(missing.Outcome, Is.EqualTo("404 template_not_found"));
        });
    }

    [Test]
    public async Task TheImportCheck_SaysWhatAFileHolds_AndWhatItCannotBring_AsTheImportThenDoes()
    {
        var spring = await FormAsync("Spring", _events);
        await DesignAsync(spring, [Text("name", "Name"), Text("email", "Email")]);
        var file = await ExportAsync(spring);
        var definition = file["definition"]!.AsObject();
        var settings = definition["settings"] as JsonObject ?? (JsonObject)(definition["settings"] = new JsonObject());
        var appearance = settings["appearance"] as JsonObject ?? (JsonObject)(settings["appearance"] = new JsonObject());
        appearance["theme"] = "spring-campaign";
        definition["outcomes"] = new JsonArray(new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["kind"] = "redirect", ["redirect"] = new JsonObject { ["content"] = Guid.NewGuid().ToString() } });
        var json = file.ToJsonString();
        var withActions = (JsonObject)file.DeepClone();
        withActions["definition"]!["actions"] = new JsonArray(new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["type"] = "email", ["name"] = "Notify" });

        var check = await CallAsync(Designer, HttpMethod.Post, "form/import/check", new { json, folderKey = _events });
        var imported = await CallAsync(Designer, HttpMethod.Post, "form/import", new { json, folderKey = _events });
        var unreadable = await CallAsync(Designer, HttpMethod.Post, "form/import/check", new { json = "{ \"form\": " });
        var refusedCheck = await CallAsync("hr", HttpMethod.Post, "form/import/check", new { json = withActions.ToJsonString(), folderKey = _hr });
        var refused = await CallAsync("hr", HttpMethod.Post, "form/import", new { json = withActions.ToJsonString(), folderKey = _hr });

        Assert.Multiple(() =>
        {
            Assert.That(check.Status, Is.EqualTo(HttpStatusCode.OK), check.ToString());
            Assert.That((check.Body!["name"]!.GetValue<string>(), check.Body["alias"]!.GetValue<string>()), Is.EqualTo(("Spring", "spring")));
            Assert.That(check.Body["holds"]!.ToJsonString(), Is.EqualTo("""{"pages":1,"fields":2,"rules":0,"outcomes":1,"actions":0}"""));
            Assert.That(Codes(check), Is.EqualTo(new[] { "theme_missing", "page_missing" }));
            Assert.That(check.Body["notes"]![1]!["path"]!.GetValue<string>(), Is.EqualTo("$.definition.outcomes[0]"), "where it is in the file");
            Assert.That(imported.Status, Is.EqualTo(HttpStatusCode.Created), imported.ToString());
            Assert.That(Codes(imported), Is.EqualTo(Codes(check)), "the import's own notes are those the check gave");
            Assert.That(imported.Body!["form"]!["folderKey"]!.GetValue<Guid>(), Is.EqualTo(_events));
            Assert.That(imported.Body["form"]!["isPublished"]!.GetValue<bool>(), Is.False, "a draft");
            Assert.That(unreadable.Status, Is.EqualTo(HttpStatusCode.BadRequest), unreadable.ToString());
            Assert.That((refusedCheck.Outcome, refused.Outcome), Is.EqualTo(("403 forbidden", "403 forbidden")), "a file with actions needs manageActions where it lands");
        });
    }

    [Test]
    public async Task AnExport_IsTheDraftsFile_NamedByTheAlias()
    {
        var spring = await FormAsync("Spring", _events);
        await DesignAsync(spring, [Text("name", "Name")]);

        using var answer = await Api.SendAsync(Designer, $"form/{spring}/export");
        var file = JsonNode.Parse(await answer.Content.ReadAsStringAsync())!;
        var version = await CallAsync(Designer, HttpMethod.Get, $"form/{spring}/export?version=99");

        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(answer.Content.Headers.ContentDisposition?.FileName?.Trim('"'), Is.EqualTo("spring.json"));
            Assert.That(file["form"]!["alias"]!.GetValue<string>(), Is.EqualTo("spring"));
            Assert.That(file["definition"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!["alias"]!.GetValue<string>(), Is.EqualTo("name"), "the draft");
            Assert.That(version.Outcome, Is.EqualTo("404 version_not_found"));
        });
    }

    [Test]
    public async Task TheFieldSearch_FindsPublishedFieldsOfFormsTheUserSees_ByLabelOrAlias()
    {
        var rating = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "grid",
            Alias = "rating",
            Label = new LocalizedText("Service rating"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "good", Label = new LocalizedText("Good") }] },
            Rows = [new GridRow { Key = Guid.NewGuid(), Label = new LocalizedText("Speed") }, new GridRow { Key = Guid.NewGuid(), Label = new LocalizedText("Price") }],
        };
        var spring = await FormAsync("Spring", _events);
        await DesignAsync(spring, [Text("colour", "Favourite colour"), rating, Text("glove", "Glove size")], publish: true);
        await DesignAsync(spring, [Text("colour", "Favourite colour"), rating, Text("dob", "Date of birth")], publish: true);
        await DesignAsync(spring, [Text("colour", "Favourite colour"), rating, Text("dob", "Date of birth"), Text("shoe", "Shoe size")]);
        // A preview entry gives a field only the draft has a row of its own (ADR-0003); a search does not find it.
        var springRow = (await GetRequiredService<IFormRepository>().GetAsync(spring))!;
        await GetRequiredService<IFormRepository>().AddFieldAsync(springRow.Id, new CatalogueFieldValues(Guid.NewGuid(), "hint", "Hint for the preview", "text", FieldStorageKind.Text, false, 9, null, null, null));
        var payroll = await FormAsync("Payroll", _hr);
        await DesignAsync(payroll, [Text("band", "Salary band")], publish: true);
        var summer = await FormAsync("Summer", _events);
        await DesignAsync(summer, [Text("hat", "Hat size")], publish: true);
        await Forms.UnpublishAsync(summer, await VersionAsync(summer), null);

        var byLabel = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=Colour");
        var byAlias = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=COLOUR");
        var draftOnly = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=shoe");
        var hidden = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=salary");
        var hrSees = await CallAsync("hr", HttpMethod.Get, "form/search/field?text=salary");
        var grid = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=rating");
        var aliasOnly = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=DOB");
        var dropped = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=glove");
        var preview = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=hint");
        var unpublished = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=hat");
        await TrashFormAsync(spring);
        var trashed = await CallAsync(Designer, HttpMethod.Get, "form/search/field?text=colour");

        Assert.Multiple(() =>
        {
            Assert.That(byLabel.Status, Is.EqualTo(HttpStatusCode.OK), byLabel.ToString());
            var match = byLabel.Body!["items"]![0]!;
            Assert.That(
                (match["formKey"]!.GetValue<Guid>(), match["formName"]!.GetValue<string>(), match["folderName"]!.GetValue<string>(), match["label"]!.GetValue<string>(), match["alias"]!.GetValue<string>()),
                Is.EqualTo((spring, "Spring", "Events", "Favourite colour", "colour")));
            Assert.That(byAlias.Body!["total"]!.GetValue<int>(), Is.EqualTo(1), "an alias, in any letter case");
            Assert.That(draftOnly.Body!["total"]!.GetValue<int>(), Is.Zero, "a field only the draft has is found once it is published");
            Assert.That(hidden.Body!["total"]!.GetValue<int>(), Is.Zero, "the designer does not see Payroll");
            Assert.That(hrSees.Body!["total"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(grid.Body!["items"]!.AsArray().Select(item => item!["label"]!.GetValue<string>()), Is.EqualTo(new[] { "Service rating" }), "a grid once, not its rows, which share its alias");
            Assert.That(unpublished.Body!["total"]!.GetValue<int>(), Is.Zero, "visitors get nothing of an unpublished form");
            Assert.That(aliasOnly.Body!["items"]!.AsArray().Select(item => item!["label"]!.GetValue<string>()), Is.EqualTo(new[] { "Date of birth" }), "by its alias alone");
            Assert.That(dropped.Body!["total"]!.GetValue<int>(), Is.Zero, "a field the last publish dropped");
            Assert.That(preview.Body!["total"]!.GetValue<int>(), Is.Zero, "a row a preview entry wrote for the draft");
            Assert.That(trashed.Body!["total"]!.GetValue<int>(), Is.Zero, "a form in the trash is not searched");
        });
    }

    [Test]
    public async Task AFoldersList_CountsTheLastDaysEntries_ThatTheUserMayView_AndSaysWhoChangedEachItem()
    {
        var inner = await FolderAsync("Inner", _events);
        var spring = await FormAsync("Spring", _events);
        var summer = (await CallAsync(Designer, HttpMethod.Post, "form", new { name = "Summer", folderKey = _events })).Body!["key"]!.GetValue<Guid>();
        var autumn = await FormAsync("Autumn", inner);
        await DesignAsync(spring, [Text("name", "Name")], publish: true);
        await DesignAsync(autumn, [Text("name", "Name")], publish: true);
        // The last 30 days count: an entry of 31 days ago no more, one of 29 days ago still. The clock goes back for
        // them only, as the API's sign-ins take their time from it too.
        var clock = GetRequiredService<TestClock>();
        clock.SetNow(DateTimeOffset.UtcNow.AddDays(-31));
        await EntryAsync(spring, EntryState.Submitted);
        clock.SetNow(DateTimeOffset.UtcNow.AddDays(-29));
        await EntryAsync(spring, EntryState.Submitted);
        clock.Reset();
        await EntryAsync(spring, EntryState.Approved);
        await EntryAsync(spring, EntryState.Spam);
        await EntryAsync(spring, EntryState.Submitted, isTest: true);
        await EntryAsync(autumn, EntryState.Submitted);

        var root = await CallAsync("viewer", HttpMethod.Get, "library/items");
        var events = await CallAsync("viewer", HttpMethod.Get, $"library/items?folderKey={_events}");
        var designer = await CallAsync(Designer, HttpMethod.Get, $"library/items?folderKey={_events}");
        var hidden = await CallAsync(Designer, HttpMethod.Get, $"library/items?folderKey={_hr}");

        Assert.Multiple(() =>
        {
            Assert.That(Recent(root, _events), Is.EqualTo(3), "a folder sums the forms below it, its folders' included");
            Assert.That((Recent(events, inner), Recent(events, spring)), Is.EqualTo((1L, 2L)), "no spam, no test");
            Assert.That(Row(events, summer)!["updatedBy"]?.GetValue<string>(), Is.EqualTo(Designer), "who changed it last");
            Assert.That(Row(events, spring)!["updatedBy"], Is.Null, "Jiron Form Builder changed it last");
            Assert.That(Row(events, spring)!["updatedUtc"], Is.Not.Null);
            Assert.That(Recent(events, summer), Is.Zero, "no entries yet");
            Assert.That((Recent(designer, inner), Recent(designer, spring)), Is.EqualTo(((long?)null, (long?)null)), "the designer may view no entries");
            Assert.That(hidden.Outcome, Is.EqualTo("404 folder_not_found"));
        });
    }

    [Test]
    public async Task TheDraft_SaysWhetherTheFormHasEntries_AsYesOrNo_ToADesignerWhoMayNotViewThem()
    {
        var spring = await FormAsync("Spring", _events);
        await DesignAsync(spring, [Text("name", "Name")], publish: true);
        var before = await CallAsync(Designer, HttpMethod.Get, $"form/{spring}/draft");
        await EntryAsync(spring, EntryState.Spam);
        var after = await CallAsync(Designer, HttpMethod.Get, $"form/{spring}/draft");

        Assert.Multiple(() =>
        {
            Assert.That(before.Body!["hasEntries"]!.GetValue<bool>(), Is.False, before.ToString());
            Assert.That(after.Body!["hasEntries"]!.GetValue<bool>(), Is.True, "an entry in any state, spam too, holds the aliases it was sent with");
        });
    }

    [Test]
    public async Task TheTrashsList_SaysWhenAndByWhom_AndWhatADeleteTakes()
    {
        var spring = await FormAsync("Spring", _events);
        await DesignAsync(spring, [Text("name", "Name")], publish: true);
        await EntryAsync(spring, EntryState.Submitted);
        await EntryAsync(spring, EntryState.Spam);
        await FileAsync(spring);
        var payroll = await FormAsync("Payroll", _hr);
        var trashed = await CallAsync(Designer, HttpMethod.Post, $"form/{spring}/trash", new { concurrencyVersion = await VersionAsync(spring) });
        await TrashFormAsync(payroll);

        var bin = await CallAsync(Designer, HttpMethod.Get, "library/trash");
        var holdings = await CallAsync("admin", HttpMethod.Get, $"form/{spring}/holdings");
        var designerHoldings = await CallAsync(Designer, HttpMethod.Get, $"form/{spring}/holdings");
        var adminEmpties = await CallAsync("admin", HttpMethod.Get, "trash/holdings");
        var designerEmpties = await CallAsync(Designer, HttpMethod.Get, "trash/holdings");

        Assert.Multiple(() =>
        {
            Assert.That(trashed.Status, Is.EqualTo(HttpStatusCode.OK), trashed.ToString());
            Assert.That(bin.Body!["items"]!.AsArray().Select(row => row!["item"]!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Spring" }), "what the designer sees");
            Assert.That(Row(bin, spring)!["updatedBy"]!.GetValue<string>(), Is.EqualTo(Designer), "who put it there");
            Assert.That(Row(bin, spring)!["trashedUtc"], Is.Not.Null);
            Assert.That(holdings.Body!.ToJsonString(), Is.EqualTo("""{"folders":0,"forms":1,"entries":2,"files":1,"left":0}"""), "every entry, spam too, and every file");
            Assert.That(designerHoldings.Body!.ToJsonString(), Is.EqualTo("""{"folders":0,"forms":1,"entries":null,"files":null,"left":0}"""), "the designer may neither view nor delete its entries");
            Assert.That(adminEmpties.Body!.ToJsonString(), Is.EqualTo("""{"folders":0,"forms":2,"entries":2,"files":1,"left":0}"""));
            Assert.That(designerEmpties.Body!.ToJsonString(), Is.EqualTo("""{"folders":0,"forms":0,"entries":0,"files":0,"left":2}"""), "the designer may delete no entries");
        });
    }

    [Test]
    public async Task TheTree_MarksAFormWithUnseenEntries_ForWhoMayViewThem()
    {
        var spring = await FormAsync("Spring", _events);
        var autumn = await FormAsync("Autumn", _events);
        await DesignAsync(spring, [Text("name", "Name")], publish: true);
        await DesignAsync(autumn, [Text("name", "Name")], publish: true);
        await EntryAsync(spring, EntryState.Submitted);
        await EntryAsync(autumn, EntryState.Submitted, seen: true);
        await EntryAsync(autumn, EntryState.Spam);

        var viewer = await CallAsync("viewer", HttpMethod.Get, $"tree/children?parentKey={_events}");
        var designer = await CallAsync(Designer, HttpMethod.Get, $"tree/children?parentKey={_events}");

        Assert.Multiple(() =>
        {
            Assert.That(Unseen(viewer), Is.EqualTo(new[] { ("Autumn", false), ("Spring", true) }), "seen, and spam, are no news");
            Assert.That(Unseen(designer), Is.EqualTo(new[] { ("Autumn", false), ("Spring", false) }), "the designer may view no entries");
        });
    }

    [Test]
    public async Task AnEmptyFolder_GoesAtOnce_AndOneThatHoldsAnything_IsRefused()
    {
        var empty = await FolderAsync("Empty");
        var withFolder = await FolderAsync("Outer");
        await FolderAsync("Inner", withFolder);
        var withTrashedForm = await FolderAsync("Old");
        await TrashFormAsync(await FormAsync("Spring", withTrashedForm));

        var viewer = await CallAsync("viewer", HttpMethod.Delete, $"folder/{empty}");
        var deleted = await CallAsync(Designer, HttpMethod.Delete, $"folder/{empty}");
        var outer = await CallAsync(Designer, HttpMethod.Delete, $"folder/{withFolder}");
        var old = await CallAsync(Designer, HttpMethod.Delete, $"folder/{withTrashedForm}");

        Assert.Multiple(async () =>
        {
            Assert.That((viewer.Outcome, viewer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "design")));
            Assert.That(deleted.Body?.ToJsonString(), Is.EqualTo("""{"folders":1,"forms":0,"entries":0,"files":0}"""), deleted.ToString());
            Assert.That(await GetRequiredService<IFormBuilderFolderService>().GetAsync(empty), Is.Null);
            Assert.That(outer.Outcome, Is.EqualTo("409 folder_not_empty"));
            Assert.That(old.Outcome, Is.EqualTo("409 folder_not_empty"), "a form it holds in the trash counts too");
        });
    }

    private static FormField Text(string alias, string label) => new() { Key = Guid.NewGuid(), Type = "text", Alias = alias, Label = new LocalizedText(label) };

    // An email action; with a field, one that runs when that field says "sales", with the field in its subject.
    private static FormAction Mail(Guid? field = null, Guid? replyTo = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = "email",
        Name = "Notify",
        Rules = field is { } key ? [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Run, Conditions = [new RuleCondition { Field = key, Operator = "equals", Value = new ConditionOperand.Text("sales") }] }] : [],
        Settings = SettingsObject.Parse("""{"to":["team@example.com"],"subject":{"":"About {{ field.topic }}"}""" + (replyTo is { } reply ? $$""","replyToField":"{{reply}}"}""" : "}")),
    };

    private static IEnumerable<string> Codes(ApiAnswer answer) => answer.Body!["notes"]!.AsArray().Select(note => note!["code"]!.GetValue<string>());

    private static JsonNode? Row(ApiAnswer answer, Guid key) =>
        answer.Body!["items"]!.AsArray().FirstOrDefault(row => row!["item"]!["key"]!.GetValue<Guid>() == key);

    private static long? Recent(ApiAnswer answer, Guid key) => Row(answer, key)!["recentEntries"]?.GetValue<long>();

    private static IEnumerable<(string, bool)> Unseen(ApiAnswer answer) =>
        answer.Body!["items"]!.AsArray().Select(item => (item!["name"]!.GetValue<string>(), item["hasUnseenEntries"]!.GetValue<bool>()));

    private async Task<int> VersionAsync(Guid form) => (await Forms.GetAsync(form))!.ConcurrencyVersion;

    private async Task<int> ActionCountAsync(Guid form) => (await Forms.GetDraftAsync(form))!.Definition!.Actions.Count;

    // Gives a form's draft these fields in its first group and these actions, and publishes it when asked.
    private async Task DesignAsync(Guid form, IReadOnlyList<FormField> fields, IReadOnlyList<FormAction>? actions = null, bool publish = false)
    {
        var draft = (await Forms.GetDraftAsync(form))!;
        var definition = draft.Definition!;
        var page = definition.Pages[0];
        definition = definition with { Pages = [page with { Groups = [page.Groups[0] with { Fields = fields }] }], Actions = actions ?? [] };
        var saved = (await Forms.SaveDraftAsync(form, draft.Form.ConcurrencyVersion, definition, null, null))!;
        if (publish)
        {
            await Forms.PublishAsync(form, saved.Form.ConcurrencyVersion, null, null);
        }
    }

    private async Task<JsonObject> ExportAsync(Guid form)
    {
        using var answer = await Api.SendAsync(Designer, $"form/{form}/export");
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return JsonNode.Parse(await answer.Content.ReadAsStringAsync())!.AsObject();
    }

    private async Task EntryAsync(Guid form, EntryState state, bool isTest = false, bool seen = false)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        await GetRequiredService<IEntryRepository>().CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            IsTest = isTest,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FirstSeenUtc = seen ? DateTimeOffset.UtcNow : null,
        });
    }

    // An upload that waits for an entry, which a delete takes too.
    private async Task FileAsync(Guid form)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        await GetRequiredService<IEntryRepository>().AddFileAsync(new NewEntryFile(Guid.NewGuid(), row.Id, Guid.NewGuid(), "cv.pdf", Guid.NewGuid().ToString("N"), "application/pdf", 10, new string('b', 64), "pending"));
    }
}
