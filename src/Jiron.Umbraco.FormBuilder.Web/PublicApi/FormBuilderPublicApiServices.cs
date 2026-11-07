using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Web.Common.ApplicationBuilder;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>The registrations of the public API (ADR-0084), which the Web composer makes.</summary>
internal static class FormBuilderPublicApiServices
{
    /// <summary>The name of the pipeline filter that puts the public API's middleware before Umbraco's routing.</summary>
    public const string PipelineFilterName = "Jiron.FormBuilder.PublicApi";

    /// <summary>
    /// Adds the controllers below the configured base path, their filter, the middleware before routing, and the OpenAPI
    /// document <c>jfb-public</c> with its operation names. Safe to call more than once.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderPublicApi(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(FormBuilderPublicApiController).Assembly)
            .ConfigureApplicationPartManager(manager =>
            {
                if (!manager.FeatureProviders.OfType<FormBuilderPublicControllerFeatureProvider>().Any())
                {
                    manager.FeatureProviders.Add(new FormBuilderPublicControllerFeatureProvider());
                }
            });
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, FormBuilderPublicApiRoutes>());
        builder.Services.TryAddSingleton<FormBuilderPublicApiPaths>();
        builder.Services.TryAddScoped<FormBuilderPublicApiFilter>();
        builder.Services.TryAddSingleton<ServerKeys>();
        builder.Services.TryAddSingleton<FormBuilderRateLimiter>();
        builder.Services.TryAddSingleton<FormRateLimits>();
        builder.Services.TryAddSingleton<FormBuilderRateLimitMiddleware>();
        builder.Services.TryAddSingleton<FormBuilderPublicApiMiddleware>();
        builder.Services.TryAddSingleton<Picker.FormBuilderDeliveryApiMiddleware>();
        builder.Services.TryAddSingleton<FormRequests>();
        builder.Services.TryAddSingleton<UploadTokens>();
        builder.Services.TryAddSingleton<ChallengeTokens>();
        builder.Services.TryAddSingleton<PreviewTickets>();
        builder.Services.TryAddSingleton<PlacementTokens>();
        builder.Services.TryAddSingleton<IVerificationTokens, VerificationTokens>();
        builder.Services.TryAddSingleton<ChallengeRequests>();
        builder.Services.TryAddSingleton<UploadRequests>();
        builder.Services.TryAddSingleton<EntryRequests>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<SwaggerGenOptions>, ConfigureFormBuilderPublicApiSwagger>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationIdHandler, FormBuilderPublicOperationIdHandler>());
        builder.Services.Configure<UmbracoPipelineOptions>(options =>
        {
            if (!options.PipelineFilters.Any(filter => filter.Name == PipelineFilterName))
            {
                // The form picker's markers in the Content Delivery API's answers are resolved on the way out (ADR-0127).
                options.AddFilter(new UmbracoPipelineFilter(PipelineFilterName)
                {
                    PrePipeline = app => app.UseMiddleware<Picker.FormBuilderDeliveryApiMiddleware>().UseMiddleware<FormBuilderPublicApiMiddleware>(),
                    PostRouting = app => app.UseMiddleware<FormBuilderRateLimitMiddleware>(),
                });
            }
        });
        return builder;
    }
}

/// <summary>The public API's paths, read once from the host configuration (ADR-0084).</summary>
/// <param name="options">The host configuration.</param>
internal sealed class FormBuilderPublicApiPaths(IOptions<FormBuilderOptions> options)
{
    /// <summary>The path every endpoint lives under: the base path and the version, such as <c>/jfb/api/v1</c>.</summary>
    public string Root { get; } = options.Value.PublicApi.BasePath + "/" + FormBuilderPublicApi.Version;

    /// <summary>The origins besides the site's own that may call the API from the browser, as browsers send them.</summary>
    public IReadOnlySet<string> AllowedOrigins { get; } =
        options.Value.PublicApi.AllowedOrigins.Select(PublicApiRules.Origin).OfType<string>().ToHashSet(StringComparer.Ordinal);
}

/// <summary>Puts the base path and the version before the route of every controller of the public API (ADR-0084).</summary>
/// <param name="paths">The public API's paths.</param>
internal sealed class FormBuilderPublicApiRoutes(FormBuilderPublicApiPaths paths) : IConfigureOptions<MvcOptions>
{
    /// <inheritdoc />
    public void Configure(MvcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Conventions.Add(new Prefix(paths.Root.TrimStart('/')));
    }

    private sealed class Prefix(string root) : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            if (!typeof(FormBuilderPublicApiController).IsAssignableFrom(controller.ControllerType))
            {
                return;
            }

            var prefix = new AttributeRouteModel(new RouteAttribute(root));
            foreach (var selector in controller.Selectors)
            {
                selector.AttributeRouteModel = selector.AttributeRouteModel is { } own
                    ? AttributeRouteModel.CombineAttributeRouteModel(prefix, own)
                    : prefix;
            }
        }
    }
}

/// <summary>
/// Adds the public API's controllers to MVC: they are internal, like the services they use, and MVC finds only public
/// controllers by itself.
/// </summary>
internal sealed class FormBuilderPublicControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    /// <inheritdoc />
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        foreach (var type in typeof(FormBuilderPublicControllerFeatureProvider).Assembly.GetTypes())
        {
            var info = type.GetTypeInfo();
            if (info is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(FormBuilderPublicApiController).IsAssignableFrom(type)
                && !feature.Controllers.Contains(info))
            {
                feature.Controllers.Add(info);
            }
        }
    }
}
