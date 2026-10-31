using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The rebuild of the "used on" index (ADR-0017, ADR-0131): it walks Umbraco's documents in id order, the recycle bin's
/// included, and writes each one's rows anew, then removes the rows of content that no longer exists. A request is a
/// <c>jfbKeyValue</c> row that holds where the walk goes on, so the walk runs in bounded parts on one server at a time
/// and goes on after a restart; content saved meanwhile is kept current by <see cref="FormReferenceHandler"/>.
/// </summary>
internal sealed partial class FormReferenceRebuild(
    IReferenceRepository references,
    IKeyValueRepository values,
    IContentService contents,
    FormReferenceIndex index,
    TimeProvider timeProvider,
    ILogger<FormReferenceRebuild> logger)
{
    /// <summary>How many documents one part of the walk reads at once.</summary>
    public const int BatchSize = 100;

    /// <summary>The request's value once every document is done and the rows of deleted content are next.</summary>
    public const string OrphansNext = "orphans";

    private const string Key = RequestReferenceRebuildV1.RebuildKey;

    /// <summary>Asks for a rebuild from the first document; a walk under way starts again.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task RequestAsync(CancellationToken cancellationToken = default) => values.SetAsync(Key, RequestReferenceRebuildV1.FromStart, cancellationToken);

    /// <summary>Whether a rebuild was asked for and has not finished.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> IsDueAsync(CancellationToken cancellationToken = default) => await values.GetAsync(Key, cancellationToken) is not null;

    /// <summary>
    /// Goes on with a requested rebuild, one batch after another, until it is done or <paramref name="deadline"/> has
    /// passed. True when no rebuild is left to do.
    /// </summary>
    /// <param name="deadline">When to stop after the current batch.</param>
    /// <param name="cancellationToken">Cancels the walk between writes.</param>
    public async Task<bool> ContinueAsync(DateTimeOffset deadline, CancellationToken cancellationToken = default)
    {
        while (await values.GetAsync(Key, cancellationToken) is { } next)
        {
            if (next == OrphansNext)
            {
                var removed = await references.DeleteOrphansAsync(cancellationToken);
                await values.DeleteAsync(Key, cancellationToken);
                LogDone(logger, removed);
                return true;
            }

            // A value this version cannot read starts the walk again: a whole walk is always right.
            var after = int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
            var batch = await references.DocumentsAfterAsync(after, BatchSize, cancellationToken);
            foreach (var content in batch.Count == 0 ? [] : contents.GetByIds(batch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await index.IndexAsync(content, cancellationToken);
            }

            await values.SetAsync(Key, batch.Count == 0 ? OrphansNext : batch[^1].ToString(CultureInfo.InvariantCulture), cancellationToken);
            if (timeProvider.GetUtcNow() >= deadline)
            {
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder rebuilt the forms used by content; removed {Removed} references of deleted content")]
    private static partial void LogDone(ILogger logger, int removed);
}

/// <summary>
/// The reference job (ADR-0131): one server of the cluster goes on with a requested rebuild every minute, within
/// <see cref="Budget"/>, because Umbraco's runner holds every other distributed job of the server while it waits. It
/// runs only while Umbraco runs and no Jiron Form Builder migration is pending; with nothing requested it reads one row.
/// </summary>
internal sealed partial class FormReferenceRebuildJob(
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    FormReferenceRebuild rebuild,
    TimeProvider timeProvider,
    ILogger<FormReferenceRebuildJob> logger)
    : IDistributedBackgroundJob
{
    /// <summary>The job's name in Umbraco's job table.</summary>
    public const string JobName = "Jiron.FormBuilder.References";

    /// <summary>How long one run works at most, so that it returns within ten seconds (ADR-0007).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    /// <inheritdoc />
    public string Name => JobName;

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromMinutes(1);

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
            await rebuild.ContinueAsync(timeProvider.GetUtcNow() + Budget, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(logger, Period, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's rebuild of the forms used by content failed; it goes on in {Period}")]
    private static partial void LogFailed(ILogger logger, TimeSpan period, Exception exception);
}
