using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// Whether the user a download ticket was issued to may still download its export when the link is followed (16.1's
/// review): a ticket counts for a minute, and in it the user may be disabled, or lose what the export needed. The checks
/// are those the ticket was issued after, made again for the ticket's user, as a link's request carries no token.
/// </summary>
/// <param name="users">Umbraco's users.</param>
/// <param name="access">The permission model.</param>
/// <param name="forms">The forms.</param>
/// <param name="jobs">The background exports.</param>
internal sealed class ExportDownloadHolders(IUserService users, IFormBuilderAccessService access, IFormBuilderFormService forms, EntryExportJobs jobs)
{
    /// <summary>Whether the ticket's user may still download its export of a form: <c>export</c> on it, and <c>viewSensitive</c> when sensitive fields went along.</summary>
    /// <param name="ticket">The ticket, as read.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<bool> MayDownloadFormExportAsync(ExportDownloadTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        if (await UserAsync(ticket.UserKey) is not { } user
            || await forms.GetAsync(ticket.FormKey, cancellationToken) is not { } form
            || await jobs.GetAsync(ticket.FormKey, ticket.ExportKey, ticket.UserKey, cancellationToken) is not { } record)
        {
            return false;
        }

        var held = await access.GetForFormAsync(user, FormAccessTarget.Of(form), cancellationToken);
        return held.HasFlag(FormBuilderPermissions.Export) && (!record.Request.Sensitive || held.HasFlag(FormBuilderPermissions.ViewSensitive));
    }

    /// <summary>Whether the ticket's user may still download its export of a person's data: <c>privacyTools</c>.</summary>
    /// <param name="ticket">The ticket, as read.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<bool> MayDownloadPersonExportAsync(ExportDownloadTicket ticket, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return await UserAsync(ticket.UserKey) is { } user
            && (await access.GetGlobalAsync(user, cancellationToken)).HasFlag(FormBuilderPermissions.PrivacyTools);
    }

    // The user while they may sign in: neither disabled nor locked out.
    private async Task<AccessUser?> UserAsync(Guid key) =>
        await users.GetAsync(key) is { UserState: not (UserState.Disabled or UserState.LockedOut) } user ? AccessUser.Of(user) : null;
}
