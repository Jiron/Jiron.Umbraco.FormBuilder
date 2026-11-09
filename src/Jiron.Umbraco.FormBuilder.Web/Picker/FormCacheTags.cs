using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The output cache's tags of a page that uses forms (ADR-0004, ADR-0132): <see cref="AnyForm"/> and
/// <c>jfb-form-&lt;key&gt;</c> for each form the "used on" index names for it, so a change to a form evicts exactly the
/// pages that use it. A page that uses none gets no tag. When the index cannot be read, the page gets
/// <see cref="AnyForm"/> only and is logged; it is never kept from the cache or from rendering.
/// </summary>
/// <param name="forms">The forms a page uses.</param>
/// <param name="logger">Where a failed read is logged.</param>
internal sealed partial class FormCacheTags(PageForms forms, ILogger<FormCacheTags> logger) : IWebsiteOutputCacheTagProvider
{
    /// <summary>The tag of every page that uses a form.</summary>
    public const string AnyForm = "jfb-form";

    /// <summary>The tag of the pages that use a form.</summary>
    /// <param name="formKey">The form's key.</param>
    public static string Of(Guid formKey) => AnyForm + "-" + formKey.ToString("D");

    /// <inheritdoc />
    public IEnumerable<string> GetTags(IPublishedContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        IReadOnlyList<Guid> used;
        try
        {
            used = forms.Of(content.Key);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogReadFailed(logger, content.Key, LoggedException.Of(exception));
            return [AnyForm];
        }

        return used.Count == 0 ? [] : [AnyForm, .. used.Select(Of)];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not read the forms content {ContentKey} uses; its cached page is evicted only with every form's")]
    private static partial void LogReadFailed(ILogger logger, Guid contentKey, Exception exception);
}

/// <summary>
/// Evicts the cached pages that use a form after a change visitors see (ADR-0004, ADR-0132): a publish, an unpublish,
/// a rename, a move to or from the trash, a delete. It follows the form cache refresher, which runs on every server, so
/// each server evicts from its own output cache; a change to many forms at once, or one it cannot read, evicts every
/// page that uses a form. With the output cache off, Umbraco registers no cache manager, and nothing is evicted.
/// </summary>
/// <param name="services">The site's services, which hold Umbraco's website output cache only while it is on.</param>
internal sealed class FormCacheEviction(IServiceProvider services) : INotificationAsyncHandler<FormBuilderFormCacheRefresherNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(FormBuilderFormCacheRefresherNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (services.GetService<IWebsiteOutputCacheManager>() is not { } cache)
        {
            return;
        }

        if (notification.MessageType == MessageType.RefreshByPayload && notification.MessageObject is FormCacheChange[] changes)
        {
            foreach (var tag in changes.Select(change => change.Key == Guid.Empty ? FormCacheTags.AnyForm : FormCacheTags.Of(change.Key)).Distinct(StringComparer.Ordinal))
            {
                await cache.EvictByTagAsync(tag, cancellationToken);
            }

            return;
        }

        await cache.EvictByTagAsync(FormCacheTags.AnyForm, cancellationToken);
    }
}
