using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>
/// The processor (ROADMAP 8.1, ADR-0007): runs in the order shown, stopping the rest, retries with fake time, leases that run
/// out into uncertain, the late finisher, an attempt cut off by its time, and the pause switch.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ActionProcessorTests : ActionTestBase
{
    [Test]
    public async Task TheRunsOfASubmission_GoInTheOrderShown_EachAfterTheOnesBefore()
    {
        var order = new List<string>();
        var form = await ActionFormAsync(Action("first"), Action("second"), Action("third"));
        foreach (var name in new[] { "first", "second", "third" })
        {
            Scripted.Scripts[name] = (_, _) =>
            {
                lock (order)
                {
                    order.Add(name);
                }

                return Task.FromResult(ActionResult.Succeeded);
            };
        }

        var entry = await SubmitAsync(form);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "first", "second", "third" }));
            Assert.That(runs.Select(run => run.State), Is.All.EqualTo(ActionRunState.Succeeded));
            Assert.That(runs.Select(run => run.Attempts), Is.All.EqualTo(1));
            Assert.That(runs.Select(run => run.NextAttemptUtc), Is.All.Null);
            Assert.That(runs.Select(run => run.FinishedUtc), Is.All.EqualTo(Clock.GetUtcNow()));
        });
    }

    [Test]
    public async Task AnAttempt_ReadsTheEntry_AndAKeyThatStaysOverItsAttempts()
    {
        var seen = new List<ActionRunContext>();
        var form = await ActionFormAsync(Action("reads", target: "somewhere"));
        Scripted.Scripts["reads"] = (context, _) =>
        {
            seen.Add(context);
            return Task.FromResult(seen.Count == 1 ? ActionResult.TryAgain("not_yet") : ActionResult.Succeeded);
        };

        var entry = await SubmitAsync(form);
        await DrainAsync(entry.Id);
        Clock.Advance(TimeSpan.FromMinutes(1));
        await DrainAsync(entry.Id);

        var first = seen[0];
        Assert.Multiple(() =>
        {
            Assert.That(seen, Has.Count.EqualTo(2));
            Assert.That(first.Trigger, Is.EqualTo(Core.Definitions.ActionTrigger.Submitted));
            Assert.That(first.Scope.Entry?.Key, Is.EqualTo(entry.Key));
            Assert.That(first.Scope.Entry?.Number, Is.EqualTo(entry.Number));
            Assert.That(first.Scope.FormName, Is.EqualTo("Contact us"));
            Assert.That(first.Scope.Answers.Find(NameField.Key)?.Values[0].Text, Is.EqualTo("Ada"), "the stored answers");
            Assert.That(first.Attempt, Is.EqualTo(1));
            Assert.That(seen[1].Attempt, Is.EqualTo(2));
            Assert.That(seen[1].DeliveryKey, Is.EqualTo(first.DeliveryKey), "a receiver can notice a repeat");
            Assert.That(Scripted.SettingsSeen, Does.Contain("somewhere"));
        });
    }

    [Test]
    public async Task AnAttemptThatFails_ComesAgainAfterTheBackOff_AndFailsForGoodAfterTheFifth()
    {
        var form = await ActionFormAsync(Action("down"));
        Scripted.Scripts["down"] = (_, _) => Task.FromResult(ActionResult.TryAgain("http_503", "The receiver answered 503."));
        var entry = await SubmitAsync(form);

        var seen = new List<(int Attempts, TimeSpan? Wait)>();
        foreach (var wait in ActionAttempts.BackOff)
        {
            var run = (await DrainAsync(entry.Id)).Single();
            seen.Add((run.Attempts, run.NextAttemptUtc - Clock.GetUtcNow()));
            Assert.That(run.State, Is.EqualTo(ActionRunState.Pending));
            Clock.Advance(wait - TimeSpan.FromSeconds(1));
            Assert.That((await DrainAsync(entry.Id)).Single().Attempts, Is.EqualTo(run.Attempts), "not before it is due");
            Clock.Advance(TimeSpan.FromSeconds(1));
        }

        var last = (await DrainAsync(entry.Id)).Single();
        Clock.Advance(TimeSpan.FromHours(2));
        var after = (await DrainAsync(entry.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new (int, TimeSpan?)[]
            {
                (1, TimeSpan.FromMinutes(1)), (2, TimeSpan.FromMinutes(3)), (3, TimeSpan.FromMinutes(9)), (4, TimeSpan.FromMinutes(27)),
            }));
            Assert.That(last.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(last.Attempts, Is.EqualTo(5));
            Assert.That(last.LastError, Is.EqualTo(new DeliveryError("http_503", "The receiver answered 503.")));
            Assert.That(after.Attempts, Is.EqualTo(5), "final");
            Assert.That(Scripted.StartedOf("down"), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task ARetryingRun_DoesNotHoldTheLaterOnesBack()
    {
        var form = await ActionFormAsync(Action("flaky"), Action("later"));
        Scripted.Scripts["flaky"] = (_, _) => Task.FromResult(ActionResult.TryAgain("http_503"));
        var entry = await SubmitAsync(form);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(runs[0].State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(runs[1].State, Is.EqualTo(ActionRunState.Succeeded));
        });
    }

    [Test]
    public async Task ARunThatStopsTheRest_HoldsThemWhileItRetries_AndSkipsThemWhenItFailsForGood()
    {
        var form = await ActionFormAsync(Action("stopper", stopsTheRest: true), Action("rest"));
        Scripted.Scripts["stopper"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        var entry = await SubmitAsync(form);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(runs[0].State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(runs[0].Attempts, Is.EqualTo(1), "a failure for good needs no more attempts");
            Assert.That(runs[1].State, Is.EqualTo(ActionRunState.Skipped));
            Assert.That(runs[1].LastError?.Code, Is.EqualTo(DeliveryError.Stopped));
            Assert.That(Scripted.StartedOf("rest"), Is.Zero);
        });
    }

    [Test]
    public async Task AThrowingType_TriesAgain_AndItsMessageIsNotKept()
    {
        var form = await ActionFormAsync(Action("throws"));
        Scripted.Scripts["throws"] = (_, _) => throw new InvalidOperationException("JFB_TEST what a visitor typed");
        var entry = await SubmitAsync(form);

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(run.LastError?.Code, Is.EqualTo(ActionAttempts.Threw));
            Assert.That(run.LastError?.Message, Is.EqualTo("System.InvalidOperationException"), "its message may hold what a visitor typed");
        });
    }

    [Test]
    public async Task AResultsMessage_ReachesTheLogOnOneLine()
    {
        var form = await ActionFormAsync(Action("multiline"));
        Scripted.Scripts["multiline"] = (_, _) => Task.FromResult(ActionResult.Failed("refused", "first\r\nsecond\tthird"));
        var entry = await SubmitAsync(form);

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.That(run.LastError?.Message, Is.EqualTo("first  second third"));
    }

    [Test]
    public async Task ARunWhoseTypeTheSiteLacks_FailsForGood()
    {
        var form = await ActionFormAsync(Action("known"));
        var entry = await SubmitAsync(form);
        var queued = (await Runs.ListForEntryAsync(entry.Id)).Single();
        await Runs.AddAsync(entry.Id, queued.FormVersionId, Guid.NewGuid(), [new NewActionRun(Guid.NewGuid(), "gone.type", Core.Definitions.ActionTrigger.Submitted, 0, false)]);

        var runs = await DrainAsync(entry.Id);

        Assert.That(runs.Single(run => run.ActionType == "gone.type"), Has.Property(nameof(StoredActionRun.State)).EqualTo(ActionRunState.Failed).And.Property(nameof(StoredActionRun.LastError)).EqualTo(new DeliveryError(ActionAttempts.UnknownType, "The site has no action type 'gone.type'.")));
    }

    [Test]
    public async Task ARunWhoseVersionHasNoSuchAction_FailsForGood()
    {
        var form = await ActionFormAsync(Action("known"));
        var entry = await SubmitAsync(form);
        var queued = (await Runs.ListForEntryAsync(entry.Id)).Single();
        await Runs.AddAsync(entry.Id, queued.FormVersionId, Guid.NewGuid(), [new NewActionRun(Guid.NewGuid(), ScriptedActionType.Alias, Core.Definitions.ActionTrigger.Submitted, 0, false)]);

        var runs = await DrainAsync(entry.Id);

        var unknown = runs.Single(run => run.ActionKey != queued.ActionKey);
        Assert.Multiple(() =>
        {
            Assert.That(unknown.LastError?.Code, Is.EqualTo(ActionAttempts.UnknownAction));
            Assert.That(unknown.State, Is.EqualTo(ActionRunState.Failed), "no later attempt can change that");
        });
    }

    [Test]
    public async Task ALeaseThatRunsOut_TurnsTheRunUncertain_AndNothingRunsItAgain()
    {
        var form = await ActionFormAsync(Action("crashes"));
        var entry = await SubmitAsync(form);
        var queued = (await Runs.ListForEntryAsync(entry.Id)).Single();

        // Another server claims it and is never heard of again.
        var claimed = (await Runs.ClaimAsync(queued.Id, "gone-server", ActionRunQueue.Lease))!;
        Clock.Advance(ActionRunQueue.Lease - TimeSpan.FromSeconds(1));
        var before = (await DrainAsync(entry.Id)).Single();
        Clock.Advance(TimeSpan.FromSeconds(2));
        var after = (await DrainAsync(entry.Id)).Single();
        Clock.Advance(TimeSpan.FromDays(1));
        var later = (await DrainAsync(entry.Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(before.State, Is.EqualTo(ActionRunState.Running), "while its lease holds");
            Assert.That(after.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(after.LastError?.Code, Is.EqualTo(DeliveryError.LeaseExpired));
            Assert.That(after.LockedBy, Is.EqualTo(claimed.LockedBy), "so the attempt that held it can still say how it went");
            Assert.That(later.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(Scripted.StartedOf("crashes"), Is.Zero, "nothing retries it by itself");
        });
    }

    [Test]
    public async Task ALateFinisher_ReplacesUncertainWithItsResult()
    {
        var finish = new TaskCompletionSource<ActionResult>();
        var started = new TaskCompletionSource();
        var form = await ActionFormAsync(Action("slow"));
        Scripted.Scripts["slow"] = (_, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        var entry = await SubmitAsync(form);

        var pass = Pass.RunAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Clock.Advance(ActionRunQueue.Lease + TimeSpan.FromSeconds(1));
        await Runs.ExpireLeasesAsync();
        var uncertain = (await Runs.ListForEntryAsync(entry.Id)).Single();
        finish.SetResult(ActionResult.Succeeded);
        await pass;
        var finished = (await Runs.ListForEntryAsync(entry.Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(uncertain.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(finished.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(finished.LastError, Is.Null);
        });
    }

    [Test]
    public async Task ALateFinisher_OfAnAttemptRetriedMeanwhile_RecordsNothing()
    {
        var finish = new TaskCompletionSource<ActionResult>();
        var started = new TaskCompletionSource();
        var form = await ActionFormAsync(Action("slow"));
        Scripted.Scripts["slow"] = (_, _) =>
        {
            started.TrySetResult();
            return finish.Task;
        };
        var entry = await SubmitAsync(form);

        var pass = Pass.RunAsync(CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Clock.Advance(ActionRunQueue.Lease + TimeSpan.FromSeconds(1));
        await Runs.ExpireLeasesAsync();
        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();
        await Runs.RequeueAsync(run.Id, ActionRunState.Uncertain);
        var claimed = (await Runs.ClaimAsync(run.Id, "second-attempt", ActionRunQueue.Lease))!;
        finish.SetResult(ActionResult.Failed("too_late"));
        await pass;

        Assert.That(await Runs.GetAsync(run.Id), Is.EqualTo(claimed), "the second attempt's claim still stands");
    }

    [Test]
    public async Task AnAttemptPastItsTime_IsCutOff_AndBecomesUncertain()
    {
        var form = await ActionFormAsync(Action("late"));
        Scripted.Scripts["late"] = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ActionResult.Succeeded;
        };
        var entry = await SubmitAsync(form);

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(run.LastError?.Code, Is.EqualTo(ActionAttempts.TimedOut));
        });
    }

    [Test]
    public async Task AnAttemptTheHostStops_IsCutOff_AndBecomesUncertain()
    {
        using var stopping = new CancellationTokenSource();
        var form = await ActionFormAsync(Action("stopped") with { Type = PatientActionType.Alias });
        var started = new TaskCompletionSource();
        Patient.Scripts["stopped"] = async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ActionResult.Succeeded;
        };
        var entry = await SubmitAsync(form);

        var pass = Pass.RunAsync(stopping.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await stopping.CancelAsync();
        await pass.WaitAsync(TimeSpan.FromSeconds(30));

        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(run.LastError?.Code, Is.EqualTo(ActionAttempts.HostStopped));
        });
    }

    [Test]
    public async Task AnErrorOnceTheAttemptWasToldToStop_MayHaveBeenDelivered_SoItBecomesUncertain()
    {
        var form = await ActionFormAsync(Action("wraps"));
        Scripted.Scripts["wraps"] = async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException exception)
            {
                throw new InvalidOperationException("JFB_TEST: the receiver stopped answering", exception);
            }

            return ActionResult.Succeeded;
        };
        var entry = await SubmitAsync(form);

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Uncertain), "no attempt of its own runs it a second time");
            Assert.That(run.LastError?.Code, Is.EqualTo(ActionAttempts.TimedOut));
        });
    }

    [Test]
    public async Task ACancellationThatIsNotOurs_MayHaveBeenDelivered_SoItBecomesUncertain()
    {
        var form = await ActionFormAsync(Action("itsOwnTimeout"));
        Scripted.Scripts["itsOwnTimeout"] = (_, _) => throw new TaskCanceledException("JFB_TEST: the client's own time ran out");
        var entry = await SubmitAsync(form);

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.That(run.State, Is.EqualTo(ActionRunState.Uncertain));
    }

    [Test]
    public async Task FailingToReadAnEntry_NeverFailsTheRunForGood_AsNothingWasSent()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();
        for (var attempt = 1; attempt < ActionAttempts.MaxAttempts; attempt++)
        {
            var held = (await Runs.ClaimAsync(run.Id, "server-a", ActionRunQueue.Lease))!;
            await Runs.RetryAsync(run.Id, held.LockedBy!, TimeSpan.Zero, new DeliveryError("refused"));
        }

        var last = (await Runs.ClaimAsync(run.Id, "server-a", ActionRunQueue.Lease))!;
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        await GetRequiredService<ActionAttempts>().RunAsync(last, stopped.Token);

        var after = (await Runs.GetAsync(run.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(last.Attempts, Is.EqualTo(ActionAttempts.MaxAttempts));
            Assert.That(after.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(after.LastError?.Code, Is.EqualTo(ActionAttempts.HostStopped));
            Assert.That(Scripted.StartedOf("one"), Is.Zero);
        });
    }

    [Test]
    public async Task AHandlerThatHangs_HoldsAnAttemptUpForAMomentOnly()
    {
        var seen = GetRequiredService<SeenNotifications>();
        var form = await ActionFormAsync(Action("hangs"));
        var entry = await SubmitAsync(form);
        try
        {
            seen.Hang.Reset();
            var run = (await DrainAsync(entry.Id)).Single();

            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
        }
        finally
        {
            seen.Hang.Set();
        }
    }

    [Test]
    public async Task ATypeThatDoesNotStop_IsCutOff_AndItsLateResultCounts()
    {
        var finish = new TaskCompletionSource<ActionResult>();
        var form = await ActionFormAsync(Action("deaf"));
        Scripted.Scripts["deaf"] = (_, _) => finish.Task;
        var entry = await SubmitAsync(form);

        var run = (await DrainAsync(entry.Id)).Single();
        finish.SetResult(ActionResult.Succeeded);
        StoredActionRun? late = null;
        for (var i = 0; i < 200 && late?.State != ActionRunState.Succeeded; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25));
            late = await Runs.GetAsync(run.Id);
        }

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Uncertain), "the pass did not wait for it");
            Assert.That(run.LastError?.Code, Is.EqualTo(ActionAttempts.TimedOut));
            Assert.That(late?.State, Is.EqualTo(ActionRunState.Succeeded));
        });
    }

    [Test]
    public async Task AWholePass_TakesNoMoreThanItsLimit_AndWakesItselfForTheRest()
    {
        var form = await ActionFormAsync(Action("one"));
        var running = 0;
        var most = 0;
        Scripted.Scripts["one"] = async (_, token) =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref most, now);
            await Task.Delay(TimeSpan.FromMilliseconds(30), token);
            Interlocked.Decrement(ref running);
            return ActionResult.Succeeded;
        };
        for (var i = 0; i < ActionRunQueue.PerPass + 2; i++)
        {
            await SubmitAsync(form);
        }

        // Empty what the submissions left in the channel, as the worker of this host is off.
        using var drained = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var wake = GetRequiredService<OutboxWake>();
        await wake.WaitAsync(drained.Token);
        await Pass.RunAsync(CancellationToken.None);
        var firstPass = Scripted.StartedOf("one");
        using var woken = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var wokeItself = await wake.WaitAsync(woken.Token);
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(firstPass, Is.EqualTo(ActionRunQueue.PerPass));
            Assert.That(most, Is.LessThanOrEqualTo(ActionRunQueue.AtATime));
            Assert.That(most, Is.GreaterThan(1), "they do go side by side");
            Assert.That(wokeItself, Is.True);
            Assert.That(Scripted.StartedOf("one"), Is.EqualTo(ActionRunQueue.PerPass + 2));
        });
    }

    [Test]
    public async Task APassPastItsDeadline_ClaimsNothingMore()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        var queue = GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<ActionRunQueue>().Single();

        var left = await queue.PassAsync("server-a", Clock.GetUtcNow(), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(Scripted.StartedOf("one"), Is.Zero);
            Assert.That(left, Is.True, "and it says what it left");
            Assert.That((await Runs.ListForEntryAsync(entry.Id)).Single().State, Is.EqualTo(ActionRunState.Pending));
        });
    }

    [Test]
    public async Task WhilePaused_NothingRuns_NothingIsLost_AndEveryQueuedRunGoesOutOnceItIsNot()
    {
        var form = await ActionFormAsync(Action("first"), Action("second"));
        Pause(true);
        var entries = new[] { await SubmitAsync(form), await SubmitAsync(form) };
        await Pass.RunAsync(CancellationToken.None);
        var whilePaused = (await Runs.ListForEntryAsync(entries[0].Id)).Concat(await Runs.ListForEntryAsync(entries[1].Id)).ToList();
        var attempts = whilePaused.Sum(run => run.Attempts);
        var started = Scripted.Started.Count;

        Pause(false);
        await Pass.RunAsync(CancellationToken.None);
        await Pass.RunAsync(CancellationToken.None);
        var after = (await Runs.ListForEntryAsync(entries[0].Id)).Concat(await Runs.ListForEntryAsync(entries[1].Id)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(whilePaused, Has.Count.EqualTo(4), "entries are still stored and their runs queued");
            Assert.That(attempts, Is.Zero, "no attempt is used");
            Assert.That(started, Is.Zero);
            Assert.That(after.Select(run => run.State), Is.All.EqualTo(ActionRunState.Succeeded));
            Assert.That(Scripted.Started, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task WhilePaused_APassTouchesNothing_NotEvenALeaseThatRanOut()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        var queued = (await Runs.ListForEntryAsync(entry.Id)).Single();
        await Runs.ClaimAsync(queued.Id, "gone-server", ActionRunQueue.Lease);
        Clock.Advance(ActionRunQueue.Lease + TimeSpan.FromSeconds(1));
        Pause(true);

        await Pass.RunAsync(CancellationToken.None);

        Assert.That((await Runs.GetAsync(queued.Id))!.State, Is.EqualTo(ActionRunState.Running));
    }

    [Test]
    public async Task APauseDuringAPass_StopsItsClaims()
    {
        var form = await ActionFormAsync(Action("pauses"), Action("after"));
        Scripted.Scripts["pauses"] = (_, _) =>
        {
            Pause(true);
            return Task.FromResult(ActionResult.Succeeded);
        };
        var entry = await SubmitAsync(form);

        await Pass.RunAsync(CancellationToken.None);

        var runs = await Runs.ListForEntryAsync(entry.Id);
        Assert.Multiple(() =>
        {
            Assert.That(runs[0].State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(runs[1].Attempts, Is.Zero);
        });
    }

    [Test]
    public async Task EachAttempt_TellsTheSitesHandlers_WhoseFailureChangesNothing()
    {
        var form = await ActionFormAsync(Action("works"), Action("fails"), Action("again"));
        Scripted.Scripts["fails"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        Scripted.Scripts["again"] = (_, _) => Task.FromResult(ActionResult.TryAgain("http_503"));
        var seen = GetRequiredService<SeenNotifications>();
        var entry = await SubmitAsync(form);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(seen.Running, Is.EqualTo(new[] { "works", "fails", "again" }));
            Assert.That(seen.Completed, Is.EqualTo(new[] { "works" }));
            Assert.That(seen.Failed, Is.EqualTo(new[] { "fails:False:refused", "again:True:http_503" }));
            Assert.That(runs.Select(run => run.State), Is.EqualTo(new[] { ActionRunState.Succeeded, ActionRunState.Failed, ActionRunState.Pending }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<SeenNotifications>();
        builder.AddNotificationHandler<ActionRunningNotification, SeenNotificationsHandler>();
        builder.AddNotificationHandler<ActionRunCompletedNotification, SeenNotificationsHandler>();
        builder.AddNotificationHandler<ActionRunFailedNotification, SeenNotificationsHandler>();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}

/// <summary>What the action notifications said.</summary>
internal sealed class SeenNotifications
{
    public List<string> Running { get; } = [];

    public List<string> Completed { get; } = [];

    public List<string> Failed { get; } = [];

    public ManualResetEventSlim Hang { get; } = new(initialState: true);
}

/// <summary>Writes down what the action notifications say, then throws, as a site's handler may.</summary>
internal sealed class SeenNotificationsHandler(SeenNotifications seen)
    : INotificationHandler<ActionRunningNotification>, INotificationHandler<ActionRunCompletedNotification>, INotificationHandler<ActionRunFailedNotification>
{
    public void Handle(ActionRunningNotification notification)
    {
        seen.Hang.Wait();
        seen.Running.Add(notification.Context.Action.Name!);
        throw new InvalidOperationException("JFB_TEST: a site's handler fails");
    }

    public void Handle(ActionRunCompletedNotification notification)
    {
        seen.Completed.Add(notification.Context.Action.Name!);
        throw new InvalidOperationException("JFB_TEST: a site's handler fails");
    }

    public void Handle(ActionRunFailedNotification notification)
    {
        seen.Failed.Add($"{notification.Context.Action.Name}:{notification.WillRetry}:{notification.Code}");
        throw new InvalidOperationException("JFB_TEST: a site's handler fails");
    }
}
