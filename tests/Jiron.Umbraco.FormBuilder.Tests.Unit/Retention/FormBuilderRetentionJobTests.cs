using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Retention;

/// <summary>
/// The retention job (ADR-0007, ADR-0045): it runs only while Umbraco runs and no migration is pending, runs its parts in
/// order within its budget, logs a part that fails and goes on with the next, and gives up only when it is cancelled.
/// </summary>
[TestFixture]
public sealed class FormBuilderRetentionJobTests
{
    private readonly List<string> _ran = [];
    private TestTimeProvider _clock = null!;
    private IRuntimeState _runtime = null!;
    private TestMigrationState _migrations = null!;
    private RecordingLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _ran.Clear();
        _clock = new TestTimeProvider();
        _runtime = Substitute.For<IRuntimeState>();
        _runtime.Level.Returns(RuntimeLevel.Run);
        _migrations = new TestMigrationState();
        _logger = new RecordingLogger();
    }

    [Test]
    public void TheJob_RunsHourly_UnderItsOwnName_WithinTenSeconds()
    {
        var job = Job();

        Assert.Multiple(() =>
        {
            Assert.That(job.Name, Is.EqualTo("Jiron.FormBuilder.Retention"));
            Assert.That(job.Period, Is.EqualTo(TimeSpan.FromHours(1)));
            Assert.That(FormBuilderRetentionJob.Budget, Is.EqualTo(TimeSpan.FromSeconds(8)), "with room left to return within ten seconds (ADR-0007)");
        });
    }

    [Test]
    public async Task ARun_RunsEveryPart_InOrder_WithTheDeadlineOfItsBudget()
    {
        var deadlines = new List<DateTimeOffset>();

        await Job(Part("first", deadline => deadlines.Add(deadline)), Part("second", deadline => deadlines.Add(deadline))).ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_ran, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(deadlines, Is.All.EqualTo(_clock.GetUtcNow() + FormBuilderRetentionJob.Budget));
            Assert.That(_logger.Entries, Is.Empty);
        });
    }

    [TestCase(RuntimeLevel.Unknown)]
    [TestCase(RuntimeLevel.Boot)]
    [TestCase(RuntimeLevel.Install)]
    [TestCase(RuntimeLevel.Upgrade)]
    [TestCase(RuntimeLevel.Upgrading)]
    [TestCase(RuntimeLevel.BootFailed)]
    public async Task ARun_DoesNothing_UnlessUmbracoRuns(RuntimeLevel level)
    {
        _runtime.Level.Returns(level);

        await Job(Part("first")).ExecuteAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_ran, Is.Empty);
            Assert.That(_migrations.Asked, Is.Zero, "the database is not asked either");
        });
    }

    [Test]
    public async Task ARun_DoesNothing_WhileAMigrationIsPending()
    {
        _migrations.Pending = true;

        await Job(Part("first")).ExecuteAsync(CancellationToken.None);

        Assert.That(_ran, Is.Empty);
    }

    [Test]
    public async Task APartThatFails_IsLogged_AndTheNextOneRuns()
    {
        var failure = new InvalidOperationException("JFB_TEST: a part that fails");

        await Job(Part("first", _ => throw failure), Part("second")).ExecuteAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_ran, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(_logger.Entries.Select(entry => (entry.Level, entry.Message, entry.Exception?.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Error, "Jiron Form Builder's retention of first failed; it runs again in 01:00:00", LoggedException.Of(failure).Message),
            }));
        });
    }

    [Test]
    public void ACancelledRun_GivesUp_ButACancellationOfItsOwnIsAFailure()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var job = Job(Part("first", _ => throw new OperationCanceledException(cancelled.Token)), Part("second"));

        Assert.That(() => job.ExecuteAsync(cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(_ran, Is.EqualTo(new[] { "first" }), "the next part does not run");

        _ran.Clear();
        Assert.That(() => job.ExecuteAsync(CancellationToken.None), Throws.Nothing, "a part's own time-out while the run goes on");
        Assert.Multiple(() =>
        {
            Assert.That(_ran, Is.EqualTo(new[] { "first", "second" }));
            Assert.That(_logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error }));
        });
    }

    [Test]
    public async Task APartThatUsesTheBudget_LeavesTheNextOnesToTheNextRun()
    {
        await Job(Part("first", deadline => _clock.Advance(deadline - _clock.GetUtcNow())), Part("second")).ExecuteAsync(CancellationToken.None);

        Assert.That(_ran, Is.EqualTo(new[] { "first" }));
    }

    private FormBuilderRetentionJob Job(params IFormBuilderRetentionPart[] parts) => new(_runtime, _migrations, parts, _clock, _logger);

    private TestPart Part(string name, Action<DateTimeOffset>? run = null) => new(name, _ran, run);

    private sealed class TestMigrationState : IFormBuilderMigrationState
    {
        public bool Pending { get; set; }

        public int Asked { get; private set; }

        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default)
        {
            Asked++;
            return ValueTask.FromResult(Pending);
        }

        public void Invalidate()
        {
        }
    }

    // A part that records that it ran, then does what the test gives it.
    private sealed class TestPart(string name, List<string> ran, Action<DateTimeOffset>? run) : IFormBuilderRetentionPart
    {
        public string Name => name;

        public Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
        {
            ran.Add(name);
            run?.Invoke(deadline);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger : ILogger<FormBuilderRetentionJob>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
