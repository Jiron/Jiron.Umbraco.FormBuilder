using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>The key of the cached access snapshot and its loading token (ADR-0048).</summary>
internal static class AccessCacheKeys
{
    /// <summary>The start of both keys.</summary>
    public const string Prefix = "Jiron.FormBuilder.Access.";

    /// <summary>The snapshot's key.</summary>
    public const string Snapshot = Prefix + "Snapshot";

    /// <summary>The loading token's key beside the snapshot's.</summary>
    public const string Loading = Snapshot + ".loading";
}

/// <summary>Published on every server when the access cache refresher ran.</summary>
/// <param name="messageObject">The refresher's payload.</param>
/// <param name="messageType">How it refreshes.</param>
internal sealed class FormBuilderAccessCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);

/// <summary>
/// Drops the access snapshot on every server after a change of a grant, a restriction, a folder or a user group
/// (ADR-0048). The changing server drops its own copy at once; the others on their next sync, usually within seconds.
/// </summary>
internal sealed class FormBuilderAccessCacheRefresher(AppCaches appCaches, IEventAggregator eventAggregator, ICacheRefresherNotificationFactory factory)
    : CacheRefresherBase<FormBuilderAccessCacheRefresherNotification>(appCaches, eventAggregator, factory)
{
    /// <summary>The refresher's id in Umbraco's cache instructions; fixed for good (ADR-0001).</summary>
    public static readonly Guid UniqueId = new("5f0d6a2e-8c3b-4e9a-9a51-2d7c4be0f3a8");

    /// <inheritdoc />
    public override Guid RefresherUniqueId => UniqueId;

    /// <inheritdoc />
    public override string Name => "Jiron Form Builder access";

    /// <inheritdoc />
    public override void RefreshAll()
    {
        Forget(AppCaches.RuntimeCache);
        base.RefreshAll();
    }

    /// <summary>Drops this server's snapshot and its loading token.</summary>
    internal static void Forget(IAppPolicyCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.ClearByKey(AccessCacheKeys.Prefix);
    }
}

/// <summary>Drops the access snapshot after a committed change, here at once and on the other servers through the refresher.</summary>
internal sealed class AccessSnapshotInvalidator(AppCaches appCaches, DistributedCache distributedCache)
{
    /// <summary>Called after every committed change the evaluation reads.</summary>
    public void Changed()
    {
        FormBuilderAccessCacheRefresher.Forget(appCaches.RuntimeCache);
        distributedCache.RefreshAll(FormBuilderAccessCacheRefresher.UniqueId);
    }
}
