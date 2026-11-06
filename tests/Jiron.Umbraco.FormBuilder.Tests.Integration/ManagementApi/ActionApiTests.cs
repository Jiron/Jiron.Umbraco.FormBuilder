using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;
using ContentType = Umbraco.Cms.Core.Models.ContentType;
using IDataType = Umbraco.Cms.Core.Models.IDataType;
using PropertyGroup = Umbraco.Cms.Core.Models.PropertyGroup;
using PropertyGroupType = Umbraco.Cms.Core.Models.PropertyGroupType;
using PropertyType = Umbraco.Cms.Core.Models.PropertyType;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The endpoints of the action framework (ROADMAP 8.1, section 4.4, section 4.7): the action types, an entry's runs, the retry, which
/// refuses an uncertain run without a confirmation, and test runs, of which a type that reaches what the action is set up to reach
/// needs a confirmation too.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ActionApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IActionRunRepository Runs => GetRequiredService<IActionRunRepository>();

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private ScriptedActionType Scripted => GetRequiredService<FormBuilderActionTypeCollection>().OfType<ScriptedActionType>().Single();

    /// <summary>
    /// A site whose actions are not paused, as test runs need since they are refused while paused (ADR-0190); the host has no
    /// worker, so nothing goes out in the background.
    /// </summary>
    [SetUp]
    public void Unpause() => GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Actions.Paused = false;

    [Test]
    public async Task TheActionTypes_AreServedToAUserOfTheSection_WithTheirSettings()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");

        var scripted = answer.Body!["types"]!.AsArray().Single(type => type!["alias"]!.GetValue<string>() == ScriptedActionType.Alias)!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(scripted["name"]!.GetValue<string>(), Is.EqualTo("Scripted"));
            Assert.That(scripted["icon"]!.GetValue<string>(), Is.EqualTo("icon-script"));
            Assert.That(scripted["test"]!.GetValue<string>(), Is.EqualTo("afterConfirmation"));
            Assert.That(scripted["settings"]![0]!["name"]!.GetValue<string>(), Is.EqualTo("target"));
        });
    }

    [Test]
    public async Task TheBuiltInActionTypes_AreServedAsTheReviewedSnapshotHolds()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");
        var builtIn = new JsonArray([.. answer.Body!["types"]!.AsArray().Where(type => type!["alias"]!.GetValue<string>() is EmailActionType.Alias or Core.Webhooks.WebhookActionType.Alias or SetStateActionType.Alias or ContentActionType.Alias).Select(type => type!.DeepClone())]);
        var served = builtIn.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        var path = RepositoryRoot.Path(typeof(ActionApiTests).Assembly, "docs", "api", "action-types.json");
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(path, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {path}. Review the diff, check the backoffice's dictionaries, commit it, and run again without it.");
        }

        Assert.That((await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the action types changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task TheEmailType_OffersItsPresets()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");

        var email = answer.Body!["types"]!.AsArray().Single(type => type!["alias"]!.GetValue<string>() == EmailActionType.Alias)!;
        var presets = email["presets"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(email["test"]!.GetValue<string>(), Is.EqualTo("toCurrentUser"));
            Assert.That(presets.Select(preset => preset!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "#jfbActionPresets_emailNotifyTeam", "#jfbActionPresets_emailConfirmSender" }));
            Assert.That(presets.All(preset => JsonNode.Parse(preset!["settings"]!.GetValue<string>())!["subject"] is not null), Is.True);
        });
    }

    [Test]
    public async Task TheWebhookType_OffersAPlainWebhookFirst_ThenSlackAndTeams()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");

        var webhook = answer.Body!["types"]!.AsArray().Single(type => type!["alias"]!.GetValue<string>() == Core.Webhooks.WebhookActionType.Alias)!;
        var presets = webhook["presets"]!.AsArray();
        Assert.Multiple(() =>
        {
            // The designer offers a type only through its presets (ADR-0188): the plain one has the type's own name.
            Assert.That(presets.Select(preset => preset!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "#jfbActionTypes_webhook", "#jfbActionPresets_webhookSlack", "#jfbActionPresets_webhookTeams" }));
            Assert.That(JsonNode.Parse(presets[0]!["settings"]!.GetValue<string>())!.AsObject(), Is.Empty, "a plain webhook starts with the type's defaults");
        });
    }

    [Test]
    public async Task TheContentType_OffersTheSitesDocumentTypes_ByName_ElementTypesLeftOut()
    {
        await StoryTypeAsync();

        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type");

        var content = answer.Body!["types"]!.AsArray().Single(type => type!["alias"]!.GetValue<string>() == ContentActionType.Alias)!;
        var choices = content["settings"]!.AsArray().Single(setting => setting!["name"]!.GetValue<string>() == "documentType")!["choices"]!.AsArray();
        Assert.That(choices.Select(choice => (choice!["value"]!.GetValue<string>(), choice["label"]!.GetValue<string>())), Is.EqualTo(new[] { ("jfbStory", "Story") }));
    }

    [Test]
    public async Task ADocumentType_IsServedWithThePropertiesAFormCanSet_AndWhatEachTakes()
    {
        await StoryTypeAsync();

        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type/content/document-type/jfbStory");

        var properties = answer.Body!["properties"]!.AsArray().Select(property => property!).ToList();
        string? Text(JsonNode property, string name) => property[name]?.GetValue<string>();
        string[] Types(JsonNode property) => [.. property["fieldTypes"]!.AsArray().Select(type => type!.GetValue<string>())];
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That((Text(answer.Body!, "alias"), Text(answer.Body!, "name")), Is.EqualTo(("jfbStory", "Story")));
            // Rich text holds no answer and no value written: it is left out.
            Assert.That(properties.Select(property => (Text(property, "alias"), Text(property, "name"), Text(property, "group"), Text(property, "value"))), Is.EqualTo(new (string?, string?, string?, string?)[]
            {
                ("title", "Title", "Content", "text"),
                ("count", "Count", "Content", "wholeNumber"),
                ("photo", "Photo", null, null),
            }));
            Assert.That(Types(properties[0]), Does.Contain("text").And.Contain("email").And.Not.Contain("file").And.Not.Contain("heading"));
            Assert.That(Types(properties[1]), Does.Contain("number").And.Not.Contain("text"));
            Assert.That(Types(properties[2]), Is.EqualTo(new[] { "file" }));
        });
    }

    [Test]
    public async Task ADocumentTypesProperties_ComeInTheOrderUmbracoShowsThem_TabsAndCompositionsIncluded()
    {
        var dataTypes = (await GetRequiredService<IDataTypeService>().GetAllAsync()).ToList();
        var strings = GetRequiredService<IShortStringHelper>();
        var types = GetRequiredService<IContentTypeService>();
        PropertyType Text(string alias, int order) =>
            new(strings, dataTypes.First(item => item.EditorAlias == Constants.PropertyEditors.Aliases.TextBox), alias) { Name = alias, SortOrder = order };
        // A composition whose group of the same alias is the type's own; its property comes by its order within it.
        var shared = new ContentType(strings, Constants.System.Root) { Alias = "jfbShared", Name = "Shared" };
        shared.AddPropertyType(Text("subtitle", 2), "content", "Content");
        // A composition's tab sorted before the type's own, which Umbraco lists after them.
        shared.PropertyGroups.Add(new PropertyGroup(true) { Alias = "media", Name = "Media", Type = PropertyGroupType.Tab, SortOrder = 0 });
        shared.AddPropertyType(Text("picture", 0), "media", "Media");
        await types.CreateAsync(shared, Constants.Security.SuperUserKey);
        var tabbed = new ContentType(strings, Constants.System.Root) { Alias = "jfbTabbed", Name = "Tabbed" };
        tabbed.PropertyGroups.Add(new PropertyGroup(true) { Alias = "settings", Name = "Settings", Type = PropertyGroupType.Tab, SortOrder = 2 });
        tabbed.PropertyGroups.Add(new PropertyGroup(true) { Alias = "about", Name = "About", Type = PropertyGroupType.Tab, SortOrder = 1 });
        // A group in the second tab, sorted before the first tab's: tabs come first by their order.
        tabbed.AddPropertyType(Text("metaTitle", 0), "settings/seo", "SEO");
        tabbed.AddPropertyType(Text("hidden", 0), "settings", "Settings");
        tabbed.AddPropertyType(Text("lede", 0), "about/intro", "Intro");
        tabbed.AddPropertyType(Text("title", 1), "content", "Content");
        tabbed.PropertyGroups["settings/seo"]!.SortOrder = 0;
        tabbed.PropertyGroups["content"]!.SortOrder = 5;
        tabbed.AddContentType(shared);
        await types.CreateAsync(tabbed, Constants.Security.SuperUserKey);

        var answer = await CallAsync("noAccess", HttpMethod.Get, "action-type/content/document-type/jfbTabbed");

        Assert.That(
            answer.Body!["properties"]!.AsArray().Select(property => (property!["alias"]!.GetValue<string>(), property["group"]?.GetValue<string>())),
            Is.EqualTo(new (string, string?)[]
            {
                ("title", "Content"),
                ("subtitle", "Content"),
                ("picture", "Media"),
                ("lede", "Intro"),
                ("hidden", "Settings"),
                ("metaTitle", "SEO"),
            }),
            answer.ToString());
    }

    [TestCase("nothere", TestName = "A document type the site lacks is not found")]
    [TestCase("jfbPart", TestName = "An element type is not found")]
    public async Task ADocumentTypeNoItemCanHave_IsNotFound(string alias)
    {
        await StoryTypeAsync();

        var answer = await CallAsync("noAccess", HttpMethod.Get, $"action-type/content/document-type/{alias}");

        Assert.That(answer.Outcome, Is.EqualTo("404 document_type_not_found"), answer.ToString());
    }

    [Test]
    public async Task AnEntrysRuns_AreListedForAUserWhoSeesItsEntries_ForThatFormOnly()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Failed);
        var other = await FormAsync("Other");

        var answer = await CallAsync(Admin, HttpMethod.Get, Path(form, entry));
        var elsewhere = await CallAsync(Admin, HttpMethod.Get, Path(other, entry));
        var nothing = await CallAsync(Admin, HttpMethod.Get, Path(form, Guid.NewGuid()));

        var first = answer.Body!.AsArray()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(first["id"]!.GetValue<long>(), Is.EqualTo(run.Id));
            Assert.That(first["state"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(first["trigger"]!.GetValue<string>(), Is.EqualTo("submitted"));
            Assert.That(first["actionType"]!.GetValue<string>(), Is.EqualTo(ScriptedActionType.Alias));
            Assert.That(first["errorCode"]!.GetValue<string>(), Is.EqualTo("refused"));
            Assert.That(first["errorMessage"]!.GetValue<string>(), Is.EqualTo("It said no."));
            Assert.That(first["attempts"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(elsewhere.Outcome, Is.EqualTo("404 action_run_not_found"), elsewhere.ToString());
            Assert.That(nothing.Outcome, Is.EqualTo("404 action_run_not_found"), nothing.ToString());
        });
    }

    [Test]
    public async Task ARetryOfAFailedRun_QueuesItAgain_WithItsAttemptsStartingOver_AndIsAudited()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Failed);

        var answer = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { });

        var stored = await Runs.GetAsync(run.Id);
        var audited = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = ActionRunRetries.RetriedAction }, PageRequest.First(5));
        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["state"]!.GetValue<string>(), Is.EqualTo("pending"));
            Assert.That(stored!.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(stored.Attempts, Is.Zero);
            Assert.That(audited.Items.Single().EntityKey, Is.EqualTo(form));
            Assert.That(audited.Items.Single().Details, Does.Contain($"\"run\":{run.Id}").And.Contain("\"uncertain\":false"));
        });
    }

    [Test]
    public async Task ARetryOfAnUncertainRun_IsRefusedWithoutItsConfirmation_AndGoesWithIt()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Uncertain);

        var without = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { });
        var saidNo = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { confirmed = false });
        var noBody = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry");
        var stillUncertain = await Runs.GetAsync(run.Id);
        var confirmed = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { confirmed = true });

        Assert.Multiple(async () =>
        {
            Assert.That(without.Outcome, Is.EqualTo("409 retry_needs_confirmation"), without.ToString());
            Assert.That(saidNo.Outcome, Is.EqualTo("409 retry_needs_confirmation"), saidNo.ToString());
            Assert.That(noBody.Outcome, Is.EqualTo("409 retry_needs_confirmation"), noBody.ToString());
            Assert.That(stillUncertain!.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(confirmed.Outcome, Is.EqualTo("200"), confirmed.ToString());
            Assert.That((await Runs.GetAsync(run.Id))!.State, Is.EqualTo(ActionRunState.Pending));
        });
    }

    [Test]
    public async Task ARetryOfARunThatDidNotFail_IsRefused_AndOneThatIsNotTheEntrysIsNotFound()
    {
        var (form, entry, run) = await RunInAsync(ActionRunState.Succeeded);
        var (_, otherEntry, otherRun) = await RunInAsync(ActionRunState.Failed);

        var succeeded = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{run.Id}/retry", new { confirmed = true });
        var notItsRun = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/{otherRun.Id}/retry", new { confirmed = true });
        var notItsForm = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, otherEntry)}/{otherRun.Id}/retry", new { confirmed = true });
        var noRun = await CallAsync(Admin, HttpMethod.Post, $"{Path(form, entry)}/999999/retry", new { confirmed = true });

        Assert.Multiple(() =>
        {
            Assert.That(succeeded.Outcome, Is.EqualTo("409 run_not_retryable"), succeeded.ToString());
            Assert.That(notItsRun.Outcome, Is.EqualTo("404 action_run_not_found"), notItsRun.ToString());
            Assert.That(notItsForm.Outcome, Is.EqualTo("404 action_run_not_found"), notItsForm.ToString());
            Assert.That(noRun.Outcome, Is.EqualTo("404 action_run_not_found"), noRun.ToString());
        });
    }

    [Test]
    public async Task ATestRun_ThatReachesWhatTheActionIsSetUpToReach_AsksFirst_AndChangesNothing()
    {
        var action = Scripted0("target-of-the-test");
        var form = await PublishedAsync(action);

        var without = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{action.Key}/test", new { });
        var seenWithout = Scripted.Tests.Count;
        var confirmed = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{action.Key}/test", new { confirmed = true });

        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var context = Scripted.Tests.Single();
        Assert.Multiple(async () =>
        {
            Assert.That(without.Outcome, Is.EqualTo("409 test_needs_confirmation"), without.ToString());
            Assert.That(seenWithout, Is.Zero);
            Assert.That(confirmed.Outcome, Is.EqualTo("200"), confirmed.ToString());
            Assert.That(confirmed.Body!["outcome"]!.GetValue<string>(), Is.EqualTo("sent"));
            Assert.That(confirmed.Body!["message"]!.GetValue<string>(), Is.EqualTo("Sent to target-of-the-test"));
            Assert.That(confirmed.Body!["preview"]!.AsArray().Select(line => (line!["text"]!.GetValue<string>(), line["code"]?.GetValue<string>(), line["args"]!.AsArray().Count)), Is.EqualTo(new[] { ("one line", (string?)null, 0) }), "a line of plain words");
            Assert.That(context.Action.Key, Is.EqualTo(action.Key));
            Assert.That(context.Scope.Entry!.Number, Is.EqualTo(SampleEntries.Number), "the sample entry previews show (ADR-0187)");
            Assert.That(context.User.Name, Is.Not.Empty);
            Assert.That(context.User.Email, Does.Contain("@"));
            Assert.That(await Entries.ListIdsAsync(row.Id, 5), Is.Empty, "nothing is stored");
        });
    }

    [Test]
    public async Task ATestRun_OfTheDraftTheDesignerHolds_RunsTheActionAsItHoldsIt_SavedOrNot()
    {
        var stored = Scripted0("stored");
        var form = await PublishedAsync(stored);
        var draft = (await CallAsync(Admin, HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!;
        var held = draft.DeepClone();
        var unsaved = Guid.NewGuid();
        held["actions"]![0]!["settings"] = JsonNode.Parse("""{"target":"changed"}""");
        held["actions"]!.AsArray().Add(JsonNode.Parse($$$"""{"key":"{{{unsaved}}}","type":"{{{ScriptedActionType.Alias}}}","name":"New","settings":{"target":"unsaved"}}"""));

        var changed = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{stored.Key}/test", new { confirmed = true, definition = held });
        var added = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{unsaved}/test", new { confirmed = true, definition = held });
        var notStored = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{unsaved}/test", new { confirmed = true });
        var unreadable = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{stored.Key}/test", new { confirmed = true, definition = new JsonArray() });
        held["actions"]![0]!["settings"] = JsonNode.Parse("""{"target":["not","a","text"]}""");
        var unfit = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{stored.Key}/test", new { confirmed = true, definition = held });
        var unfitUnasked = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{stored.Key}/test", new { definition = held });

        Assert.Multiple(() =>
        {
            Assert.That(changed.Outcome, Is.EqualTo("200"), changed.ToString());
            Assert.That(added.Outcome, Is.EqualTo("200"), added.ToString());
            Assert.That(Scripted.Tests.Select(test => test.Action.Settings!.Value.GetProperty("target").GetString()), Is.EqualTo(new[] { "changed", "unsaved" }), "as the designer holds them");
            Assert.That(notStored.Outcome, Is.EqualTo("404 action_not_found"), "the stored draft has no such action");
            Assert.That(unreadable.Status, Is.EqualTo(HttpStatusCode.BadRequest), unreadable.ToString());
            Assert.That(unfit.Outcome, Is.EqualTo("409 action_settings_invalid"), unfit.ToString());
            Assert.That(unfitUnasked.Outcome, Is.EqualTo("409 action_settings_invalid"), "nobody is asked to confirm a test that cannot run");
            Assert.That(Scripted.Tests, Has.Count.EqualTo(2), "nothing ran for settings its type cannot read");
            Assert.That(changed.Body!["code"], Is.Null, "a type without codes leaves the message");
            Assert.That(changed.Body!["recipient"], Is.Null);
        });
    }

    [Test]
    public async Task ATestRun_ThatThrows_FailsWithoutWhatItThrew()
    {
        var action = Scripted0("throws");
        var form = await PublishedAsync(action);

        var answer = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{action.Key}/test", new { confirmed = true });

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.ToString());
            Assert.That(answer.Body!["outcome"]!.GetValue<string>(), Is.EqualTo("failed"));
            Assert.That(answer.Body["code"]!.GetValue<string>(), Is.EqualTo(ActionTestCodes.Failed));
            Assert.That(answer.Body["message"]!.GetValue<string>(), Is.EqualTo(ActionTestRuns.FailedMessage), "never what it threw");
            Assert.That(answer.Body["recipient"], Is.Null, "an address only for a mail to who asks");
        });
    }

    [Test]
    public async Task AnEmailsTestRun_SaysItsCode_AndTheTestersAddress()
    {
        var mail = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = EmailActionType.Alias,
            Name = "Mail",
            Settings = SettingsObject.Parse("""{"to":["team@jfb.test"],"subject":{"":"S"},"body":{"":"<p>B</p>"}}"""),
        };
        var form = await PublishedAsync(mail);

        var answer = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{mail.Key}/test", new { });

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.ToString());
            Assert.That(answer.Body!["code"]!.GetValue<string>(), Is.EqualTo(ActionTestCodes.SentToYou));
            Assert.That(answer.Body["recipient"]!.GetValue<string>(), Does.StartWith("api-").And.EndWith("@jfb.test"), "the asking user's address");
        });
    }

    [Test]
    public async Task AnEmailsTestRun_SaysItsFirstLine_InTheTestersLanguage()
    {
        var mail = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = EmailActionType.Alias,
            Name = "Mail",
            Settings = SettingsObject.Parse("""{"to":["team@jfb.test"],"subject":{"":"S"},"body":{"":"<p>B</p>"}}"""),
        };
        var form = await PublishedAsync(mail);
        var english = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{mail.Key}/test", new { });
        var users = GetRequiredService<IUserService>();
        var admin = users.GetByEmail(english.Body!["recipient"]!.GetValue<string>())!;
        admin.Language = "de-DE";
        users.Save(admin);

        var german = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{mail.Key}/test", new { });

        Assert.Multiple(() =>
        {
            Assert.That(english.Body!["preview"]![1]!["text"]!.GetValue<string>(), Does.Contain("This is a test. The real email goes to team@jfb.test."), english.ToString());
            Assert.That(german.Body!["preview"]![1]!["text"]!.GetValue<string>(), Does.Contain("Dies ist ein Test. Die echte E-Mail geht an team@jfb.test."), german.ToString());
        });
    }

    [Test]
    public async Task ATestRun_OfAnActionTheDraftLacks_IsNotFound()
    {
        var form = await PublishedAsync(Scripted0("target"));

        var answer = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/action/{Guid.NewGuid()}/test", new { confirmed = true });

        Assert.That(answer.Outcome, Is.EqualTo("404 action_not_found"), answer.ToString());
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().ActionTypes().Append<ScriptedActionType>();
        builder.Services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(OutboxWorker));

        // A sender that can send, so an email's test goes into the queue (ADR-0190).
        builder.Services.AddUnique<IEmailSender, ScriptedEmailSender>();
    }

    private static string Path(Guid form, Guid entry) => $"form/{form}/entry/{entry}/action-run";

    // A document type an item may have, Story, with a title and a count in a group, a photo outside one and rich text, and
    // an element type, Part, which no item has.
    private async Task StoryTypeAsync()
    {
        var dataTypes = (await GetRequiredService<IDataTypeService>().GetAllAsync()).ToList();
        var strings = GetRequiredService<IShortStringHelper>();
        var types = GetRequiredService<IContentTypeService>();
        IDataType Of(string editor) => dataTypes.First(item => item.EditorAlias == editor);
        var story = new ContentType(strings, Constants.System.Root) { Alias = "jfbStory", Name = "Story", AllowedAsRoot = true };
        story.AddPropertyType(new PropertyType(strings, Of(Constants.PropertyEditors.Aliases.TextBox), "title") { Name = "Title" }, "content", "Content");
        story.AddPropertyType(new PropertyType(strings, Of(Constants.PropertyEditors.Aliases.Integer), "count") { Name = "Count" }, "content", "Content");
        story.AddPropertyType(new PropertyType(strings, Of(Constants.PropertyEditors.Aliases.RichText), "body") { Name = "Body" }, "content", "Content");
        story.AddPropertyType(new PropertyType(strings, Of(Constants.PropertyEditors.Aliases.MediaPicker3), "photo") { Name = "Photo" });
        await types.CreateAsync(story, Constants.Security.SuperUserKey);
        await types.CreateAsync(new ContentType(strings, Constants.System.Root) { Alias = "jfbPart", Name = "Part", IsElement = true }, Constants.Security.SuperUserKey);
    }

    private static FormAction Scripted0(string target) => new()
    {
        Key = Guid.NewGuid(),
        Type = ScriptedActionType.Alias,
        Name = "Scripted",
        Settings = SettingsObject.Parse($$"""{"target":"{{target}}"}"""),
    };

    private async Task<Guid> PublishedAsync(params FormAction[] actions)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Form {Guid.NewGuid():N}", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!) with { Actions = actions }, null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    // A form with an entry whose one run is in the state.
    private async Task<(Guid Form, Guid Entry, StoredActionRun Run)> RunInAsync(ActionRunState state)
    {
        var action = Scripted0("target");
        var form = await PublishedAsync(action);
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
        var run = (await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, ActionTrigger.Submitted, 0, false)]))!.Single();
        var token = (await Runs.ClaimAsync(run.Id, "server-a", TimeSpan.FromMinutes(5)))!.LockedBy!;
        var error = new DeliveryError("refused", "It said no.");
        _ = state switch
        {
            ActionRunState.Failed => await Runs.FailAsync(run.Id, token, error),
            ActionRunState.Uncertain => await Runs.CutOffAsync(run.Id, token, error),
            _ => await Runs.SucceedAsync(run.Id, token),
        };
        return (form, entry.Key, (await Runs.GetAsync(run.Id))!);
    }
}
