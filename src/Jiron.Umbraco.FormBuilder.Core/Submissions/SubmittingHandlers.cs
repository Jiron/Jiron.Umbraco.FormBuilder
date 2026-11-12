using Jiron.Umbraco.FormBuilder.Core.Entries;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// Asks a site's handlers of <see cref="EntrySubmittingNotification"/> whether an entry may be stored (ADR-0233): their
/// errors, or none. A handler that throws or takes longer than <see cref="Limit"/> is logged and has no word, so a site's
/// failure never loses a visitor's entry.
/// </summary>
/// <param name="events">Umbraco's notifications.</param>
/// <param name="time">The clock of the limit.</param>
/// <param name="logger">Where a failure goes.</param>
internal sealed partial class SubmittingHandlers(IEventAggregator events, TimeProvider time, ILogger<SubmittingHandlers> logger)
{
    /// <summary>How long the handlers may hold up the visitor.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Publishes the notification and answers its errors: those its handlers added, then, for a cancel, one at the form
    /// for each of its messages, or one without words when it has none. None when a handler failed or ran out of time.
    /// </summary>
    /// <param name="notification">The notification.</param>
    public async Task<IReadOnlyList<EntrySubmittingError>> AskAsync(EntrySubmittingNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        bool cancelled;
        try
        {
            cancelled = await Task.Run(() => events.PublishCancelableAsync(notification), CancellationToken.None)
                .WaitAsync(Limit, time, CancellationToken.None);
        }
        catch (TimeoutException)
        {
            LogTooSlow(logger, notification.FormAlias, Limit.TotalSeconds);
            return [];
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailed(logger, notification.FormAlias, LoggedException.Of(exception));
            return [];
        }

        if (!cancelled)
        {
            return [.. notification.Errors];
        }

        var said = notification.Messages.GetAll()
            .Select(message => message.Message?.Trim())
            .Where(message => !string.IsNullOrEmpty(message))
            .Select(message => new EntrySubmittingError(null, null, message!))
            .ToList();
        if (said.Count == 0 && notification.Errors.Count == 0)
        {
            LogCancelledWithoutWords(logger, notification.FormAlias);
            return [new EntrySubmittingError(null, null, string.Empty)];
        }

        return [.. notification.Errors, .. said];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of EntrySubmittingNotification for the form {Form} failed; the entry is stored without its word.")]
    private static partial void LogFailed(ILogger logger, string form, LoggedException exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The handlers of EntrySubmittingNotification for the form {Form} took longer than {Seconds} seconds; the entry is stored without their word.")]
    private static partial void LogTooSlow(ILogger logger, string form, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A handler of EntrySubmittingNotification cancelled an entry of the form {Form} without words; the visitor reads the words of a check without its own message. Give it words with AddFormError or CancelOperation.")]
    private static partial void LogCancelledWithoutWords(ILogger logger, string form);
}
