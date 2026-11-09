using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// The part of the retention job that sweeps what a dead process left in the default file store (ADR-0041, ADR-0222): a
/// save's partial file, a deleted file's bytes, and a stored file no <c>jfbEntryFile</c> row names, each once it is a day
/// old, so a save or an upload still under way is never touched. It goes through the store's folders in order, from where
/// the last run stopped, and starts a new pass a day after the last one ended. A store of the site's own is not swept here.
/// A file's name is never logged.
/// </summary>
/// <param name="store">The file store.</param>
/// <param name="entries">Which names a row names.</param>
/// <param name="keyValues">Where the pass keeps how far it got.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Where the counts go.</param>
internal sealed partial class FileStoreSweep(
    IFormBuilderFileStore store,
    IEntryRepository entries,
    IKeyValueRepository keyValues,
    TimeProvider time,
    ILogger<FileStoreSweep> logger) : IFormBuilderRetentionPart
{
    /// <summary>How old a leftover is before it goes.</summary>
    public static readonly TimeSpan OldAfter = TimeSpan.FromDays(1);

    /// <summary>The key of how far the pass got: the last folder done, or <see cref="Done"/> and when the pass ended.</summary>
    public const string ProgressKey = "jfb.retention.filesweep";

    // What a finished pass writes before the moment it ended.
    private const string Done = "done:";

    /// <inheritdoc />
    public string Name => "the file store's leftovers";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        if (store is not IFileStoreLeftovers leftovers)
        {
            return;
        }

        var progress = await keyValues.GetAsync(ProgressKey, cancellationToken);
        var after = string.Empty;
        if (progress is not null && progress.StartsWith(Done, StringComparison.Ordinal))
        {
            if (DateTimeOffset.TryParse(progress[Done.Length..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var ended) && time.GetUtcNow() - ended < OldAfter)
            {
                return;
            }
        }
        else if (progress is not null)
        {
            after = progress;
        }

        var removed = 0;
        foreach (var folder in leftovers.Folders().Where(name => string.CompareOrdinal(name, after) > 0))
        {
            if (time.GetUtcNow() >= deadline)
            {
                await SaveAsync(progress, after, cancellationToken);
                Log(removed);
                return;
            }

            removed += await SweepAsync(leftovers, folder, cancellationToken);
            after = folder;
        }

        await SaveAsync(progress, Done + time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture), cancellationToken);
        Log(removed);
    }

    // The folder's leftovers a day old: every partial file and deleted file's bytes, and the stored files no row names.
    private async Task<int> SweepAsync(IFileStoreLeftovers leftovers, string folder, CancellationToken cancellationToken)
    {
        var old = time.GetUtcNow() - OldAfter;
        var files = leftovers.FilesIn(folder).Where(file => file.LastWriteUtc <= old).ToList();
        var stored = files.Where(file => file.Kind == StoredFileKind.Stored).Select(file => file.Name).ToList();
        var named = stored.Count == 0 ? new HashSet<string>() : await entries.ListStoredNamesAsync(stored, cancellationToken);
        var removed = 0;
        foreach (var file in files.Where(file => file.Kind != StoredFileKind.Stored || !named.Contains(file.Name)))
        {
            try
            {
                leftovers.DeleteLeftover(folder, file.Name);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogKept(logger, exception.GetType().Name);
            }
        }

        return removed;
    }

    private async Task SaveAsync(string? read, string value, CancellationToken cancellationToken)
    {
        if (read is not null && await keyValues.TryReplaceAsync(ProgressKey, read, value, cancellationToken))
        {
            return;
        }

        await keyValues.TryAddAsync(ProgressKey, value, cancellationToken);
    }

    private void Log(int removed)
    {
        if (removed > 0)
        {
            LogRemoved(logger, removed);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder's retention removed {Count} leftovers from the file store")]
    private static partial void LogRemoved(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not remove a leftover from the file store ({Failure}); the next pass tries again")]
    private static partial void LogKept(ILogger logger, string failure);
}
