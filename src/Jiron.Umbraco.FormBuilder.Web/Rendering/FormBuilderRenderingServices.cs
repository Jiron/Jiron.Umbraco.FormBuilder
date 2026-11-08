using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;
using Umbraco.Cms.Web.Website.Caching;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>The registrations of the website rendering (ADR-0092, ADR-0093, ADR-0104), which the Web composer makes.</summary>
internal static class FormBuilderRenderingServices
{
    /// <summary>The name of the pipeline filter that puts the asset middleware before Umbraco's routing.</summary>
    public const string AssetsFilterName = "Jiron.FormBuilder.WebsiteAssets";

    /// <summary>The name of the pipeline filter that puts the reader of a post's files before Umbraco's routing (ADR-0102).</summary>
    public const string FilesFilterName = "Jiron.FormBuilder.PostFiles";

    /// <summary>
    /// Adds the website assets, their middleware before routing, the page's asset tags, the rendering of forms (the view
    /// component's services) and the output cache's vary rule for <c>jfb-sent</c>, never a request filter, which would
    /// replace the site's. Safe to call more than once.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderRendering(this IUmbracoBuilder builder)
    {
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderEmailView, RazorEmailView>());
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<WebsiteAssets>();
        builder.Services.TryAddSingleton<WebsiteAssetsMiddleware>();
        builder.Services.TryAddSingleton<PageAssets>();
        builder.Services.TryAddSingleton<FormViews>();
        builder.Services.TryAddSingleton<FormModels>();
        builder.Services.TryAddSingleton<OutcomePreviews>();
        builder.Services.TryAddSingleton<FormPreviewDocuments>();
        builder.Services.TryAddSingleton<ThemePreviewDocuments>();
        builder.Services.TryAddSingleton<FormRendering>();
        builder.Services.TryAddSingleton<FormPostHandler>();
        builder.Services.TryAddSingleton<FormPostFilesMiddleware>();
        builder.Services.TryAddSingleton<SentTokens>();
        builder.Services.TryAddSingleton<SentCopies>();
        builder.Services.TryAddSingleton<BackOfficeVisitors>();
        builder.Services.TryAddScoped<FormMembers>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IWebsiteOutputCacheVaryByProvider, SentVaryBy>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, ThemeStylesheetStartupCheck>());
        builder.Services.Configure<UmbracoPipelineOptions>(options =>
        {
            if (!options.PipelineFilters.Any(filter => filter.Name == AssetsFilterName))
            {
                options.AddFilter(new UmbracoPipelineFilter(AssetsFilterName) { PrePipeline = app => app.UseMiddleware<WebsiteAssetsMiddleware>() });
            }

            // Before routing, which reads a post's form while it picks the page (ADR-0006, ADR-0102).
            if (!options.PipelineFilters.Any(filter => filter.Name == FilesFilterName))
            {
                options.AddFilter(new UmbracoPipelineFilter(FilesFilterName) { PrePipeline = app => app.UseMiddleware<FormPostFilesMiddleware>() });
            }
        });
        return builder;
    }
}
