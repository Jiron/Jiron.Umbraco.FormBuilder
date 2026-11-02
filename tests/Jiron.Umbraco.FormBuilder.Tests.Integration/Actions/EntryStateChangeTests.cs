using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>Changes of an entry's state, their triggers, their undo window and their Undo (ROADMAP 8.5, ADR-0023, ADR-0162).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryStateChangeTests : ActionTestBase
{
    private static readonly Guid User = Guid.Parse("71e00000-0000-4000-8000-000000000001");

    private EntryStateChanges Changes => GetRequiredService<EntryStateChanges>();

    private HeardEntries Heard => GetRequiredService<HeardEntries>();

    private static Func<FormDefinition, FormDefinition> Waits(params FormAction[] actions) => definition => definition with
    {
        Actions = actions,
        Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual } },
    };

    private Task<Guid> WaitingFormAsync(params FormAction[] actions) =>
        PublishAsync(name: Guid.NewGuid().ToString("N"), change: Waits(actions));

    [Test]
    public async Task WithoutApproval_AnEntryIsApprovedAtOnce_AndItsApprovedActionsRunWithTheOthers()
    {
        var form = await ActionFormAsync(Action("first"), Action("on approval", ActionTrigger.Approved), Action("on rejection", ActionTrigger.Rejected));

        var entry = await SubmitAsync(form);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Approved));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "first", "on approval" }), "in the order shown, and no rejection");
            Assert.That(runs.Select(run => run.Trigger), Is.EqualTo(new[] { ActionTrigger.Submitted, ActionTrigger.Approved }));
            Assert.That(Heard.Heard.Select(item => item.GetType()), Is.EqualTo(new[] { typeof(EntrySubmittedNotification), typeof(EntryApprovedNotification) }));
        });
    }

    [Test]
    public async Task WithApproval_AnEntryWaits_AndRunsOnlyItsSubmittedActions()
    {
        var form = await WaitingFormAsync(Action("first"), Action("on approval", ActionTrigger.Approved));

        var entry = await SubmitAsync(form);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Submitted));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "first" }));
            Assert.That(runs, Has.Count.EqualTo(1));
        });
    }

    [TestCase("Approved", "on approval")]
    [TestCase("Rejected", "on rejection")]
    public async Task AUsersChange_FiresItsTrigger_OnlyAfterTheWindow(string state, string action)
    {
        var to = Enum.Parse<EntryState>(state);
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved), Action("on rejection", ActionTrigger.Rejected));
        var entry = await SubmitAsync(form);

        var change = await Changes.ChangeAsync(form, [entry.Key], to, User);
        await Pass.RunAsync(CancellationToken.None);
        var startedInside = Scripted.Started.Count;
        var heardInside = Heard.Heard.Count(item => item is EntryApprovedNotification or EntryRejectedNotification);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(change.Moved.Single().State, Is.EqualTo(to));
            Assert.That(change.UndoUntilUtc, Is.EqualTo(Clock.GetUtcNow()));
            Assert.That(startedInside, Is.Zero, "the actions wait");
            Assert.That(heardInside, Is.Zero, "and so do the entry events");
            Assert.That(Scripted.Started, Is.EqualTo(new[] { action }));
            Assert.That(Heard.Heard.OfType<EntryNotification>().Last().State, Is.EqualTo(to == EntryState.Approved ? "approved" : "rejected"));
        });
    }

    [Test]
    public async Task ABulkChange_MovesEachItCan_AndSaysWhichItCouldNot()
    {
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved));
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        var other = await SubmitAsync(await WaitingFormAsync());
        var gone = Guid.NewGuid();

        var change = await Changes.ChangeAsync(form, [first.Key, second.Key, first.Key, other.Key, gone], EntryState.Approved, User);
        var again = await Changes.ChangeAsync(form, [first.Key], EntryState.Approved, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await DrainAsync(first.Id);

        Assert.Multiple(() =>
        {
            Assert.That(change.Moved.Select(entry => entry.Key), Is.EqualTo(new[] { first.Key, second.Key }));
            Assert.That(change.Unmoved, Is.EqualTo(new[] { other.Key, gone }));
            Assert.That(again.Moved, Is.Empty, "already there");
            Assert.That(again.UndoUntilUtc, Is.Null);
            Assert.That(Scripted.StartedOf("on approval"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Undo_InsideTheWindow_PutsEveryEntryBack_SkipsItsRuns_AndNothingGoesOut()
    {
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved));
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        var change = await Changes.ChangeAsync(form, [first.Key, second.Key], EntryState.Approved, User);

        Clock.Advance(EntrySpamFolder.UndoWindow - TimeSpan.FromSeconds(1));
        var undone = await Changes.UndoAsync(form, change.ChangeKey, User);
        var again = await Changes.UndoAsync(form, change.ChangeKey, User);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await Pass.RunAsync(CancellationToken.None);

        var runs = await Runs.ListForEntryAsync(first.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.Done));
            Assert.That(again, Is.EqualTo(EntryUndoStatus.TooLate), "once is enough");
            Assert.That((await Entries.GetByIdAsync(first.Id))!.State, Is.EqualTo(EntryState.Submitted));
            Assert.That((await Entries.GetByIdAsync(second.Id))!.State, Is.EqualTo(EntryState.Submitted));
            Assert.That(runs.Single().State, Is.EqualTo(ActionRunState.Skipped));
            Assert.That(runs.Single().LastError?.Code, Is.EqualTo(DeliveryError.Undone));
            Assert.That(Scripted.Started, Is.Empty);
            Assert.That(Heard.Heard.OfType<EntryApprovedNotification>(), Is.Empty);
            Assert.That((await Events.ListForEntryAsync(first.Id)).Select(item => item.Kind), Does.Contain(EntryStateChanges.UndoneEvent));
        });
    }

    [Test]
    public async Task Undo_AfterTheWindow_IsRefused_AndTheEntryKeepsItsState()
    {
        var form = await WaitingFormAsync();
        var entry = await SubmitAsync(form);
        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Rejected, User);

        Clock.Advance(EntrySpamFolder.UndoWindow);
        var late = await Changes.UndoAsync(form, change.ChangeKey, User);

        Assert.Multiple(async () =>
        {
            Assert.That(late, Is.EqualTo(EntryUndoStatus.TooLate));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Rejected));
        });
    }

    [Test]
    public async Task Undo_OfAnotherFormsChange_OrAnUnknownOne_IsNotFound()
    {
        var form = await WaitingFormAsync();
        var entry = await SubmitAsync(form);
        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Rejected, User);

        Assert.Multiple(async () =>
        {
            Assert.That(await Changes.UndoAsync(await WaitingFormAsync(), change.ChangeKey, User), Is.EqualTo(EntryUndoStatus.NotFound));
            Assert.That(await Changes.UndoAsync(form, Guid.NewGuid(), User), Is.EqualTo(EntryUndoStatus.NotFound));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Rejected));
        });
    }

    [Test]
    public async Task ASystemChange_RunsAtOnce_AndCannotBeUndone()
    {
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved));
        var entry = await SubmitAsync(form);

        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, null);
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(change.UndoUntilUtc, Is.Null);
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "on approval" }));
            Assert.That(Heard.Heard.OfType<EntryApprovedNotification>().Count(), Is.EqualTo(1));
            Assert.That(await Changes.UndoAsync(form, change.ChangeKey, User), Is.EqualTo(EntryUndoStatus.NotFound));
        });
    }

    [Test]
    public async Task ASetStateAction_MovesTheEntry_UnderItsRule_AndTheTriggerFiresAtOnce()
    {
        var approve = new FormAction { Key = Guid.NewGuid(), Type = SetStateActionType.Alias, Name = "Approve", Settings = SettingsObject.Parse("""{"state":"approved"}""") };
        var form = await WaitingFormAsync(approve, Action("on approval", ActionTrigger.Approved));

        var entry = await SubmitAsync(form);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Approved));
            Assert.That(runs.Select(run => run.State), Is.All.EqualTo(ActionRunState.Succeeded));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "on approval" }));
        });
    }

    [Test]
    public async Task ASetStateAction_OnAnEntryAlreadyThere_HasNothingToDo()
    {
        var approve = new FormAction { Key = Guid.NewGuid(), Type = SetStateActionType.Alias, Name = "Approve", Settings = SettingsObject.Parse("""{"state":"approved"}""") };
        var form = await ActionFormAsync(approve);

        var entry = await SubmitAsync(form);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single().State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(Heard.Heard.OfType<EntryApprovedNotification>().Count(), Is.EqualTo(1), "approved as it came in, and not again");
        });
    }

    [Test]
    public async Task AUsersNotSpam_WaitsToo_AndItsUndoPutsItBackInSpam()
    {
        var form = await WaitingFormAsync(Action("first"));
        var entry = await SpamAsync(form);

        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Submitted, User);
        await Pass.RunAsync(CancellationToken.None);
        var startedInside = Scripted.Started.Count;
        var undone = await Changes.UndoAsync(form, change.ChangeKey, User);

        Assert.Multiple(async () =>
        {
            Assert.That(change.Moved.Single().State, Is.EqualTo(EntryState.Submitted));
            Assert.That(startedInside, Is.Zero);
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.Done));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Spam));
            Assert.That(Heard.Heard.OfType<EntrySubmittedNotification>(), Is.Empty);
        });
    }

    [Test]
    public async Task AUsersNotSpam_IsToldOf_OnceTheWindowIsOver()
    {
        var form = await WaitingFormAsync(Action("first"));
        var entry = await SpamAsync(form);

        await Changes.ChangeAsync(form, [entry.Key], EntryState.Submitted, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "first" }));
            Assert.That(Heard.Heard.OfType<EntrySubmittedNotification>().Single().EntryKey, Is.EqualTo(entry.Key));
        });
    }

    [Test]
    public async Task AnUnverifiedEntry_IsNotApprovedAround_ItsConfirmation()
    {
        var form = await WaitingFormAsync();
        var entry = await SubmitAsync(form);
        await Entries.ChangeStateAsync(entry.Id, EntryState.Submitted, EntryState.Unverified, null);

        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);

        Assert.That(change.Unmoved, Is.EqualTo(new[] { entry.Key }));
    }

    [Test]
    public async Task EveryMove_IsOnTheTimeline()
    {
        var form = await WaitingFormAsync();
        var entry = await SubmitAsync(form);

        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);

        var moved = (await Events.ListForChangeAsync(change.ChangeKey)).Single();
        await Changes.UndoAsync(form, change.ChangeKey, User);
        var audit = GetRequiredService<IAuditRepository>();
        var changed = await audit.QueryAsync(new AuditQuery { Action = EntryStateChanges.ChangedAction }, new PageRequest(0, 10));
        var undone = await audit.QueryAsync(new AuditQuery { Action = EntryStateChanges.UndoneAction }, new PageRequest(0, 10));
        Assert.Multiple(() =>
        {
            Assert.That(moved.Kind, Is.EqualTo(EntryStateChanges.ChangedEvent));
            Assert.That(moved.UserKey, Is.EqualTo(User));
            Assert.That(moved.Details, Is.EqualTo("""{"from":"submitted","to":"approved"}"""));
            Assert.That(changed.Items.Single().Details, Does.Contain(entry.Key.ToString()).And.Contain("approved"));
            Assert.That(changed.Items.Single().UserKey, Is.EqualTo(User));
            Assert.That(undone.Items.Single().Details, Does.Contain(entry.Key.ToString()).And.Contain("submitted"));
        });
    }

    [Test]
    public async Task ABulkRejection_WaitsToo_AndFiresForEach()
    {
        var form = await WaitingFormAsync(Action("on rejection", ActionTrigger.Rejected));
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);

        await Changes.ChangeAsync(form, [first.Key, second.Key], EntryState.Rejected, User);
        await Pass.RunAsync(CancellationToken.None);
        var inside = Scripted.Started.Count;
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await DrainAsync(first.Id);

        Assert.Multiple(() =>
        {
            Assert.That(inside, Is.Zero);
            Assert.That(Scripted.StartedOf("on rejection"), Is.EqualTo(2));
            Assert.That(Heard.Heard.OfType<EntryRejectedNotification>().Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ALaterMove_ReplacesWhatAnEarlierOneHadWaiting()
    {
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved), Action("on rejection", ActionTrigger.Rejected));
        var entry = await SubmitAsync(form);

        await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);
        await Changes.ChangeAsync(form, [entry.Key], EntryState.Rejected, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "on rejection" }));
            Assert.That(runs.Single(run => run.State == ActionRunState.Skipped).LastError?.Code, Is.EqualTo(DeliveryError.Superseded));
            Assert.That(Heard.Heard.OfType<EntryApprovedNotification>(), Is.Empty);
            Assert.That(Heard.Heard.OfType<EntryRejectedNotification>().Count(), Is.EqualTo(1));
        });
    }

    [TestCase("Rejected")]
    [TestCase("Spam")]
    public async Task AnEntryRejectedOrInSpam_StartsNothingItHasNotStarted(string state)
    {
        var form = await WaitingFormAsync(Action("first"));
        var entry = await SubmitAsync(form);

        await Changes.ChangeAsync(form, [entry.Key], Enum.Parse<EntryState>(state), User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(Scripted.Started, Is.Empty);
            Assert.That(runs.Single().LastError?.Code, Is.EqualTo(DeliveryError.Superseded));
        });
    }

    [Test]
    public async Task AChangeThatReplacedWork_CannotBeUndone_AndTheAnswerSaysSo()
    {
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved));
        var entry = await SubmitAsync(form);
        await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);

        var second = await Changes.ChangeAsync(form, [entry.Key], EntryState.Rejected, User);
        var undone = await Changes.UndoAsync(form, second.ChangeKey, User);

        Assert.Multiple(async () =>
        {
            Assert.That(second.UndoUntilUtc, Is.Null);
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.TooLate));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Rejected));
        });
    }

    [Test]
    public async Task Undo_OfAChangeSomeoneMovedOnFrom_ChangesNothing()
    {
        var form = await WaitingFormAsync(Action("on rejection", ActionTrigger.Rejected));
        var entry = await SubmitAsync(form);
        var first = await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);
        await Changes.ChangeAsync(form, [entry.Key], EntryState.Rejected, User);

        var undone = await Changes.UndoAsync(form, first.ChangeKey, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.TooLate));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Rejected));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "on rejection" }), "the later change still goes out");
        });
    }

    [Test]
    public async Task Undo_OfAChangeWithARunStarted_ChangesNothing()
    {
        var form = await WaitingFormAsync(Action("on approval", ActionTrigger.Approved));
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        var change = await Changes.ChangeAsync(form, [first.Key, second.Key], EntryState.Approved, User);
        await GetRequiredService<FormBuilderDatabase>().WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.ActionRun)} SET {n.C("attempts")} = 1 WHERE {n.C("entryId")} = @0", second.Id);
            });

        var undone = await Changes.UndoAsync(form, change.ChangeKey, User);

        Assert.Multiple(async () =>
        {
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.TooLate));
            Assert.That((await Entries.GetByIdAsync(first.Id))!.State, Is.EqualTo(EntryState.Approved), "all of it stays, not the first one back");
            Assert.That((await Runs.ListForEntryAsync(first.Id)).Single().State, Is.EqualTo(ActionRunState.Pending));
            Assert.That((await Events.ListForChangeAsync(change.ChangeKey)).Select(item => item.Kind), Does.Not.Contain(EntryStateChanges.UndoneEvent));
        });
    }

    [Test]
    public async Task Undo_OfAChangeTheQueueGotToFirst_ChangesNothing()
    {
        var form = await WaitingFormAsync();
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        var change = await Changes.ChangeAsync(form, [first.Key, second.Key], EntryState.Approved, User);
        await Events.ClaimDueAsync((await Events.ListForChangeAsync(change.ChangeKey))[0].Id);

        var undone = await Changes.UndoAsync(form, change.ChangeKey, User);

        Assert.Multiple(async () =>
        {
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.TooLate));
            Assert.That((await Entries.GetByIdAsync(first.Id))!.State, Is.EqualTo(EntryState.Approved));
            Assert.That((await Entries.GetByIdAsync(second.Id))!.State, Is.EqualTo(EntryState.Approved), "and the one the queue did not reach stays too");
        });
    }

    [Test]
    public async Task ATripToSpamAndBack_RunsNothingTwice_AndCountsOnce()
    {
        var form = await WaitingFormAsync(Action("first"));
        var entry = await SubmitAsync(form);
        await DrainAsync(entry.Id);

        await Changes.ChangeAsync(form, [entry.Key], EntryState.Spam, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await Pass.RunAsync(CancellationToken.None);
        await Changes.ChangeAsync(form, [entry.Key], EntryState.Submitted, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await DrainAsync(entry.Id);

        var row = (await FormRows.GetAsync(form))!;
        var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var counted = (await GetRequiredService<IDailyStatRepository>().ListAsync(row.Id, today.AddDays(-1), today.AddDays(1))).Where(stat => stat.Metric == "submissions").Sum(stat => stat.Value);
        Assert.Multiple(() =>
        {
            Assert.That(Scripted.StartedOf("first"), Is.EqualTo(1));
            Assert.That(counted, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AUsersNotSpam_CountsOnlyPastItsWindow_AndIsInTheAuditLog()
    {
        var form = await WaitingFormAsync();
        var entry = await SpamAsync(form);
        var row = (await FormRows.GetAsync(form))!;
        var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var stats = GetRequiredService<IDailyStatRepository>();
        async Task<long> CountedAsync() => (await stats.ListAsync(row.Id, today.AddDays(-1), today.AddDays(1))).Where(stat => stat.Metric == "submissions").Sum(stat => stat.Value);

        var undone = await Changes.ChangeAsync(form, [entry.Key], EntryState.Submitted, User);
        await Changes.UndoAsync(form, undone.ChangeKey, User);
        var afterUndo = await CountedAsync();
        await Changes.ChangeAsync(form, [entry.Key], EntryState.Submitted, User);
        var inside = await CountedAsync();
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await Pass.RunAsync(CancellationToken.None);

        var audited = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = EntryStateChanges.ChangedAction }, new PageRequest(0, 10));
        Assert.Multiple(async () =>
        {
            Assert.That(afterUndo, Is.Zero);
            Assert.That(inside, Is.Zero);
            Assert.That(await CountedAsync(), Is.EqualTo(1));
            Assert.That(audited.Items, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task AMoveAndItsUndo_GiveAPlaceBack_AndTakeItAgain()
    {
        var seat = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "dropdown",
            Alias = "seat",
            Label = new LocalizedText("Seat"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 1 }] },
        };
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: Waits(), fields: [NameField, seat]);
        var entry = await SubmitAsync(form, """{"name":"Ada","seat":"front"}""");
        var row = (await FormRows.GetAsync(form))!;
        var place = new OptionPlace(seat.Key, "front");
        async Task<int> TakenAsync() => (await Entries.GetPlacesTakenAsync(row.Id, [place])).GetValueOrDefault(place);

        var before = await TakenAsync();
        var change = await Changes.ChangeAsync(form, [entry.Key], EntryState.Rejected, User);
        var rejected = await TakenAsync();
        await Changes.UndoAsync(form, change.ChangeKey, User);

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(1));
            Assert.That(rejected, Is.Zero);
            Assert.That(await TakenAsync(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AValueKept_WaitsForAUsersApproval_ServesItsRun_AndGoesAfterIt()
    {
        string? seen = null;
        var kept = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "kept", Label = new LocalizedText("Kept"), Keep = FieldKeep.UntilActionsRun };
        Scripted.Scripts["on approval"] = (context, _) =>
        {
            seen = context.Scope.Answers.Find(kept.Key)?.Values[0].Text;
            return Task.FromResult(ActionResult.Succeeded);
        };
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: Waits(Action("on approval", ActionTrigger.Approved)), fields: [NameField, kept]);
        var entry = await SubmitAsync(form, """{"name":"Ada","kept":"for a moment"}""");
        var fieldId = (await FormRows.ListFieldsAsync((await FormRows.GetAsync(form))!.Id)).Single(field => field.Values.FieldKey == kept.Key).Id;
        async Task<bool> HeldAsync() => (await Entries.ListValuesAsync([entry.Id])).Any(value => value.FieldId == fieldId);

        await Pass.RunAsync(CancellationToken.None);
        var whileWaiting = await HeldAsync();
        await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(whileWaiting, Is.True);
            Assert.That(seen, Is.EqualTo("for a moment"));
            Assert.That(await HeldAsync(), Is.False);
        });
    }

    [Test]
    public async Task ARunEndingInsideAWindow_LetsGoOfNothingAnUndoWouldNeed()
    {
        var kept = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "kept", Label = new LocalizedText("Kept"), Keep = FieldKeep.UntilActionsRun };
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: Waits(Action("first"), Action("on rejection", ActionTrigger.Rejected)), fields: [NameField, kept]);
        var entry = await SubmitAsync(form, """{"name":"Ada","kept":"for a moment"}""");
        var fieldId = (await FormRows.ListFieldsAsync((await FormRows.GetAsync(form))!.Id)).Single(field => field.Values.FieldKey == kept.Key).Id;
        async Task<bool> HeldAsync() => (await Entries.ListValuesAsync([entry.Id])).Any(value => value.FieldId == fieldId);

        await Changes.ChangeAsync(form, [entry.Key], EntryState.Approved, User);
        await DrainAsync(entry.Id);
        var inside = await HeldAsync();
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "first" }));
            Assert.That(inside, Is.True, "an Undo could still bring its rejection action back");
            Assert.That(await HeldAsync(), Is.False);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<HeardEntries>();
        builder.AddNotificationAsyncHandler<EntrySubmittedNotification, HeardEntriesHandler>();
        builder.AddNotificationAsyncHandler<EntryApprovedNotification, HeardEntriesHandler>();
        builder.AddNotificationAsyncHandler<EntryRejectedNotification, HeardEntriesHandler>();
    }

    private async Task<StoredEntry> SpamAsync(Guid form)
    {
        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""", System.Text.Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var row = (await FormRows.GetAsync(form))!;
        return (await Entries.GetByIdAsync((await Entries.ListIdsAsync(row.Id, 10))[^1]))!;
    }
}
