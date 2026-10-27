using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// The keys of the options that content and data type lists keep until the site changes (ADR-0075), and how long a
/// server keeps them at most when nothing changes; a key's load has the key with <c>.loading</c> after it. URL lists
/// keep their copies under keys of their own (ADR-0076).
/// </summary>
internal static class OptionSiteCacheKeys
{
    /// <summary>The start of the keys of content options.</summary>
    public const string ContentPrefix = "Jiron.FormBuilder.OptionSource.content.";

    /// <summary>The start of the keys of data type options.</summary>
    public const string DataTypePrefix = "Jiron.FormBuilder.OptionSource.dataType.";

    /// <summary>The start of the keys of the copies URL lists fetched.</summary>
    public const string UrlPrefix = "Jiron.FormBuilder.OptionSource.url.";

    /// <summary>The longest a server keeps options nobody changed: the time only bounds what it keeps of lists nobody reads.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromHours(12);

    /// <summary>The key of the content options of a configuration under a root, in a culture.</summary>
    public static string Content(ContentOptionListSettings settings, Guid root, string culture)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var types = string.Join(",", settings.DocumentTypes.Select(type => type.ToString("N")).Order(StringComparer.Ordinal));
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{root:N}|{settings.Descendants}|{types}|{settings.ValueFrom.Length}:{settings.ValueFrom}|{settings.LabelFrom.Length}:{settings.LabelFrom}|{settings.Order}|{culture}");
        return ContentPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>The key of a data type's options.</summary>
    public static string DataType(Guid key) => DataTypePrefix + key.ToString("D");

    /// <summary>The key of the copy of a URL list's configuration: the address and the three paths, each with its length.</summary>
    public static string Url(UrlOptionListSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var url = settings.Url ?? string.Empty;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{url.Length}:{url}|{settings.ListPath.Length}:{settings.ListPath}|{settings.ValueFrom.Length}:{settings.ValueFrom}|{settings.LabelFrom.Length}:{settings.LabelFrom}");
        return UrlPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32];
    }

    /// <summary>The key of the load of what <paramref name="key"/> keeps.</summary>
    public static string Loading(string key) => key + ".loading";
}

/// <summary>
/// Drops what content and data type lists keep on every server when the site changes (ADR-0075): Umbraco's own cache
/// refreshers tell each server of a change to content, document types, data types, public access or languages, and
/// publish these notifications there once they ran. A data type's change drops content options too, as a page's
/// values read through its data types.
/// </summary>
/// <param name="appCaches">The caches, whose runtime cache keeps the options.</param>
internal sealed class OptionSiteCacheHandler(AppCaches appCaches) :
    INotificationHandler<ContentCacheRefresherNotification>,
    INotificationHandler<ContentTypeCacheRefresherNotification>,
    INotificationHandler<DataTypeCacheRefresherNotification>,
    INotificationHandler<PublicAccessCacheRefresherNotification>,
    INotificationHandler<LanguageCacheRefresherNotification>
{
    /// <inheritdoc />
    public void Handle(ContentCacheRefresherNotification notification) => ForgetContent();

    /// <inheritdoc />
    public void Handle(ContentTypeCacheRefresherNotification notification) => ForgetContent();

    /// <inheritdoc />
    public void Handle(DataTypeCacheRefresherNotification notification)
    {
        appCaches.RuntimeCache.ClearByKey(OptionSiteCacheKeys.DataTypePrefix);
        ForgetContent();
    }

    /// <inheritdoc />
    public void Handle(PublicAccessCacheRefresherNotification notification) => ForgetContent();

    /// <inheritdoc />
    public void Handle(LanguageCacheRefresherNotification notification) => ForgetContent();

    private void ForgetContent() => appCaches.RuntimeCache.ClearByKey(OptionSiteCacheKeys.ContentPrefix);
}
