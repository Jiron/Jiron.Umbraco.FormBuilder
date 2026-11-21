using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Examine;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>
/// The test host's start-up rebuild (ROADMAP 24.1): what Umbraco's own handler rebuilds, with no delay, on the
/// background queue, once per host, and only on a site that runs.
/// </summary>
[TestFixture]
public sealed class IndexRebuildAtOnceHandlerTests
{
    [Test]
    public async Task HandleAsync_ColdBoot_RebuildsEveryIndexWithNoDelayOnTheBackgroundQueue()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();

        await Handler(rebuilder, new IndexRebuildOnce(), SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);

        await rebuilder.Received(1).RebuildIndexesAsync(false, TimeSpan.Zero, true);
        await rebuilder.Received(1).RebuildIndexesAsync(Arg.Any<bool>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleAsync_WarmBoot_RebuildsOnlyTheEmptyIndexesWithNoDelay()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();

        await Handler(rebuilder, new IndexRebuildOnce(), SyncBootState.WarmBoot).HandleAsync(Started(), CancellationToken.None);

        await rebuilder.Received(1).RebuildIndexesAsync(true, TimeSpan.Zero, true);
    }

    [Test]
    public async Task HandleAsync_TheNotificationAgain_RebuildsNoMore()
    {
        var rebuilder = Substitute.For<IIndexRebuilder>();
        var once = new IndexRebuildOnce();

        // The container makes a handler for each notification; a restart publishes it again.
        await Handler(rebuilder, once, SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);
        await Handler(rebuilder, once, SyncBootState.ColdBoot).HandleAsync(new UmbracoApplicationStartedNotification(isRestarting: true), CancellationToken.None);

        await rebuilder.Received(1).RebuildIndexesAsync(Arg.Any<bool>(), Arg.Any<TimeSpan?>(), Arg.Any<bool>());
    }

    [Test]
    public async Task HandleAsync_EachHostOfAProcess_RebuildsOnce()
    {
        var first = Substitute.For<IIndexRebuilder>();
        var second = Substitute.For<IIndexRebuilder>();

        await Handler(first, new IndexRebuildOnce(), SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);
        await Handler(second, new IndexRebuildOnce(), SyncBootState.ColdBoot).HandleAsync(Started(), CancellationToken.None);

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

        await Handler(rebuilder, once, SyncBootState.ColdBoot, level).HandleAsync(Started(), CancellationToken.None);

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

    private static IUmbracoBuilder Builder()
    {
        var builder = Substitute.For<IUmbracoBuilder>();
        builder.Services.Returns(new ServiceCollection());
        return builder;
    }

    private static IndexRebuildAtOnceHandler Handler(IIndexRebuilder rebuilder, IndexRebuildOnce once, SyncBootState boot, RuntimeLevel level = RuntimeLevel.Run)
    {
        var bootState = Substitute.For<ISyncBootStateAccessor>();
        bootState.GetSyncBootState().Returns(boot);
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        return new IndexRebuildAtOnceHandler(bootState, rebuilder, runtime, once);
    }

    private static UmbracoApplicationStartedNotification Started() => new(isRestarting: false);

    private sealed class OtherStartedHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
    {
        public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
