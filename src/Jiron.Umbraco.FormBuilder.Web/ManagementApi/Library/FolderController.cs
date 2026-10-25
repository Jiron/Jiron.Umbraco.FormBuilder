using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library's folders (ADR-0047, ADR-0050): get, create, rename, move, trash, restore and delete for good. Creating
/// needs <c>design</c> on the parent, every change <c>design</c> on the folder, a move also on the new parent (and
/// <c>managePermissions</c> with a confirmation when it gives a group more access), and a permanent delete
/// <c>design</c> and <c>deleteEntries</c> on everything it removes.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/folder")]
[ApiExplorerSettings(GroupName = "Folder")]
internal sealed class FolderController(
    IFormBuilderFolderService folders,
    IFormBuilderLibraryService library,
    IFormBuilderAccessService access,
    IAuthorizationService authorization)
    : FormBuilderManagementApiController
{
    /// <summary>A folder that the user sees, with their permissions on it.</summary>
    /// <param name="key">The folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection(On = FormBuilderAccessResourceKind.Folder, Key = "key")]
    [ProducesResponseType(typeof(FolderResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid key, CancellationToken cancellationToken = default) =>
        RequestAccess.Folder is { } folder && folder.Key == key
            ? Ok(await ModelAsync(folder, cancellationToken))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);

    /// <summary>Creates a folder at the root or in a folder outside the trash; answers <c>201</c> with it.</summary>
    /// <param name="request">The folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "parentKey")]
    [ProducesResponseType(typeof(FolderResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateFolderRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folder = await folders.CreateAsync(new NewFolderRequest(request.Name, RequestAccess.Folder?.Key, RequestAccess.User.Key), cancellationToken);
        Response.Headers[Constants.Headers.GeneratedResource] = folder.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, await ModelAsync(folder, cancellationToken));
    }

    /// <summary>Renames a folder.</summary>
    /// <param name="key">The folder.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "key")]
    [ProducesResponseType(typeof(FolderResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(Guid key, RenameFolderRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await folders.RenameAsync(key, request.Name, RequestAccess.User.Key, cancellationToken) is { } folder
            ? Ok(await ModelAsync(folder, cancellationToken))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
    }

    /// <summary>
    /// Moves a folder with everything in it to the root or into a folder outside the trash. A move that gives user
    /// groups more access needs <c>managePermissions</c> and <see cref="MoveFolderRequestModel.WidenAccess"/>; until then
    /// it answers <c>403 forbidden</c> or <c>409 widens_access</c> with the gains.
    /// </summary>
    /// <param name="key">The folder.</param>
    /// <param name="request">Where it goes.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}/move")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "key")]
    [ProducesResponseType(typeof(FolderResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Move(Guid key, MoveFolderRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folder = RequestAccess.Folder!;
        var parentKey = request.ParentKey == Guid.Empty ? null : request.ParentKey;
        if (parentKey is { } parent && await folders.GetAsync(parent, cancellationToken) is null)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
        }

        var onTarget = await authorization.AuthorizeAsync(User, FormBuilderAccessResource.ForFolder(parentKey), FormBuilderPolicies.Design);
        if (!onTarget.Succeeded)
        {
            return FormBuilderProblems.Refused(onTarget, FormBuilderAccessResourceKind.Folder);
        }

        var gains = await access.GetMoveGainsAsync(new AccessMove(PermissionTarget.Folder, folder.Key, folder.IsRestricted, folder.ParentKey, parentKey), cancellationToken);
        if (gains.Count > 0)
        {
            var listed = new Dictionary<string, object?> { ["gains"] = gains.Select(gain => new AccessGainResponseModel(gain.UserGroupKey, PermissionRules.NamesOf(gain.Gained))).ToList() };
            if (!(await authorization.AuthorizeAsync(User, FormBuilderPolicies.ManagePermissions)).Succeeded)
            {
                return FormBuilderProblems.Missing(FormBuilderPermissions.ManagePermissions, listed);
            }

            if (!request.WidenAccess)
            {
                return FormBuilderProblems.Problem(
                    StatusCodes.Status409Conflict,
                    FormBuilderProblems.WidensAccessCode,
                    "The move gives other user groups access to this folder.",
                    "Confirm the move to give them that access.",
                    listed);
            }
        }

        return await folders.MoveAsync(key, parentKey, RequestAccess.User.Key, cancellationToken) is { } moved
            ? Ok(await ModelAsync(moved, cancellationToken))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
    }

    /// <summary>Puts a folder with everything below it in the trash.</summary>
    /// <param name="key">The folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/trash")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "key")]
    [ProducesResponseType(typeof(LibraryChangeResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Trash(Guid key, CancellationToken cancellationToken = default) =>
        await folders.TrashAsync(key, RequestAccess.User.Key, cancellationToken) is { } change
            ? Ok(LibraryChangeResponseModel.Of(change))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);

    /// <summary>Takes a folder out of the trash with what went there with it, into its parent or to the root.</summary>
    /// <param name="key">The folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/restore")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "key")]
    [ProducesResponseType(typeof(LibraryChangeResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid key, CancellationToken cancellationToken = default) =>
        await folders.RestoreAsync(key, RequestAccess.User.Key, cancellationToken) is { } change
            ? Ok(LibraryChangeResponseModel.Of(change))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);

    /// <summary>
    /// Deletes a folder in the trash for good, with the folders below it, the forms in them and their entries. It needs
    /// <c>design</c> and <c>deleteEntries</c> on all of it; otherwise nothing goes.
    /// </summary>
    /// <param name="key">The folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderLibraryService.ToDelete, On = FormBuilderAccessResourceKind.Folder, Key = "key")]
    [ProducesResponseType(typeof(LibraryDeletionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid key, CancellationToken cancellationToken = default)
    {
        if (await folders.GetRemovalAsync(key, cancellationToken) is not { } removal)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
        }

        var missing = await library.MissingToDeleteAsync(RequestAccess.User, removal, cancellationToken);
        if (missing != FormBuilderPermissions.None)
        {
            return FormBuilderProblems.Missing(PermissionRules.Each.First(permission => (missing & permission) != FormBuilderPermissions.None));
        }

        return await folders.DeleteAsync(key, RequestAccess.User.Key, cancellationToken) is { } deleted
            ? Ok(LibraryDeletionResponseModel.Of(deleted))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
    }

    private async Task<FolderResponseModel> ModelAsync(FolderInfo folder, CancellationToken cancellationToken) =>
        FolderResponseModel.Of(folder, await access.GetForFolderAsync(RequestAccess.User, folder.Key, cancellationToken));
}
