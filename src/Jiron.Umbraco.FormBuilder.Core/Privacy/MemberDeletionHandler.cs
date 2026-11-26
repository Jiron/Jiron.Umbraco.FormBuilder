using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>
/// What a member's deletion in Umbraco does to their entries (ADR-0231): the privacy tools' erase for that member
/// (ADR-0223), the entries anonymised or deleted as Settings > Privacy says, their drafts deleted, entries a legal hold
/// keeps left as they are, and the erase audited with the user who deleted the member, or no user when Umbraco deleted
/// them itself. A failure is logged and never undoes the member's deletion, which has happened already. Before the
/// package's tables are there, no entry can name the member, and it does nothing (27.1).
/// </summary>
/// <param name="people">The privacy tools' find and erase.</param>
/// <param name="settings">The privacy settings, which say how the entries go.</param>
/// <param name="backOffice">The backoffice user on the request, if any.</param>
/// <param name="tables">Whether the package's tables are there.</param>
/// <param name="logger">Where a failed erase is logged, with the member's key only.</param>
internal sealed partial class MemberDeletionHandler(
    PersonData people,
    IFormBuilderSettingsService settings,
    IBackOfficeSecurityAccessor backOffice,
    FormBuilderTablesCheck tables,
    ILogger<MemberDeletionHandler> logger) : INotificationAsyncHandler<MemberDeletedNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(MemberDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var members = notification.DeletedEntities.Select(member => member.Key).Where(key => key != Guid.Empty).Distinct().ToList();
        if (members.Count == 0 || !await tables.AreThereAsync(cancellationToken))
        {
            return;
        }

        var mode = (await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)).Value.MemberDeleted == MemberDeletion.Delete
            ? PersonEraseMode.Delete
            : PersonEraseMode.Anonymise;
        var user = backOffice.BackOfficeSecurity?.CurrentUser?.Key;
        foreach (var member in members)
        {
            try
            {
                await people.EraseAsync(PersonQuery.Of(null, member), mode, user, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogEraseFailed(logger, LoggedException.Of(exception), member);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The entries of deleted member {Member} could not be erased; Privacy > People can erase them by member.")]
    private static partial void LogEraseFailed(ILogger logger, LoggedException exception, Guid member);
}
