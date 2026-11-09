using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>The registrations of the privacy tools (ADR-0223), which the core composer makes.</summary>
internal static class FormBuilderPrivacyServices
{
    /// <summary>Registers finding a person, exporting everything about them, and erasing or anonymising their entries.</summary>
    public static IUmbracoBuilder AddFormBuilderPrivacy(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<EntryAnonymiser>();
        builder.Services.TryAddSingleton<PersonExports>();
        builder.Services.TryAddSingleton<PersonData>();
        return builder;
    }
}
