using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The action outbox (D6, ADR-0007, ADR-0040): runs queued with their entry in batches, one per change, claimed once with
/// a token of each claim's own, held back by the earlier runs of their batch, finished by the claim's holder only,
/// uncertain when a lease runs out and still recorded by the late finisher, the rest of a batch skipped after a failure
/// that stops it, retried from the log and undone. Every change with a stale token or state changes nothing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class ActionRunRepositoryTests : RepositoryTestBase
{
    private const string Server = "server-a JFB_TEST";
    private const string Stale = Server + "/00000000000000000000000000000000";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly DeliveryError Refused = new("http_503", "The receiver answered 503.");

    [Test]
    public async Task Runs_AreQueuedPendingAndDue_InTheirOrder()
    {
        var (entryId, versionId) = await EntryAsync();
        var change = Guid.NewGuid();
        var window = Now.AddSeconds(10);

        var runs = await ActionRuns.AddAsync(
            entryId,
            versionId,
            change,
            [
                Run(2, ActionTrigger.Verified, stopsTheRest: true),
                Run(0, ActionTrigger.Submitted),
                Run(1, ActionTrigger.Approved),
                Run(1, ActionTrigger.Rejected),
            ],
            TimeSpan.FromSeconds(10));
        var atOnce = (await QueueAsync(entryId, versionId, [Run(0)]))[0];

        Assert.That(runs, Is.Not.Null);
        var first = runs![0];
        Assert.Multiple(async () =>
        {
            Assert.That(runs.Select(run => (run.SortOrder, run.Trigger)), Is.EqualTo(new[] { (0, ActionTrigger.Submitted), (1, ActionTrigger.Approved), (1, ActionTrigger.Rejected), (2, ActionTrigger.Verified) }), "by position, then as given");
            Assert.That(runs.Select(run => run.Id), Is.Ordered.Ascending, "the ids follow the order");
            Assert.That(runs.All(run => run is { State: ActionRunState.Pending, Attempts: 0, LockedBy: null, LockedUntilUtc: null, LastError: null, StartedUtc: null, FinishedUtc: null }), Is.True);
            Assert.That(runs.All(run => run.EntryId == entryId && run.FormVersionId == versionId && run.CreatedUtc == Now && run.ChangeKey == change), Is.True);
            Assert.That(runs.All(run => run.NextAttemptUtc == window), Is.True, "the end of the undo window");
            Assert.That(atOnce.NextAttemptUtc, Is.EqualTo(Now), "at once");
            Assert.That(atOnce.ChangeKey, Is.Not.EqualTo(change));
            Assert.That(runs[3].StopOnFailure, Is.True);
            Assert.That(runs[3].ActionType, Is.EqualTo("Jiron.FormBuilder.Webhook"));
            Assert.That(await ActionRuns.ListForEntryAsync(entryId), Is.EqualTo(runs.Append(atOnce)), "in the order they were queued");
            Assert.That(await ActionRuns.GetAsync(first.Id), Is.EqualTo(first));
            Assert.That(await ActionRuns.GetAsync(atOnce.Id + 100), Is.Null);
            Assert.That(Query(db => db.Fetch<string>($"SELECT {Q("triggerKind")} FROM {T(FormBuilderTables.ActionRun)} ORDER BY {Q("id")}")), Is.EqualTo(new[] { "submitted", "approved", "rejected", "verified", "submitted" }), "the definition's names");
            Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { atOnce.Id }), "the window holds its own batch only");
        });

        Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { atOnce.Id, first.Id }), "the longest due first");
    }

    [Test]
    public async Task Runs_ForAnEntryThatDoesNotExist_AreNotQueued()
    {
        var (entryId, versionId) = await EntryAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.AddAsync(entryId + 100, versionId, Guid.NewGuid(), [Run(0)]), Is.Null);
            Assert.That(Count(), Is.Zero);
        });
    }

    [Test]
    public async Task ASecondBatchOfOneChange_ForTheSameEntry_IsRefused()
    {
        var (entryId, versionId) = await EntryAsync();
        var (other, _) = await EntryAsync();
        var change = Guid.NewGuid();
        await QueueAsync(entryId, versionId, [Run(0)], change);

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await ActionRuns.AddAsync(entryId, versionId, change, [Run(1)]),
                Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.DuplicateKey));
            Assert.That(Count(), Is.EqualTo(1), "nothing written");
            Assert.That(await ActionRuns.AddAsync(other, versionId, change, [Run(0)]), Has.Count.EqualTo(1), "a bulk change queues a batch for each entry");
        });
    }

    [Test]
    public async Task Runs_JoinTheSubmissionsUnit_AndRollBackWithIt()
    {
        var form = await CreateFormAsync();
        var key = Guid.NewGuid();

        Assert.That(
            async () => await Database.WriteAsync<int>(async _ =>
            {
                var entry = await Entries.CreateAsync(NewEntry(form.Id, form.DraftVersionId!.Value) with { Key = key });
                Assert.That(await ActionRuns.AddAsync(entry!.Id, form.DraftVersionId!.Value, Guid.NewGuid(), [Run(0), Run(1)]), Has.Count.EqualTo(2));
                throw new InvalidOperationException("JFB_TEST: the unit fails after the runs");
            }),
            Throws.InvalidOperationException);

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.GetAsync(key), Is.Null);
            Assert.That(Count(), Is.Zero, "no run without its entry");
        });
    }

    [Test]
    public async Task AClaim_TakesADueRun_ForOneClaimOnly()
    {
        var (entryId, versionId) = await EntryAsync();
        var due = (await QueueAsync(entryId, versionId, [Run(0)]))[0];
        var waiting = (await QueueAsync(entryId, versionId, [Run(0)], wait: TimeSpan.FromMinutes(1)))[0];

        var claimed = await ActionRuns.ClaimAsync(due.Id, Server, Lease);

        Assert.Multiple(async () =>
        {
            Assert.That(claimed, Is.EqualTo(due with { State = ActionRunState.Running, Attempts = 1, LockedBy = claimed?.LockedBy, LockedUntilUtc = Now + Lease, StartedUtc = Now }));
            Assert.That(claimed?.LockedBy, Does.Match("^server-a JFB_TEST/[0-9a-f]{32}$"), "the claimant's name and 128 random bits");
            Assert.That(await ActionRuns.ClaimAsync(due.Id, "server-b JFB_TEST", Lease), Is.Null, "claimed once");
            Assert.That(await ActionRuns.GetAsync(due.Id), Is.EqualTo(claimed), "the second claim changed nothing");
            Assert.That(await ActionRuns.ClaimAsync(waiting.Id, "server-b JFB_TEST", Lease), Is.Null, "not due yet");
            Assert.That(await ActionRuns.GetAsync(waiting.Id), Is.EqualTo(waiting));
            Assert.That(await ActionRuns.ClaimAsync(waiting.Id + 100, "server-b JFB_TEST", Lease), Is.Null, "no such run");
        });

        Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.That(await ActionRuns.ClaimAsync(waiting.Id, "server-b JFB_TEST", Lease), Is.Not.Null, "due now");
    }

    [Test]
    public async Task EveryClaim_GetsATokenOfItsOwn_SoALateFinisherNeverRecordsOverALaterAttempt()
    {
        var run = (await NewRunsAsync(1))[0];
        var first = await TakeAsync(run);
        await ActionRuns.CutOffAsync(run.Id, first, new DeliveryError("host_stopped"));
        await ActionRuns.RequeueAsync(run.Id, ActionRunState.Uncertain);

        var second = await TakeAsync(run);

        Assert.Multiple(async () =>
        {
            Assert.That(second, Is.Not.EqualTo(first), "the same claimant, another token");
            Assert.That(await ActionRuns.SucceedAsync(run.Id, first), Is.False, "the first attempt finishes late");
            Assert.That(await ActionRuns.GetAsync(run.Id), Is.EqualTo(run with { State = ActionRunState.Running, Attempts = 1, LockedBy = second, LockedUntilUtc = Now + Lease, StartedUtc = Now, LastError = new DeliveryError("host_stopped") }));
        });
    }

    [Test]
    public async Task ParallelClaims_LetExactlyOneThrough()
    {
        var run = (await NewRunsAsync(1))[0];

        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => ActionRuns.ClaimAsync(run.Id, $"server-{i} JFB_TEST", Lease))));

        Assert.Multiple(async () =>
        {
            Assert.That(claims.Count(claim => claim is not null), Is.EqualTo(1));
            Assert.That((await ActionRuns.GetAsync(run.Id))!.Attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnEarlierRun_HoldsTheLaterOnesOfItsBatchBack_UntilItsFirstAttemptIsOver()
    {
        var (entryId, versionId) = await EntryAsync();
        var (other, _) = await EntryAsync();
        var change = Guid.NewGuid();
        var runs = await QueueAsync(entryId, versionId, [Run(0), Run(1), Run(1), Run(2)], change);
        var later = (await QueueAsync(entryId, versionId, [Run(5)]))[0];
        var elsewhere = (await QueueAsync(other, versionId, [Run(5)], change))[0];
        var (first, second, twin, last) = (runs[0], runs[1], runs[2], runs[3]);

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { first.Id, later.Id, elsewhere.Id }), "waiting for its first attempt holds the rest of its batch back; another change's run does not wait, nor another entry's of the same bulk change");
            Assert.That(await ActionRuns.ClaimAsync(second.Id, Server, Lease), Is.Null);
        });

        var token = await TakeAsync(first);
        Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { later.Id, elsewhere.Id }), "running holds the rest back");

        await ActionRuns.RetryAsync(first.Id, token, TimeSpan.FromMinutes(1), Refused);
        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { second.Id, later.Id, elsewhere.Id }), "a retrying run does not; the tie goes by id");
            Assert.That(await ActionRuns.ClaimAsync(twin.Id, Server, Lease), Is.Null, "the run queued first comes first");
        });

        token = await TakeAsync(second);
        await ActionRuns.CutOffAsync(second.Id, token, new DeliveryError("timeout"));
        var claimed = await ActionRuns.ClaimAsync(twin.Id, Server, Lease);
        Assert.That(claimed, Is.Not.Null, "an uncertain run does not hold the rest back");
        await ActionRuns.SucceedAsync(twin.Id, claimed!.LockedBy!);

        Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { last.Id, later.Id, elsewhere.Id, first.Id }), "the longest due first, then by id");
        Assert.That(await ActionRuns.DueAsync(1), Is.EqualTo(new[] { last.Id }));
    }

    [Test]
    public async Task ARunThatStopsTheRest_HoldsTheRestOfItsBatchBack_UntilItIsFinal()
    {
        var (entryId, versionId) = await EntryAsync();
        var runs = await QueueAsync(entryId, versionId, [Run(0, stopsTheRest: true), Run(1)]);
        var other = (await QueueAsync(entryId, versionId, [Run(0)]))[0];
        var (stopper, rest) = (runs[0], runs[1]);

        var token = await TakeAsync(stopper);
        await ActionRuns.RetryAsync(stopper.Id, token, TimeSpan.Zero, Refused);
        Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { stopper.Id, other.Id }), "while it retries; another change's run does not wait");

        token = await TakeAsync(stopper);
        await ActionRuns.CutOffAsync(stopper.Id, token, new DeliveryError("host_stopped"));
        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { other.Id }), "while it is uncertain");
            Assert.That(await ActionRuns.ClaimAsync(rest.Id, Server, Lease), Is.Null);
        });

        await ActionRuns.SucceedAsync(stopper.Id, token);
        Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { rest.Id, other.Id }), "once it is final");
    }

    [Test]
    public async Task Outcomes_AreRecorded_OnlyByTheClaimsToken()
    {
        // A claim for each outcome, with an entry beside each whose runs wait; every outcome with a stale token first.
        var held = new List<StoredActionRun>();
        foreach (var run in await NewRunsAsync(4))
        {
            held.Add((await ActionRuns.ClaimAsync(run.Id, Server, Lease))!);
            await AnotherEntryWaitsAsync();
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.SucceedAsync(held[0].Id, held[1].LockedBy!), Is.False, "another claim's token");
            Assert.That(await ActionRuns.RetryAsync(held[1].Id, held[2].LockedBy!, TimeSpan.Zero, Refused), Is.False);
            Assert.That(await ActionRuns.FailAsync(held[2].Id, held[3].LockedBy!, Refused), Is.False);
            Assert.That(await ActionRuns.CutOffAsync(held[3].Id, held[0].LockedBy!, Refused), Is.False);
            Assert.That(await ActionRuns.SucceedAsync(held[0].Id, Stale), Is.False);
            Assert.That(await ActionRuns.RetryAsync(held[1].Id, Stale, TimeSpan.Zero, Refused), Is.False);
            Assert.That(await ActionRuns.FailAsync(held[2].Id, Stale, Refused), Is.False);
            Assert.That(await ActionRuns.CutOffAsync(held[3].Id, Stale, Refused), Is.False);
            foreach (var run in held)
            {
                Assert.That(await ActionRuns.GetAsync(run.Id), Is.EqualTo(run), "a stale token changes nothing");
            }
        });

        Clock.Advance(TimeSpan.FromSeconds(30));
        var later = Now.AddSeconds(30);
        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.SucceedAsync(held[0].Id, held[0].LockedBy!), Is.True);
            Assert.That(await ActionRuns.RetryAsync(held[1].Id, held[1].LockedBy!, TimeSpan.FromMinutes(3), Refused), Is.True);
            Assert.That(await ActionRuns.FailAsync(held[2].Id, held[2].LockedBy!, Refused), Is.True);
            Assert.That(await ActionRuns.CutOffAsync(held[3].Id, held[3].LockedBy!, Refused), Is.True);
        });

        var after = await Task.WhenAll(held.Select(run => ActionRuns.GetAsync(run.Id)));
        Assert.Multiple(async () =>
        {
            Assert.That(after[0], Is.EqualTo(held[0] with { State = ActionRunState.Succeeded, FinishedUtc = later, NextAttemptUtc = null, LockedUntilUtc = null }));
            Assert.That(after[1], Is.EqualTo(held[1] with { State = ActionRunState.Pending, NextAttemptUtc = later.AddMinutes(3), LockedBy = null, LockedUntilUtc = null, LastError = Refused }));
            Assert.That(after[2], Is.EqualTo(held[2] with { State = ActionRunState.Failed, FinishedUtc = later, NextAttemptUtc = null, LockedUntilUtc = null, LastError = Refused }));
            Assert.That(after[3], Is.EqualTo(held[3] with { State = ActionRunState.Uncertain, LastError = Refused }), "cut off: the token stays for the late finisher");
            Assert.That(await ActionRuns.SucceedAsync(held[0].Id, held[0].LockedBy!), Is.False, "a final run takes no outcome");
            Assert.That(await ActionRuns.FailAsync(held[1].Id, held[1].LockedBy!, Refused), Is.False, "a pending run is no attempt's");
            Assert.That(await ActionRuns.GetAsync(held[0].Id), Is.EqualTo(after[0]));
            Assert.That(await ActionRuns.GetAsync(held[1].Id), Is.EqualTo(after[1]));
        });
    }

    [Test]
    public async Task ASuccess_ClearsTheErrorOfEarlierAttempts_AndALongMessageIsCut()
    {
        var run = (await NewRunsAsync(1))[0];
        var face = char.ConvertFromUtf32(0x1F600);
        var message = new string('m', ActionRunRepository.MaxErrorMessageLength - 1) + face + "tail";

        var token = await TakeAsync(run);
        await ActionRuns.RetryAsync(run.Id, token, TimeSpan.Zero, new DeliveryError("smtp.refused", message));
        var retried = await ActionRuns.GetAsync(run.Id);
        Clock.Advance(TimeSpan.FromMinutes(1));
        token = await TakeAsync(run);
        await ActionRuns.SucceedAsync(run.Id, token);

        Assert.Multiple(async () =>
        {
            Assert.That(retried!.LastError, Is.EqualTo(new DeliveryError("smtp.refused", new string('m', ActionRunRepository.MaxErrorMessageLength - 1))), "cut before the pair, not inside it");
            var done = await ActionRuns.GetAsync(run.Id);
            Assert.That(done!.LastError, Is.Null);
            Assert.That(done.Attempts, Is.EqualTo(2));
            Assert.That(done.StartedUtc, Is.EqualTo(Now), "the first attempt's start");
        });
    }

    [Test]
    public async Task ALeaseThatRanOut_MakesTheRunUncertain_AndTheLateFinisherStillRecordsItsResult()
    {
        var runs = await NewRunsAsync(2);
        var (late, retried) = (runs[0], runs[1]);
        var earlier = await TakeAsync(late);
        await ActionRuns.RetryAsync(late.Id, earlier, TimeSpan.Zero, Refused);
        var lateToken = await TakeAsync(late);
        await AnotherEntryWaitsAsync();
        var retriedToken = await TakeAsync(retried);

        Clock.Advance(Lease);
        Assert.That(await ActionRuns.ExpireLeasesAsync(), Is.Zero, "a lease that ends now still holds");

        Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.ExpireLeasesAsync(), Is.EqualTo(2));
            Assert.That(await ActionRuns.ExpireLeasesAsync(), Is.Zero, "once");
            var expired = await ActionRuns.GetAsync(late.Id);
            Assert.That(expired, Is.EqualTo(expired! with { State = ActionRunState.Uncertain, LockedBy = lateToken, LastError = new DeliveryError(DeliveryError.LeaseExpired) }), "the earlier attempt's message goes");
            Assert.That(await ActionRuns.DueAsync(10), Has.None.EqualTo(late.Id).And.None.EqualTo(retried.Id), "nothing claims an uncertain run");
            Assert.That(await ActionRuns.ClaimAsync(late.Id, "server-b JFB_TEST", Lease), Is.Null);
        });

        // The user retries one of them meanwhile; that late finisher then records nothing.
        await ActionRuns.RequeueAsync(retried.Id, ActionRunState.Uncertain);
        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.SucceedAsync(late.Id, lateToken), Is.True, "the late finisher");
            Assert.That((await ActionRuns.GetAsync(late.Id))!.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(await ActionRuns.SucceedAsync(retried.Id, retriedToken), Is.False, "its token was cleared");
            Assert.That((await ActionRuns.GetAsync(retried.Id))!.State, Is.EqualTo(ActionRunState.Pending));
        });
    }

    [Test]
    public async Task AFailureForGood_OfARunThatStopsTheRest_SkipsTheLaterPendingRunsOfItsBatch()
    {
        var (entryId, versionId) = await EntryAsync();
        var (other, _) = await EntryAsync();
        var change = Guid.NewGuid();
        var runs = await QueueAsync(entryId, versionId, [Run(0), Run(1, stopsTheRest: true), Run(1), Run(2), Run(3)], change);
        var otherChange = (await QueueAsync(entryId, versionId, [Run(5)]))[0];
        var elsewhere = (await QueueAsync(other, versionId, [Run(5)], change))[0];
        var (before, stopper, twin) = (runs[0], runs[1], runs[2]);
        var token = await TakeAsync(before);
        await ActionRuns.RetryAsync(before.Id, token, TimeSpan.FromHours(1), Refused);
        token = await TakeAsync(stopper);

        Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.That(await ActionRuns.FailAsync(stopper.Id, token, Refused), Is.True);

        var after = await ActionRuns.ListForEntryAsync(entryId);
        Assert.Multiple(async () =>
        {
            Assert.That(after.Select(run => run.State), Is.EqualTo(new[] { ActionRunState.Pending, ActionRunState.Failed, ActionRunState.Skipped, ActionRunState.Skipped, ActionRunState.Skipped, ActionRunState.Pending }), "the earlier run keeps its retry");
            Assert.That(after.Skip(2).Take(3).All(run => run.LastError == new DeliveryError(DeliveryError.Stopped) && run.FinishedUtc == Now.AddSeconds(5) && run.NextAttemptUtc is null && run.Attempts == 0), Is.True);
            Assert.That(after.Single(run => run.Id == twin.Id).State, Is.EqualTo(ActionRunState.Skipped), "a tie queued after it");
            Assert.That(after[^1], Is.EqualTo(otherChange), "another change's run of the entry");
            Assert.That(await ActionRuns.GetAsync(elsewhere.Id), Is.EqualTo(elsewhere), "another entry's run of the same bulk change");
        });
    }

    [Test]
    public async Task ALateFailureForGood_OfAnUncertainRunThatStopsTheRest_StillSkipsTheRest()
    {
        var (entryId, versionId) = await EntryAsync();
        var runs = await QueueAsync(entryId, versionId, [Run(0, stopsTheRest: true), Run(1)]);
        var token = await TakeAsync(runs[0]);
        Clock.Advance(Lease + TimeSpan.FromSeconds(1));
        await ActionRuns.ExpireLeasesAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.FailAsync(runs[0].Id, token, Refused), Is.True, "the late finisher");
            Assert.That((await ActionRuns.GetAsync(runs[1].Id))!.State, Is.EqualTo(ActionRunState.Skipped));
        });
    }

    [Test]
    public async Task AFailureForGood_OfARunThatDoesNotStopTheRest_SkipsNothing()
    {
        var (entryId, versionId) = await EntryAsync();
        var runs = await QueueAsync(entryId, versionId, [Run(0), Run(1)]);
        var token = await TakeAsync(runs[0]);

        await ActionRuns.FailAsync(runs[0].Id, token, Refused);

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.GetAsync(runs[1].Id), Is.EqualTo(runs[1]));
            Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { runs[1].Id }), "a failed run holds nothing back");
        });
    }

    [Test]
    public async Task ARetryFromTheLog_StartsOver_FromTheStateTheUserSaw_AndBringsBackTheRestOfItsBatch()
    {
        var (entryId, versionId) = await EntryAsync();
        var (third, _) = await EntryAsync();
        var change = Guid.NewGuid();
        var runs = await QueueAsync(entryId, versionId, [Run(0), Run(1, stopsTheRest: true), Run(2)], change);
        var other = await QueueAsync(entryId, versionId, [Run(0, stopsTheRest: true), Run(1)]);
        var bulk = await QueueAsync(third, versionId, [Run(1, stopsTheRest: true), Run(2)], change);
        var (plain, stopper, rest) = (runs[0], runs[1], runs[2]);

        // The plain run retries while the stopper fails for good and stops the rest; then the plain run fails for good
        // too. Another change of the entry, and the same change for another entry, have a failed stopper and a stopped
        // run of their own.
        var token = await TakeAsync(plain);
        await ActionRuns.RetryAsync(plain.Id, token, TimeSpan.Zero, Refused);
        await ActionRuns.FailAsync(stopper.Id, await TakeAsync(stopper), Refused);
        await ActionRuns.FailAsync(plain.Id, await TakeAsync(plain), Refused);
        await ActionRuns.FailAsync(other[0].Id, await TakeAsync(other[0]), Refused);
        await ActionRuns.FailAsync(bulk[0].Id, await TakeAsync(bulk[0]), Refused);
        var stopped = await ActionRuns.GetAsync(other[1].Id);
        var stoppedElsewhere = await ActionRuns.GetAsync(bulk[1].Id);

        Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.That(async () => await ActionRuns.RequeueAsync(stopper.Id, ActionRunState.Uncertain), Throws.InstanceOf<FormBuilderConcurrencyException>(), "not in the state the user saw");
        await ActionRuns.RequeueAsync(plain.Id, ActionRunState.Failed);
        Assert.That((await ActionRuns.GetAsync(rest.Id))!.State, Is.EqualTo(ActionRunState.Skipped), "a run that does not stop the rest brings nothing back");
        var retried = await ActionRuns.RequeueAsync(stopper.Id, ActionRunState.Failed);

        var later = Now.AddMinutes(10);
        Assert.Multiple(async () =>
        {
            Assert.That(retried!.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(retried, Is.EqualTo(retried with { Attempts = 0, NextAttemptUtc = later, LockedBy = null, LockedUntilUtc = null, FinishedUtc = null, LastError = Refused }), "the last error stays until the next attempt");
            Assert.That(await ActionRuns.GetAsync(rest.Id), Is.EqualTo(rest with { NextAttemptUtc = later }), "the rest it stopped waits for it again");
            Assert.That(await ActionRuns.GetAsync(other[1].Id), Is.EqualTo(stopped), "another change's stopped run stays");
            Assert.That(await ActionRuns.GetAsync(bulk[1].Id), Is.EqualTo(stoppedElsewhere), "and another entry's of the same change");
            Assert.That(await ActionRuns.DueAsync(10), Is.EqualTo(new[] { plain.Id }), "they run in their order again");
            Assert.That(async () => await ActionRuns.RequeueAsync(stopper.Id, ActionRunState.Failed), Throws.InstanceOf<FormBuilderConcurrencyException>(), "twice");
            Assert.That(await ActionRuns.RequeueAsync(bulk[1].Id + 100, ActionRunState.Failed), Is.Null);
        });
    }

    [Test]
    public async Task Undo_SkipsTheChangesRunsThatHaveNotStarted()
    {
        var (entryId, versionId) = await EntryAsync();
        var change = Guid.NewGuid();
        var runs = await QueueAsync(entryId, versionId, [Run(0, stopsTheRest: true), Run(1), Run(2)], change, TimeSpan.FromSeconds(10));
        var other = (await QueueAsync(entryId, versionId, [Run(0)], wait: TimeSpan.FromSeconds(10)))[0];
        Clock.Advance(TimeSpan.FromSeconds(10));
        var token = await TakeAsync(runs[0]);
        await ActionRuns.RetryAsync(runs[0].Id, token, TimeSpan.Zero, Refused);
        var tried = await ActionRuns.GetAsync(runs[0].Id);

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionRuns.SkipChangeAsync(change), Is.EqualTo(2));
            Assert.That(await ActionRuns.GetAsync(runs[1].Id), Is.EqualTo(runs[1] with { State = ActionRunState.Skipped, NextAttemptUtc = null, FinishedUtc = Now.AddSeconds(10), LastError = new DeliveryError(DeliveryError.Undone) }));
            Assert.That(await ActionRuns.GetAsync(runs[0].Id), Is.EqualTo(tried), "tried: it stays");
            Assert.That(await ActionRuns.GetAsync(other.Id), Is.EqualTo(other), "another change's run");
            Assert.That(await ActionRuns.SkipChangeAsync(change), Is.Zero, "once");
        });

        // The run that was tried fails for good and is retried: what undo skipped stays skipped.
        await ActionRuns.FailAsync(runs[0].Id, await TakeAsync(runs[0]), Refused);
        await ActionRuns.RequeueAsync(runs[0].Id, ActionRunState.Failed);
        Assert.That((await ActionRuns.GetAsync(runs[2].Id))!.LastError, Is.EqualTo(new DeliveryError(DeliveryError.Undone)));
    }

    [Test]
    public async Task DeletingTheEntry_DeletesItsRuns()
    {
        var (entryId, versionId) = await EntryAsync();
        await QueueAsync(entryId, versionId, [Run(0), Run(1)]);

        await Entries.DeleteAsync(entryId);

        Assert.That(Count(), Is.Zero);
    }

    private static NewActionRun Run(int seq, ActionTrigger trigger = ActionTrigger.Submitted, bool stopsTheRest = false) =>
        new(Guid.NewGuid(), "Jiron.FormBuilder.Webhook", trigger, seq, stopsTheRest);

    private int Count() => Query(db => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {T(FormBuilderTables.ActionRun)}"));

    private async Task<(long EntryId, int VersionId)> EntryAsync()
    {
        var form = await CreateFormAsync();
        return (await AddEntryAsync(form.Id, form.DraftVersionId!.Value), form.DraftVersionId!.Value);
    }

    // One change's batch of runs for an entry.
    private async Task<List<StoredActionRun>> QueueAsync(long entryId, int versionId, IReadOnlyList<NewActionRun> runs, Guid? change = null, TimeSpan wait = default) =>
        [.. (await ActionRuns.AddAsync(entryId, versionId, change ?? Guid.NewGuid(), runs, wait))!];

    // Claims a run that must be claimable, and gives the claim's token.
    private async Task<string> TakeAsync(StoredActionRun run)
    {
        var claimed = await ActionRuns.ClaimAsync(run.Id, Server, Lease);
        Assert.That(claimed, Is.Not.Null, "the run can be claimed");
        return claimed!.LockedBy!;
    }

    // Runs of one action each, for entries of their own, so the order holds none of them back.
    private async Task<List<StoredActionRun>> NewRunsAsync(int count)
    {
        var runs = new List<StoredActionRun>();
        for (var i = 0; i < count; i++)
        {
            var (entryId, versionId) = await EntryAsync();
            runs.Add((await QueueAsync(entryId, versionId, [Run(i)]))[0]);
        }

        return runs;
    }

    // An entry whose run waits behind its first one: rows that a too wide statement would change.
    private async Task AnotherEntryWaitsAsync()
    {
        var (entryId, versionId) = await EntryAsync();
        await QueueAsync(entryId, versionId, [Run(0), Run(1)]);
    }
}
