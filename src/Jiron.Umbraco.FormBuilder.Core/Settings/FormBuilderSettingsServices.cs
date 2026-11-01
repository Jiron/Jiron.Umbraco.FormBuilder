using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>The registrations of the backoffice settings (ADR-0008, ADR-0043), which the core composer makes.</summary>
internal static class FormBuilderSettingsServices
{
    /// <summary>
    /// Registers the reader and the save, the cache refresher, and each section's validator. A section's migrations
    /// (<see cref="ISettingsSectionMigration"/>) and host rules (<see cref="ISettingsHostRule{T}"/>) are registered with
    /// it.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderSettings(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<IFormBuilderSettingsService, FormBuilderSettingsService>();
        builder.Services.TryAddSingleton<SiteTimeZones>();
        builder.CacheRefreshers().Add<FormBuilderSettingsCacheRefresher>();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISettingsValidator<GeneralSettings>, GeneralSettingsValidator>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ISettingsValidator<ProtectionSettings>, ProtectionSettingsValidator>());
        return builder;
    }
}
