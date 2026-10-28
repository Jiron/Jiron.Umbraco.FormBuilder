using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>The registrations of the website rendering (ADR-0092), which the Web composer makes.</summary>
internal static class FormBuilderRenderingServices
{
    /// <summary>The name of the pipeline filter that puts the asset middleware before Umbraco's routing.</summary>
    public const string AssetsFilterName = "Jiron.FormBuilder.WebsiteAssets";

    /// <summary>Adds the website assets, their middleware before routing and the page's asset tags. Safe to call more than once.</summary>
    public static IUmbracoBuilder AddFormBuilderRendering(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<WebsiteAssets>();
        builder.Services.TryAddSingleton<WebsiteAssetsMiddleware>();
        builder.Services.TryAddSingleton<PageAssets>();
        builder.Services.Configure<UmbracoPipelineOptions>(options =>
        {
            if (!options.PipelineFilters.Any(filter => filter.Name == AssetsFilterName))
            {
                options.AddFilter(new UmbracoPipelineFilter(AssetsFilterName) { PrePipeline = app => app.UseMiddleware<WebsiteAssetsMiddleware>() });
            }
        });
        return builder;
    }
}
