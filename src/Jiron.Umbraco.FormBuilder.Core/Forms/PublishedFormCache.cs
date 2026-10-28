using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A changed form, as the cache refresher carries it to every server; the empty key stands for every form.</summary>
/// <param name="Key">The form's key, or <see cref="Guid.Empty"/> after a change to many forms at once.</param>
internal sealed record FormCacheChange(Guid Key);

/// <summary>
/// The keys of what each server keeps of forms for the website (ADR-0044, ADR-0084): <c>Jiron.FormBuilder.Form.&lt;key&gt;</c>
/// for a form's published version, with its load beside it, and <c>Jiron.FormBuilder.Form.alias.&lt;alias&gt;</c> for the
/// form an alias names.
/// </summary>
internal static class FormCacheKeys
{
    /// <summary>The start of every key.</summary>
    public const string Prefix = "Jiron.FormBuilder.Form.";

    /// <summary>The start of every alias's key.</summary>
    public const string AliasPrefix = Prefix + "alias.";

    /// <summary>The key of a form's published version.</summary>
    public static string Published(Guid key) => Prefix + key.ToString("D");

    /// <summary>The key of the load of a form's published version.</summary>
    public static string PublishedLoading(Guid key) => Published(key) + ".loading";

    /// <summary>The key of the form an alias names, in any letter case.</summary>
    public static string Alias(string alias) => AliasPrefix + alias.ToLowerInvariant();

    /// <summary>The key of the load of the form an alias names.</summary>
    public static string AliasLoading(string alias) => Alias(alias) + ".loading";
}

/// <summary>Published on every server when the form cache refresher ran.</summary>
/// <param name="messageObject">The refresher's payload.</param>
/// <param name="messageType">How it refreshes.</param>
internal sealed class FormBuilderFormCacheRefresherNotification(object messageObject, MessageType messageType)
    : CacheRefresherNotification(messageObject, messageType);

/// <summary>
/// Drops what every server keeps of a form for the website after a change visitors see (ADR-0044, ADR-0084): a
/// publish, an unpublish, a rename, a move to or from the trash, a delete. The changing server runs it at once; the
/// others on their next sync, usually within seconds.
/// </summary>
internal sealed class FormBuilderFormCacheRefresher(
    AppCaches appCaches,
    IJsonSerializer serializer,
    IEventAggregator eventAggregator,
    ICacheRefresherNotificationFactory factory)
    : PayloadCacheRefresherBase<FormBuilderFormCacheRefresherNotification, FormCacheChange>(appCaches, serializer, eventAggregator, factory)
{
    /// <summary>The refresher's id in Umbraco's cache instructions; fixed for good (ADR-0001).</summary>
    public static readonly Guid UniqueId = new("1913e5a5-021d-4c44-980f-33c6b2462b71");

    /// <inheritdoc />
    public override Guid RefresherUniqueId => UniqueId;

    /// <inheritdoc />
    public override string Name => "Jiron Form Builder forms";

    /// <inheritdoc />
    public override void Refresh(FormCacheChange[] payloads)
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
        AppCaches.RuntimeCache.ClearByKey(FormCacheKeys.Prefix);
        base.RefreshAll();
    }

    /// <summary>
    /// Drops what this server keeps of a changed form, and every alias, since a rename frees one; a change it cannot read,
    /// or one to many forms, drops every form.
    /// </summary>
    internal static void Forget(IAppPolicyCache cache, FormCacheChange? change)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (change is null || change.Key == Guid.Empty)
        {
            cache.ClearByKey(FormCacheKeys.Prefix);
            return;
        }

        cache.ClearByKey(FormCacheKeys.Published(change.Key));
        cache.ClearByKey(FormCacheKeys.AliasPrefix);
    }
}

/// <summary>
/// Drops what every server keeps of a form for the website, here at once and on the other servers through the refresher.
/// The form and folder services call it after each committed change visitors see (ADR-0084).
/// </summary>
internal sealed class FormCacheInvalidator(AppCaches appCaches, DistributedCache distributedCache)
{
    /// <summary>After a change to one form.</summary>
    public void Changed(Guid key) => Send(new FormCacheChange(key));

    /// <summary>After a change to the forms of whole folders, which names none of them.</summary>
    public void ChangedAll() => Send(new FormCacheChange(Guid.Empty));

    private void Send(FormCacheChange change)
    {
        FormBuilderFormCacheRefresher.Forget(appCaches.RuntimeCache, change);
        distributedCache.RefreshByPayload(FormBuilderFormCacheRefresher.UniqueId, [change]);
    }
}

/// <summary>
/// A form's published version as the website serves it, with its public projection (ADR-0035), kept until it changes.
/// Its <see cref="PublishedForm.Form"/> is as of the last change visitors see: what only the backoffice shows of it (its
/// folder, its concurrency version, when it last changed) may be older.
/// </summary>
/// <param name="Published">The published version, read with the host's designer locks applied (ADR-0060).</param>
/// <param name="Projection">Its public projection.</param>
internal sealed record CachedForm(PublishedForm Published, PublicForm Projection);

/// <summary>
/// The published forms the website serves (ADR-0044, ADR-0084): each server keeps a form's published version, read once
/// per miss however many requests ask at once (ADR-0043), until <see cref="FormBuilderFormCacheRefresher"/> drops it.
/// A form that is not served (missing from the website, in the trash, unpublished or unreadable) is kept as such when it
/// exists, so asking for it reads the database once; a key or an alias that names no form is never kept, so visitors
/// cannot fill the cache.
/// </summary>
internal sealed partial class PublishedFormCache(
    IFormBuilderFormService forms,
    FieldTypeRegistry types,
    AppCaches appCaches,
    ILogger<PublishedFormCache> logger)
{
    // A form is kept until it changes; the time only bounds what a server keeps of forms nobody asks for.
    private static readonly TimeSpan Duration = TimeSpan.FromHours(12);

    // The marks of what is not served: a form the website does not get, and a key or alias that names no form.
    private static readonly NotServedForm NotServed = new();
    private static readonly NoForm Missing = new();

    /// <summary>The form with this key as the website serves it; null when it is missing, in the trash, unpublished or unreadable.</summary>
    /// <param name="key">The form's key.</param>
    /// <param name="cancellationToken">Stops waiting for the read; the read itself goes on for the other requests.</param>
    public async Task<CachedForm?> GetAsync(Guid key, CancellationToken cancellationToken = default)
    {
        var kept = await RuntimeCacheLoads.GetAsync(
            appCaches.RuntimeCache,
            FormCacheKeys.Published(key),
            FormCacheKeys.PublishedLoading(key),
            () => LoadAsync(key),
            Duration,
            cancellationToken,
            value => value is not NoForm);
        return kept as CachedForm;
    }

    /// <summary>The key of the form an alias names in any letter case, trashed ones included; null when it names none.</summary>
    /// <param name="alias">The alias.</param>
    /// <param name="cancellationToken">Stops waiting for the read; the read itself goes on for the other requests.</param>
    public async Task<Guid?> KeyOfAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        var kept = await RuntimeCacheLoads.GetAsync(
            appCaches.RuntimeCache,
            FormCacheKeys.Alias(alias),
            FormCacheKeys.AliasLoading(alias),
            async () => await forms.GetByAliasAsync(alias, CancellationToken.None) is { } form ? new FormKey(form.Key) : (object)Missing,
            Duration,
            cancellationToken,
            value => value is not NoForm);
        return (kept as FormKey)?.Key;
    }

    // The form's row first, so a key that names no form costs one read.
    private async Task<object> LoadAsync(Guid key)
    {
        if (await forms.GetAsync(key, CancellationToken.None) is null)
        {
            return Missing;
        }

        PublishedForm? published;
        try
        {
            published = await forms.GetPublishedAsync(key, CancellationToken.None);
        }
        catch (FormBuilderValidationException exception)
        {
            // Logged once per server and load, as the mark is kept until the form changes.
            LogUnreadable(logger, key, exception.Problems.Count);
            return NotServed;
        }

        return published is null ? NotServed : new CachedForm(published, PublicProjection.Create(published.Definition, types));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The published version of the form {FormKey} cannot be read, so the website answers it as missing until it is published again. Problems: {Problems}")]
    private static partial void LogUnreadable(ILogger logger, Guid formKey, int problems);

    private sealed record FormKey(Guid Key);

    private sealed class NotServedForm;

    private sealed class NoForm;
}
