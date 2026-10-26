using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>The registrations of merge tags (ADR-0065), which the core composer makes.</summary>
internal static class FormBuilderMergeTagServices
{
    /// <summary>
    /// Registers the collections of the site's own merge-tag sources and filters, the built-in sources, the culture
    /// formats and the service, with the start-up check of the site's sources and filters.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderMergeTags(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Asking for the collection builders registers the collections when the container is built, whichever composer
        // asked first.
        builder.JironFormBuilder().MergeTagSources();
        builder.JironFormBuilder().MergeTagFilters();
        builder.Services.AddMergeTagService();
        return builder;
    }

    /// <summary>Registers the service over the collections of the site's own sources and filters, and the start-up check of both.</summary>
    public static IServiceCollection AddMergeTagService(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<MergeTagCultures>();
        services.TryAddSingleton<MergeTagClock>();
        services.TryAddSingleton(provider => new MergeTagRegistry(
            BuiltInSources(provider.GetRequiredService<MergeTagClock>(), provider.GetRequiredService<MergeTagCultures>()),
            provider.GetRequiredService<FormBuilderMergeTagSourceCollection>(),
            provider.GetRequiredService<FormBuilderMergeTagFilterCollection>()));
        services.TryAddSingleton<IFormBuilderMergeTagService, FormBuilderMergeTagService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, MergeTagStartupCheck>());
        return services;
    }

    /// <summary>The built-in sources this version has, in the order the picker offers them.</summary>
    public static IReadOnlyList<IFormBuilderMergeTagSource> BuiltInSources(MergeTagClock clock, MergeTagCultures cultures) =>
    [
        new EntryMergeTagSource(clock, cultures),
        new FormMergeTagSource(),
        new QueryMergeTagSource(),
        new ContextMergeTagSource(),
        new DateMergeTagSource(clock, cultures),
    ];
}
