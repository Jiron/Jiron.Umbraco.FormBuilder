using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>
/// What wakes the worker, and every job of Jiron Form Builder (ROADMAP 8.1, ADR-0007, ADR-0033): the commit of a queued run wakes
/// the server's worker and a rollback does not, the outbox job only wakes it, and every job returns within ten seconds and does
/// nothing while a migration is pending.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OutboxJobAndWakeTests : ActionTestBase
{
    private readonly PendingSwitch _pending = new();
    private readonly SpyPart _part = new();

    private OutboxWake Wake => GetRequiredService<OutboxWake>();

    private FormBuilderDatabase Database => GetRequiredService<FormBuilderDatabase>();

    [Test]
    public async Task ACommitThatQueuedRuns_WakesTheWorker_OnlyOnceItIsCommitted()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        await TakeWakeAsync();
        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();
        var woken = false;

        await Database.WriteAsync(async _ =>
        {
            await Runs.AddAsync(entry.Id, run.FormVersionId, Guid.NewGuid(), [new NewActionRun(run.ActionKey, run.ActionType, ActionTrigger.Submitted, 0, false)]);
            woken = await TakeWakeAsync();
            return true;
        });

        Assert.Multiple(async () =>
        {
            Assert.That(woken, Is.False, "not before the commit");
            Assert.That(await TakeWakeAsync(), Is.True, "after it");
        });
    }

    [Test]
    public async Task ARollback_WakesNothing()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        await TakeWakeAsync();
        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();

        Assert.That(
            async () => await Database.WriteAsync<int>(async _ =>
            {
                await Runs.AddAsync(entry.Id, run.FormVersionId, Guid.NewGuid(), [new NewActionRun(run.ActionKey, run.ActionType, ActionTrigger.Submitted, 0, false)]);
                throw new InvalidOperationException("JFB_TEST: the unit fails after the runs");
            }),
            Throws.InvalidOperationException);

        Assert.That(await TakeWakeAsync(), Is.False);
    }

    [Test]
    public async Task ARetryFromTheLog_WakesTheWorkerToo()
    {
        var form = await ActionFormAsync(Action("refused"));
        Scripted.Scripts["refused"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        var entry = await SubmitAsync(form);
        var run = (await DrainAsync(entry.Id)).Single();
        await TakeWakeAsync();

        await Runs.RequeueAsync(run.Id, ActionRunState.Failed);

        Assert.That(await TakeWakeAsync(), Is.True);
    }

    [Test]
    public async Task ManyWakes_AddUpToOne()
    {
        await TakeWakeAsync();
        Wake.Wake();
        Wake.Wake();
        Wake.Wake();

        Assert.Multiple(async () =>
        {
            Assert.That(await TakeWakeAsync(), Is.True);
            Assert.That(await TakeWakeAsync(), Is.False);
        });
    }

    [Test]
    public async Task WhilePaused_AWakeDoesNothing()
    {
        await TakeWakeAsync();
        Pause(true);

        Wake.Wake();

        Assert.That(await TakeWakeAsync(), Is.False);
    }

    [Test]
    public async Task TheJob_OnlyWakesTheWorker_AndClaimsNothing()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        await TakeWakeAsync();
        var job = GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().OfType<FormBuilderOutboxJob>().Single();

        await job.ExecuteAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(job.Name, Is.EqualTo("Jiron.FormBuilder.Outbox"));
            Assert.That(job.Period, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(await TakeWakeAsync(), Is.True);
            Assert.That((await Runs.ListForEntryAsync(entry.Id)).Single().Attempts, Is.Zero, "the worker does the work");
        });
    }

    [Test]
    public async Task EveryJob_ReturnsWithinTenSeconds_AndDoesNothingWhileAMigrationIsPending()
    {
        var form = await ActionFormAsync(Action("one"));
        var entry = await SubmitAsync(form);
        await TakeWakeAsync();
        var distributed = GetRequiredService<IEnumerable<IDistributedBackgroundJob>>().Where(IsOurs).ToList();
        var recurring = GetRequiredService<IEnumerable<IRecurringBackgroundJob>>().Where(IsOurs).ToList();
        _pending.Pending = true;
        var pendingTimes = new Dictionary<string, TimeSpan>();
        foreach (var job in distributed)
        {
            pendingTimes[job.Name] = await TimeAsync(() => job.ExecuteAsync(CancellationToken.None));
        }

        foreach (var job in recurring)
        {
            pendingTimes[job.GetType().Name] = await TimeAsync(() => job.RunJobAsync(CancellationToken.None));
        }

        var asked = _pending.Asked;
        var wokenWhilePending = await TakeWakeAsync();
        var partRanWhilePending = _part.Runs;
        await Pass.RunAsync(CancellationToken.None);
        var attemptsWhilePending = (await Runs.ListForEntryAsync(entry.Id)).Single().Attempts;

        _pending.Pending = false;
        var times = new Dictionary<string, TimeSpan>();
        foreach (var job in distributed)
        {
            times[job.Name] = await TimeAsync(() => job.ExecuteAsync(CancellationToken.None));
        }

        foreach (var job in recurring)
        {
            times[job.GetType().Name] = await TimeAsync(() => job.RunJobAsync(CancellationToken.None));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(distributed.Select(job => job.Name), Does.Contain("Jiron.FormBuilder.Outbox").And.Contain("Jiron.FormBuilder.Retention"));
            Assert.That(recurring, Is.Not.Empty);
            Assert.That(distributed.Select(job => job.Period), Is.All.GreaterThanOrEqualTo(TimeSpan.FromMinutes(1)), "periods are a minute or longer");
            Assert.That(pendingTimes.Values, Is.All.LessThan(TimeSpan.FromSeconds(10)), string.Join(", ", pendingTimes));
            Assert.That(times.Values, Is.All.LessThan(TimeSpan.FromSeconds(10)), string.Join(", ", times));
            Assert.That(asked, Is.GreaterThanOrEqualTo(distributed.Count + recurring.Count), "each job asks first");
            Assert.That(wokenWhilePending, Is.False);
            Assert.That(partRanWhilePending, Is.Zero);
            Assert.That(attemptsWhilePending, Is.Zero, "a pass does nothing while a migration is pending either");
            Assert.That(_part.Runs, Is.GreaterThan(0), "the retention part ran once none was pending");
            Assert.That(await TakeWakeAsync(), Is.True);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IFormBuilderMigrationState>(_pending);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart>(_part));
    }

    private static bool IsOurs(object job) => job.GetType().Assembly == typeof(OutboxWake).Assembly;

    private static async Task<TimeSpan> TimeAsync(Func<Task> run)
    {
        var watch = Stopwatch.StartNew();
        await run();
        return watch.Elapsed;
    }

    // Takes what the channel holds: whether a wake was waiting. This host has no worker to take it.
    private async Task<bool> TakeWakeAsync()
    {
        using var shortly = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            return await Wake.WaitAsync(shortly.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

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

    private sealed class SpyPart : IFormBuilderRetentionPart
    {
        private int _runs;

        public string Name => "spy";

        public int Runs => Volatile.Read(ref _runs);

        public Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runs);
            return Task.CompletedTask;
        }
    }
}

/// <summary>The worker of a host as it runs: a submission's commit wakes it, and the run goes without a pass from the test.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OutboxWorkerTests : ActionTestBase
{
    /// <inheritdoc />
    private protected override bool KeepsTheWorker => true;

    [Test]
    public async Task ASubmission_WakesTheWorker_WhichRunsItsActions()
    {
        var form = await ActionFormAsync(Action("first"), Action("second"));

        var entry = await SubmitAsync(form);
        IReadOnlyList<StoredActionRun> runs = [];
        for (var i = 0; i < 300; i++)
        {
            runs = await Runs.ListForEntryAsync(entry.Id);
            if (runs.All(run => run.State == ActionRunState.Succeeded))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        Assert.That(runs.Select(run => run.State), Is.All.EqualTo(ActionRunState.Succeeded));
    }
}
