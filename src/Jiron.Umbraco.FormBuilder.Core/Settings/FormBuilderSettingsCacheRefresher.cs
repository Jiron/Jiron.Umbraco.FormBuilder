using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>A saved section, as the cache refresher carries it to every server.</summary>
/// <param name="Scope">"global", or a site's scope.</param>
/// <param name="Section">The section's alias.</param>
internal sealed record SettingsCacheChange(string Scope, string Section);

/// <summary>The keys of the cached sections (ADR-0008): <c>Jiron.FormBuilder.Settings.&lt;scope&gt;.&lt;section&gt;</c>.</summary>
internal static class SettingsCacheKeys
{
    /// <summary>The start of every key.</summary>
    public const string Prefix = "Jiron.FormBuilder.Settings.";

    /// <summary>The end of the loading token's key beside a section's key.</summary>
    public const string LoadingSuffix = ".loading";

    /// <summary>The key of a section in a scope.</summary>
    public static string Of(string scope, string section) => $"{Prefix}{scope}.{section}";

    /// <summary>
    /// The keys a change clears, with their loading tokens: its own scope's, and for the global scope every scope's,
    /// since each site's values rest on the global row.
    /// </summary>
    public static string Pattern(SettingsCacheChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var scope = change.Scope == SettingsScopes.Global ? "[^.]+" : Regex.Escape(change.Scope);
        return $"^{Regex.Escape(Prefix)}{scope}\\.{Regex.Escape(change.Section)}({Regex.Escape(LoadingSuffix)})?$";
    }
}

/// <summary>Published on every server when the settings cache refresher ran.</summary>
/// <param name="messageObject">The refresher's payload.</param>
/// <param name="messageType">How it refreshes.</param>
internal sealed class FormBuilderSettingsCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);

/// <summary>
/// Drops cached settings on every server after a save (ADR-0008, ADR-0043). The saving server runs it at once; the
/// others on their next sync, usually within seconds.
/// </summary>
internal sealed class FormBuilderSettingsCacheRefresher(
    AppCaches appCaches,
    IJsonSerializer serializer,
    IEventAggregator eventAggregator,
    ICacheRefresherNotificationFactory factory)
    : PayloadCacheRefresherBase<FormBuilderSettingsCacheRefresherNotification, SettingsCacheChange>(appCaches, serializer, eventAggregator, factory)
{
    /// <summary>The refresher's id in Umbraco's cache instructions; fixed for good (ADR-0001).</summary>
    public static readonly Guid UniqueId = new("b9ab1ec0-3f41-4c65-842b-339f59ee21c4");

    /// <inheritdoc />
    public override Guid RefresherUniqueId => UniqueId;

    /// <inheritdoc />
    public override string Name => "Jiron Form Builder settings";

    /// <inheritdoc />
    public override void Refresh(SettingsCacheChange[] payloads)
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
        AppCaches.RuntimeCache.ClearByKey(SettingsCacheKeys.Prefix);
        base.RefreshAll();
    }

    /// <summary>Drops what a saved section affects on this server; a change it cannot read drops every section.</summary>
    internal static void Forget(IAppPolicyCache cache, SettingsCacheChange change)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (change is null || !SettingsScopes.IsValid(change.Scope) || !SettingsRepository.IsValidSection(change.Section))
        {
            cache.ClearByKey(SettingsCacheKeys.Prefix);
            return;
        }

        cache.ClearByRegex(SettingsCacheKeys.Pattern(change));
    }
}
