using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>The registrations of entries' answers (ADR-0070, ADR-0072), which the core composer makes.</summary>
internal static class FormBuilderEntryServices
{
    /// <summary>
    /// Registers the reader of a submission's answers, the consent wordings and the store that writes an entry's answers
    /// and reads them back typed, the Overview's newest entries, and the entries lists with their titles, the user's
    /// columns and filter sets, and the bulk bar's delete and re-run (ADR-0207).
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderEntries(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<SubmissionReader>();
        builder.Services.TryAddSingleton<ConsentWordings>();
        builder.Services.TryAddSingleton<EntryAnswerStore>();
        builder.Services.TryAddSingleton<RecentEntries>();
        builder.Services.TryAddSingleton<EntryTitles>();
        builder.Services.TryAddSingleton<EntryUserSettings>();
        builder.Services.TryAddSingleton<EntryLists>();
        builder.Services.TryAddSingleton<EntryDeletions>();
        builder.Services.TryAddSingleton<EntryActionReruns>();
        return builder;
    }
}
