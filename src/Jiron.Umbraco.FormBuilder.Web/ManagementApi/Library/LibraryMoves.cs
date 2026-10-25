using System.Security.Claims;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// What a move, or a restore that lands somewhere else, needs beyond <c>design</c> on what moves (ADR-0050, ADR-0051):
/// <c>design</c> on the folder it goes to (at the root: the folder-level part of the global grants), and, when it gives
/// user groups more access, <c>managePermissions</c> and the confirmation <c>widenAccess</c>.
/// </summary>
internal static class LibraryMoves
{
    /// <summary>
    /// The answer that refuses <paramref name="move"/>, or null when it may go: the refusal of <c>design</c> on the new
    /// folder, <c>403 forbidden</c> naming <c>managePermissions</c>, or <c>409 widens_access</c> until it is confirmed;
    /// the last two list the gains.
    /// </summary>
    public static async Task<IActionResult?> RefusalAsync(
        IFormBuilderAccessService access,
        IAuthorizationService authorization,
        ClaimsPrincipal user,
        AccessMove move,
        bool widenAccess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(move);
        var onTarget = await authorization.AuthorizeAsync(user, FormBuilderAccessResource.ForFolder(move.ToFolderKey), FormBuilderPolicies.Design);
        if (!onTarget.Succeeded)
        {
            return FormBuilderProblems.Refused(onTarget, FormBuilderAccessResourceKind.Folder);
        }

        var gains = await access.GetMoveGainsAsync(move, cancellationToken);
        if (gains.Count == 0)
        {
            return null;
        }

        var listed = new Dictionary<string, object?> { ["gains"] = gains.Select(gain => new AccessGainResponseModel(gain.UserGroupKey, PermissionRules.NamesOf(gain.Gained))).ToList() };
        if (!(await authorization.AuthorizeAsync(user, FormBuilderPolicies.ManagePermissions)).Succeeded)
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManagePermissions, listed);
        }

        return widenAccess
            ? null
            : FormBuilderProblems.Problem(
                StatusCodes.Status409Conflict,
                FormBuilderProblems.WidensAccessCode,
                move.ItemType == PermissionTarget.Form ? "This gives other user groups access to the form." : "This gives other user groups access to the folder.",
                "Confirm it to give them that access.",
                listed);
    }
}
