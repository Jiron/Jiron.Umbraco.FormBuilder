using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;

/// <summary>Every <see cref="SettingsSavedNotification"/> a site published, and a switch that makes its handler fail.</summary>
public sealed class SavedNotifications
{
    private readonly List<SettingsSavedNotification> _seen = [];

    /// <summary>The notifications so far, oldest first.</summary>
    public IReadOnlyList<SettingsSavedNotification> Seen => _seen;

    /// <summary>True to make the handler throw after it recorded the notification.</summary>
    public bool Fail { get; set; }

    /// <summary>Records a notification.</summary>
    public void Add(SettingsSavedNotification notification) => _seen.Add(notification);
}

/// <summary>Records each <see cref="SettingsSavedNotification"/>, as an extension's handler would see it.</summary>
public sealed class SavedNotificationHandler(SavedNotifications notifications) : INotificationAsyncHandler<SettingsSavedNotification>
{
    /// <inheritdoc />
    public Task HandleAsync(SettingsSavedNotification notification, CancellationToken cancellationToken)
    {
        notifications.Add(notification);
        return notifications.Fail ? throw new InvalidOperationException("JFB_TEST: a handler that fails") : Task.CompletedTask;
    }
}

/// <summary>
/// The settings repository with a hook after each read, so a test can let another server save between a read and the
/// moment the reader caches what it read.
/// </summary>
internal sealed class HookedSettingsRepository(SettingsRepository inner) : ISettingsRepository
{
    /// <summary>Runs once, after the next read of a section, before the reader goes on.</summary>
    public Func<Task>? AfterNextGet { get; set; }

    /// <summary>How many sections were read.</summary>
    public int Gets { get; private set; }

    /// <inheritdoc />
    public async Task<StoredSettings?> GetAsync(string scope, string section, CancellationToken cancellationToken = default)
    {
        var row = await inner.GetAsync(scope, section, cancellationToken);
        Gets++;
        if (AfterNextGet is { } hook)
        {
            AfterNextGet = null;
            await hook();
        }

        return row;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSettings>> ListAsync(string scope, CancellationToken cancellationToken = default) => inner.ListAsync(scope, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSettings>> ListSectionAsync(string section, CancellationToken cancellationToken = default) => inner.ListSectionAsync(section, cancellationToken);

    /// <inheritdoc />
    public Task<StoredSettings> SaveAsync(SettingsSave save, CancellationToken cancellationToken = default) => inner.SaveAsync(save, cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string scope, string section, int expectedVersion, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(scope, section, expectedVersion, cancellationToken);
}

/// <summary>
/// A server messenger that records the payload instructions a site sends to the other servers and delivers none, so a
/// test sees what the service clears on its own server by itself.
/// </summary>
internal sealed class RecordingServerMessenger : IServerMessenger
{
    private readonly ConcurrentQueue<(Guid Refresher, string Payload)> _refreshes = new();

    /// <summary>Each payload instruction so far: the refresher and its payloads, comma-separated.</summary>
    public IReadOnlyCollection<(Guid Refresher, string Payload)> Refreshes => _refreshes;

    /// <inheritdoc />
    public void QueueRefresh<TPayload>(ICacheRefresher refresher, TPayload[] payload) =>
        _refreshes.Enqueue((refresher.RefresherUniqueId, string.Join(", ", payload)));

    /// <inheritdoc />
    public void QueueRefresh<T>(ICacheRefresher refresher, Func<T, int> getNumericId, params T[] instances)
    {
    }

    /// <inheritdoc />
    public void QueueRefresh<T>(ICacheRefresher refresher, Func<T, Guid> getGuidId, params T[] instances)
    {
    }

    /// <inheritdoc />
    public void QueueRefresh(ICacheRefresher refresher, params int[] numericIds)
    {
    }

    /// <inheritdoc />
    public void QueueRefresh(ICacheRefresher refresher, params Guid[] guidIds)
    {
    }

    /// <inheritdoc />
    public void QueueRemove<T>(ICacheRefresher refresher, Func<T, int> getNumericId, params T[] instances)
    {
    }

    /// <inheritdoc />
    public void QueueRemove(ICacheRefresher refresher, params int[] numericIds)
    {
    }

    /// <inheritdoc />
    public void QueueRefreshAll(ICacheRefresher refresher)
    {
    }

    /// <inheritdoc />
    public void Sync()
    {
    }

    /// <inheritdoc />
    public void SendMessages()
    {
    }
}
