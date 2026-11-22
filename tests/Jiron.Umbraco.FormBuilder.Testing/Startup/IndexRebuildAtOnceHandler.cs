using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Examine;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// Rebuilds Umbraco's search indexes as soon as a test host has started (ROADMAP 24.1). Umbraco's own handler of the
/// started notification asks for the same rebuild with a fixed minute's delay, and the browser tests' start-up wait
/// idled through that minute; <see cref="StartupMarkerComposer"/> puts this handler in its place when
/// <c>JironFormBuilderTesting:IndexRebuildAtOnce</c> is on. It rebuilds what Umbraco would (every index on a cold boot,
/// the empty ones otherwise), on Umbraco's background queue as Umbraco does, and only once per host: the notification
/// comes again when the host restarts, and the once is the host's own (<see cref="IndexRebuildOnce"/>), not the
/// process's, so every host a test process starts rebuilds once.
/// </summary>
public sealed class IndexRebuildAtOnceHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
{
    private readonly ISyncBootStateAccessor _bootState;
    private readonly IIndexRebuilder _rebuilder;
    private readonly IRuntimeState _runtime;
    private readonly IndexRebuildOnce _once;

    /// <summary>Creates the handler; the container makes one for each notification, and they share the host's once.</summary>
    public IndexRebuildAtOnceHandler(ISyncBootStateAccessor bootState, IIndexRebuilder rebuilder, IRuntimeState runtime, IndexRebuildOnce once)
    {
        _bootState = bootState;
        _rebuilder = rebuilder;
        _runtime = runtime;
        _once = once;
    }

    /// <inheritdoc />
    public async Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
    {
        // As Umbraco's own handler: only a site that runs, not one that installs or upgrades, and once.
        if (_runtime.Level != RuntimeLevel.Run || !_once.TryClaim())
        {
            return;
        }

        var onlyEmpty = _bootState.GetSyncBootState() != SyncBootState.ColdBoot;
        await _rebuilder.RebuildIndexesAsync(onlyEmpty, TimeSpan.Zero, useBackgroundThread: true);
    }
}

/// <summary>Whether a host's start-up rebuild has been asked for: one per host, so its handlers ask once.</summary>
public sealed class IndexRebuildOnce
{
    private int _claimed;

    /// <summary>True the first time it is called, false after that.</summary>
    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}
