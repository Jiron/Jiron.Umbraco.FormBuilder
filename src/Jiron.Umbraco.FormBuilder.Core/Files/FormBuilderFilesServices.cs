using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>The registrations of the file store (ADR-0041), which the core composer makes.</summary>
internal static class FormBuilderFilesServices
{
    /// <summary>
    /// Registers the physical store unless the site registered a store of its own first; one the site registers later
    /// replaces it. Its root is part of the host configuration, which checks it when the site starts (ADR-0042); Web
    /// supplies the <see cref="IHostFolders"/>. Registers the host's limits for files as the settings checks see them
    /// (ADR-0087).
    /// </summary>
    public static IServiceCollection AddFormBuilderFiles(this IServiceCollection services)
    {
        services.TryAddSingleton<IFormBuilderFileStore, PhysicalFileStore>();
        services.TryAddSingleton(provider => FieldHostLimits.Of(provider.GetRequiredService<IOptions<FormBuilderOptions>>().Value.Files));
        return services;
    }
}
