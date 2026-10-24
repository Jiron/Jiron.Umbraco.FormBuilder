using Jiron.Umbraco.FormBuilder.Core.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>The registrations of the host configuration and the start-up checks (ADR-0042), which the core composer makes.</summary>
internal static class FormBuilderConfigurationServices
{
    /// <summary>
    /// Binds <see cref="FormBuilderOptions"/> once from <c>JironFormBuilder</c> and checks it when the site starts, with the
    /// keys it does not have; takes the definition limits from it; and checks the runtime's globalization when the site
    /// starts. Web supplies the <see cref="IHostFolders"/> the file store's root is checked against.
    /// </summary>
    public static IServiceCollection AddFormBuilderOptions(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(FormBuilderOptions.SectionName);
        services.AddOptions<FormBuilderOptions>().Bind(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderOptions>, FormBuilderOptionsValidator>(
            provider => new FormBuilderOptionsValidator(provider.GetRequiredService<IHostFolders>(), section)));
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<FormBuilderOptions>>().Value.Limits.Definitions);

        services.TryAddSingleton(_ => GlobalizationFacts.OfThisRuntime());
        services.AddOptions<FormBuilderStartupChecks>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, GlobalizationStartupCheck>());
        return services;
    }
}
