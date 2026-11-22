using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.HostedServices;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Infrastructure.Services;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// The browser tests' host does Umbraco's start-up work as soon as its own seeding has finished (ROADMAP 24.1), on a
/// real Umbraco with the composers run as a site runs them and the test host's settings: Umbraco's start-up rebuild and
/// its jobs' hosted service are gone, the host's own are there once, and neither the rebuild nor the jobs start before
/// every announced seeder has reported. The start-up marker logs its line once, last.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class IndexRebuildAtOnceTests : UmbracoIntegrationTest
{
    // A seeder of the test's own, which reports only when the test says so.
    private const string HeldSeeder = "held by the test";

    // Made for each test, as its host is.
    private IDistributedJobService _jobs = null!;
    private IServiceCollection _services = null!;

    /// <summary>The configuration of every test's site, as the browser tests' host passes it in its environment.</summary>
    public IndexRebuildAtOnceTests()
    {
        InMemoryConfiguration[FormBuilderTestingOptions.SectionName + ":" + nameof(FormBuilderTestingOptions.StartupMarker)] = "true";
        InMemoryConfiguration[FormBuilderTestingOptions.SectionName + ":" + nameof(FormBuilderTestingOptions.StartupWorkAtOnce)] = "true";
    }

    [Test]
    public void Boot_WithTheStartupWorkAtOnce_HasNoStartupWorkOfUmbracosAndItsOwnOnce()
    {
        var handlers = _services
            .Where(descriptor => descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartedNotification>)
                || descriptor.ServiceType == typeof(INotificationHandler<UmbracoApplicationStartedNotification>)
                || descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoRequestBeginNotification>)
                || descriptor.ServiceType == typeof(INotificationHandler<UmbracoRequestBeginNotification>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        var hosted = _services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IHostedService)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(handlers, Has.No.Member(typeof(RebuildOnStartedHandler)));
            // A start-up rebuild Umbraco renamed or added would be in its Examine namespace, as both of its have been.
            Assert.That(handlers.Where(type => type?.Namespace == typeof(RebuildOnStartedHandler).Namespace), Is.Empty);
            Assert.That(handlers.Count(type => type == typeof(IndexRebuildAtOnceHandler)), Is.EqualTo(1));
            Assert.That(hosted.Select(descriptor => descriptor.ImplementationType), Has.No.Member(typeof(DistributedBackgroundJobHostedService)));
            Assert.That(GetRequiredService<IEnumerable<IHostedService>>().OfType<UmbracoJobsAfterSeeding>().Count(), Is.EqualTo(1));
            Assert.That(GetRequiredService<IEnumerable<IHostedService>>().OfType<DistributedBackgroundJobHostedService>(), Is.Empty);
            Assert.That(GetRequiredService<IOptionsMonitor<DistributedJobSettings>>().CurrentValue.Delay, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public async Task Started_TheStartupWork_WaitsForEverySeederThenRunsOnceWithinSeconds()
    {
        // Whether or not the harness published it as it started the host: published again, as on a restart, and again,
        // it asks for one rebuild. Every seeder of the host's own has reported then; the test's is still out.
        var events = GetRequiredService<IEventAggregator>();
        await events.PublishAsync(new UmbracoApplicationStartedNotification(isRestarting: false));
        await events.PublishAsync(new UmbracoApplicationStartedNotification(isRestarting: true));
        var seeding = GetRequiredService<StartupSeeding>();

        // The harness runs no worker for Umbraco's background queue, so the test is the worker: it runs what was queued.
        var queue = GetRequiredService<IBackgroundTaskQueue>();
        var queuedBefore = await RunQueuedAsync(queue);
        var jobsBefore = _jobs.ReceivedCalls().Count();
        var finishedBefore = seeding.IsFinished;

        seeding.Report(HeldSeeder);

        // Umbraco's delay would be inside the queued work, so a minute's delay fails the time limit.
        var watch = Stopwatch.StartNew();
        var queuedAfter = await RunQueuedAsync(queue);
        var elapsed = watch.Elapsed;
        await WaitUntilAsync(() => _jobs.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IDistributedJobService.EnsureJobsAsync)));

        var markers = Markers(GetRequiredService<LogCapture>());
        Assert.Multiple(() =>
        {
            Assert.That(finishedBefore, Is.False, "the test's seeder had not reported");
            Assert.That(queuedBefore, Is.Zero, "no rebuild before every seeder has reported");
            Assert.That(jobsBefore, Is.Zero, "no jobs before every seeder has reported");
            Assert.That(queuedAfter, Is.EqualTo(1), "one rebuild queued after");
            Assert.That(elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "with no delay");
            Assert.That(markers, Has.Count.EqualTo(1), "the marker once");
            Assert.That(markers.FirstOrDefault()?.Message, Does.Contain("InternalIndex").And.Contain("ExternalIndex"));
            Assert.That(_jobs.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IDistributedJobService.EnsureJobsAsync)), Is.EqualTo(1), "Umbraco's jobs registered once");
        });
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        _services = builder.Services;
        _jobs = Substitute.For<IDistributedJobService>();
        _jobs.TryTakeRunnableAsync().Returns((IDistributedBackgroundJob?)null);

        // The harness leaves out Umbraco's background jobs; this is how Umbraco's own registration adds the service
        // (IndexRebuildAtOnceHandlerTests reads it from Umbraco), and the fake keeps its jobs off the test's database.
        builder.Services.AddHostedService<DistributedBackgroundJobHostedService>();
        builder.Services.AddUnique(_jobs);
        builder.Services.AddStartupSeeder(HeldSeeder);
        builder.AddComposers();
        builder.Services.AddLogCapture();
    }

    /// <summary>Runs the queued work until none comes within two seconds, and returns how much there was.</summary>
    private static async Task<int> RunQueuedAsync(IBackgroundTaskQueue queue)
    {
        var queued = 0;
        while (await Next(queue) is { } work)
        {
            queued++;
            await work(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        }

        return queued;
    }

    /// <summary>The next work item of the queue, or null when none comes within two seconds.</summary>
    private static async Task<Func<CancellationToken, Task>?> Next(IBackgroundTaskQueue queue)
    {
        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            return await queue.DequeueAsync(wait.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition() && deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(50);
        }
    }

    private static List<CapturedLog> Markers(LogCapture log) =>
        log.Entries.Where(entry => entry.Message.StartsWith(StartupMarkerPopulator.Marker, StringComparison.Ordinal)).ToList();
}
