using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>The registrations of option lists and sources (ADR-0073), which the core composer makes.</summary>
internal static class FormBuilderOptionListServices
{
    /// <summary>
    /// Registers the collection of option sources with the standard lists, its registry and its start-up check, the
    /// kinds, the library's lists with their cache refresher, the option service, and the publish check of a field's
    /// source configuration.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderOptionLists(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Asking for the collection builder registers the collection when the container is built, whichever composer
        // asked first.
        builder.JironFormBuilder().OptionSources();
        builder.CacheRefreshers().Add<FormBuilderOptionListCacheRefresher>();
        builder.Services.TryAddSingleton<OptionSourceRegistry>();
        builder.Services.TryAddSingleton<OptionKindRegistry>();
        builder.Services.TryAddSingleton<OptionListService>();
        builder.Services.TryAddSingleton<IFormBuilderOptionService, FormBuilderOptionService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormPublishCheck, OptionSourcePublishCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, OptionSourceStartupCheck>());
        return builder;
    }
}
