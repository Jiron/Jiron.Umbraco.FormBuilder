using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// The orphan cleanup (ADR-0009, ADR-0144, ADR-0148): an upload no entry took within a day, when its token has run out, goes
/// with its bytes. The bytes go first, so one that cannot be deleted keeps its row and comes round again.
/// A file's name is never logged.
/// </summary>
internal sealed partial class OrphanFileCleanup(
    IEntryRepository entries,
    IFormBuilderFileStore store,
    TimeProvider time,
    ILogger<OrphanFileCleanup> logger)
{
    /// <summary>How long an upload waits for an entry: its token's day, and the longest a request that read the token may still take.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromHours(24) + TimeSpan.FromMinutes(5);

    /// <summary>The most files one read nominates.</summary>
    public const int Nominations = 200;

    /// <summary>Removes what has waited too long, until the deadline.</summary>
    /// <param name="deadline">When to stop starting new removals; what is left goes in the next run.</param>
    /// <param name="cancellationToken">Cancelled when the site stops.</param>
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - KeptFor;
        var removed = 0;
        while (time.GetUtcNow() < deadline)
        {
            var progress = false;
            foreach (var file in await entries.ListOrphanFilesAsync(cutoff, Nominations, cancellationToken))
            {
                if (await RemoveAsync(file, cancellationToken))
                {
                    removed++;
                    progress = true;
                }

                if (time.GetUtcNow() >= deadline)
                {
                    break;
                }
            }

            // Nothing left, or nothing that would go: the next run tries again.
            if (!progress)
            {
                break;
            }
        }

        if (removed > 0)
        {
            LogRemoved(logger, removed);
        }
    }

    private async Task<bool> RemoveAsync(StoredEntryFile file, CancellationToken cancellationToken)
    {
        try
        {
            await store.DeleteAsync(file.StoredName, cancellationToken);
            return await entries.DeleteFileAsync(file.Id, entryId: null, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            LogKept(logger, file.Key, exception.GetType().Name);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder removed uploads no entry took ({Count})")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not remove the upload {FileKey} ({Failure}); it tries again in the next run")]
    private static partial void LogKept(ILogger logger, Guid fileKey, string failure);
}

/// <summary>
/// Runs the orphan cleanup on a timer of its own (ADR-0148), every minute, as the uploads the rate limits let in would outrun
/// a share of the hourly retention job. It runs on the server that schedules, only while Umbraco runs and no migration is pending.
/// </summary>
internal sealed class OrphanFileJob(
    OrphanFileCleanup cleanup,
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    IHostApplicationLifetime lifetime,
    TimeProvider time)
    : IRecurringBackgroundJob
{
    /// <summary>How long one run starts new removals.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public event EventHandler? PeriodChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public TimeSpan Delay => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public ServerRole[] ServerRoles => [ServerRole.Single, ServerRole.SchedulingPublisher];

    /// <inheritdoc />
    public async Task RunJobAsync()
    {
        var stopping = lifetime.ApplicationStopping;
        if (runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(stopping))
        {
            return;
        }

        await cleanup.RunAsync(time.GetUtcNow() + Budget, stopping);
    }
}
