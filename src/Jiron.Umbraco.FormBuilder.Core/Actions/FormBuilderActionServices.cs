using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>The registrations of the action framework (ADR-0007, ADR-0158), which the core composer makes.</summary>
internal static class FormBuilderActionServices
{
    /// <summary>
    /// Registers the collection of action types, the registry over it and the host's locks of their settings with their
    /// start-up checks and publish check, the worker with its job and wake, the attempts, the settling of entries, the
    /// retries and the test runs.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderActions(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Asking for the collection builder registers the collection when the container is built, whichever composer
        // asked first.
        builder.JironFormBuilder().ActionTypes();
        builder.Services.TryAddSingleton(provider => new ActionTypeRegistry(provider.GetRequiredService<FormBuilderActionTypeCollection>()));
        builder.Services.TryAddSingleton<ActionSettingLocks>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, ActionTypeStartupCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FormBuilderStartupChecks>, ActionSettingLockStartupCheck>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormCheck, ActionPublishCheck>());

        builder.Services.TryAddSingleton<ActionSettlement>();
        builder.Services.TryAddSingleton<ActionAttempts>();
        builder.Services.TryAddSingleton<ActionRunRetries>();
        builder.Services.TryAddSingleton<ActionLastRuns>();
        builder.Services.TryAddSingleton<ActionTestRuns>();
        builder.Services.TryAddSingleton<OutboxWake>();
        builder.Services.TryAddSingleton<JobHeartbeat>();
        builder.Services.TryAddSingleton<OutboxPass>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxQueue, ActionRunQueue>());
        builder.AddFormBuilderEmail();
        builder.AddFormBuilderWebhooks();
        builder.JironFormBuilder().ActionTypes().Append<SetStateActionType>().Append<ContentActionType>();
        builder.Services.TryAddSingleton<ContentItemTypes>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxQueue, EntryChangeQueue>());
        builder.Services.AddHostedService<OutboxWorker>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDistributedBackgroundJob, FormBuilderOutboxJob>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, UsedChallengeCleanup>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, NeverStoredEntryCleanup>());
        builder.AddNotificationHandler<OutboxQueuedNotification, OutboxQueuedHandler>();
        return builder;
    }
}

/// <summary>Forgets the used challenges of entries that are gone once no challenge of their time can come back (ADR-0158).</summary>
/// <param name="settlement">Where they are kept.</param>
internal sealed class UsedChallengeCleanup(ActionSettlement settlement) : IFormBuilderRetentionPart
{
    /// <inheritdoc />
    public string Name => "used challenges";

    /// <inheritdoc />
    public Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken) => settlement.ForgetUsedChallengesAsync(cancellationToken);
}
