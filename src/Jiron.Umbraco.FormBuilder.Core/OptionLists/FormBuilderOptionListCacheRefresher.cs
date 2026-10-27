using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>A changed option list, as the cache refresher carries it to every server.</summary>
/// <param name="Key">The list's key.</param>
internal sealed record OptionListCacheChange(Guid Key);

/// <summary>
/// The keys of what is kept of option lists (ADR-0073): <c>Jiron.FormBuilder.OptionList.&lt;key&gt;</c> for a list's
/// row, and keys that start with it for its load and its kept options, so a change drops them together.
/// </summary>
internal static class OptionListCacheKeys
{
    /// <summary>The start of every key.</summary>
    public const string Prefix = "Jiron.FormBuilder.OptionList.";

    /// <summary>The key of a list's row, which every other key of the list starts with.</summary>
    public static string Row(Guid key) => Prefix + key.ToString("D");

    /// <summary>The key of the load of a list's row.</summary>
    public static string RowLoading(Guid key) => Row(key) + ".loading";

    /// <summary>The key of a list's options kept for a culture, a page and the parent's answers.</summary>
    public static string Options(Guid key, string culture, Guid? page, IReadOnlyList<string> parentValues) =>
        $"{Row(key)}.options.{culture}.{page?.ToString("N") ?? "-"}.{Hash(parentValues)}";

    // The parent's answers in any order as one short text: each value with its length, so no two lists give one text.
    private static string Hash(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            return "-";
        }

        var text = string.Concat(values.Order(StringComparer.Ordinal).Select(value => string.Create(CultureInfo.InvariantCulture, $"{value.Length}:{value}")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }
}

/// <summary>Published on every server when the option list cache refresher ran.</summary>
/// <param name="messageObject">The refresher's payload.</param>
/// <param name="messageType">How it refreshes.</param>
internal sealed class FormBuilderOptionListCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);

/// <summary>
/// Drops what is kept of an option list on every server after a change (ADR-0073). The changing server runs it at
/// once; the others on their next sync, usually within seconds.
/// </summary>
internal sealed class FormBuilderOptionListCacheRefresher(
    AppCaches appCaches,
    IJsonSerializer serializer,
    IEventAggregator eventAggregator,
    ICacheRefresherNotificationFactory factory)
    : PayloadCacheRefresherBase<FormBuilderOptionListCacheRefresherNotification, OptionListCacheChange>(appCaches, serializer, eventAggregator, factory)
{
    /// <summary>The refresher's id in Umbraco's cache instructions; fixed for good (ADR-0001).</summary>
    public static readonly Guid UniqueId = new("8ca74819-3a1e-4d49-b9fe-9de5d8347f07");

    /// <inheritdoc />
    public override Guid RefresherUniqueId => UniqueId;

    /// <inheritdoc />
    public override string Name => "Jiron Form Builder option lists";

    /// <inheritdoc />
    public override void Refresh(OptionListCacheChange[] payloads)
    {
        ArgumentNullException.ThrowIfNull(payloads);
        foreach (var change in payloads)
        {
            Forget(AppCaches.RuntimeCache, change);
        }

        base.Refresh(payloads);
    }

    /// <inheritdoc />
    public override void RefreshAll()
    {
        AppCaches.RuntimeCache.ClearByKey(OptionListCacheKeys.Prefix);
        base.RefreshAll();
    }

    /// <summary>Drops what is kept of a changed list on this server; a change it cannot read drops every list.</summary>
    internal static void Forget(IAppPolicyCache cache, OptionListCacheChange? change)
    {
        ArgumentNullException.ThrowIfNull(cache);
        cache.ClearByKey(change is null || change.Key == Guid.Empty ? OptionListCacheKeys.Prefix : OptionListCacheKeys.Row(change.Key));
    }
}
