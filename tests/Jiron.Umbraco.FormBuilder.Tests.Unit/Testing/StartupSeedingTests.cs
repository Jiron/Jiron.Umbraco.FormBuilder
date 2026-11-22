using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The registry of a test host's start-up seeding (ROADMAP 24.1): finished once every announced seeder has reported,
/// and the work that waits for it starts then, once.
/// </summary>
[TestFixture]
public sealed class StartupSeedingTests
{
    [Test]
    public void New_NoSeederAnnounced_IsFinishedFromTheStart()
    {
        var seeding = Seeding();

        Assert.Multiple(() =>
        {
            Assert.That(seeding.IsFinished, Is.True);
            Assert.That(seeding.Finished.IsCompletedSuccessfully, Is.True);
        });
    }

    [Test]
    public void Report_SeveralSeeders_FinishesOnlyAfterTheLast()
    {
        var seeding = Seeding("people", "fake AI", "demo");

        seeding.Report("demo");
        seeding.Report("people");
        var beforeTheLast = seeding.IsFinished;
        seeding.Report("fake AI");

        Assert.Multiple(() =>
        {
            Assert.That(beforeTheLast, Is.False);
            Assert.That(seeding.IsFinished, Is.True);
            Assert.That(seeding.Finished.IsCompletedSuccessfully, Is.True);
        });
    }

    [Test]
    public async Task Report_FromASeederThatFailed_StillCounts()
    {
        var seeding = Seeding("people");

        Assert.ThrowsAsync<InvalidOperationException>(() => FailingSeederAsync(seeding));

        await seeding.Finished.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(seeding.IsFinished, Is.True);
    }

    [Test]
    public void Report_AnUnknownNameOrTheSameTwice_ChangesNothing()
    {
        var seeding = Seeding("people", "demo");

        seeding.Report("someone else");
        seeding.Report("people");
        seeding.Report("people");
        var afterTheRepeats = seeding.IsFinished;
        seeding.Report("demo");
        seeding.Report("demo");

        Assert.Multiple(() =>
        {
            Assert.That(afterTheRepeats, Is.False, "a second report of one seeder does not stand in for another");
            Assert.That(seeding.IsFinished, Is.True);
        });
    }

    [Test]
    public void WhenFinished_RegisteredBefore_RunsOnceAtTheLastReportAndNotBefore()
    {
        var seeding = Seeding("people", "demo");
        var runs = 0;

        seeding.WhenFinished(() => Run(ref runs));
        seeding.Report("people");
        var beforeTheLast = runs;
        seeding.Report("demo");
        seeding.Report("demo");
        seeding.Report("people");

        Assert.Multiple(() =>
        {
            Assert.That(beforeTheLast, Is.Zero);
            Assert.That(runs, Is.EqualTo(1));
        });
    }

    [Test]
    public void WhenFinished_RegisteredAfter_RunsOnceAtOnce()
    {
        var seeding = Seeding("people");
        seeding.Report("people");
        var runs = 0;

        seeding.WhenFinished(() => Run(ref runs));
        seeding.Report("people");

        Assert.That(runs, Is.EqualTo(1));
    }

    [Test]
    public void WhenFinished_WorkThatFails_IsLoggedAndDoesNotFailTheReportOrTheWorkAfterIt()
    {
        var logger = new CountingLogger();
        var seeding = new StartupSeeding([new StartupSeeder("people")], logger);
        var runs = 0;

        seeding.WhenFinished(() => throw new InvalidOperationException("thrown"));
        seeding.WhenFinished(() => Task.FromException(new InvalidOperationException("faulted")));
        seeding.WhenFinished(() => Run(ref runs));

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => seeding.Report("people"));
            Assert.That(runs, Is.EqualTo(1));
            Assert.That(logger.Errors, Is.EqualTo(2), "each failure logged");
        });
    }

    [Test]
    public void Report_ManySeedersAtOnce_FinishesOnceAndRunsTheWorkOnce()
    {
        var names = Enumerable.Range(0, 64).Select(i => "seeder " + i).ToArray();
        var seeding = Seeding(names);
        var runs = 0;
        seeding.WhenFinished(() => Run(ref runs));

        Parallel.ForEach(names.Concat(names), seeding.Report);

        Assert.Multiple(() =>
        {
            Assert.That(seeding.IsFinished, Is.True);
            Assert.That(runs, Is.EqualTo(1));
        });
    }

    [Test]
    public void AddStartupSeeder_SeveralSeeders_RegistersEachNameAndOneRegistryForTheHost()
    {
        var services = new ServiceCollection().AddLogging();

        services.AddStartupSeeder("people").AddStartupSeeder("demo").AddStartupSeeding();

        using var provider = services.BuildServiceProvider();
        var seeding = provider.GetRequiredService<StartupSeeding>();
        Assert.Multiple(() =>
        {
            Assert.That(services.Count(descriptor => descriptor.ServiceType == typeof(StartupSeeding) && descriptor.Lifetime == ServiceLifetime.Singleton), Is.EqualTo(1));
            Assert.That(provider.GetServices<StartupSeeder>().Select(seeder => seeder.Name), Is.EquivalentTo(new[] { "people", "demo" }));
            Assert.That(provider.GetRequiredService<StartupSeeding>(), Is.SameAs(seeding));
            Assert.That(seeding.IsFinished, Is.False);
        });
    }

    private static StartupSeeding Seeding(params string[] names) =>
        new(names.Select(name => new StartupSeeder(name)), NullLogger<StartupSeeding>.Instance);

    private static Task Run(ref int runs)
    {
        Interlocked.Increment(ref runs);
        return Task.CompletedTask;
    }

    private sealed class CountingLogger : ILogger<StartupSeeding>
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error && exception is InvalidOperationException)
            {
                Errors++;
            }
        }
    }

    // As a seeder does: its whole work inside try, the report in finally.
    private static async Task FailingSeederAsync(StartupSeeding seeding)
    {
        try
        {
            await Task.Yield();
            throw new InvalidOperationException("the seeding failed");
        }
        finally
        {
            seeding.Report("people");
        }
    }
}
