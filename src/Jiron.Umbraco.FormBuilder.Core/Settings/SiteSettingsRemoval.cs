using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Security;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// A site's root deleted for good takes what the site set of its own with it (ADR-0043, ADR-0232): each content item
/// Umbraco deletes, from the Recycle Bin or at once, has the settings rows of its key removed, audited with the user who
/// deleted it, or no user when Umbraco did, and an item that was no site removes nothing. A root in the Recycle Bin keeps
/// its rows, as it may come back. A failure is logged and never undoes the deletion, which has happened already; the rows
/// then stay unread, as no page or domain names the gone site, until the next deletion of that key, which never comes.
/// </summary>
/// <param name="settings">The settings, whose site rows go.</param>
/// <param name="backOffice">The backoffice user on the request, if any.</param>
/// <param name="logger">Where a failed removal is logged, with the root's key only.</param>
internal sealed partial class SiteSettingsRemoval(
    IFormBuilderSettingsService settings,
    IBackOfficeSecurityAccessor backOffice,
    ILogger<SiteSettingsRemoval> logger) : INotificationAsyncHandler<ContentDeletedNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(ContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var user = backOffice.BackOfficeSecurity?.CurrentUser?.Key;
        foreach (var root in notification.DeletedEntities.Select(content => content.Key).Where(key => key != Guid.Empty).Distinct())
        {
            try
            {
                await settings.RemoveSiteAsync(root, user, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogRemovalFailed(logger, exception, root);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The settings of deleted content {Root} could not be removed; they stay unread, as no page names that site.")]
    private static partial void LogRemovalFailed(ILogger logger, Exception exception, Guid root);
}
