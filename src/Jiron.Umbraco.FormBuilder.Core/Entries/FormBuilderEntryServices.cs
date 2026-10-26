using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>The registrations of entries' answers (ADR-0070), which the core composer makes.</summary>
internal static class FormBuilderEntryServices
{
    /// <summary>Registers the consent wordings and the store that writes an entry's answers and reads them back typed.</summary>
    public static IUmbracoBuilder AddFormBuilderEntries(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<ConsentWordings>();
        builder.Services.TryAddSingleton<EntryAnswerStore>();
        return builder;
    }
}
