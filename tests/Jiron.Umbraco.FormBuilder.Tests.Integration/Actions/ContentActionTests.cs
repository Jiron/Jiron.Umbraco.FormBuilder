using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>The content action, against a real document type (ROADMAP 8.5, ADR-0162).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ContentActionTests : ActionTestBase
{
    private const string TypeAlias = "jfbNote";

    private static readonly FormField Message = new() { Key = Guid.Parse("c2000000-0000-4000-8000-000000000001"), Type = "textarea", Alias = "message", Label = new LocalizedText("Message") };

    private static readonly FormField Note = new() { Key = Guid.Parse("c2000000-0000-4000-8000-000000000002"), Type = "text", Alias = "privateNote", Label = new LocalizedText("Note"), Sensitive = true };

    private static readonly FormField Upload = new() { Key = Guid.Parse("c2000000-0000-4000-8000-000000000003"), Type = "file", Alias = "upload", Label = new LocalizedText("Upload") };

    private IContentService Contents => GetRequiredService<IContentService>();

    private IMediaService Media => GetRequiredService<IMediaService>();

    private MemoryFileStore Files => GetRequiredService<MemoryFileStore>();

    private static FormAction Create(string settings) => new()
    {
        Key = Guid.NewGuid(),
        Type = ContentActionType.Alias,
        Name = "Create",
        Settings = SettingsObject.Parse(settings),
    };

    private static string Settings(string more = "") =>
        $$$"""{"documentType":"{{{TypeAlias}}}","name":"From {{ field.name }}","mapping":[{"property":"body","field":"{{{Message.Key}}}"},{"property":"title","value":"Note of {{ field.name }}"}]{{{more}}}}""";

    private Task<Guid> FormAsync(params FormAction[] actions) =>
        PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = actions }, fields: [NameField, Message, Note, Upload]);

    private IContent OnlyItem() => Contents.GetRootContent().Single(item => item.ContentType.Alias == TypeAlias);

    [SetUp]
    public async Task MakeTheDocumentType()
    {
        var dataTypes = (await GetRequiredService<IDataTypeService>().GetAllAsync()).ToList();
        var strings = GetRequiredService<IShortStringHelper>();
        var type = new ContentType(strings, Constants.System.Root) { Alias = TypeAlias, Name = "Note", AllowedAsRoot = true };
        foreach (var (alias, editor) in new[]
        {
            ("title", Constants.PropertyEditors.Aliases.TextBox),
            ("body", Constants.PropertyEditors.Aliases.TextArea),
            ("count", Constants.PropertyEditors.Aliases.Integer),
            ("flag", Constants.PropertyEditors.Aliases.Boolean),
            ("files", Constants.PropertyEditors.Aliases.MediaPicker3),
        })
        {
            type.AddPropertyType(new PropertyType(strings, dataTypes.First(item => item.EditorAlias == editor), alias) { Name = alias });
        }

        await GetRequiredService<IContentTypeService>().CreateAsync(type, Constants.Security.SuperUserKey);
    }

    [Test]
    public async Task AnEntry_CreatesAnItem_WithItsMappedAndFixedValues_SavedOnly()
    {
        var form = await FormAsync(Create(Settings()));

        var entry = await SubmitAsync(form, """{"name":"Ada","message":"Hello <b>you</b>"}""");
        var run = (await DrainAsync(entry.Id)).Single();

        var item = OnlyItem();
        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(item.Name, Is.EqualTo("From Ada"));
            Assert.That(item.GetValue<string>("body"), Is.EqualTo("Hello <b>you</b>"));
            Assert.That(item.GetValue<string>("title"), Is.EqualTo("Note of Ada"));
            Assert.That(item.Published, Is.False, "an editor looks at it first");
        });
    }

    [Test]
    public async Task AnEntry_CanPublishTheItem()
    {
        var form = await FormAsync(Create(Settings(""","publish":true""")));

        var entry = await SubmitAsync(form, """{"name":"Ada","message":"Hello"}""");
        await DrainAsync(entry.Id);

        Assert.That(OnlyItem().Published, Is.True);
    }

    [Test]
    public async Task ARunThatGoesOutAgain_FindsWhatItCreated()
    {
        var action = Create("""{"documentType":"jfbNote","name":"Fixed name"}""");
        var form = await FormAsync(action);
        var definition = (await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!;
        var type = GetRequiredService<FormBuilderActionTypeCollection>().Single(each => each.Declaration.Alias == ContentActionType.Alias);
        ActionRunContext Attempt(string delivery, int attempt) => new()
        {
            Action = action,
            Trigger = ActionTrigger.Submitted,
            Attempt = attempt,
            DeliveryKey = delivery,
            Scope = new Core.MergeTags.MergeTagScope { Form = definition, FormName = "F", FormAlias = "f", Culture = "en" },
        };

        await type.RunAsync(Attempt("the-delivery", 1), CancellationToken.None);
        await type.RunAsync(Attempt("the-delivery", 2), CancellationToken.None);
        var once = Contents.GetRootContent().Count(item => item.ContentType.Alias == TypeAlias);
        await type.RunAsync(Attempt("another-delivery", 1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(once, Is.EqualTo(1));
            Assert.That(Contents.GetRootContent().Count(item => item.ContentType.Alias == TypeAlias), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TwoEntries_MakeTwoItems_UnlessTheActionUpdatesByName()
    {
        var form = await FormAsync(Create(Settings()));
        var updating = await FormAsync(Create(Settings(""","updateExisting":true""")));

        await DrainAsync((await SubmitAsync(form, """{"name":"Ada","message":"one"}""")).Id);
        await DrainAsync((await SubmitAsync(form, """{"name":"Ada","message":"two"}""")).Id);
        var both = Contents.GetRootContent().Count(item => item.ContentType.Alias == TypeAlias);
        await DrainAsync((await SubmitAsync(updating, """{"name":"Grace","message":"one"}""")).Id);
        await DrainAsync((await SubmitAsync(updating, """{"name":"Grace","message":"two"}""")).Id);

        var graces = Contents.GetRootContent().Where(item => item.Name == "From Grace").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EqualTo(2));
            Assert.That(graces, Has.Count.EqualTo(1));
            Assert.That(graces[0].GetValue<string>("body"), Is.EqualTo("two"));
        });
    }

    [Test]
    public async Task APickedParent_HoldsTheItem_AndOneThatIsGone_FailsTheRun()
    {
        var parent = Contents.Create("Inbox", Constants.System.Root, TypeAlias);
        Contents.Save(parent);
        var other = Contents.Create("Gone soon", Constants.System.Root, TypeAlias);
        Contents.Save(other);
        var form = await FormAsync(Create(Settings($$""","parent":"{{parent.Key}}" """)));
        var doomed = await FormAsync(Create(Settings($$""","parent":"{{other.Key}}" """)));
        Contents.Delete(other);

        var entry = await SubmitAsync(form, """{"name":"Ada","message":"Hello"}""");
        await DrainAsync(entry.Id);
        var lost = (await DrainAsync((await SubmitAsync(doomed, """{"name":"Ada","message":"Hello"}""")).Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(Contents.GetPagedChildren(parent.Id, 0, 10, out _, null, null, null).Single().Name, Is.EqualTo("From Ada"));
            Assert.That(lost.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(lost.LastError?.Code, Is.EqualTo(ContentActionType.TargetMissing));
        });
    }

    [Test]
    public async Task APageFromTheRequest_HoldsTheItem_OnlyAtOrBelowTheItemTheActionStaysUnder()
    {
        var inbox = Contents.Create("Inbox", Constants.System.Root, TypeAlias);
        Contents.Save(inbox);
        var page = Contents.Create("Page", inbox.Id, TypeAlias);
        Contents.Save(page);
        var outside = Contents.Create("Elsewhere", Constants.System.Root, TypeAlias);
        Contents.Save(outside);
        var action = Create(Settings($$""","parentSource":"page","parent":"{{inbox.Key}}" """));
        var form = await FormAsync(action);

        Task<ActionResult> FromAsync(Guid? key) => RunDirectlyAsync(form, action, scope => scope with { Page = key });
        var below = await FromAsync(page.Key);
        var itself = await FromAsync(inbox.Key);
        var away = await FromAsync(outside.Key);
        var none = await FromAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(below.Kind, Is.EqualTo(ActionResultKind.Succeeded));
            Assert.That(itself.Kind, Is.EqualTo(ActionResultKind.Succeeded));
            Assert.That(away.Kind, Is.EqualTo(ActionResultKind.Failed));
            Assert.That(away.Code, Is.EqualTo(ContentActionType.TargetMissing));
            Assert.That(none.Code, Is.EqualTo(ContentActionType.TargetMissing));
            Assert.That(Contents.GetPagedChildren(outside.Id, 0, 10, out _, null, null, null), Is.Empty);
            Assert.That(Contents.GetPagedChildren(page.Id, 0, 10, out _, null, null, null).Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ASiteRoot_HoldsTheItem_OnlyWithinReach()
    {
        var site = Contents.Create("Site", Constants.System.Root, TypeAlias);
        Contents.Save(site);
        var other = Contents.Create("Other site", Constants.System.Root, TypeAlias);
        Contents.Save(other);
        var action = Create(Settings($$""","parentSource":"siteRoot","parent":"{{site.Key}}" """));
        var form = await FormAsync(action);

        var inside = await RunDirectlyAsync(form, action, scope => scope with { Site = site.Key });
        var outside = await RunDirectlyAsync(form, action, scope => scope with { Site = other.Key });

        Assert.Multiple(() =>
        {
            Assert.That(inside.Kind, Is.EqualTo(ActionResultKind.Succeeded));
            Assert.That(outside.Code, Is.EqualTo(ContentActionType.TargetMissing));
            Assert.That(Contents.GetPagedChildren(site.Id, 0, 10, out _, null, null, null).Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ANumberAndATick_ReachTheirEditors_AsWhatTheyStore()
    {
        var age = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "age", Label = new LocalizedText("Age") };
        var tick = new FormField { Key = Guid.NewGuid(), Type = "yesno", Alias = "tick", Label = new LocalizedText("Tick") };
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Actions = [Create($$"""{"documentType":"{{TypeAlias}}","name":"Typed","mapping":[{"property":"count","field":"{{age.Key}}"},{"property":"flag","field":"{{tick.Key}}"},{"property":"title","field":"{{age.Key}}"}]}""")] },
            fields: [NameField, age, tick]);

        await DrainAsync((await SubmitAsync(form, """{"name":"Ada","age":"42","tick":"yes"}""")).Id);

        Assert.Multiple(() =>
        {
            Assert.That(OnlyItem().GetValue<int>("count"), Is.EqualTo(42));
            Assert.That(OnlyItem().GetValue<bool>("flag"), Is.True);
            Assert.That(OnlyItem().GetValue<string>("title"), Is.EqualTo("42"));
        });
    }

    [Test]
    public async Task AConsent_ReachesATrueFalseEditor_AsGiven()
    {
        var consent = new FormField { Key = Guid.NewGuid(), Type = "consent", Alias = "agree", Label = new LocalizedText("Agree"), Settings = SettingsObject.Parse("""{"text":{"":"I agree."}}""") };
        var action = Create($$"""{"documentType":"{{TypeAlias}}","name":"Agreed","mapping":[{"property":"flag","field":"{{consent.Key}}"}]}""");
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [action] }, fields: [NameField, consent]);
        var row = (await FormRows.GetAsync(form))!;
        var field = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == consent.Key);

        // What a consent stores is the hash of the wording the visitor saw, which is no true or false.
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Approved,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            Values = [new NewEntryValue(field.Id, new string('b', 64))],
        }))!;

        var result = await RunDirectlyAsync(form, action, scope => scope, entry);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(ActionResultKind.Succeeded), result.Code);
            Assert.That(OnlyItem().GetValue<bool>("flag"), Is.True);
        });
    }

    [Test]
    public async Task ANumberTheEditorCannotStore_FailsTheRun()
    {
        var age = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "age", Label = new LocalizedText("Age") };
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Actions = [Create($$"""{"documentType":"{{TypeAlias}}","name":"Typed","mapping":[{"property":"count","field":"{{age.Key}}"}]}""")] },
            fields: [NameField, age]);

        var run = (await DrainAsync((await SubmitAsync(form, """{"name":"Ada","age":"99999999999"}""")).Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(ContentActionType.Refused));
            Assert.That(Contents.GetRootContent().Where(item => item.ContentType.Alias == TypeAlias), Is.Empty);
        });
    }

    [TestCase("Boolean", "true", true, 1)]
    [TestCase("Boolean", "yes", true, 1)]
    [TestCase("Boolean", "no", true, 0)]
    [TestCase("Boolean", "", true, 0)]
    [TestCase("Boolean", "maybe", false, null)]
    [TestCase("Integer", "42", true, 42)]
    [TestCase("Integer", "", true, null)]
    [TestCase("Integer", "4.5", false, null)]
    [TestCase("Integer", "99999999999", false, null)]
    public void AnAnswer_Converts_OrSaysItCannot(string editor, string text, bool fits, object? expected)
    {
        var alias = editor == "Boolean" ? Constants.PropertyEditors.Aliases.Boolean : Constants.PropertyEditors.Aliases.Integer;

        var converted = ContentActionType.TryConvert(alias, text, out var value);

        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.EqualTo(fits));
            if (fits)
            {
                Assert.That(value, Is.EqualTo(expected));
            }
        });
    }

    [TestCase("number", Constants.PropertyEditors.Aliases.Integer, true)]
    [TestCase("scale", Constants.PropertyEditors.Aliases.Decimal, true)]
    [TestCase("calculation", Constants.PropertyEditors.Aliases.Label, true)]
    [TestCase("yesno", Constants.PropertyEditors.Aliases.Boolean, true)]
    [TestCase("consent", Constants.PropertyEditors.Aliases.Boolean, true)]
    [TestCase("date", Constants.PropertyEditors.Aliases.DateTime, true)]
    [TestCase("time", Constants.PropertyEditors.Aliases.DateTime, false)]
    [TestCase("time", Constants.PropertyEditors.Aliases.TextBox, true)]
    [TestCase("text", Constants.PropertyEditors.Aliases.Integer, false)]
    [TestCase("heading", Constants.PropertyEditors.Aliases.TextBox, false)]
    [TestCase("captcha", Constants.PropertyEditors.Aliases.TextBox, false)]
    public void AFieldFits_OnlyWhatCanHoldItsAnswer(string fieldType, string editor, bool fits) =>
        Assert.That(ContentActionType.Fits(fieldType, editor), Is.EqualTo(fits));

    [Test]
    public async Task AVariantType_GetsTheEntrysLanguage_AndPublishesThat()
    {
        var dataTypes = (await GetRequiredService<IDataTypeService>().GetAllAsync()).ToList();
        var strings = GetRequiredService<IShortStringHelper>();
        var variant = new ContentType(strings, Constants.System.Root) { Alias = "jfbVariantNote", Name = "Variant note", AllowedAsRoot = true, Variations = ContentVariation.Culture };
        variant.AddPropertyType(new PropertyType(strings, dataTypes.First(item => item.EditorAlias == Constants.PropertyEditors.Aliases.TextBox), "title") { Name = "title", Variations = ContentVariation.Culture });
        await GetRequiredService<IContentTypeService>().CreateAsync(variant, Constants.Security.SuperUserKey);
        var form = await FormAsync(Create("""{"documentType":"jfbVariantNote","name":"Note of {{ field.name }}","publish":true,"mapping":[{"property":"title","value":"Written"}]}"""));

        var run = (await DrainAsync((await SubmitAsync(form)).Id)).Single();

        var item = Contents.GetRootContent().Single(each => each.ContentType.Alias == "jfbVariantNote");
        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded), run.LastError?.Code);
            Assert.That(item.GetCultureName("en-US"), Is.EqualTo("Note of Ada"));
            Assert.That(item.GetValue<string>("title", "en-US"), Is.EqualTo("Written"));
            Assert.That(item.IsCulturePublished("en-US"), Is.True);
        });
    }

    [Test]
    public async Task AVeryLongName_IsCut_ToWhatAnItemHolds()
    {
        var form = await FormAsync(Create($$"""{"documentType":"{{TypeAlias}}","name":"{{new string('n', 300)}}"}"""));

        var run = (await DrainAsync((await SubmitAsync(form)).Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(OnlyItem().Name, Has.Length.EqualTo(255));
        });
    }

    [Test]
    public async Task AnUpdateByName_LooksPastTheFirstPageOfChildren()
    {
        const int Others = 500;
        var parent = Contents.Create("Inbox", Constants.System.Root, TypeAlias);
        Contents.Save(parent);
        for (var i = 0; i < Others; i++)
        {
            Contents.Save(Contents.Create($"Other {i}", parent.Id, TypeAlias));
        }

        Contents.Save(Contents.Create("From Ada", parent.Id, TypeAlias));
        var form = await FormAsync(Create(Settings($$""","parent":"{{parent.Key}}","updateExisting":true""")));

        await DrainAsync((await SubmitAsync(form, """{"name":"Ada","message":"Hello"}""")).Id);

        _ = Contents.GetPagedChildren(parent.Id, 0, 1, out var total, null, null, null).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(total, Is.EqualTo(Others + 1), "no second item");
            Assert.That(Contents.GetPagedChildren(parent.Id, 1, Others, out _, null, null, null).Single().GetValue<string>("body"), Is.EqualTo("Hello"));
        });
    }

    [Test]
    public async Task ASensitiveField_IsCopied_OnlyWhereTheActionNamesIt()
    {
        var mapping = $$"""{"documentType":"{{TypeAlias}}","name":"Secret","mapping":[{"property":"body","field":"{{Note.Key}}"}]""";
        var form = await FormAsync(Create(mapping + "}"));
        var named = await FormAsync(Create(mapping + $$""","includeSensitive":["{{Note.Key}}"]}"""));

        await DrainAsync((await SubmitAsync(form, """{"name":"Ada","privateNote":"hidden-note"}""")).Id);
        var without = OnlyItem().GetValue<string>("body");
        Contents.Delete(OnlyItem());
        await DrainAsync((await SubmitAsync(named, """{"name":"Ada","privateNote":"hidden-note"}""")).Id);

        Assert.Multiple(() =>
        {
            Assert.That(without, Is.Null, "no value, and no mask in its place");
            Assert.That(OnlyItem().GetValue<string>("body"), Is.EqualTo("hidden-note"));
        });
    }

    [Test]
    public async Task AnUpload_GoesToMedia_OnlyWhenTheMappingNamesIt_AndNotWhenQuarantined()
    {
        var folder = Media.CreateMediaWithIdentity("Uploads", Constants.System.Root, Constants.Conventions.MediaTypes.Folder);
        var mapped = await FormAsync(Create($$"""{"documentType":"{{TypeAlias}}","name":"With file","mediaFolder":"{{folder.Key}}","mapping":[{"property":"files","field":"{{Upload.Key}}"}]}"""));
        var unmapped = await FormAsync(Create(Settings()));

        await DrainAsync((await EntryWithFilesAsync(unmapped)).Id);
        var mediaAfterUnmapped = Media.GetPagedChildren(folder.Id, 0, 10, out _).Count() + Media.GetRootMedia().Count();
        await DrainAsync((await EntryWithFilesAsync(mapped)).Id);

        var copied = Media.GetPagedChildren(folder.Id, 0, 10, out _).ToList();
        var picked = Contents.GetRootContent().Single(item => item.Name == "With file").GetValue<string>("files");
        Assert.Multiple(() =>
        {
            Assert.That(mediaAfterUnmapped, Is.EqualTo(1), "only the folder");
            Assert.That(copied.Select(item => item.Name), Is.EqualTo(new[] { "clean.pdf" }), "not the quarantined one, and into the chosen folder");
            Assert.That(picked, Does.Contain(copied[0].Key.ToString()));
        });
    }

    [Test]
    public async Task ARetry_FindsTheMediaItMadeBefore_AndAPendingScanWaits()
    {
        var action = Create($$"""{"documentType":"{{TypeAlias}}","name":"With file","mapping":[{"property":"files","field":"{{Upload.Key}}"}]}""");
        var form = await FormAsync(action);
        var entry = await EntryWithFilesAsync(form);
        var clean = (await Entries.ListFilesAsync(entry.Id)).Single(file => file.ScanState == FileScanStates.Clean);
        await Entries.ChangeScanStateAsync(clean.Id, FileScanStates.Clean, FileScanStates.Pending);
        Task<ActionResult> AttemptAsync() => RunDirectlyAsync(form, action, scope => scope, entry);

        var whilePending = await AttemptAsync();
        var nothingYet = Media.GetRootMedia().Count();
        await Entries.ChangeScanStateAsync(clean.Id, FileScanStates.Pending, FileScanStates.Clean);
        await AttemptAsync();
        var first = Media.GetRootMedia().Select(item => item.Key).ToList();
        var value = Contents.GetRootContent().Single(item => item.Name == "With file").GetValue<string>("files");
        await AttemptAsync();

        Assert.Multiple(() =>
        {
            Assert.That(whilePending.Kind, Is.EqualTo(ActionResultKind.TryAgain));
            Assert.That(whilePending.Code, Is.EqualTo(ContentActionType.ScanPending));
            Assert.That(nothingYet, Is.Zero);
            Assert.That(first, Has.Count.EqualTo(1));
            Assert.That(Media.GetRootMedia().Select(item => item.Key), Is.EqualTo(first), "the second attempt made no second copy");
            Assert.That(Contents.GetRootContent().Single(item => item.Name == "With file").GetValue<string>("files"), Is.EqualTo(value));
        });
    }

    [Test]
    public async Task AFileUmbracoDoesNotAllow_StaysOutOfTheMediaLibrary()
    {
        GetRequiredService<IOptionsMonitor<ContentSettings>>().CurrentValue.DisallowedUploadedFileExtensions = new HashSet<string> { "pdf" };
        var form = await FormAsync(Create($$"""{"documentType":"{{TypeAlias}}","name":"With file","mapping":[{"property":"files","field":"{{Upload.Key}}"}]}"""));

        var run = (await DrainAsync((await EntryWithFilesAsync(form)).Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(Media.GetRootMedia(), Is.Empty);
        });
    }

    [Test]
    public async Task ARunThatFailsOnAnAnswer_LeavesNothingInTheMediaLibrary()
    {
        var age = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "age", Label = new LocalizedText("Age") };
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Actions = [Create($$"""{"documentType":"{{TypeAlias}}","name":"Both","mapping":[{"property":"files","field":"{{Upload.Key}}"},{"property":"count","field":"{{age.Key}}"}]}""")] },
            fields: [NameField, Upload, age]);
        var row = (await FormRows.GetAsync(form))!;
        var uploadField = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == Upload.Key);
        var ageField = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == age.Key);
        var file = await FileAsync(row.Id, "clean.pdf", FileScanStates.Clean);
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Approved,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [file.Id],
            Values = [new NewEntryValue(uploadField.Id, "clean.pdf", FileId: file.Id), new NewEntryValue(ageField.Id, "99999999999")],
        }))!;

        var result = await RunDirectlyAsync(form, (await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!.Actions.Single(), scope => scope, entry);

        Assert.Multiple(() =>
        {
            Assert.That(result.Code, Is.EqualTo(ContentActionType.Refused));
            Assert.That(Media.GetRootMedia(), Is.Empty);
        });
    }

    [Test]
    public async Task AChildOfASensitiveGroup_IsNotCopied_UntilTheActionNamesTheGroup()
    {
        var child = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "count", Label = new LocalizedText("Count") };
        var group = new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "rows", Label = new LocalizedText("Rows"), Sensitive = true, Fields = [child] };
        var mapping = $$"""{"documentType":"{{TypeAlias}}","name":"Group","mapping":[{"property":"count","field":"{{child.Key}}"}]""";
        var without = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Create(mapping + "}")] }, fields: [NameField, group]);
        var named = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Create(mapping + $$""","includeSensitive":["{{group.Key}}"]}""")] }, fields: [NameField, group]);

        await DrainAsync((await SubmitAsync(without, """{"name":"Ada","rows":[{"count":"4"}]}""")).Id);
        var left = OnlyItem().GetValue<int?>("count");
        Contents.Delete(OnlyItem());
        await DrainAsync((await SubmitAsync(named, """{"name":"Ada","rows":[{"count":"4"}]}""")).Id);

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.Null.Or.Zero);
            Assert.That(OnlyItem().GetValue<int>("count"), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task ATrashedParent_FailsTheRun()
    {
        var parent = Contents.Create("Inbox", Constants.System.Root, TypeAlias);
        Contents.Save(parent);
        var form = await FormAsync(Create(Settings($$""","parent":"{{parent.Key}}" """)));
        Contents.MoveToRecycleBin(parent);

        var run = (await DrainAsync((await SubmitAsync(form, """{"name":"Ada","message":"Hello"}""")).Id)).Single();

        Assert.That(run.LastError?.Code, Is.EqualTo(ContentActionType.TargetMissing));
    }

    [Test]
    public async Task ASensitiveFile_StaysOutOfTheMediaLibrary_UntilTheActionNamesIt()
    {
        var hidden = new FormField { Key = Guid.NewGuid(), Type = "file", Alias = "hiddenUpload", Label = new LocalizedText("Hidden"), Sensitive = true };
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Actions = [Create($$"""{"documentType":"{{TypeAlias}}","name":"Secret file","mapping":[{"property":"files","field":"{{hidden.Key}}"}]}""")] },
            fields: [NameField, Message, Note, Upload, hidden]);

        var run = (await DrainAsync((await SubmitAsync(form)).Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(Media.GetRootMedia(), Is.Empty);
        });
    }

    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","value":"{{ form.name"}]}""", "mapping", TestName = "A value whose tag does not close")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"count","value":"{{ field.name }}"}]}""", "mapping", TestName = "A tag where a number goes")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"count","value":"many"}]}""", "mapping", TestName = "Words where a number goes")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"files","value":"x"}]}""", "mapping", TestName = "A value where media goes")]
    [TestCase("""{"documentType":"jfbNote","name":"N","parentSource":"page"}""", "parent", TestName = "A page parent without a limit")]
    [TestCase("""{"documentType":"jfbNote","name":"N","parentSource":"siteRoot"}""", "parent", TestName = "A site root parent without a limit")]
    [TestCase("""{"documentType":"nothere","name":"N"}""", "documentType", TestName = "A document type the site lacks")]
    [TestCase("""{"documentType":"jfbNote"}""", "name", TestName = "No name")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"gone","value":"x"}]}""", "mapping", TestName = "A property the type lacks")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"count","field":"c2000000-0000-4000-8000-000000000001"}]}""", "mapping", TestName = "A text answer into a number")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","field":"c2000000-0000-4000-8000-000000000003"}]}""", "mapping", TestName = "A file into a text")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"files","field":"c2000000-0000-4000-8000-000000000001"}]}""", "mapping", TestName = "A text answer into media")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title"}]}""", "mapping", TestName = "Neither a field nor a value")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","value":"x","field":"c2000000-0000-4000-8000-000000000001"}]}""", "mapping", TestName = "Both a field and a value")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","field":"c2000000-0000-4000-8000-0000000000ff"}]}""", "mapping", TestName = "A field of another form")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":{"title":"x"}}""", "mapping", TestName = "A mapping that is no list")]
    [TestCase("""{"documentType":"jfbNote","name":"{{ form.name"}""", "name", TestName = "A name whose tag does not close")]
    public void Publishing_RefusesWhatCannotBeStored(string settings, string setting)
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await FormAsync(Create(settings)));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain($"$.actions[0].settings.{setting}"), string.Join(", ", refused.Problems));
    }

    [Test]
    public async Task TagsThatNameNothing_StillPublish_AndTheFormCheckWarnsAtTheirSettings()
    {
        var form = await FormAsync(Create("""{"documentType":"jfbNote","name":"From {{ nothing.here }}","mapping":[{"property":"title","value":"Of {{ nothing.there }}"}]}"""));

        var issues = await GetRequiredService<FormCheck>().CheckAsync((await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!, CancellationToken.None);

        // The form's hints (its file field without a size of its own, 9.5e) are no business of the action's.
        Assert.That(issues.Where(issue => issue.Severity != FormCheckSeverity.Hint).Select(issue => (issue.Severity, issue.Code, issue.Path, issue.Message)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, "unknown_path", "$.actions[0].settings.name", "The merge tag at character 9 names nothing this form or site has, so it shows nothing."),
            (FormCheckSeverity.Warning, "unknown_path", "$.actions[0].settings.mapping", "In the value of 'title': The merge tag at character 7 names nothing this form or site has, so it shows nothing."),
        }));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.Services.AddSingleton<IFormBuilderFileScanner, FindsNothing>();
    }

    // One attempt of the action as the outbox makes it, with what the request brought; the outbox never calls it twice with the same key on its own.
    private async Task<ActionResult> RunDirectlyAsync(Guid form, FormAction action, Func<MergeTagScope, MergeTagScope> brought, StoredEntry? stored = null)
    {
        var entry = stored ?? await SubmitAsync(form, """{"name":"Ada","message":"Hello"}""");
        var scope = (await GetRequiredService<SubmissionEntries>().ScopeAsync(entry))!;
        var type = GetRequiredService<ActionTypeRegistry>().Find(ContentActionType.Alias)!;
        return await type.RunAsync(
            new ActionRunContext { Action = action, Trigger = ActionTrigger.Submitted, Scope = brought(scope), Attempt = 1, DeliveryKey = $"direct-{entry.Key}", EntryId = entry.Id },
            CancellationToken.None);
    }

    private async Task<StoredEntry> EntryWithFilesAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        var field = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == Upload.Key);
        var clean = await FileAsync(row.Id, "clean.pdf", FileScanStates.Clean);
        var bad = await FileAsync(row.Id, "bad.pdf", FileScanStates.Clean);
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Approved,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [clean.Id, bad.Id],
            Values = [new NewEntryValue(field.Id, "clean.pdf", FileId: clean.Id), new NewEntryValue(field.Id, "bad.pdf", ValueIndex: 1, FileId: bad.Id)],
        }))!;
        await Entries.ChangeScanStateAsync(bad.Id, FileScanStates.Clean, FileScanStates.Quarantined);
        var definition = (await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!;
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(definition.Actions[0].Key, definition.Actions[0].Type, ActionTrigger.Submitted, 0, false)]);
        return entry;
    }

    private async Task<StoredEntryFile> FileAsync(int formId, string name, string state)
    {
        var id = FormBuilderFileId.New();
        await Files.SaveAsync(id, new MemoryStream(new byte[3]));
        return (await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), formId, Upload.Key, name, id, "application/pdf", 3, new string('a', 64), state)))!;
    }

    private sealed class FindsNothing : IFormBuilderFileScanner
    {
        public Task<FormBuilderFileScanOutcome> ScanAsync(FormBuilderFileToScan file, CancellationToken cancellationToken) => Task.FromResult(FormBuilderFileScanOutcome.Clean);
    }
}
