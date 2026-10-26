using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>The registrations of the field types (ADR-0055), which the core composer makes.</summary>
internal static class FormBuilderFieldTypeServices
{
    /// <summary>
    /// Registers the collection of field types (the built-in ones, and whatever the site's composers change), the
    /// registry over it and the host's locks of their settings, with their start-up checks.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderFieldTypes(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Asking for the collection builder registers the collection when the container is built, whichever composer
        // asked first.
        builder.JironFormBuilder().FieldTypes();
        builder.Services.AddFieldTypeRegistry();
        return builder;
    }

    /// <summary>
    /// Registers the registry over the collection of field types and the host's locks of their settings (ADR-0060), with
    /// the start-up check of each, and the check of their availability.
    /// </summary>
    public static IServiceCollection AddFieldTypeRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(provider => new FieldTypeRegistry(provider.GetRequiredService<FormBuilderFieldTypeCollection>()));
        services.TryAddSingleton<FieldSettingLocks>();
        services.TryAddSingleton<FieldTypeAvailabilityService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, FieldTypeStartupCheck>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, FieldSettingLockStartupCheck>());
        return services;
    }
}
