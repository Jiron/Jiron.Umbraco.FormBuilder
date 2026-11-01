using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// The orphan cleanup, a part of the retention job (ADR-0009, ADR-0144): an upload no entry took within a day, when its token
/// has run out, goes with its bytes. The bytes go first, so one that cannot be deleted keeps its row and comes round again.
/// A file's name is never logged.
/// </summary>
internal sealed partial class OrphanFileCleanup(
    IEntryRepository entries,
    IFormBuilderFileStore store,
    TimeProvider time,
    ILogger<OrphanFileCleanup> logger)
    : IFormBuilderRetentionPart
{
    /// <summary>How long an upload waits for an entry: its token's day, and the longest a request that read the token may still take.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromHours(24) + TimeSpan.FromMinutes(5);

    /// <summary>The most files one read nominates.</summary>
    public const int Nominations = 200;

    /// <inheritdoc />
    public string Name => "uploads no entry took";

    /// <inheritdoc />
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
