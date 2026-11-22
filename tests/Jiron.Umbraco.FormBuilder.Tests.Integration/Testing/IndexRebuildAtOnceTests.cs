using System.Diagnostics;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.HostedServices;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// The browser tests' host rebuilds its indexes at once (ROADMAP 24.1), on a real Umbraco with the composers run as a
/// site runs them and the test host's settings: Umbraco's start-up rebuild, a minute after the start, is gone, the
/// host's own runs as soon as the site has started, and the start-up marker logs its line once, last.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class IndexRebuildAtOnceTests : UmbracoIntegrationTest
{
    private IServiceCollection _services = null!;

    /// <summary>The configuration of every test's site, as the browser tests' host passes it in its environment.</summary>
    public IndexRebuildAtOnceTests()
    {
        InMemoryConfiguration[FormBuilderTestingOptions.SectionName + ":" + nameof(FormBuilderTestingOptions.StartupMarker)] = "true";
        InMemoryConfiguration[FormBuilderTestingOptions.SectionName + ":" + nameof(FormBuilderTestingOptions.IndexRebuildAtOnce)] = "true";
    }

    [Test]
    public void Boot_WithTheRebuildAtOnce_HasNoStartupRebuildOfUmbracosAndItsOwnOnce()
    {
        var handlers = _services
            .Where(descriptor => descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartedNotification>)
                || descriptor.ServiceType == typeof(INotificationHandler<UmbracoApplicationStartedNotification>)
                || descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoRequestBeginNotification>)
                || descriptor.ServiceType == typeof(INotificationHandler<UmbracoRequestBeginNotification>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(handlers, Has.No.Member(typeof(RebuildOnStartedHandler)));
            // A start-up rebuild Umbraco renamed or added would be in its Examine namespace, as both of its have been.
            Assert.That(handlers.Where(type => type?.Namespace == typeof(RebuildOnStartedHandler).Namespace), Is.Empty);
            Assert.That(handlers.Count(type => type == typeof(IndexRebuildAtOnceHandler)), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Started_TheHostsStartupRebuild_IsQueuedOnceAndLogsTheMarkerOnceWithinSeconds()
    {
        // Whether or not the harness published it as it started the host: published again, as on a restart, and again,
        // it asks for one rebuild.
        var events = GetRequiredService<IEventAggregator>();
        await events.PublishAsync(new UmbracoApplicationStartedNotification(isRestarting: false));
        await events.PublishAsync(new UmbracoApplicationStartedNotification(isRestarting: true));

        // The harness runs no worker for Umbraco's background queue, so the test is the worker: it runs what was queued.
        // Umbraco's delay would be inside the queued work, so a minute's delay fails the time limit.
        var queue = GetRequiredService<IBackgroundTaskQueue>();
        var queued = 0;
        var watch = Stopwatch.StartNew();
        while (await Next(queue) is { } work)
        {
            queued++;
            await work(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(30));
        }

        var markers = Markers(GetRequiredService<LogCapture>());
        Assert.Multiple(() =>
        {
            Assert.That(queued, Is.EqualTo(1), "one rebuild queued");
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "with no delay");
            Assert.That(markers, Has.Count.EqualTo(1), "the marker once");
            Assert.That(markers.FirstOrDefault()?.Message, Does.Contain("InternalIndex").And.Contain("ExternalIndex"));
        });
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        _services = builder.Services;
        builder.AddComposers();
        builder.Services.AddLogCapture();
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

    private static List<CapturedLog> Markers(LogCapture log) =>
        log.Entries.Where(entry => entry.Message.StartsWith(StartupMarkerPopulator.Marker, StringComparison.Ordinal)).ToList();
}
