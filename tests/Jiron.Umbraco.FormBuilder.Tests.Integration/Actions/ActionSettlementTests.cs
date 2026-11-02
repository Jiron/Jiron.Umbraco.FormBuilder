using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>
/// What goes once an entry's actions are through (ROADMAP 8.1, section 4.4, ADR-0158): a value kept only until the actions have
/// run stays while a run is retrying or uncertain or a trigger can still fire, and goes with an entry event once every run is
/// final; a form that stores no entries lets the whole entry go the same way, its files too, at once when no action can fire, and
/// still spends a challenge once.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ActionSettlementTests : ActionTestBase
{
    private static readonly FormField Note = new()
    {
        Key = Guid.Parse("81000000-0000-4000-8000-000000000001"),
        Type = "textarea",
        Alias = "note",
        Label = new LocalizedText("Note"),
        Keep = FieldKeep.UntilActionsRun,
    };

    private static readonly FormField Upload = new()
    {
        Key = Guid.Parse("81000000-0000-4000-8000-000000000002"),
        Type = "file",
        Alias = "upload",
        Label = new LocalizedText("Upload"),
        Keep = FieldKeep.UntilActionsRun,
    };

    private const string WithNote = """{"name":"Ada Lovelace","note":"For the actions"}""";

    private ActionSettlement Settlement => GetRequiredService<ActionSettlement>();

    private MemoryFileStore Files => GetRequiredService<MemoryFileStore>();

    [Test]
    public async Task AKeptValue_StaysWhileARunRetries_AndGoesWithAnEvent_OnceEveryRunIsFinal()
    {
        var failing = true;
        var action = Action("flaky");
        var form = await PublishAsync(change: definition => definition with { Actions = [action] }, fields: [NameField, Note]);
        Scripted.Scripts["flaky"] = (_, _) => Task.FromResult(failing ? ActionResult.TryAgain("http_503") : ActionResult.Succeeded);
        var entry = await SubmitAsync(form, WithNote);

        await DrainAsync(entry.Id);
        var whileRetrying = await NoteOfAsync(entry);
        var searchWhileRetrying = (await Entries.GetByIdAsync(entry.Id))!.SearchText;
        failing = false;
        Clock.Advance(TimeSpan.FromMinutes(1));
        await DrainAsync(entry.Id);

        var timeline = await Events.ListForEntryAsync(entry.Id);
        var removed = timeline.Where(item => item.Kind == ActionSettlement.ValuesRemovedEvent).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(whileRetrying, Is.EqualTo("For the actions"));
            Assert.That(searchWhileRetrying, Does.Contain("for the actions"));
            Assert.That(await NoteOfAsync(entry), Is.Null);
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.SearchText, Is.EqualTo("ada lovelace"), "search no longer finds it, and finds the rest");
            Assert.That(await NameOfAsync(entry), Is.EqualTo("Ada Lovelace"), "the other answers stay");
            Assert.That(removed, Has.Count.EqualTo(1));
            Assert.That(removed[0].Details, Is.EqualTo($$"""{"fields":["{{Note.Key}}"]}"""), "the keys, and nothing typed");
        });
    }

    [Test]
    public async Task AKeptValuesGoing_LeavesTheOtherAnswersWholeInSearch()
    {
        var long1 = string.Join(' ', Enumerable.Repeat("word", 100)) + " findme";
        var message = new FormField { Key = Guid.NewGuid(), Type = "textarea", Alias = "message", Label = new LocalizedText("Message") };
        var form = await PublishAsync(change: definition => definition with { Actions = [Action("once")] }, fields: [NameField, message, Note]);
        var entry = await SubmitAsync(form, "{\"name\":\"Ada\",\"message\":\"" + long1 + "\",\"note\":\"For the actions\"}");

        await DrainAsync(entry.Id);

        var search = (await Entries.GetByIdAsync(entry.Id))!.SearchText;
        Assert.Multiple(() =>
        {
            Assert.That(search, Does.Contain("findme"), "not cut as a value's key is");
            Assert.That(search, Does.Not.Contain("for the actions"));
        });
    }

    [Test]
    public async Task AKeptValue_SeenByEachRun_UntilTheLastOne()
    {
        var seen = new List<string?>();
        var form = await PublishAsync(change: definition => definition with { Actions = [Action("first"), Action("second")] }, fields: [NameField, Note]);
        foreach (var name in new[] { "first", "second" })
        {
            Scripted.Scripts[name] = (context, _) =>
            {
                seen.Add(context.Scope.Answers.Find(Note.Key)?.Values[0].Text);
                return Task.FromResult(ActionResult.Succeeded);
            };
        }

        var entry = await SubmitAsync(form, WithNote);
        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(seen, Is.EqualTo(new[] { "For the actions", "For the actions" }));
            Assert.That(await NoteOfAsync(entry), Is.Null);
        });
    }

    [Test]
    public async Task AKeptValue_GoesAlsoWhenTheRunsFailedForGoodOrWereSkipped()
    {
        var form = await PublishAsync(change: definition => definition with { Actions = [Action("stopper", stopsTheRest: true), Action("rest")] }, fields: [NameField, Note]);
        Scripted.Scripts["stopper"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        var entry = await SubmitAsync(form, WithNote);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(runs.Select(run => run.State), Is.EqualTo(new[] { ActionRunState.Failed, ActionRunState.Skipped }));
            Assert.That(await NoteOfAsync(entry), Is.Null);
        });
    }

    [Test]
    public async Task AKeptValue_StaysWhileARunIsUncertain()
    {
        var form = await PublishAsync(change: definition => definition with { Actions = [Action("cut")] }, fields: [NameField, Note]);
        Scripted.Scripts["cut"] = (_, _) => Task.FromResult(ActionResult.Uncertain("no_answer"));
        var entry = await SubmitAsync(form, WithNote);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(runs.Single().State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(await NoteOfAsync(entry), Is.EqualTo("For the actions"), "a retry from the log may still need it");
        });
    }

    [Test]
    public async Task AKeptValue_StaysWhileAnotherRunIsUncertain_WhenTheLastOneFinishes()
    {
        var form = await PublishAsync(change: definition => definition with { Actions = [Action("cut"), Action("fine")] }, fields: [NameField, Note]);
        Scripted.Scripts["cut"] = (_, _) => Task.FromResult(ActionResult.Uncertain("no_answer"));
        var entry = await SubmitAsync(form, WithNote);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(runs.Select(run => run.State), Is.EqualTo(new[] { ActionRunState.Uncertain, ActionRunState.Succeeded }));
            Assert.That(await NoteOfAsync(entry), Is.EqualTo("For the actions"));
        });
    }

    [Test]
    public async Task AKeptValue_StaysWhileATriggerCanStillFire_AndGoesWhenNoneCan()
    {
        var form = await PublishAsync(
            change: definition => definition with
            {
                Actions = [Action("now"), Action("onApproval", ActionTrigger.Approved)],
                Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual } },
            },
            fields: [NameField, Note]);
        var entry = await SubmitAsync(form, WithNote);

        await DrainAsync(entry.Id);
        var waitingForApproval = await NoteOfAsync(entry);
        await Entries.ChangeStateAsync(entry.Id, EntryState.Submitted, EntryState.Approved, null);
        await Settlement.SettleAsync(entry.Id, await DefinitionOfAsync(entry));

        Assert.Multiple(async () =>
        {
            Assert.That(waitingForApproval, Is.EqualTo("For the actions"));
            Assert.That(await NoteOfAsync(entry), Is.Null);
        });
    }

    [TestCase("a form that waits for approval, with an approved action", ApprovalMode.Manual, ActionTrigger.Approved, nameof(EntryState.Submitted), true)]
    [TestCase("a form that waits for approval, with a rejected action", ApprovalMode.Manual, ActionTrigger.Rejected, nameof(EntryState.Submitted), true)]
    [TestCase("a form that waits for approval, with only a submitted action", ApprovalMode.Manual, ActionTrigger.Submitted, nameof(EntryState.Submitted), false)]
    [TestCase("approved at once, with an approved action", ApprovalMode.Automatic, ActionTrigger.Approved, nameof(EntryState.Submitted), true)]
    [TestCase("approved at once, with a rejected action", ApprovalMode.Automatic, ActionTrigger.Rejected, nameof(EntryState.Submitted), false)]
    [TestCase("an approved entry", ApprovalMode.Manual, ActionTrigger.Rejected, nameof(EntryState.Approved), false)]
    [TestCase("a rejected entry", ApprovalMode.Manual, ActionTrigger.Approved, nameof(EntryState.Rejected), false)]
    [TestCase("an unverified entry", ApprovalMode.Automatic, ActionTrigger.Submitted, nameof(EntryState.Unverified), true)]
    [TestCase("an entry in spam", ApprovalMode.Automatic, ActionTrigger.Submitted, nameof(EntryState.Spam), true)]
    public void ATrigger_CanStillFire_ForTheEntriesItCan(string what, ApprovalMode approval, ActionTrigger trigger, string state, bool can)
    {
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Actions = [Action("one", trigger)],
            Settings = new FormSettings { Submission = new FormSubmissionSettings { Approval = approval } },
        };

        Assert.That(ActionSettlement.TriggerCanFire(definition, Enum.Parse<EntryState>(state)), Is.EqualTo(can), what);
    }

    [Test]
    public void NoTrigger_FiresWhenTheActionsAreSwitchedOff()
    {
        var definition = new FormDefinition { Key = Guid.NewGuid(), Actions = [Action("off") with { Enabled = false }] };

        Assert.Multiple(() =>
        {
            Assert.That(ActionSettlement.TriggerCanFire(definition, EntryState.Unverified), Is.False);
            Assert.That(ActionSettlement.TriggerCanFire(definition, EntryState.Spam), Is.False);
        });
    }

    [Test]
    public async Task AKeptFile_GoesWithItsValue_ItsRowAndItsBytes()
    {
        var form = await PublishAsync(change: definition => definition with { Actions = [Action("once")] }, fields: [NameField, Upload]);
        var (entry, stored) = await EntryWithFileAsync(form);

        var (_, _) = (await DrainAsync(entry.Id), 0);

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.ListFilesAsync(entry.Id), Is.Empty);
            Assert.That(Files.Ids, Does.Not.Contain(stored));
            Assert.That(await Entries.ListValuesAsync([entry.Id]), Is.Empty);
        });
    }

    [Test]
    public async Task AFormThatStoresNoEntries_LetsTheEntryGoAtOnce_WhenNoActionCanFire_AndStillAnswers()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));

        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada"}}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var body = await JsonAsync(answer);
        var row = (await FormRows.GetAsync(form))!;

        Assert.Multiple(async () =>
        {
            Assert.That(Succeeded(answer), Is.True, body.ToJsonString());
            Assert.That(body["receipt"]?["reference"]?.GetValue<string>(), Does.EndWith("-00001"));
            Assert.That(body["outcome"], Is.Not.Null);
            Assert.That(await Entries.ListIdsAsync(row.Id, 10), Is.Empty);
        });
    }

    [Test]
    public async Task AFormThatStoresNoEntries_KeepsTheEntryUntilItsRunsAreFinal_ThenLetsItGoWithItsFilesAndUnsentMail()
    {
        var failing = true;
        var form = await PublishAsync(change: definition => Stored(EntryStorage.Never)(definition) with { Actions = [Action("flaky")] }, fields: [NameField, Upload]);
        Scripted.Scripts["flaky"] = (_, _) => Task.FromResult(failing ? ActionResult.TryAgain("http_503") : ActionResult.Succeeded);
        var (entry, stored) = await EntryWithFileAsync(form);
        await GetRequiredService<IEmailRepository>().AddAsync(new NewEmail(EmailKind.Action, "team@example.com", "site@example.com", "Hello", "Hello", "en-US", EntryId: entry.Id));

        await DrainAsync(entry.Id);
        var whileRetrying = await Entries.GetByIdAsync(entry.Id);
        var fileWhileRetrying = Files.Ids.Contains(stored);
        failing = false;
        Clock.Advance(TimeSpan.FromMinutes(1));
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(whileRetrying, Is.Not.Null);
            Assert.That(fileWhileRetrying, Is.True);
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
            Assert.That(Files.Ids, Does.Not.Contain(stored));
            Assert.That(await Runs.ListForEntryAsync(entry.Id), Is.Empty);
            Assert.That(await GetRequiredService<IEmailRepository>().DueAsync(10), Is.Empty, "its queued mail goes with it");
        });
    }

    [Test]
    public async Task AFormThatStoresNoEntries_KeepsAnEntryInSpam_UntilAPersonDecides()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));
        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var row = (await FormRows.GetAsync(form))!;
        var id = (await Entries.ListIdsAsync(row.Id, 10)).Single();
        var inSpam = (await Entries.GetByIdAsync(id))!;

        await GetRequiredService<EntrySpamFolder>().MarkAsNotSpamAsync(inSpam.Key, null);

        Assert.Multiple(async () =>
        {
            Assert.That(inSpam.State, Is.EqualTo(EntryState.Spam), "no lead is lost");
            Assert.That(await Entries.GetByIdAsync(id), Is.Null, "no action can run for it, so it goes once it is no spam");
        });
    }

    [Test]
    public async Task ByAUser_TakenOutOfSpam_WaitsOutTheUndoWindow()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));
        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var row = (await FormRows.GetAsync(form))!;
        var id = (await Entries.ListIdsAsync(row.Id, 10)).Single();

        await GetRequiredService<EntrySpamFolder>().MarkAsNotSpamAsync((await Entries.GetByIdAsync(id))!.Key, Editor);

        Assert.That(await Entries.GetByIdAsync(id), Is.Not.Null, "Undo still has something to restore");
    }

    [Test]
    public async Task AnEntryLeftBehind_OfAFormThatStoresNone_IsFoundByRetention()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));
        var kept = await PublishAsync("Kept");
        async Task<StoredEntry> LeftBehindAsync(Guid key, EntryState state)
        {
            var row = (await FormRows.GetAsync(key))!;
            return (await Entries.CreateAsync(new NewEntry
            {
                Key = Guid.NewGuid(),
                FormId = row.Id,
                FormVersionId = row.PublishedVersionId!.Value,
                State = state,
                SubmissionId = Guid.NewGuid(),
                SubmissionHash = new string('a', 64),
                Culture = "en-US",
            }))!;
        }

        var leftBehind = await LeftBehindAsync(form, EntryState.Submitted);
        var inSpam = await LeftBehindAsync(form, EntryState.Spam);
        var ofAnotherForm = await LeftBehindAsync(kept, EntryState.Submitted);
        var part = GetRequiredService<IEnumerable<Core.Retention.IFormBuilderRetentionPart>>().OfType<NeverStoredEntryCleanup>().Single();

        await part.RunAsync(Clock.GetUtcNow() + TimeSpan.FromSeconds(8), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.GetByIdAsync(leftBehind.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(inSpam.Id), Is.Not.Null);
            Assert.That(await Entries.GetByIdAsync(ofAnotherForm.Id), Is.Not.Null);
        });
    }

    [Test]
    public async Task ARequestThatEndsBeforeItsOutcome_StillLetsTheEntryGo()
    {
        var outcome = new FormOutcome { Key = Guid.NewGuid(), Kind = OutcomeKind.Message, Message = new LocalizedHtml("<p>{{ waits.forever }}</p>") };
        var form = await PublishAsync(change: definition => Stored(EntryStorage.Never)(definition) with { Outcomes = [outcome] });
        var row = (await FormRows.GetAsync(form))!;
        var challenge = await ChallengeAsync(form);
        var source = GetRequiredService<FormBuilderMergeTagSourceCollection>().OfType<WaitingSource>().Single();
        using var ending = new CancellationTokenSource();
        using var content = new StringContent("{\"revision\":1,\"challenge\":\"" + challenge + "\",\"values\":{\"name\":\"Ada\"}}", Encoding.UTF8, "application/json");

        var request = Client.PostAsync($"{Root}/forms/{form}/entries", content, ending.Token);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var whileRendering = await Entries.ListIdsAsync(row.Id, 10);
        await ending.CancelAsync();
        Assert.That(async () => await request, Throws.InstanceOf<OperationCanceledException>());
        IReadOnlyList<long> after = whileRendering;
        for (var i = 0; i < 200 && after.Count > 0; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25));
            after = await Entries.ListIdsAsync(row.Id, 10);
        }

        Assert.Multiple(() =>
        {
            Assert.That(whileRendering, Has.Count.EqualTo(1), "it was stored");
            Assert.That(after, Is.Empty);
        });
    }

    [Test]
    public async Task AFormThatStoresEntries_KeepsTheEntry_WithoutAnyAction()
    {
        var form = await PublishAsync();
        var entry = await SubmitAsync(form);

        await Settlement.SettleAsync(entry.Id, await DefinitionOfAsync(entry));

        Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Not.Null);
    }

    [Test]
    public async Task TakenOutOfSpam_AKeptValueGoes_WhenNoActionCanRunForTheEntry()
    {
        var action = Action("onlyForGrace") with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Run, Conditions = [new RuleCondition { Field = NameField.Key, Operator = "equals", Value = new ConditionOperand.Text("Grace") }] }],
        };
        var form = await PublishAsync(change: definition => definition with { Actions = [action] }, fields: [NameField, Note]);
        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada","note":"For the actions"},"honeypot":"Buy now"}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var row = (await FormRows.GetAsync(form))!;
        var entry = (await Entries.GetByIdAsync((await Entries.ListIdsAsync(row.Id, 10)).Single()))!;
        var inSpam = await NoteOfAsync(entry);

        await GetRequiredService<EntrySpamFolder>().MarkAsNotSpamAsync(entry.Key, null);

        Assert.Multiple(async () =>
        {
            Assert.That(inSpam, Is.EqualTo("For the actions"), "marking it as no spam may still run an action");
            Assert.That(await Runs.ListForEntryAsync(entry.Id), Is.Empty);
            Assert.That(await NoteOfAsync(entry), Is.Null);
        });
    }

    [Test]
    public async Task AFormThatStoresNoEntries_SpendsAChallengeOnce_ThoughItsEntryIsGone()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));
        var challenge = await ChallengeAsync(form);
        async Task<string> PostAsync()
        {
            using var content = new StringContent("{\"revision\":1,\"challenge\":\"" + challenge + "\",\"values\":{\"name\":\"Ada\"}}", Encoding.UTF8, "application/json");
            using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
            return Succeeded(answer) ? "accepted" : await RefusalAsync(answer);
        }

        var first = await PostAsync();
        var second = await PostAsync();
        var fresh = await ChallengeAsync(form);
        challenge = fresh;
        var third = await PostAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("accepted"));
            Assert.That(second, Is.EqualTo("401 challenge_required"));
            Assert.That(third, Is.EqualTo("accepted"));
        });
    }

    [Test]
    public async Task AUsedChallenge_StaysSpent_WhenTheFormIsPublishedToStoreEntries()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));
        var challenge = await ChallengeAsync(form);
        async Task<string> PostAsync()
        {
            using var content = new StringContent("{\"revision\":1,\"challenge\":\"" + challenge + "\",\"values\":{\"name\":\"Ada\"}}", Encoding.UTF8, "application/json");
            using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
            return Succeeded(answer) ? "accepted" : await RefusalAsync(answer);
        }

        var first = await PostAsync();
        await SaveAndPublishAsync(form, Stored(EntryStorage.Always));
        var again = await PostAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("accepted"));
            Assert.That(again, Is.EqualTo("401 challenge_required"));
        });
    }

    [Test]
    public async Task AUsedChallenge_IsForgotten_OnceItsTimeHasPassed()
    {
        var form = await PublishAsync(change: Stored(EntryStorage.Never));
        var challenge = await ChallengeAsync(form);
        using var content = new StringContent("{\"revision\":1,\"challenge\":\"" + challenge + "\",\"values\":{\"name\":\"Ada\"}}", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var keys = GetRequiredService<IKeyValueRepository>();
        var before = await keys.ListAsync("jfb.usedchallenge.");

        var early = await Settlement.ForgetUsedChallengesAsync(CancellationToken.None);
        Clock.Advance(FormChallenges.Lifetime + FormChallenges.ReceiptGrace + TimeSpan.FromMinutes(1));
        var late = await Settlement.ForgetUsedChallengesAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(before, Has.Count.EqualTo(1));
            Assert.That(early, Is.Zero);
            Assert.That(late, Is.EqualTo(1));
            Assert.That(await keys.ListAsync("jfb.usedchallenge."), Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.JironFormBuilder().MergeTagSources().Append<WaitingSource>();
    }

    private static Func<FormDefinition, FormDefinition> Stored(EntryStorage storage) => definition =>
        definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Store = storage } } };

    private async Task<FormDefinition> DefinitionOfAsync(StoredEntry entry) =>
        GetRequiredService<Core.Forms.FormDocuments>().Readable((await FormRows.GetVersionByIdAsync(entry.FormVersionId))!);

    private Task<string?> NoteOfAsync(StoredEntry entry) => TextOfAsync(entry, Note.Key);

    private Task<string?> NameOfAsync(StoredEntry entry) => TextOfAsync(entry, NameField.Key);

    private async Task<string?> TextOfAsync(StoredEntry entry, Guid field)
    {
        var row = (await FormRows.ListFieldsAsync(entry.FormId)).Single(item => item.Values.FieldKey == field);
        return (await Entries.ListValuesAsync([entry.Id])).FirstOrDefault(value => value.FieldId == row.Id)?.Text;
    }

    // An entry with a kept file, stored as a submission stores it, and its run queued.
    private async Task<(StoredEntry Entry, string Stored)> EntryWithFileAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        var id = FormBuilderFileId.New();
        await Files.SaveAsync(id, new MemoryStream("%PDF-bytes"u8.ToArray()));
        var file = (await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), row.Id, Upload.Key, "report.pdf", id, "application/pdf", 9, new string('a', 64), FileScanStates.Unscanned)))!;
        var field = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == Upload.Key);
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [file.Id],
            Values = [new NewEntryValue(field.Id, "report.pdf", FileId: file.Id)],
        }))!;
        var definition = await DefinitionOfAsync(entry);
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), SubmissionEntries.Runs(definition, [.. definition.Actions.Select(action => DefinitionLogic.Id(action.Key))]));
        return (entry, id);
    }

    /// <summary>A merge-tag source that holds the outcome up until the request ends.</summary>
    public sealed class WaitingSource : IFormBuilderMergeTagSource
    {
        /// <summary>Set when an outcome asks for it.</summary>
        public TaskCompletionSource Entered { get; } = new();

        /// <inheritdoc />
        public string Name => "waits";

        /// <inheritdoc />
        public bool IsPerVisitor => false;

        /// <inheritdoc />
        public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
