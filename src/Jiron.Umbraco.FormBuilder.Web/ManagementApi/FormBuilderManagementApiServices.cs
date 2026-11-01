using System.Reflection;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi;

/// <summary>The registrations of the management API (ADR-0050), which the Web composer makes.</summary>
internal static class FormBuilderManagementApiServices
{
    /// <summary>
    /// Adds the policies (ADR-0049), the controllers, their access and exception filters, and the OpenAPI document
    /// <c>jfb</c> with its operation and schema names. Safe to call more than once.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderManagementApi(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddFormBuilderAuthorization();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(FormBuilderManagementApiController).Assembly)
            .ConfigureApplicationPartManager(manager =>
            {
                if (!manager.FeatureProviders.OfType<FormBuilderControllerFeatureProvider>().Any())
                {
                    manager.FeatureProviders.Add(new FormBuilderControllerFeatureProvider());
                }
            });
        builder.Services.TryAddScoped<FormBuilderAccessFilter>();
        builder.Services.TryAddSingleton<Library.FormSensitivityChecks>();
        builder.Services.TryAddSingleton<Picker.FormBlockTypes>();
        builder.Services.TryAddSingleton<FormBuilderExceptionFilter>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<SwaggerGenOptions>, ConfigureFormBuilderManagementApiSwagger>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationIdHandler, FormBuilderOperationIdHandler>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISchemaIdHandler, FormBuilderSchemaIdHandler>());
        return builder;
    }
}

/// <summary>
/// Adds the management API's controllers to MVC: they are internal, like the services they use, and MVC finds only
/// public controllers by itself, so Jiron Form Builder's public surface stays its extension points.
/// </summary>
internal sealed class FormBuilderControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    /// <inheritdoc />
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        foreach (var type in typeof(FormBuilderControllerFeatureProvider).Assembly.GetTypes())
        {
            var info = type.GetTypeInfo();
            if (info is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }
                && typeof(FormBuilderManagementApiController).IsAssignableFrom(type)
                && !feature.Controllers.Contains(info))
            {
                feature.Controllers.Add(info);
            }
        }
    }
}
