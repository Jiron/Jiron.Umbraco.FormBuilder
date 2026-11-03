using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Setup;

/// <summary>The registrations of the setup checklist and the health checks (ADR-0165), which the web composer makes.</summary>
internal static class FormBuilderSetupServices
{
    /// <summary>Registers the checklist's detectors and every health probe; Umbraco finds the health checks by their attribute.</summary>
    public static IUmbracoBuilder AddFormBuilderSetup(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<SetupDetectors>();
        builder.Services.TryAddSingleton<SetupChecklist>();
        builder.Services.TryAddSingleton<SiteServers>();
        builder.Services.TryAddSingleton<KeyRingProfile>();
        builder.Services.TryAddSingleton<FormBuilderHealth>();
        Probe<EmailHealth>(builder);
        Probe<UploadsHealth>(builder);
        Probe<KeyRingHealth>(builder);
        Probe<JobsHealth>(builder);
        Probe<DeliveriesHealth>(builder);
        Probe<MigrationsHealth>(builder);
        Probe<BaseUrlHealth>(builder);
        Probe<UsedOnHealth>(builder);
        return builder;
    }

    // One instance for its own check and for the Overview, which reads them all.
    private static void Probe<T>(IUmbracoBuilder builder)
        where T : HealthProbe
    {
        builder.Services.TryAddSingleton<T>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<HealthProbe, T>(provider => provider.GetRequiredService<T>()));
    }
}
