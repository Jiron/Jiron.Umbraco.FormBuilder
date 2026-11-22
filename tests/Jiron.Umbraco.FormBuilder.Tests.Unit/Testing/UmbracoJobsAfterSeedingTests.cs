using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The wait in front of Umbraco's distributed jobs on a test host (ROADMAP 24.1): Umbraco's service is made and started
/// only once the host's own seeding has finished, stopped with the host, and never started when the host stops first.
/// A fake stands in for Umbraco's service.
/// </summary>
[TestFixture]
public sealed class UmbracoJobsAfterSeedingTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    [Test]
    public async Task StartAsync_BeforeTheSeedingHasFinished_NeitherMakesNorStartsUmbracos()
    {
        var umbracos = new FakeHostedService();
        var made = 0;
        using var service = Service(Seeding("people"), () =>
        {
            made++;
            return umbracos;
        });

        await service.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(made, Is.Zero);
            Assert.That(umbracos.Started.Task.IsCompleted, Is.False);
        });
    }

    [Test]
    public async Task StartAsync_ThenTheSeedingFinishes_StartsUmbracosOnce()
    {
        var umbracos = new FakeHostedService();
        var seeding = Seeding("people", "demo");
        var made = 0;
        using var service = Service(seeding, () =>
        {
            made++;
            return umbracos;
        });

        await service.StartAsync(CancellationToken.None);
        seeding.Report("people");
        await Task.Delay(100);
        var afterTheFirst = umbracos.Started.Task.IsCompleted;
        seeding.Report("demo");
        await umbracos.Started.Task.WaitAsync(Limit);

        Assert.Multiple(() =>
        {
            Assert.That(afterTheFirst, Is.False, "not before the last seeder");
            Assert.That(made, Is.EqualTo(1));
            Assert.That(umbracos.Starts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task StartAsync_TheSeedingFinishedAlready_StartsUmbracos()
    {
        var umbracos = new FakeHostedService();
        using var service = Service(Seeding(), () => umbracos);

        await service.StartAsync(CancellationToken.None);

        await umbracos.Started.Task.WaitAsync(Limit);
        Assert.That(umbracos.Starts, Is.EqualTo(1));
    }

    [Test]
    public async Task StopAsync_AfterUmbracosStarted_StopsIt()
    {
        var umbracos = new FakeHostedService();
        using var service = Service(Seeding(), () => umbracos);
        await service.StartAsync(CancellationToken.None);
        await umbracos.Started.Task.WaitAsync(Limit);

        await service.StopAsync(CancellationToken.None).WaitAsync(Limit);

        Assert.That(umbracos.Stops, Is.EqualTo(1));
    }

    [Test]
    public async Task StopAsync_BeforeTheSeedingHasFinished_NeverStartsUmbracos()
    {
        var umbracos = new FakeHostedService();
        var seeding = Seeding("people");
        var made = 0;
        using var service = Service(seeding, () =>
        {
            made++;
            return umbracos;
        });
        await service.StartAsync(CancellationToken.None);

        await service.StopAsync(CancellationToken.None).WaitAsync(Limit);
        seeding.Report("people");
        await Task.Delay(200);

        Assert.Multiple(() =>
        {
            Assert.That(made, Is.Zero);
            Assert.That(umbracos.Starts, Is.Zero);
            Assert.That(umbracos.Stops, Is.Zero);
        });
    }

    [Test]
    public async Task Dispose_AfterUmbracosStarted_DisposesIt()
    {
        var umbracos = new FakeHostedService();
        var service = Service(Seeding(), () => umbracos);
        await service.StartAsync(CancellationToken.None);
        await umbracos.Started.Task.WaitAsync(Limit);
        await service.StopAsync(CancellationToken.None).WaitAsync(Limit);

        service.Dispose();

        Assert.That(umbracos.Disposed, Is.True, "the instance is the service's own, not the container's");
    }

    private static UmbracoJobsAfterSeeding Service(StartupSeeding seeding, Func<IHostedService> umbracos) =>
        new(seeding, umbracos, NullLogger<UmbracoJobsAfterSeeding>.Instance);

    private static StartupSeeding Seeding(params string[] names) =>
        new(names.Select(name => new StartupSeeder(name)), NullLogger<StartupSeeding>.Instance);

    private sealed class FakeHostedService : IHostedService, IDisposable
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public bool Disposed { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            Started.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;
    }
}
