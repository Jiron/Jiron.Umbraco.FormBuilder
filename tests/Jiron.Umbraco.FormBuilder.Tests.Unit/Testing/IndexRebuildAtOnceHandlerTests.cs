using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.DependencyInjection;
using Umbraco.Cms.Infrastructure.Examine;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The test host's start-up rebuild (ROADMAP 24.1): what Umbraco's own handler rebuilds, with no delay, on the
/// background queue, once per host, only on a site that runs, and only once the host's own seeding has finished; and
/// the composer that puts it and the jobs' wait in place of Umbraco's.
/// </summary>
[TestFixture]
public sealed class IndexRebuildAtOnceHandlerTests
{
    [Test]
    public async Task HandleAsync_BeforeTheSeedingHasFinished_AsksForNoRebuildAndDoesNotWaitForIt()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();
        var seeding = Seeding("people");

        var handled = Handler(rebuilder, new IndexRebuildOnce(), seeding, SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(handled.IsCompletedSuccessfully, Is.True, "the handlers after this one are not held up");
            Assert.That(rebuilder.ReceivedCalls(), Is.Empty);
        });
        seeding.Report("people");
        await rebuilder.Received(1).RebuildIndexesAsync(false, TimeSpan.Zero, true);
    }

    [Test]
    public async Task HandleAsync_ColdBoot_RebuildsEveryIndexWithNoDelayOnTheBackgroundQueue()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();

        await Handler(rebuilder, new IndexRebuildOnce(), Seeding(), SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);

        await rebuilder.Received(1).RebuildIndexesAsync(false, TimeSpan.Zero, true);
        await rebuilder.Received(1).RebuildIndexesAsync(Arg.Any<bool>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleAsync_WarmBoot_RebuildsOnlyTheEmptyIndexesWithNoDelay()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();

        await Handler(rebuilder, new IndexRebuildOnce(), Seeding(), SyncBootState.WarmBoot).HandleAsync(Started(), CancellationToken.None);

        await rebuilder.Received(1).RebuildIndexesAsync(true, TimeSpan.Zero, true);
    }

    [Test]
    public async Task HandleAsync_TheNotificationAgain_RebuildsNoMore()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();
        var once = new IndexRebuildOnce();
        var seeding = Seeding("people");

        // The container makes a handler for each notification; a restart publishes it again.
        await Handler(rebuilder, once, seeding, SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);
        await Handler(rebuilder, once, seeding, SyncBootState.ColdBoot).HandleAsync(new UmbracoApplicationStartedNotification(isRestarting: true), CancellationToken.None);
        seeding.Report("people");
        await Handler(rebuilder, once, seeding, SyncBootState.ColdBoot).HandleAsync(new UmbracoApplicationStartedNotification(isRestarting: true), CancellationToken.None);

        await rebuilder.Received(1).RebuildIndexesAsync(Arg.Any<bool>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleAsync_EachHostOfAProcess_RebuildsOnce()
    {
        var first = Substitute.For<IIndexRebuilder>();
        var second = Substitute.For<IIndexRebuilder>();

        await Handler(first, new IndexRebuildOnce(), Seeding(), SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);
        await Handler(second, new IndexRebuildOnce(), Seeding(), SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);

        await first.Received(1).RebuildIndexesAsync(false, TimeSpan.Zero, true);
        await second.Received(1).RebuildIndexesAsync(false, TimeSpan.Zero, true);
    }

    [TestCase(RuntimeLevel.Install)]
    [TestCase(RuntimeLevel.Upgrade)]
    [TestCase(RuntimeLevel.BootFailed)]
    public async Task HandleAsync_ASiteThatDoesNotRun_RebuildsNothingAndKeepsItsOnce(RuntimeLevel level)
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();
        var once = new IndexRebuildOnce();
        var seeding = Seeding("people");

        await Handler(rebuilder, once, seeding, SyncBootState.ColdBoot, level).HandleAsync(Started(), CancellationToken.None);
        seeding.Report("people");

        Assert.Multiple(() =>
        {
            Assert.That(rebuilder.ReceivedCalls(), Is.Empty);
            Assert.That(once.TryClaim(), Is.True, "the rebuild is still to come once the site runs");
        });
    }

    [Test]
    public void TryClaim_ManyAtOnce_IsTrueForExactlyOne()
    {
        var once = new IndexRebuildOnce();

        var claims = Enumerable.Range(0, 64).AsParallel().Select(_ => once.TryClaim()).ToList();

        Assert.That(claims.Count(claimed => claimed), Is.EqualTo(1));
    }

    [Test]
    public void ReplaceUmbracosStartupRebuild_WithUmbracosHandler_LeavesOnlyItsOwn()
    {
        var builder = Builder();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, RebuildOnStartedHandler>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, OtherStartedHandler>();

        StartupMarkerComposer.ReplaceUmbracosStartupRebuild(builder);

        var handlers = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartedNotification>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        Assert.Multiple(() =>
        {
            Assert.That(handlers, Is.EquivalentTo(new[] { typeof(OtherStartedHandler), typeof(IndexRebuildAtOnceHandler) }));
            Assert.That(builder.Services.Count(descriptor => descriptor.ServiceType == typeof(IndexRebuildOnce) && descriptor.Lifetime == ServiceLifetime.Singleton), Is.EqualTo(1));
            Assert.That(builder.Services.Count(descriptor => descriptor.ServiceType == typeof(StartupSeeding) && descriptor.Lifetime == ServiceLifetime.Singleton), Is.EqualTo(1));
        });
    }

    [Test]
    public void ReplaceUmbracosStartupRebuild_WithoutUmbracosHandler_Throws()
    {
        var builder = Builder();

        var error = Assert.Throws<InvalidOperationException>(() => StartupMarkerComposer.ReplaceUmbracosStartupRebuild(builder));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain(nameof(RebuildOnStartedHandler)));
            Assert.That(builder.Services, Is.Empty, "nothing of its own registered either");
        });
    }

    [Test]
    public void AddBackgroundJobs_Umbracos_RegistersItsJobsServiceAsAHostedServiceByItsType()
    {
        // How Umbraco registers the service the composer takes out: read from Umbraco's own registration.
        var builder = Builder();

        builder.AddBackgroundJobs();

        var jobs = builder.Services.Where(descriptor => descriptor is { IsKeyedService: false, ImplementationType: not null }
            && descriptor.ImplementationType == typeof(DistributedBackgroundJobHostedService)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(jobs, Has.Count.EqualTo(1));
            Assert.That(jobs[0].ServiceType, Is.EqualTo(typeof(IHostedService)));
            Assert.That(jobs[0].Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
        });
    }

    [Test]
    public void StartUmbracosJobsAfterSeeding_WithUmbracosBackgroundJobs_PutsItsOwnInUmbracosPlaceWithNoDelay()
    {
        var builder = Builder();
        builder.AddBackgroundJobs();
        var before = builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList();
        var place = before.FindIndex(descriptor => descriptor.ImplementationType == typeof(DistributedBackgroundJobHostedService));

        StartupMarkerComposer.StartUmbracosJobsAfterSeeding(builder);

        var after = builder.Services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService)).ToList();
        builder.Services.AddLogging().AddOptions();
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Multiple(() =>
        {
            Assert.That(after.Select(descriptor => descriptor.ImplementationType), Has.No.Member(typeof(DistributedBackgroundJobHostedService)));
            Assert.That(after, Has.Count.EqualTo(before.Count), "one in place of Umbraco's");
            Assert.That(after[place].ImplementationFactory, Is.Not.Null, "in Umbraco's place");
            Assert.That(after[place].ImplementationFactory!(provider), Is.TypeOf<UmbracoJobsAfterSeeding>().And.SameAs(provider.GetRequiredService<UmbracoJobsAfterSeeding>()));
            Assert.That(builder.Services.Count(descriptor => descriptor.ServiceType == typeof(UmbracoJobsAfterSeeding)), Is.EqualTo(1));
            Assert.That(provider.GetRequiredService<IOptionsMonitor<DistributedJobSettings>>().CurrentValue.Delay, Is.EqualTo(TimeSpan.Zero));
            Assert.That(provider.GetRequiredService<StartupSeeding>().IsFinished, Is.True, "no seeder announced here");
        });
    }

    [Test]
    public void StartUmbracosJobsAfterSeeding_WithoutUmbracosJobsService_Throws()
    {
        var builder = Builder();

        var error = Assert.Throws<InvalidOperationException>(() => StartupMarkerComposer.StartUmbracosJobsAfterSeeding(builder));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain(nameof(DistributedBackgroundJobHostedService)));
            Assert.That(builder.Services, Is.Empty, "nothing of its own registered either");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Compose_TheOption_ReplacesBothOfUmbracosOrNeither(bool on)
    {
        var builder = Builder(new Dictionary<string, string?>
        {
            [FormBuilderTestingOptions.SectionName + ":" + nameof(FormBuilderTestingOptions.StartupWorkAtOnce)] = on ? "true" : "false",
        });
        builder.AddBackgroundJobs();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, RebuildOnStartedHandler>();

        new StartupMarkerComposer().Compose(builder);

        var types = builder.Services.Select(descriptor => descriptor.IsKeyedService ? null : descriptor.ImplementationType).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(types.Contains(typeof(RebuildOnStartedHandler)), Is.EqualTo(!on));
            Assert.That(types.Contains(typeof(DistributedBackgroundJobHostedService)), Is.EqualTo(!on));
            Assert.That(types.Contains(typeof(IndexRebuildAtOnceHandler)), Is.EqualTo(on));
            Assert.That(builder.Services.Any(descriptor => descriptor.ServiceType == typeof(UmbracoJobsAfterSeeding)), Is.EqualTo(on));
        });
    }

    private static IUmbracoBuilder Builder(IDictionary<string, string?>? settings = null)
    {
        var builder = Substitute.For<IUmbracoBuilder>();
        builder.Services.Returns(new ServiceCollection());
        builder.Config.Returns(new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        return builder;
    }

    private static StartupSeeding Seeding(params string[] names) =>
        new(names.Select(name => new StartupSeeder(name)), NullLogger<StartupSeeding>.Instance);

    private static IndexRebuildAtOnceHandler Handler(IIndexRebuilder rebuilder, IndexRebuildOnce once, StartupSeeding seeding, SyncBootState boot, RuntimeLevel level = RuntimeLevel.Run)
    {
        var bootState = Substitute.For<ISyncBootStateAccessor>();
        bootState.GetSyncBootState().Returns(boot);
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        return new IndexRebuildAtOnceHandler(bootState, rebuilder, runtime, once, seeding);
    }

    private static UmbracoApplicationStartedNotification Started() => new(isRestarting: false);

    private sealed class OtherStartedHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
    {
        public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
