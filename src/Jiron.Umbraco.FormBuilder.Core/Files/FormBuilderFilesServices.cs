using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>The registrations of the file store (ADR-0041), which the core composer makes.</summary>
internal static class FormBuilderFilesServices
{
    /// <summary>
    /// Binds <see cref="FormBuilderFilesOptions"/> and checks its root when the site starts, and registers the physical
    /// store unless the site registered a store of its own first. Web supplies the <see cref="IHostFolders"/>.
    /// </summary>
    public static IServiceCollection AddFormBuilderFiles(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FormBuilderFilesOptions>().Bind(configuration.GetSection(FormBuilderFilesOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderFilesOptions>, FormBuilderFilesOptionsValidation>());
        services.TryAddSingleton<IFormBuilderFileStore, PhysicalFileStore>();
        return services;
    }
}
