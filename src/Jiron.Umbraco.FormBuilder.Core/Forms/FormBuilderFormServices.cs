using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>The registrations of the form service (ADR-0044), which the core composer makes.</summary>
internal static class FormBuilderFormServices
{
    /// <summary>
    /// Registers the field types (the built-in ones until 5.1 registers its registry), the publish checks in the order
    /// they run, the form and folder services, and the permanent delete they share (ADR-0047).
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderForms(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(FieldTypeCatalog.BuiltIn);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormPublishCheck, StructuralPublishCheck>());
        builder.Services.TryAddSingleton<FormRemoval>();
        builder.Services.TryAddSingleton<IFormBuilderFormService, FormBuilderFormService>();
        builder.Services.TryAddSingleton<IFormBuilderFolderService, FormBuilderFolderService>();
        return builder;
    }
}
