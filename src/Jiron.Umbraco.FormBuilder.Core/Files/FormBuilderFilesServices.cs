using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>The registrations of the file store (ADR-0041), which the core composer makes.</summary>
internal static class FormBuilderFilesServices
{
    /// <summary>
    /// Registers the physical store unless the site registered a store of its own first. Its root is part of the host
    /// configuration, which checks it when the site starts (ADR-0042); Web supplies the <see cref="IHostFolders"/>.
    /// </summary>
    public static IServiceCollection AddFormBuilderFiles(this IServiceCollection services)
    {
        services.TryAddSingleton<IFormBuilderFileStore, PhysicalFileStore>();
        return services;
    }
}
