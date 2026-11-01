using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>The registrations of the retention job (ADR-0045), which the core composer makes.</summary>
internal static class FormBuilderRetentionServices
{
    /// <summary>Registers the retention job with its draft-version cleanup, and the job of the uploads no entry took, which has a timer of its own (ADR-0148).</summary>
    public static IUmbracoBuilder AddFormBuilderRetention(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, DraftVersionCleanup>());
        builder.Services.TryAddSingleton<OrphanFileCleanup>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringBackgroundJob, OrphanFileJob>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDistributedBackgroundJob, FormBuilderRetentionJob>());
        return builder;
    }
}
