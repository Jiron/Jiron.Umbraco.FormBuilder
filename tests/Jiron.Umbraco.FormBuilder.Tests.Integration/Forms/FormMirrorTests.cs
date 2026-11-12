using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// Forms as files (D25, ADR-0220) in a folder of their own under the temporary folder: a publish writes the form's file, a
/// pass takes a file changed in source control in as a new published version with its keys and leaves the file as the
/// developer wrote it, a reformatted file is no change, and the next publish writes the file again. A file and a form that
/// both changed keep the form and the file, which the health check names until the file is taken in or written again; a
/// file of a form the site never had creates it; a stale copy is written again; a file that is no form is named; with the
/// mirror off nothing is written. The health check names a file it could not write and a change publishing refused, and
/// its action writes the files of the forms in conflict; the job runs the pass only with the mirror on and no migration
/// pending, and a pass that fails is logged. Replacing a draft with a file keeps the file's keys and the form's own name.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormMirrorTests : RepositoryTestBase
{
    private static readonly FormField Email = new() { Key = Key(1), Type = "email", Alias = "email", Label = new LocalizedText("Email address") };

    private static readonly FormField Name = new() { Key = Key(2), Type = "text", Alias = "name", Label = new LocalizedText("Name") };

    private static readonly HealthWords English = HealthWords.Of("en-US");

    private readonly PendingSwitch _pending = new();

    private string _folder = null!;

    private FormMirror Mirror => GetRequiredService<FormMirror>();

    private MirrorHealth Health => GetRequiredService<MirrorHealth>();

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private IFormBuilderFormExchangeService Exchange => GetRequiredService<IFormBuilderFormExchangeService>();

    private FormDefinitionSerializer Serializer => GetRequiredService<FormDefinitionSerializer>();

    private FormBuilderDefinitionsOptions Definitions => GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Definitions;

    private TestPublishCheck Check => GetRequiredService<TestPublishCheck>();

    [SetUp]
    public void TurnTheMirrorOn()
    {
        _folder = Path.Combine(Path.GetTempPath(), "jfb-mirror-" + Guid.NewGuid().ToString("N"));
        Definitions.Mirror = true;
        Definitions.Folder = _folder;
    }

    [TearDown]
    public void TurnTheMirrorOff()
    {
        Definitions.Mirror = false;
        Definitions.Folder = FormBuilderDefinitionsOptions.DefaultFolder;
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        if (File.Exists(_folder))
        {
            File.Delete(_folder);
        }
    }

    [Test]
    public async Task AFormWhoseAliasWindowsKeepsForADevice_IsWrittenWithAnUnderscore_AndReadBackAsItsOwn()
    {
        // "con", "nul" or "com1" with .json is a device on some Windows versions, so the mirror never writes such a name (16.1).
        var created = await Service.CreateAsync(new NewFormRequest("Con", Alias: "con", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var saved = (await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, WithFields(blank, Email, Name), null, Editor))!;
        var form = (await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor))!.Form;
        var path = Path.Combine(_folder, "_con.json");
        var written = File.Exists(path) ? FormExportFormat.Read(await File.ReadAllTextAsync(path), Serializer).Envelope : null;
        var before = await PublishedVersionAsync(form);

        await Mirror.SyncAsync();
        var after = await PublishedVersionAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That(form.Alias, Is.EqualTo("con"));
            Assert.That(written?.FormKey, Is.EqualTo(form.Key));
            Assert.That(File.Exists(Path.Combine(_folder, "con.json")), Is.False);
            Assert.That(after, Is.EqualTo(before), "the pass reads it as the form's own file, unchanged");
            Assert.That(FormMirror.OnDiskName("contact.json"), Is.EqualTo("contact.json"));
            Assert.That(FormMirror.OnDiskName("LPT9.json"), Is.EqualTo("_LPT9.json"));
        });
    }

    [Test]
    public async Task APublish_WritesItsFile_AndAFileChangedInSourceControl_IsPublishedAsANewVersion_WithItsKeys()
    {
        var form = await PublishedAsync();
        var written = FormExportFormat.Read(await File.ReadAllTextAsync(FileOf(form)), Serializer).Envelope;
        var first = await RecordAsync(form);

        await Mirror.SyncAsync();
        var unchanged = await PublishedVersionAsync(form);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var json = await ChangeFileAsync(form, email: "Your email");
        await Mirror.SyncAsync();
        var taken = await PublishedVersionAsync(form);
        var live = await LiveAsync(form);
        var comment = (await Forms.ListVersionsAsync((await Forms.GetAsync(form.Key))!.Id, FormVersionKind.Published, PageRequest.First(1))).Items[0].Comment;
        var audit = (await Audit.QueryAsync(new AuditQuery { EntityKey = form.Key }, PageRequest.First(20))).Items;

        Assert.Multiple(async () =>
        {
            Assert.That(written?.FormKey, Is.EqualTo(form.Key), "the publish wrote the form's file, named by its alias");
            Assert.That(first, Is.EqualTo(new MirrorRecord(first!.Hash, 3, "contact.json")), "and remembered it");
            Assert.That(unchanged, Is.EqualTo(3), "a pass over an unchanged file changes nothing");
            Assert.That(taken, Is.EqualTo(5), "the changed file went into the draft (4) and was published (5)");
            Assert.That(Labels(live), Is.EqualTo(new[] { "Your email", "Name" }));
            Assert.That(DefinitionKeys.Fields(live).Select(field => field.Key), Is.EqualTo(new[] { Email.Key, Name.Key }), "with the file's keys");
            Assert.That(comment, Is.EqualTo(FormMirror.FromFileComment));
            Assert.That(await File.ReadAllTextAsync(FileOf(form)), Is.EqualTo(json), "the file stays as the developer wrote it");
            Assert.That((await RecordAsync(form))?.Version, Is.EqualTo(5));
            Assert.That(audit.Where(entry => entry.UserKey is null).Select(entry => entry.Action), Is.EquivalentTo(new[] { FormBuilderFormExchangeService.ReplacedAction, "form.published" }), "Jiron Form Builder's own work");
        });

        await Mirror.SyncAsync();
        await File.WriteAllTextAsync(FileOf(form), JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        await Mirror.SyncAsync();
        Assert.That(await PublishedVersionAsync(form), Is.EqualTo(5), "taken in once, and a reformatted file is no change");

        var next = await PublishChangeAsync(form, name: "Full name");
        Assert.Multiple(async () =>
        {
            Assert.That(next, Is.EqualTo(7));
            Assert.That(Labels((await ReadFileAsync(form)).Definition), Is.EqualTo(new[] { "Your email", "Full name" }), "the next publish writes the file again");
            Assert.That((await RecordAsync(form))?.Version, Is.EqualTo(7));
            Assert.That(await Health.FindAsync(English), Is.Empty);
            Assert.That(Health.Passed(English), Is.EqualTo(English.MirrorPassed));
        });
    }

    [Test]
    public async Task AFileAndAFormThatBothChanged_KeepTheFormAndTheFile_UntilTheHealthChecksActionWritesTheFile()
    {
        var form = await PublishedAsync();
        var json = await ChangeFileAsync(form, email: "Your email");

        await PublishChangeAsync(form, name: "Full name");
        var found = await Health.FindAsync(English);
        await Mirror.SyncAsync();
        var afterPass = await Health.FindAsync(English);
        var live = await LiveAsync(form);
        var fileAfterPass = await File.ReadAllTextAsync(FileOf(form));

        Assert.Multiple(async () =>
        {
            Assert.That(found.Select(finding => (finding.Code, finding.Message)), Is.EqualTo(new[] { ("mirrorConflict", English.MirrorConflict("contact.json")) }), "the publish left the developer's file and named the conflict");
            Assert.That(afterPass.Select(finding => finding.Code), Is.EqualTo(new[] { "mirrorConflict" }), "the pass keeps the form");
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(5));
            Assert.That(Labels(live), Is.EqualTo(new[] { "Email address", "Full name" }), "the database won");
            Assert.That(fileAfterPass, Is.EqualTo(json), "and the file stays as it is");
            Assert.That(await Health.HasConflictAsync(), Is.True, "the health check offers its action");
        });

        var written = await Health.KeepTheFormsAsync();
        await Mirror.SyncAsync();
        Assert.Multiple(async () =>
        {
            Assert.That(written, Is.EqualTo(1));
            Assert.That(Labels((await ReadFileAsync(form)).Definition), Is.EqualTo(new[] { "Email address", "Full name" }), "the file holds the form");
            Assert.That(await Health.FindAsync(English), Is.Empty);
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(5), "and nothing was taken in");
        });
    }

    [Test]
    public async Task AFileChangedWhileTheFormWasPublishedElsewhere_IsAConflictOnThePass_UntilTheFileIsTakenIn()
    {
        var form = await PublishedAsync();
        Definitions.Mirror = false;
        await PublishChangeAsync(form, name: "Full name");
        Definitions.Mirror = true;
        var json = await ChangeFileAsync(form, email: "Your email");

        await Mirror.SyncAsync();
        var found = await Health.FindAsync(English);
        var stayed = await PublishedVersionAsync(form);

        var draft = (await Service.GetDraftAsync(form.Key))!;
        var replaced = (await Exchange.ReplaceAsync(new ReplaceFormRequest(form.Key, json, draft.Form.ConcurrencyVersion, Editor)))!;
        await Service.PublishAsync(form.Key, replaced.Result.Form.ConcurrencyVersion, null, Editor);
        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(found.Select(finding => finding.Code), Is.EqualTo(new[] { "mirrorConflict" }));
            Assert.That(stayed, Is.EqualTo(5), "the pass kept the form");
            Assert.That(Labels(await LiveAsync(form)), Is.EqualTo(new[] { "Your email", "Name" }), "the file, taken in by hand and published");
            Assert.That(await File.ReadAllTextAsync(FileOf(form)), Is.EqualTo(json), "which held it already, so it stays as it was written");
            Assert.That(await Health.FindAsync(English), Is.Empty, "the conflict is settled");
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(7));
        });
    }

    [Test]
    public async Task AFileOfAFormTheSiteNeverHad_CreatesIt_WithTheFilesKeys_AndPublishesIt_Once()
    {
        var source = await PublishedAsync();
        var envelope = await ReadFileAsync(source);
        var key = Guid.Parse("76000000-0000-4000-8000-000000000099");
        await File.WriteAllTextAsync(
            Path.Combine(_folder, "newsletter.json"),
            FormExportFormat.Write(envelope with { FormKey = key, Alias = "newsletter", Name = "Newsletter", Definition = envelope.Definition with { Key = key } }));

        await Mirror.SyncAsync();
        await Mirror.SyncAsync();
        var created = (await Service.GetDraftAsync(key))!;

        Assert.Multiple(async () =>
        {
            Assert.That((created.Form.Alias, created.Form.Name, created.Form.IsPublished, created.PublishedVersion), Is.EqualTo(("newsletter", "Newsletter", true, (int?)2)), "created as version 1 and published as 2, once");
            Assert.That(created.Definition!.Key, Is.EqualTo(key), "the key the file names");
            Assert.That(DefinitionKeys.Fields(created.Definition).Select(field => field.Key), Is.EqualTo(new[] { Email.Key, Name.Key }), "the file's keys");
            Assert.That((await RecordAsync(created.Form))?.File, Is.EqualTo("newsletter.json"));
            Assert.That(await Health.FindAsync(English), Is.Empty);
        });
    }

    [Test]
    public async Task AStaleCopy_IsWrittenAgain_AndNeverTakenIn()
    {
        var form = await PublishedAsync();
        var stale = await File.ReadAllTextAsync(FileOf(form));
        await PublishChangeAsync(form, name: "Full name");
        await File.WriteAllTextAsync(FileOf(form), stale);

        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(5), "the older version was not taken in");
            Assert.That(Labels((await ReadFileAsync(form)).Definition), Is.EqualTo(new[] { "Email address", "Full name" }), "the file holds the live version again");
            Assert.That(await Health.FindAsync(English), Is.Empty);
        });
    }

    [Test]
    public async Task APublishTheMirrorMissed_IsWrittenOnTheNextPass()
    {
        var form = await PublishedAsync();
        Definitions.Mirror = false;
        await PublishChangeAsync(form, name: "Full name");
        Definitions.Mirror = true;

        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(Labels((await ReadFileAsync(form)).Definition), Is.EqualTo(new[] { "Email address", "Full name" }), "the file holds the live version");
            Assert.That((await RecordAsync(form))?.Version, Is.EqualTo(5));
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(5), "and nothing was taken in");
            Assert.That(await Health.FindAsync(English), Is.Empty);
        });
    }

    [Test]
    public async Task AFileThatIsNoForm_IsNamedByTheHealthCheck_UntilItIsGone_AndAFormWithoutAFileGetsOne()
    {
        var form = await PublishedAsync();
        File.Delete(FileOf(form));
        await File.WriteAllTextAsync(Path.Combine(_folder, "Notes.json"), """{"not":"a form"}""");

        await Mirror.SyncAsync();
        var found = await Health.FindAsync(English);
        var rewritten = File.Exists(FileOf(form));
        File.Delete(Path.Combine(_folder, "Notes.json"));
        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(found.Select(finding => (finding.Code, finding.Message)), Is.EqualTo(new[] { ("mirrorUnreadable", English.MirrorUnreadable("Notes.json")) }));
            Assert.That(rewritten, Is.True, "a published form without a file gets its file");
            Assert.That(await Health.FindAsync(English), Is.Empty, "a file that is gone is no problem");
        });
    }

    [Test]
    public async Task WithTheMirrorOff_APublishWritesNoFile_AndThePassDoesNothing()
    {
        Definitions.Mirror = false;
        var form = await PublishedAsync();

        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(Directory.Exists(_folder), Is.False);
            Assert.That(await RecordAsync(form), Is.Null);
            Assert.That(await Health.FindAsync(English), Is.Empty);
            Assert.That(Health.Passed(English), Is.EqualTo(English.MirrorOff));
        });
    }

    [Test]
    public async Task TheHealthCheck_NamesAConflict_AndItsActionWritesTheFile_SoTheSitesFormWins()
    {
        var check = GetRequiredService<HealthCheckCollection>().OfType<MirrorHealthCheck>().Single();
        var form = await PublishedAsync();
        await ChangeFileAsync(form, email: "Your email");
        await PublishChangeAsync(form, name: "Full name");

        var conflict = (await check.GetStatusAsync()).ToList();
        var done = await check.ExecuteActionAsync(conflict.Single().Actions.Single());
        var after = (await check.GetStatusAsync()).Single();

        Assert.Multiple(async () =>
        {
            Assert.That(conflict.Select(status => (status.ResultType, status.Message)), Is.EqualTo(new[] { (StatusResultType.Warning, English.MirrorConflict("contact.json")) }));
            Assert.That(conflict.Single().Actions.Single().Alias, Is.EqualTo(MirrorHealthCheck.KeepAction));
            Assert.That((done.ResultType, done.Message), Is.EqualTo((StatusResultType.Success, English.MirrorKept(1))));
            Assert.That(Labels((await ReadFileAsync(form)).Definition), Is.EqualTo(new[] { "Email address", "Full name" }), "the file holds the form");
            Assert.That((after.ResultType, after.Message, after.Actions.Count()), Is.EqualTo((StatusResultType.Success, English.MirrorPassed, 0)));
        });
    }

    [Test]
    public async Task AFileThatCannotBeWritten_IsNamedByTheHealthCheck_UntilAPassWritesIt()
    {
        // A file where the folder should be: no file can go in it.
        await File.WriteAllTextAsync(_folder, "not a folder");
        var form = await PublishedAsync();
        var found = await Health.FindAsync(English);
        File.Delete(_folder);
        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(found.Select(finding => (finding.Code, finding.Message)), Is.EqualTo(new[] { ("mirrorUnwritable", English.MirrorUnwritable("contact.json")) }), "the publish went through and said so");
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(3));
            Assert.That(File.Exists(FileOf(form)), Is.True, "the next pass wrote it");
            Assert.That(await Health.FindAsync(English), Is.Empty);
        });
    }

    [Test]
    public async Task AChangedFileThatPublishingRefuses_StaysInTheDraft_AndIsNamedByTheHealthCheck_UntilItPublishes()
    {
        var form = await PublishedAsync();
        await ChangeFileAsync(form, email: "Your email");
        Check.Problems = [new DefinitionProblem("jfb_test", "$", "Not now.")];
        await Mirror.SyncAsync();
        var found = await Health.FindAsync(English);
        var draft = (await Service.GetDraftAsync(form.Key))!;
        Check.Problems = [];
        await Mirror.SyncAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(found.Select(finding => (finding.Code, finding.Message)), Is.EqualTo(new[] { ("mirrorNotPublished", English.MirrorNotPublished("contact.json")) }));
            Assert.That((draft.DraftVersion, draft.PublishedVersion), Is.EqualTo((4, (int?)3)), "the file is the draft, the live version stays");
            Assert.That(await PublishedVersionAsync(form), Is.EqualTo(5), "the next pass published it");
            Assert.That(Labels(await LiveAsync(form)), Is.EqualTo(new[] { "Your email", "Name" }));
            Assert.That(await Health.FindAsync(English), Is.Empty);
        });
    }

    [Test]
    public async Task TheJob_RunsThePass_OnlyWithTheMirrorOn_AndNoMigrationPending_AndLogsAPassThatFails()
    {
        var job = GetRequiredService<IEnumerable<IRecurringBackgroundJob>>().OfType<FormMirrorJob>().Single();
        var form = await PublishedAsync();
        File.Delete(FileOf(form));

        Definitions.Mirror = false;
        await job.RunJobAsync();
        var whileOff = File.Exists(FileOf(form));
        Definitions.Mirror = true;
        _pending.Pending = true;
        await job.RunJobAsync();
        var whilePending = File.Exists(FileOf(form));
        var asked = _pending.Asked;
        _pending.Pending = false;
        await job.RunJobAsync();
        var written = File.Exists(FileOf(form));
        Directory.Delete(_folder, recursive: true);
        await File.WriteAllTextAsync(_folder, "not a folder");

        Assert.Multiple(() =>
        {
            Assert.That((whileOff, whilePending), Is.EqualTo((false, false)), "nothing while the mirror is off or a migration is pending");
            Assert.That(asked, Is.GreaterThan(0), "the job asked");
            Assert.That(written, Is.True, "the pass wrote the form's file");
            Assert.That(job.ServerRoles, Is.EquivalentTo(new[] { ServerRole.Single, ServerRole.SchedulingPublisher }));
            Assert.DoesNotThrowAsync(job.RunJobAsync, "a pass that fails is logged, and the timer goes on");
        });
    }

    [Test]
    public async Task AReplace_MakesTheFileTheDraft_WithItsKeys_AndKeepsTheFormsNameAndItsLiveVersion()
    {
        Definitions.Mirror = false;
        var form = await PublishedAsync();
        var export = (await Exchange.ExportAsync(form.Key, 3))!;
        var envelope = FormExportFormat.Read(export.Json, Serializer).Envelope!;
        var phone = new FormField { Key = Key(3), Type = "text", Alias = "phone", Label = new LocalizedText("Phone") };
        var file = FormExportFormat.Write(envelope with { Alias = "booking", Name = "Booking", Definition = WithFields(envelope.Definition, Email, phone) });
        var draft = (await Service.GetDraftAsync(form.Key))!;

        var replaced = (await Exchange.ReplaceAsync(new ReplaceFormRequest(form.Key, file, draft.Form.ConcurrencyVersion, Editor)))!;
        var after = (await Service.GetDraftAsync(form.Key))!;
        var again = (await Exchange.ReplaceAsync(new ReplaceFormRequest(form.Key, file, replaced.Result.Form.ConcurrencyVersion, Editor)))!;
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = form.Key }, PageRequest.First(1))).Items[0];

        Assert.Multiple(async () =>
        {
            Assert.That(replaced.Result.Version, Is.EqualTo(4), "a draft save");
            Assert.That(replaced.Notes, Is.Empty);
            Assert.That((after.Form.Name, after.Form.Alias, after.PublishedVersion), Is.EqualTo(("Contact", "contact", (int?)3)), "the form keeps its name, alias and live version");
            Assert.That(after.Definition!.Key, Is.EqualTo(form.Key));
            Assert.That(DefinitionKeys.Fields(after.Definition).Select(field => field.Key), Is.EqualTo(new[] { Email.Key, phone.Key }), "the file's keys");
            Assert.That(again.Result.Version, Is.Null, "a file that holds the draft as it is writes nothing");
            Assert.That((entry.Action, entry.UserKey, entry.Details), Is.EqualTo((FormBuilderFormExchangeService.ReplacedAction, (Guid?)Editor, $$"""{"version":4,"source":"{{form.Key}}","sourceAlias":"booking","packageVersion":"{{envelope.PackageVersion}}","notes":[],"noteCounts":[]}""")));
            Assert.ThrowsAsync<FormBuilderConcurrencyException>(async () => await Exchange.ReplaceAsync(new ReplaceFormRequest(form.Key, file, draft.Form.ConcurrencyVersion, Editor)), "a replace from a version read before is refused");
            Assert.That(await Exchange.ReplaceAsync(new ReplaceFormRequest(Guid.NewGuid(), file, 1, Editor)), Is.Null);
            AssertCode(async () => await Exchange.ReplaceAsync(new ReplaceFormRequest(form.Key, "{}", replaced.Result.Form.ConcurrencyVersion, Editor)), "missing_property");
        });
    }

    private static Guid Key(int number) => Guid.Parse($"76000000-0000-4000-8000-{number:D12}");

    private static string[] Labels(FormDefinition definition) => [.. DefinitionKeys.Fields(definition).Select(field => field.Label!.ToString())];

    private string FileOf(FormInfo form) => Path.Combine(_folder, form.Alias + ".json");

    // A form with an email and a name field, published: version 1 created it, 2 saved the fields, 3 published them.
    private async Task<FormInfo> PublishedAsync()
    {
        var created = await Service.CreateAsync(new NewFormRequest("Contact", UserKey: Editor));
        var blank = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var saved = (await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, WithFields(blank, Email, Name), null, Editor))!;
        return (await Service.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor))!.Form;
    }

    // An editor's change to the name field's label, saved and published; the published version's number.
    private async Task<int> PublishChangeAsync(FormInfo form, string name)
    {
        var draft = (await Service.GetDraftAsync(form.Key))!;
        var fields = DefinitionKeys.Fields(draft.Definition!).ToList();
        var changed = WithFields(draft.Definition!, [.. fields.Select(field => field.Key == Name.Key ? field with { Label = new LocalizedText(name) } : field)]);
        var saved = (await Service.SaveDraftAsync(form.Key, draft.Form.ConcurrencyVersion, changed, null, Editor))!;
        return (await Service.PublishAsync(form.Key, saved.Form.ConcurrencyVersion, null, Editor))!.Version!.Value;
    }

    // A developer's change to the email field's label in the file, written without indentation; the file's text.
    private async Task<string> ChangeFileAsync(FormInfo form, string email)
    {
        var envelope = await ReadFileAsync(form);
        var fields = DefinitionKeys.Fields(envelope.Definition).ToList();
        var changed = WithFields(envelope.Definition, [.. fields.Select(field => field.Key == Email.Key ? field with { Label = new LocalizedText(email) } : field)]);
        var json = JsonNode.Parse(FormExportFormat.Write(envelope with { Definition = changed }))!.ToJsonString();
        await File.WriteAllTextAsync(FileOf(form), json);
        return json;
    }

    private async Task<FormExportEnvelope> ReadFileAsync(FormInfo form) =>
        FormExportFormat.Read(await File.ReadAllTextAsync(FileOf(form)), Serializer).Envelope!;

    private async Task<int?> PublishedVersionAsync(FormInfo form) => (await Service.GetDraftAsync(form.Key))!.PublishedVersion;

    private async Task<FormDefinition> LiveAsync(FormInfo form)
    {
        var export = (await Exchange.ExportAsync(form.Key, await PublishedVersionAsync(form)))!;
        return FormExportFormat.Read(export.Json, Serializer).Envelope!.Definition;
    }

    private async Task<MirrorRecord?> RecordAsync(FormInfo form) =>
        await KeyValues.GetAsync(FormMirror.RecordPrefix + form.Key.ToString("N")) is { } text ? MirrorRecord.Read(text) : null;

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<TestPublishCheck>();
        builder.Services.AddSingleton<IFormPublishCheck>(provider => provider.GetRequiredService<TestPublishCheck>());
        builder.Services.AddUnique<IFormBuilderMigrationState>(_pending);
    }

    // Whether a migration is pending, as the test sets it, and how often a job asked.
    private sealed class PendingSwitch : IFormBuilderMigrationState
    {
        private int _asked;

        public bool Pending { get; set; }

        public int Asked => Volatile.Read(ref _asked);

        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            return ValueTask.FromResult(Pending);
        }

        public void Invalidate()
        {
        }
    }
}
