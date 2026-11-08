using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>The registrations of option lists and sources (ADR-0073), which the core composer makes.</summary>
internal static class FormBuilderOptionListServices
{
    /// <summary>
    /// Registers the collection of option sources with the standard lists, its registry and its start-up check, the
    /// kinds with what content and data type lists read from Umbraco, the handler that drops what they keep when the
    /// site changes and what fetches and keeps URL lists' options, the library's lists with their cache refresher, the
    /// option service with what holds a request's answers up a chain of parents, what resolves a draft's options for the
    /// designer's canvas, and the publish check of a field's source configuration.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderOptionLists(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Asking for the collection builder registers the collection when the container is built, whichever composer
        // asked first.
        builder.JironFormBuilder().OptionSources();
        builder.CacheRefreshers().Add<FormBuilderOptionListCacheRefresher>();
        builder.Services.TryAddSingleton<OptionSourceRegistry>();
        builder.Services.TryAddSingleton<MergeTagCultures>();
        builder.Services.TryAddSingleton<OptionListUmbraco>();
        builder.Services.TryAddSingleton<UrlOptionFetcher>();
        builder.Services.TryAddSingleton<OptionKindRegistry>();
        builder.AddNotificationHandler<ContentCacheRefresherNotification, OptionSiteCacheHandler>();
        builder.AddNotificationHandler<ContentTypeCacheRefresherNotification, OptionSiteCacheHandler>();
        builder.AddNotificationHandler<DataTypeCacheRefresherNotification, OptionSiteCacheHandler>();
        builder.AddNotificationHandler<PublicAccessCacheRefresherNotification, OptionSiteCacheHandler>();
        builder.AddNotificationHandler<LanguageCacheRefresherNotification, OptionSiteCacheHandler>();
        builder.Services.TryAddSingleton<OptionListService>();
        // The library preview resolves through the built-in service, whichever one a site registers (ADR-0214).
        builder.Services.TryAddSingleton<FormBuilderOptionService>();
        builder.Services.TryAddSingleton<IFormBuilderOptionService>(services => services.GetRequiredService<FormBuilderOptionService>());
        builder.Services.TryAddSingleton<OptionListLibrary>();
        builder.Services.TryAddSingleton<CascadingOptions>();
        builder.Services.TryAddSingleton<DraftOptions>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormPublishCheck, OptionSourcePublishCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, OptionSourceStartupCheck>());
        return builder;
    }
}
