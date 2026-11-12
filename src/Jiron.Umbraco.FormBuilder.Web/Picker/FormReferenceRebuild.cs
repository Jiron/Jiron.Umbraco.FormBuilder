using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The rebuild of the "used on" index (ADR-0017, ADR-0131): it walks Umbraco's documents in id order, the recycle bin's
/// included, and writes each one's rows anew, then removes the rows of content that no longer exists. A request is a
/// <c>jfbKeyValue</c> row that holds where the walk goes on, so the walk runs in bounded parts on one server at a time
/// and goes on after a restart. Content saved meanwhile is kept current by <see cref="FormReferenceHandler"/>, whose
/// rows the walk never overwrites with an older copy, and the place moves on only from the value read, so a request
/// made during a batch starts the walk again.
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

    /// <summary>
    /// Asks for a rebuild from the first document; a walk under way starts again. Each request's value is its own, which
    /// reads as no place, so a walk at its very start can tell a new request from the one it is doing.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task RequestAsync(CancellationToken cancellationToken = default) =>
        values.SetAsync(Key, $"{RequestReferenceRebuildV1.FromStart}-{Guid.NewGuid():N}", cancellationToken);

    /// <summary>
    /// Holds a request just made back for one run of the job, so that a walk starts at least a run's period after it: long
    /// enough for every server of a cluster to have heard of the type's change that asked. True when one stands, also if
    /// another came as this one was held, which the next run holds in turn.
    /// </summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<bool> HoldNewRequestAsync(CancellationToken cancellationToken = default)
    {
        if (await values.GetAsync(Key, cancellationToken) is not { } value || !IsNewRequest(value))
        {
            return false;
        }

        await values.TryReplaceAsync(Key, value, RequestReferenceRebuildV1.FromStart, cancellationToken);
        return true;
    }

    /// <summary>Whether a rebuild was asked for and has not finished.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> IsDueAsync(CancellationToken cancellationToken = default) => await values.GetAsync(Key, cancellationToken) is not null;

    /// <summary>
    /// Goes on with a requested rebuild, one batch after another, until it is done, <paramref name="deadline"/> has passed,
    /// or a new request came during a batch, which <see cref="HoldNewRequestAsync"/> holds before its walk. True when no
    /// rebuild is left to do.
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
                if (!await values.TryDeleteAsync(Key, next, cancellationToken))
                {
                    return false;
                }

                LogDone(logger, removed);
                return true;
            }

            // A value this version cannot read starts the walk again: a whole walk is always right.
            var after = int.TryParse(next, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0;
            var batch = await references.DocumentsAfterAsync(after, BatchSize, cancellationToken);

            // The versions before the copies: a save after this read shows as a changed version, and its handler writes
            // its rows. A copy of another version is an older one from a cache or a newer one, and is left to that handler too.
            var versions = await references.VersionsOfAsync(batch, cancellationToken);
            foreach (var content in batch.Count == 0 ? [] : contents.GetByIds(batch))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (versions.TryGetValue(content.Id, out var version) && version.Id == content.VersionId)
                {
                    if (Read(content) is { } rows)
                    {
                        await references.ReplaceUnchangedContentAsync(content.Id, version, content.Key, rows, cancellationToken);
                    }
                }
            }

            // A request made during the batch stands, and is held before its walk starts.
            if (!await values.TryReplaceAsync(Key, next, batch.Count == 0 ? OrphansNext : batch[^1].ToString(CultureInfo.InvariantCulture), cancellationToken))
            {
                return false;
            }

            if (timeProvider.GetUtcNow() >= deadline)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNewRequest(string value) => value.StartsWith($"{RequestReferenceRebuildV1.FromStart}-", StringComparison.Ordinal);

    // A document whose rows cannot be worked out keeps those it has and the walk goes on: stopping here would stop every
    // document after it. A write that fails is another thing, and the whole batch runs again next time.
    private IReadOnlyList<ContentFormReference>? Read(IContent content)
    {
        try
        {
            return index.Of(content);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogDocumentFailed(logger, content.Key, LoggedException.Of(exception));
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder's rebuild could not read the forms used by content {ContentKey}, and went on without it")]
    private static partial void LogDocumentFailed(ILogger logger, Guid contentKey, LoggedException exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder rebuilt the forms used by content; removed {Removed} references of deleted content")]
    private static partial void LogDone(ILogger logger, int removed);
}

/// <summary>
/// Asks for a rebuild when stored values change without a notification for the content that holds them (ADR-0131): a
/// document or element type changed or deleted, as removing a picker property or turning a text property into a picker
/// does; a data type deleted, which takes its properties away; a data type given another editor; and a language deleted,
/// which takes its values away. A type that is new has no content yet, and another setting of a data type leaves what its values name as it was. The request is a row
/// the job reads, so many changes in a row cost one walk. A request that fails is logged and never fails the change.
/// </summary>
internal sealed partial class FormReferenceTypeHandler(FormReferenceRebuild rebuild, ILogger<FormReferenceTypeHandler> logger)
    : INotificationAsyncHandler<ContentTypeChangedNotification>,
        INotificationAsyncHandler<DataTypeSavedNotification>,
        INotificationAsyncHandler<DataTypeDeletedNotification>,
        INotificationAsyncHandler<LanguageDeletedNotification>
{
    /// <inheritdoc />
    public Task HandleAsync(ContentTypeChangedNotification notification, CancellationToken cancellationToken) =>
        notification.Changes.Any(change => change.ChangeTypes != ContentTypeChangeTypes.Create) ? RequestAsync(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc />
    public Task HandleAsync(DataTypeSavedNotification notification, CancellationToken cancellationToken) =>
        notification.SavedEntities.Any(dataType => dataType.WasPropertyDirty(nameof(IDataType.Editor)))
            ? RequestAsync(cancellationToken)
            : Task.CompletedTask;

    /// <inheritdoc />
    public Task HandleAsync(DataTypeDeletedNotification notification, CancellationToken cancellationToken) => RequestAsync(cancellationToken);

    /// <inheritdoc />
    public Task HandleAsync(LanguageDeletedNotification notification, CancellationToken cancellationToken) => RequestAsync(cancellationToken);

    private async Task RequestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await rebuild.RequestAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogRequestFailed(logger, LoggedException.Of(exception));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not ask for a rebuild of the forms used by content after a type changed; the health check's rebuild repairs them")]
    private static partial void LogRequestFailed(ILogger logger, LoggedException exception);
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
            if (!await rebuild.HoldNewRequestAsync(cancellationToken))
            {
                await rebuild.ContinueAsync(timeProvider.GetUtcNow() + Budget, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(logger, Period, LoggedException.Of(exception));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's rebuild of the forms used by content failed; it goes on in {Period}")]
    private static partial void LogFailed(ILogger logger, TimeSpan period, LoggedException exception);
}
