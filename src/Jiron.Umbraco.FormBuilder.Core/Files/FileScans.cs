using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>The scan states of an uploaded file (<c>jfbEntryFile.scanState</c>, ADR-0144).</summary>
internal static class FileScanStates
{
    /// <summary>Not scanned: the site has no scanner, or the bytes were gone when its turn came.</summary>
    public const string Unscanned = "unscanned";

    /// <summary>Waiting for the site's scanners.</summary>
    public const string Pending = "pending";

    /// <summary>Every scanner found nothing.</summary>
    public const string Clean = "clean";

    /// <summary>A scanner found malware: no entry takes the file, and nobody downloads it.</summary>
    public const string Quarantined = "quarantined";
}

/// <summary>
/// Asks the site's scanners about one file and records the answer (ADR-0144): any scanner's quarantine decides, an answer
/// from each that finds nothing makes it clean, and anything else leaves it waiting. A quarantine marks the entry that holds
/// the file with an event on its timeline. A file's name is never logged.
/// </summary>
internal sealed partial class FileScans(
    FormBuilderFileScannerCollection scanners,
    IFormBuilderFileStore store,
    IEntryRepository entries,
    IEntryEventRepository events,
    FormBuilderDatabase database,
    TimeProvider time,
    ILogger<FileScans> logger)
{
    /// <summary>The timeline event of a file quarantined after its entry took it.</summary>
    public const string QuarantinedEvent = "fileQuarantined";

    /// <summary>How long one scanner may take over one file.</summary>
    public static readonly TimeSpan ScanLimit = TimeSpan.FromMinutes(5);

    /// <summary>Scans one file that waits; a change that came first leaves it as that change did.</summary>
    /// <param name="file">The file.</param>
    /// <param name="cancellationToken">Cancelled when the site stops.</param>
    public async Task ScanAsync(StoredEntryFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        var unavailable = false;
        foreach (var scanner in scanners)
        {
            Stream? opened;
            try
            {
                opened = await store.OpenReadAsync(file.StoredName, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
            {
                // It stays waiting, and its turn passes to the next (ADR-0148).
                LogOpenFailed(logger, file.Key, exception.GetType().Name);
                return;
            }

            await using var content = opened;
            if (content is null)
            {
                LogBytesGone(logger, file.Key);
                await RecordAsync(file, FileScanStates.Unscanned, cancellationToken);
                return;
            }

            var outcome = await AskAsync(scanner, file, content, cancellationToken);
            if (outcome == FormBuilderFileScanOutcome.Quarantine)
            {
                await RecordAsync(file, FileScanStates.Quarantined, cancellationToken);
                return;
            }

            unavailable |= outcome != FormBuilderFileScanOutcome.Clean;
        }

        if (!unavailable)
        {
            await RecordAsync(file, FileScanStates.Clean, cancellationToken);
        }
    }

    // One scanner's answer, within its limit; one that fails has no answer. Only the site's stopping ends the scan.
    private async Task<FormBuilderFileScanOutcome> AskAsync(IFormBuilderFileScanner scanner, StoredEntryFile file, Stream content, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(ScanLimit, time);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var toScan = new FormBuilderFileToScan(file.Key, file.OriginalName, file.ContentType, file.SizeBytes, file.FileHash, content);
            return await scanner.ScanAsync(toScan, limit.Token).WaitAsync(limit.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            LogScanFailed(logger, scanner.GetType().Name, file.Key, exception.GetType().Name);
            return FormBuilderFileScanOutcome.Unavailable;
        }
    }

    // In one unit, so that a quarantine and its mark come together, and an entry that took the file a moment before is seen.
    private async Task RecordAsync(StoredEntryFile file, string state, CancellationToken cancellationToken)
    {
        try
        {
            await database.WriteAsync(
                async _ =>
                {
                    if (await entries.ChangeScanStateAsync(file.Id, FileScanStates.Pending, state, cancellationToken) is { EntryId: { } entry } changed && state == FileScanStates.Quarantined)
                    {
                        var details = JsonSerializer.Serialize(new { file = changed.Key, field = changed.FieldKey });
                        await events.AddAsync(new NewEntryEvent(entry, QuarantinedEvent, Details: details), cancellationToken);
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (FormBuilderConcurrencyException)
        {
            // Another server's scan of the same file came first.
            return;
        }

        if (state == FileScanStates.Quarantined)
        {
            LogQuarantined(logger, file.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder quarantined the file {FileKey}: a scanner found malware")]
    private static partial void LogQuarantined(ILogger logger, Guid fileKey);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The file scanner {Scanner} could not answer about the file {FileKey} ({Failure}); it is asked again later")]
    private static partial void LogScanFailed(ILogger logger, string scanner, Guid fileKey, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not open the file {FileKey} to scan it ({Failure}); it is asked again later")]
    private static partial void LogOpenFailed(ILogger logger, Guid fileKey, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not scan the file {FileKey}: its bytes are gone")]
    private static partial void LogBytesGone(ILogger logger, Guid fileKey);
}

/// <summary>
/// Scans what waits (ADR-0144), on a timer of its own and not in Umbraco's distributed runner, where a long scan would hold
/// every other job of the server (ADR-0007). It runs on the server that schedules, only with a scanner, only while Umbraco runs
/// and no migration is pending. What an entry holds goes first, oldest first, so uploads no entry took cannot keep it behind
/// them (ADR-0148), and then those uploads. Each goes on from where it stopped, so files a scanner cannot answer about, or that
/// cannot be opened, do not keep the others waiting: the files of entries start over in the run after they reach their end,
/// which also finds a file an entry took behind where they stood, and the uploads when they reach theirs.
/// </summary>
internal sealed class FileScanJob(
    FormBuilderFileScannerCollection scanners,
    FileScans scans,
    IEntryRepository entries,
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    IHostApplicationLifetime lifetime,
    TimeProvider time)
    : IRecurringBackgroundJob
{
    /// <summary>How long one run starts new scans.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    // Small, as what an entry holds is looked for again after each batch of uploads.
    private const int Batch = 10;

    private int _entryFilesAfter;
    private bool _entryFilesReachedEnd;
    private int _uploadsAfter;

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
    public TimeSpan Period => TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public TimeSpan Delay => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public ServerRole[] ServerRoles => [ServerRole.Single, ServerRole.SchedulingPublisher];

    /// <inheritdoc />
    public async Task RunJobAsync()
    {
        var stopping = lifetime.ApplicationStopping;
        if (scanners.Count == 0 || runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(stopping))
        {
            return;
        }

        if (_entryFilesReachedEnd)
        {
            _entryFilesAfter = 0;
            _entryFilesReachedEnd = false;
        }

        var deadline = time.GetUtcNow() + Budget;
        while (time.GetUtcNow() < deadline)
        {
            var waiting = await entries.ListEntryFilesToScanAsync(_entryFilesAfter, Batch, stopping);
            if (waiting.Count == 0)
            {
                _entryFilesReachedEnd = true;
                waiting = await entries.ListUploadsToScanAsync(_uploadsAfter, Batch, stopping);
            }

            if (waiting.Count == 0)
            {
                _uploadsAfter = 0;
                return;
            }

            foreach (var file in waiting)
            {
                // Past it before its scan, so one whose scan fails for good cannot hold the queue.
                if (file.EntryId is null)
                {
                    _uploadsAfter = file.Id;
                }
                else
                {
                    _entryFilesAfter = file.Id;
                }

                await scans.ScanAsync(file, stopping);
                if (time.GetUtcNow() >= deadline)
                {
                    return;
                }
            }
        }
    }
}
