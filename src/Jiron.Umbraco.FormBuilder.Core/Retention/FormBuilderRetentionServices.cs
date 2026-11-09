using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>The registrations of the retention job (ADR-0045, ADR-0222), which the core composer makes.</summary>
internal static class FormBuilderRetentionServices
{
    /// <summary>
    /// Registers the retention rules, the dry run and the legal holds, and the retention job with its parts in the order
    /// they run: the draft-version cleanup, the entries whose time has come, the times of entries whose rules changed, the
    /// file store's leftovers, the audit log and the daily statistics. The job of the uploads no entry took has a timer of
    /// its own (ADR-0148).
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderRetention(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<EntryRetention>();
        builder.Services.TryAddSingleton<RetentionDryRun>();
        builder.Services.TryAddSingleton<EntryHolds>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, DraftVersionCleanup>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, EntryPurge>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, RetentionRecompute>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, FileStoreSweep>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, AuditLogPurge>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, DailyStatPurge>());
        builder.Services.TryAddSingleton<OrphanFileCleanup>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringBackgroundJob, OrphanFileJob>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDistributedBackgroundJob, FormBuilderRetentionJob>());
        return builder;
    }
}
