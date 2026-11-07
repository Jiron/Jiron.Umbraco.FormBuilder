using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// The summary email's job (ADR-0209): one server of the cluster runs it every hour, while Umbraco runs and no Jiron Form
/// Builder migration is pending. Each run sends the summaries that are due and not sent yet, within <see cref="Budget"/>,
/// because Umbraco's runner holds every other distributed job of the server while it waits; the rest go on the next run.
/// </summary>
internal sealed partial class FormBuilderDigestJob(
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    EntryDigests digests,
    TimeProvider timeProvider,
    ILogger<FormBuilderDigestJob> logger)
    : IDistributedBackgroundJob
{
    /// <summary>The job's name in Umbraco's job table.</summary>
    public const string JobName = "Jiron.FormBuilder.Digest";

    /// <summary>How long one run works at most, so that it returns within ten seconds (ADR-0007).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    /// <inheritdoc />
    public string Name => JobName;

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromHours(1);

    /// <inheritdoc />
    public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(cancellationToken))
        {
            return;
        }

        try
        {
            await digests.RunAsync(timeProvider.GetUtcNow() + Budget, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            LogFailed(logger, Period, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's summary emails failed; the job runs again in {Period}")]
    private static partial void LogFailed(ILogger logger, TimeSpan period, Exception exception);
}
