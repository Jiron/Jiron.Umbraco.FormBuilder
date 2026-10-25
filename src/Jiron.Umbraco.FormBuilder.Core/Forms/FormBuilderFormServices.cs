using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>The registrations of the form services (ADR-0044, ADR-0053), which the core composer makes.</summary>
internal static class FormBuilderFormServices
{
    /// <summary>
    /// Registers the field types (the built-in ones until 5.1 registers its registry), the publish checks in the order
    /// they run, the form service with its versions and its exchange and what they share, the folder service, and the
    /// permanent delete the form and folder services share (ADR-0047).
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderForms(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(FieldTypeCatalogue.BuiltIn);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormPublishCheck, StructuralPublishCheck>());
        builder.Services.TryAddSingleton<FormRemoval>();
        builder.Services.TryAddSingleton<FormDocuments>();
        builder.Services.TryAddSingleton<FormInfos>();
        builder.Services.TryAddSingleton<FormCreation>();
        builder.Services.TryAddSingleton<IFormBuilderFormService, FormBuilderFormService>();
        builder.Services.TryAddSingleton<IFormBuilderFormVersionService, FormBuilderFormVersionService>();
        builder.Services.TryAddSingleton<IFormBuilderFormExchangeService, FormBuilderFormExchangeService>();
        builder.Services.TryAddSingleton<IFormBuilderFolderService, FormBuilderFolderService>();
        builder.Services.TryAddSingleton<IFormBuilderLibraryService, FormBuilderLibraryService>();
        return builder;
    }
}
