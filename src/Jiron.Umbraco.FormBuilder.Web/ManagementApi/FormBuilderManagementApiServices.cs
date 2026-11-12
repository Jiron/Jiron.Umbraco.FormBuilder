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
        builder.AddFormBuilderControllers(typeof(FormBuilderManagementApiController).Assembly);
        builder.Services.TryAddScoped<FormBuilderAccessFilter>();
        builder.Services.TryAddSingleton<Library.FormSensitivityChecks>();
        builder.Services.TryAddSingleton<Library.ActionGrantChecks>();
        builder.Services.TryAddSingleton<Picker.FormBlockTypes>();
        builder.Services.TryAddSingleton<Entries.ExportDownloadTickets>();
        builder.Services.TryAddSingleton<TestLimits>();
        builder.Services.TryAddScoped<Entries.ExportDownloadHolders>();
        builder.Services.TryAddSingleton<FormBuilderExceptionFilter>();
        builder.Services.TryAddSingleton<Settings.SettingsSites>();
        builder.Services.TryAddSingleton<Activity.AuditNames>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsScreen, Settings.SettingsScreen<Core.Settings.GeneralSettings>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsScreen, Settings.SettingsScreen<Core.Email.EmailSettings>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsScreen, Settings.SettingsScreen<Core.Settings.ProtectionSettings>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsScreen, Settings.SettingsScreen<Core.Files.FilesSettings>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsScreen, Settings.SettingsScreen<Core.Settings.PrivacySettings>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsScreen, Settings.SettingsScreen<Core.Settings.IntegrationSettings>>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsFacts<Core.Settings.GeneralSettings>, Settings.GeneralFacts>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsFacts<Core.Email.EmailSettings>, Settings.EmailFacts>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsFacts<Core.Settings.ProtectionSettings>, Settings.ProtectionFacts>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsFacts<Core.Files.FilesSettings>, Settings.FilesFacts>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsFacts<Core.Settings.IntegrationSettings>, Settings.IntegrationFacts>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<Settings.ISettingsFacts<Core.Settings.PrivacySettings>, Settings.PrivacyFacts>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<SwaggerGenOptions>, ConfigureFormBuilderManagementApiSwagger>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOperationIdHandler, FormBuilderOperationIdHandler>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISchemaIdHandler, FormBuilderSchemaIdHandler>());
        return builder;
    }

    /// <summary>
    /// Adds the management API's controllers of <paramref name="assembly"/> to MVC: Web's own, and the AI add-on's, which
    /// sit behind the same base controller (ADR-0018). Safe to call more than once for an assembly.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderControllers(this IUmbracoBuilder builder, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(assembly);
        builder.Services.AddControllers()
            .AddApplicationPart(assembly)
            .ConfigureApplicationPartManager(manager =>
            {
                if (!manager.FeatureProviders.OfType<FormBuilderControllerFeatureProvider>().Any(provider => provider.Assembly == assembly))
                {
                    manager.FeatureProviders.Add(new FormBuilderControllerFeatureProvider(assembly));
                }
            });
        return builder;
    }
}

/// <summary>
/// Adds the management API's controllers of one assembly to MVC: they are internal, like the services they use, and MVC
/// finds only public controllers by itself, so Jiron Form Builder's public surface stays its extension points.
/// </summary>
/// <param name="assembly">The assembly whose controllers it adds.</param>
internal sealed class FormBuilderControllerFeatureProvider(Assembly assembly) : IApplicationFeatureProvider<ControllerFeature>
{
    /// <summary>The assembly whose controllers it adds.</summary>
    public Assembly Assembly { get; } = assembly;

    /// <inheritdoc />
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        foreach (var type in Assembly.GetTypes())
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
