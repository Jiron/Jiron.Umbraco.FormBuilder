using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Actions;

/// <summary>
/// One pass of the outbox worker (ROADMAP 21.1, ADR-0252): each queue has its own time, so the runs that wait on a host that
/// never answers use up theirs and the mail after them still leaves; a queue that fails does not stop the next one.
/// </summary>
[TestFixture]
public sealed class OutboxPassTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task EachQueueHasItsOwnTime_SoTheQueueAfterASlowOneStillHasAll()
    {
        var clock = new Clock(Start);
        var runs = new Queue("runs", clock) { Takes = OutboxPass.Budget + TimeSpan.FromSeconds(30) };
        var mail = new Queue("mail", clock);
        using var wake = Wake();

        await Pass(clock, wake, runs, mail).RunAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(runs.Deadline, Is.EqualTo(Start + OutboxPass.Budget));
            Assert.That(mail.Started, Is.EqualTo(Start + OutboxPass.Budget + TimeSpan.FromSeconds(30)), "the slow queue went past its time");
            Assert.That(mail.Deadline - mail.Started, Is.EqualTo(OutboxPass.Budget), "the next queue's time starts with its own pass");
        });
    }

    [Test]
    public async Task AQueueThatFails_DoesNotStopTheNext_AndWorkLeftWakesTheWorkerAgain()
    {
        var clock = new Clock(Start);
        var runs = new Queue("runs", clock) { Fails = true };
        var mail = new Queue("mail", clock) { Leaves = true };
        using var wake = Wake();

        await Pass(clock, wake, runs, mail).RunAsync(CancellationToken.None);

        using var shortly = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var woken = await wake.WaitAsync(shortly.Token);
        Assert.Multiple(() =>
        {
            Assert.That(mail.Started, Is.EqualTo(Start));
            Assert.That(woken, Is.True, "work left due asks for another pass");
        });
    }

    private static OutboxWake Wake()
    {
        var options = Substitute.For<IOptionsMonitor<FormBuilderOptions>>();
        options.CurrentValue.Returns(new FormBuilderOptions());
        return new OutboxWake(options);
    }

    private static OutboxPass Pass(Clock clock, OutboxWake wake, params IOutboxQueue[] queues)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(RuntimeLevel.Run);
        return new OutboxPass(queues, wake, runtime, new NoMigration(), clock, NullLogger<OutboxPass>.Instance);
    }

    private sealed class NoMigration : IFormBuilderMigrationState
    {
        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);

        public void Invalidate()
        {
        }
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // A queue that notes when its pass began and the deadline it was given, and takes its time.
    private sealed class Queue(string name, Clock clock) : IOutboxQueue
    {
        public string Name => name;

        public TimeSpan Takes { get; init; }

        public bool Fails { get; init; }

        public bool Leaves { get; init; }

        public DateTimeOffset? Started { get; private set; }

        public DateTimeOffset? Deadline { get; private set; }

        public Task<bool> PassAsync(string claimant, DateTimeOffset deadline, CancellationToken stopping)
        {
            Started = clock.Now;
            Deadline = deadline;
            clock.Now += Takes;
            return Fails ? throw new InvalidOperationException("JFB_TEST: the queue fails") : Task.FromResult(Leaves);
        }
    }
}
