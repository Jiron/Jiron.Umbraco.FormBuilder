using System.Globalization;
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
    public async Task ATestRun_IsADryRun_ThatCreatesNothing()
    {
        var action = Create(Settings());
        var form = await FormAsync(action);

        var test = await GetRequiredService<ActionTestRuns>().TestAsync(form, action.Key, new ActionTestUser(Guid.NewGuid(), "ada@jfb.test", "Ada"), confirmed: false);

        var lines = test.Result!.Preview!;
        Assert.Multiple(() =>
        {
            Assert.That((test.Result!.Outcome, test.Result.Code), Is.EqualTo((ActionTestOutcome.DryRun, ActionTestCodes.DryRun)));
            Assert.That(lines.Select(line => line.Code), Is.EqualTo(new[] { "content_root", "content_sets" }));
            Assert.That(lines[0].Args.Take(2), Is.EqualTo(new[] { "save", "Note" }));
            Assert.That(lines[0].Args[2], Does.StartWith("From "), "the name, from the sample entry");
            Assert.That(lines[0].Text, Is.EqualTo($"A run would save an item of type Note named \"{lines[0].Args[2]}\" at the content root."));
            Assert.That(lines[1].Args, Is.EqualTo(new[] { "body", "title" }));
            Assert.That(Contents.GetRootContent(), Is.Empty, "nothing was created");
        });
    }

    [Test]
    public async Task ATestRun_SaysWhatARunWouldDo_WhereAndWhichProperties_AndWhatItWouldLeaveOut()
    {
        var home = HomeItem();
        var form = await FormAsync(Create(Settings()));

        var lines = (await TestHeldAsync(form, $$"""
            {"documentType":"{{TypeAlias}}","name":"Story","publish":true,"updateExisting":true,"parent":"{{home.Key}}","mapping":[
              {"property":"body","field":"{{Message.Key}}"},
              {"property":"title","field":"{{Note.Key}}"},
              {"property":"gone","value":"x"},
              {"property":"count","value":"many"},
              {"property":"flag","field":"{{Message.Key}}"},
              {"property":"files","field":"{{Upload.Key}}"}]}
            """)).Preview!;

        Assert.That(lines.Select(line => (line.Code, string.Join("|", line.Args))), Is.EqualTo(new[]
        {
            ("content_under", "publish|Note|Story|Home"),
            ("content_update", string.Empty),
            ("content_sensitive", "title|Note"),
            ("content_property_gone", "gone"),
            ("content_cannot_hold_value", "count"),
            ("content_cannot_hold", "flag|Message"),
            ("content_sets", "body|files"),
        }));
    }

    [Test]
    public async Task ATestRun_SaysTheOnePropertyItWouldSet()
    {
        var form = await FormAsync(Create(Settings()));

        var lines = (await TestHeldAsync(form, $$"""{"documentType":"{{TypeAlias}}","name":"Story","mapping":[{"property":"body","field":"{{Message.Key}}"}]}""")).Preview!;

        Assert.That(lines.Select(line => (line.Code, line.Text, string.Join("|", line.Args))).Last(), Is.EqualTo(("content_sets", "It would set body.", "body")));
    }

    [Test]
    public async Task ATestRun_SaysAPropertyThatCannotHoldTheFilesOfAnUpload()
    {
        var form = await FormAsync(Create(Settings()));

        var lines = (await TestHeldAsync(form, $$"""{"documentType":"{{TypeAlias}}","name":"Story","mapping":[{"property":"body","field":"{{Upload.Key}}"}]}""")).Preview!;

        Assert.That(lines.Select(line => (line.Code, string.Join("|", line.Args))).Last(), Is.EqualTo(("content_cannot_hold", "body|Upload")));
    }

    [TestCase("page", "content_page", TestName = "Under the visitor's page, within the item picked")]
    [TestCase("siteRoot", "content_site", TestName = "Under the visitor's site root, within the item picked")]
    public async Task ATestRun_SaysWhereAVisitorsPageWouldTakeIt(string source, string code)
    {
        var home = HomeItem();
        var form = await FormAsync(Create(Settings()));

        var where = (await TestHeldAsync(form, $$"""{"documentType":"{{TypeAlias}}","name":"Story","parentSource":"{{source}}","parent":"{{home.Key}}"}""")).Preview![0];

        Assert.That((where.Code, string.Join("|", where.Args)), Is.EqualTo((code, "save|Note|Story|Home")));
    }

    [TestCase("""{"documentType":"nothere","name":"N"}""", "target_missing", "documentType", TestName = "A document type the site lacks fails the test")]
    [TestCase("""{"documentType":"jfbNote","name":"N","parent":"c2000000-0000-4000-8000-0000000000ee"}""", "target_missing", "parent", TestName = "A parent that is gone fails the test")]
    [TestCase("""{"documentType":"jfbNote","name":"N","parentSource":"page"}""", "target_missing", "within", TestName = "A page parent without the item it must stay within fails the test")]
    [TestCase("""{"documentType":"jfbNote","name":"{{ nothing.here }}"}""", "no_name", "", TestName = "A name that comes out empty fails the test")]
    public async Task ATestRun_FailsWhereARunWould(string settings, string code, string what)
    {
        var form = await FormAsync(Create(Settings()));

        var result = await TestHeldAsync(form, settings);

        Assert.That((result.Outcome, result.Code, string.Join("|", result.Args)), Is.EqualTo((ActionTestOutcome.Failed, code, what)));
    }

    [Test]
    public async Task TheDocumentTypes_AreOfferedByName_ElementTypesLeftOut()
    {
        await ElementTypeAsync();

        var choices = await GetRequiredService<ActionTypeRegistry>().Find(ContentActionType.Alias)!.SiteChoicesAsync(CancellationToken.None);

        Assert.That(choices["documentType"].Select(choice => (choice.Value, choice.Label)), Does.Contain((TypeAlias, "Note")).And.Not.Contains(("jfbPart", "Part")));
    }

    [TestCase("""{"name":"N"}""", "Choose the document type each entry becomes.", TestName = "No document type is asked for")]
    [TestCase("""{"documentType":"nothere","name":"N"}""", "This site has no such document type.", TestName = "A document type the site lacks is said to be missing")]
    public void Publishing_SaysWhatTheDocumentTypeLacks(string settings, string message)
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await FormAsync(Create(settings)));

        Assert.That(refused!.Problems.Where(problem => problem.Path == "$.actions[0].settings.documentType").Select(problem => problem.Message), Is.EqualTo(new[] { message }));
    }

    [Test]
    public async Task AnElementType_IsNoTypeAnItemCanHave()
    {
        await ElementTypeAsync();

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await FormAsync(Create("""{"documentType":"jfbPart","name":"N"}""")));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain("$.actions[0].settings.documentType"));
    }

    [Test]
    public async Task AFieldOfARepeatableGroup_HasAnAnswerForEachRow_SoNoPropertyTakesIt()
    {
        var guest = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "guest", Label = new LocalizedText("Guest") };
        var guests = new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "guests", Label = new LocalizedText("Guests"), Fields = [guest] };
        var action = Create($$"""{"documentType":"{{TypeAlias}}","name":"N","mapping":[{"property":"title","field":"{{guest.Key}}"}]}""");

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () =>
            await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [action] }, fields: [NameField, guests]));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain("$.actions[0].settings.mapping[0].field"), string.Join(", ", refused.Problems));
    }

    [Test]
    public async Task APropertySetTwice_StillPublishes_AndTheFormCheckWarnsAtTheLaterRow()
    {
        var form = await FormAsync(Create($$"""{"documentType":"{{TypeAlias}}","name":"N","mapping":[{"property":"title","value":"A"},{"property":"body","value":"B"},{"property":"title","value":"C"}]}"""));

        var issues = await GetRequiredService<FormCheck>().CheckAsync((await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!, CancellationToken.None);

        Assert.That(issues.Where(issue => issue.Code == "content_property_twice").Select(issue => (issue.Severity, issue.Path)), Is.EqualTo(new[]
        {
            (FormCheckSeverity.Warning, "$.actions[0].settings.mapping[2].property"),
        }));
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

    [TestCase("1.5", "1.5")]
    [TestCase("-2.25", "-2.25")]
    [TestCase(" 3 ", "3")]
    [TestCase("1,5", null, TestName = "A decimal comma is no thousands separator: 1,5 is refused, never 15")]
    [TestCase("1,234.5", null)]
    [TestCase("1e3", null)]
    public void ADecimal_IsReadWithAPointOnly(string text, string? expected)
    {
        var converted = ContentActionType.TryConvert(Constants.PropertyEditors.Aliases.Decimal, text, out var value);

        Assert.That((converted, converted ? value : null), Is.EqualTo((expected is not null, expected is null ? null : (object)decimal.Parse(expected, CultureInfo.InvariantCulture))));
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

    [TestCase(false, TestName = "A child of a sensitive group is refused")]
    [TestCase(true, TestName = "A child of a sensitive group is refused, even with the group named")]
    public void AChildOfASensitiveGroup_IsRefused_LikeAnyRepeatableGroupsField(bool named)
    {
        var child = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "count", Label = new LocalizedText("Count") };
        var group = new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "rows", Label = new LocalizedText("Rows"), Sensitive = true, Fields = [child] };
        var naming = named ? $$""","includeSensitive":["{{group.Key}}"]""" : string.Empty;
        var action = Create($$"""{"documentType":"{{TypeAlias}}","name":"Group","mapping":[{"property":"count","field":"{{child.Key}}"}]{{naming}}}""");

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () =>
            await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [action] }, fields: [NameField, group]));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain("$.actions[0].settings.mapping[0].field"), string.Join(", ", refused.Problems));
    }

    [TestCase(false, 0, TestName = "A version published before keeps a sensitive group's child out")]
    [TestCase(true, 4, TestName = "A version published before copies a sensitive group's child once the action names the group")]
    public async Task AVersionPublishedBefore_KeepsASensitiveGroupsChildOut_UntilTheActionNamesTheGroup(bool named, int copied)
    {
        var child = new FormField { Key = Guid.NewGuid(), Type = "number", Alias = "count", Label = new LocalizedText("Count") };
        var group = new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "rows", Label = new LocalizedText("Rows"), Sensitive = true, Fields = [child] };
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Create($$"""{"documentType":"{{TypeAlias}}","name":"Group"}""")] }, fields: [NameField, group]);
        var entry = await SubmitAsync(form, """{"name":"Ada","rows":[{"count":"4"}]}""");
        // The action as a version published before the rule holds it: it maps the group's field, which no form publishes now.
        var naming = named ? $$""","includeSensitive":["{{group.Key}}"]""" : string.Empty;
        var action = Create($$"""{"documentType":"{{TypeAlias}}","name":"Group","mapping":[{"property":"count","field":"{{child.Key}}"}]{{naming}}}""");

        var result = await RunDirectlyAsync(form, action, scope => scope, entry);

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(ActionResultKind.Succeeded), result.Code);
            Assert.That(OnlyItem().GetValue<int?>("count") ?? 0, Is.EqualTo(copied));
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

    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","value":"{{ form.name"}]}""", "mapping[0].value", "unclosed_tag", TestName = "A value whose tag does not close")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"count","value":"{{ field.name }}"}]}""", "mapping[0].value", "content_value_cannot_hold", TestName = "A tag where a number goes")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","value":"x"},{"property":"count","value":"many"}]}""", "mapping[1].value", "content_value_cannot_hold", TestName = "Words where a number goes, in the row it is in")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"files","value":"x"}]}""", "mapping[0].value", "content_value_cannot_hold", TestName = "A value where media goes")]
    [TestCase("""{"documentType":"jfbNote","name":"N","parentSource":"page"}""", "parent", "content_parent_missing", TestName = "A page parent without a limit")]
    [TestCase("""{"documentType":"jfbNote","name":"N","parentSource":"siteRoot"}""", "parent", "content_parent_missing", TestName = "A site root parent without a limit")]
    [TestCase("""{"documentType":"nothere","name":"N"}""", "documentType", "content_type_gone", TestName = "A document type the site lacks")]
    [TestCase("""{"name":"N"}""", "documentType", "content_type_missing", TestName = "No document type")]
    [TestCase("""{"documentType":" ","name":"N"}""", "documentType", "content_type_missing", TestName = "A blank document type")]
    [TestCase("""{"documentType":"jfbNote"}""", "name", "content_name_missing", TestName = "No name")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"gone","value":"x"}]}""", "mapping[0].property", "content_property_gone", TestName = "A property the type lacks")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"count","field":"c2000000-0000-4000-8000-000000000001"}]}""", "mapping[0].field", "content_field_cannot_hold", TestName = "A text answer into a number")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","field":"c2000000-0000-4000-8000-000000000003"}]}""", "mapping[0].field", "content_field_cannot_hold", TestName = "A file into a text")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"files","field":"c2000000-0000-4000-8000-000000000001"}]}""", "mapping[0].field", "content_field_cannot_hold", TestName = "A text answer into media")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title"}]}""", "mapping", "content_mapping_invalid", TestName = "Neither a field nor a value")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","value":"x","field":"c2000000-0000-4000-8000-000000000001"}]}""", "mapping", "content_mapping_invalid", TestName = "Both a field and a value")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":[{"property":"title","field":"c2000000-0000-4000-8000-0000000000ff"}]}""", "mapping[0].field", "content_field_gone", TestName = "A field of another form")]
    [TestCase("""{"documentType":"jfbNote","name":"N","mapping":{"title":"x"}}""", "mapping", "content_mapping_invalid", TestName = "A mapping that is no list")]
    [TestCase("""{"documentType":"jfbNote","name":"{{ form.name"}""", "name", "unclosed_tag", TestName = "A name whose tag does not close")]
    public void Publishing_RefusesWhatCannotBeStored(string settings, string setting, string code)
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await FormAsync(Create(settings)));

        // Each with its code, which the backoffice says in its own words (ADR-0192).
        Assert.That(refused!.Problems.Select(problem => (problem.Path, problem.Code)), Does.Contain(($"$.actions[0].settings.{setting}", code)), string.Join(", ", refused.Problems));
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
            (FormCheckSeverity.Warning, "unknown_path", "$.actions[0].settings.mapping[0].value", "In the value of 'title': The merge tag at character 7 names nothing this form or site has, so it shows nothing."),
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

    // A test run of the form's action with these settings, as the designer holds its draft, saved or not (ADR-0190).
    private async Task<ActionTestResult> TestHeldAsync(Guid form, string settings)
    {
        var draft = (await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!;
        var action = draft.Actions[0] with { Settings = SettingsObject.Parse(settings) };
        var test = await GetRequiredService<ActionTestRuns>().TestAsync(form, action.Key, new ActionTestUser(Guid.NewGuid(), "ada@jfb.test", "Ada"), confirmed: false, held: draft with { Actions = [action] });
        return test.Result!;
    }

    // An item at the root named Home, of the action's own type, which an item may go under.
    private IContent HomeItem()
    {
        var home = Contents.Create("Home", Constants.System.Root, TypeAlias);
        Contents.Save(home);
        return home;
    }

    // An element type, which blocks have and no content item does.
    private async Task ElementTypeAsync()
    {
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), Constants.System.Root) { Alias = "jfbPart", Name = "Part", IsElement = true };
        await GetRequiredService<IContentTypeService>().CreateAsync(type, Constants.Security.SuperUserKey);
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
