using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// The steps every change of Core's services takes (ADR-0053): it is refused inside a caller's unit of work or an
/// Umbraco scope, then the cancelable notification goes out, then one unit of work writes the change with its audit
/// entries, then the notification that it happened goes out. A handler that fails after the commit is logged as
/// <typeparamref name="TService"/>'s, and the change stands.
/// </summary>
/// <typeparam name="TService">The service whose changes these are; it names the log category.</typeparam>
internal sealed class FormBuilderChanges<TService>(
    FormBuilderDatabase database,
    IAuditRepository audit,
    IEventAggregator eventAggregator,
    ILogger<TService> logger)
{
    /// <summary>
    /// Refuses a change inside a caller's unit of work or an Umbraco scope, since the change tells handlers, drops caches
    /// or both once it has committed; <paramref name="refusal"/> says which change and why.
    /// </summary>
    /// <exception cref="InvalidOperationException">A unit of work or a scope is open.</exception>
    public void EnsureOwnUnit(string refusal)
    {
        if (database.InUnitOfWork)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    /// <summary>Publishes the notification before a change: a handler that cancels it stops the change.</summary>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change, with its messages.</exception>
    public async Task CancelableAsync<TNotification>(TNotification notification)
        where TNotification : CancelableNotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (await eventAggregator.PublishCancelableAsync(notification))
        {
            throw new FormBuilderCancelledException(notification.Messages);
        }
    }

    /// <summary>
    /// Writes a change and the audit entries <paramref name="entries"/> makes of its result in one unit of work: none
    /// for a change that found nothing to change.
    /// </summary>
    public Task<T> WriteAsync<T>(Func<Task<T>> write, Func<T, IEnumerable<NewAuditEntry>> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(entries);
        return database.WriteAsync(
            async _ =>
            {
                var result = await write();
                foreach (var entry in entries(result))
                {
                    await audit.AddAsync(entry, cancellationToken);
                }

                return result;
            },
            cancellationToken);
    }

    /// <summary>
    /// Publishes the notification that a change committed. A handler that fails is logged with
    /// <paramref name="subject"/>, what changed, and the change stands.
    /// </summary>
    public async Task PublishedAsync<TNotification>(TNotification notification, string subject)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        try
        {
            await eventAggregator.PublishAsync(notification, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FormBuilderChangeLog.HandlerFailed(logger, typeof(TNotification).Name, subject, exception);
        }
    }
}

/// <summary>The log line of <see cref="FormBuilderChanges{TService}"/>, which a generic type cannot hold itself.</summary>
internal static partial class FormBuilderChangeLog
{
    /// <summary>A handler of the notification after a change failed; the change stands.</summary>
    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of {Notification} failed after a change to {Subject} committed; the change stands")]
    public static partial void HandlerFailed(ILogger logger, string notification, string subject, Exception exception);
}
