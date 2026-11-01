using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library's forms (ADR-0044, ADR-0051): search, get, the draft, create, save the draft, publish, unpublish, rename,
/// move, trash, restore and delete for good. Searching needs the Forms section and finds what the user sees; a form's
/// name and state need seeing it; everything else <c>design</c> on it, a save that changes the actions also
/// <c>manageActions</c>, a move (and a restore that lands somewhere else) <c>design</c> on the new folder and
/// <c>managePermissions</c> with a confirmation when it gives a group more access, and a delete for good
/// <c>deleteEntries</c> too. Every change sends the <c>concurrencyVersion</c> it read.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form")]
[ApiExplorerSettings(GroupName = "Form")]
internal sealed class FormController(
    IFormBuilderFormService forms,
    IFormBuilderFolderService folders,
    IFormBuilderLibraryService library,
    IFormBuilderAccessService access,
    IAuthorizationService authorization,
    FormDefinitionSerializer serializer,
    FormSensitivityChecks sensitivity)
    : FormBuilderManagementApiController
{
    /// <summary>How long a search text may be.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>The forms outside the trash whose name or alias contains the text, that the user sees, by name.</summary>
    /// <param name="text">What the name or the alias contains, in any letter case.</param>
    /// <param name="skip">How many forms to pass over.</param>
    /// <param name="take">How many forms to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("search")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(PagedViewModel<LibraryItemResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search(
        [FromQuery, Required, StringLength(MaxSearchLength)] string text,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100,
        CancellationToken cancellationToken = default) =>
        Ok(TreeController.Paged(await library.SearchFormsAsync(RequestAccess.User, text, new PageRequest(skip, take), cancellationToken)));

    /// <summary>A form that the user sees: its name, its state and the user's permissions on it.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection(On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid key, CancellationToken cancellationToken = default) =>
        RequestAccess.Form is { } form && form.Key == key
            ? Ok(await ModelAsync(form, cancellationToken))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);

    /// <summary>A form opened for editing: its draft's definition, its problems under the current limits and its versions.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/draft")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormDraftResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Draft(Guid key, CancellationToken cancellationToken = default) =>
        await forms.GetDraftAsync(key, cancellationToken) is { } draft
            ? Ok(FormDraftResponseModel.Of(draft, await ModelAsync(draft.Form, cancellationToken)))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);

    /// <summary>Creates a form from blank, at the root or in a folder outside the trash; answers <c>201</c> with it.</summary>
    /// <param name="request">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "folderKey")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var form = await forms.CreateAsync(new NewFormRequest(request.Name, request.Alias, RequestAccess.Folder?.Key, RequestAccess.User.Key), cancellationToken);
        Response.Headers[Constants.Headers.GeneratedResource] = form.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, await ModelAsync(form, cancellationToken));
    }

    /// <summary>
    /// Saves a new draft from a definition document. A save that changes the actions needs <c>manageActions</c>; one that
    /// changes nothing writes no version.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The draft.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}/draft")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormSavedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SaveDraft(Guid key, SaveFormDraftRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = serializer.Read(request.Definition.GetRawText());
        if (read.Definition is not { } definition)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        if (await forms.GetDraftAsync(key, cancellationToken) is not { } draft)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        // Compared with the draft the save is based on, so a stale save names no permission for someone else's change.
        if (draft.Form.ConcurrencyVersion != request.ConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException();
        }

        var held = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(draft.Form), cancellationToken);
        if ((held & FormBuilderPermissions.ManageActions) == FormBuilderPermissions.None && DefinitionDiff.ChangesActions(draft.Definition, definition))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
        }

        return await forms.SaveDraftAsync(key, request.ConcurrencyVersion, definition, request.Comment, RequestAccess.User.Key, cancellationToken) is { } saved
            ? Ok(await SavedAsync(saved, cancellationToken))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
    }

    /// <summary>
    /// Publishes the stored draft after every publish check; a draft that is live already writes no version. One that makes a
    /// sensitive field no longer so needs <c>viewSensitive</c> (ADR-0150).
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The publish.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/publish")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormSavedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid key, PublishFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // A stale publish meets its conflict below, and names no permission for someone else's draft.
        if (await forms.GetDraftAsync(key, cancellationToken) is { Definition: { } definition } draft
            && draft.Form.ConcurrencyVersion == request.ConcurrencyVersion
            && await sensitivity.RefusesAsync(RequestAccess.User, draft.Form, definition, cancellationToken))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ViewSensitive);
        }

        return await forms.PublishAsync(key, request.ConcurrencyVersion, request.Comment, RequestAccess.User.Key, cancellationToken) is { } published
            ? Ok(await SavedAsync(published, cancellationToken))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
    }

    /// <summary>Takes a form offline; its versions and entries stay.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The version it is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/unpublish")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unpublish(Guid key, FormChangeRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ChangedAsync(await forms.UnpublishAsync(key, request.ConcurrencyVersion, RequestAccess.User.Key, cancellationToken), cancellationToken);
    }

    /// <summary>Renames a form, and changes its alias when one is given.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The new name.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Rename(Guid key, RenameFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ChangedAsync(await forms.RenameAsync(key, request.ConcurrencyVersion, request.Name, request.Alias, RequestAccess.User.Key, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Moves a form to the root or into a folder outside the trash. A move that gives user groups more access needs
    /// <c>managePermissions</c> and <see cref="MoveFormRequestModel.WidenAccess"/>; until then it answers
    /// <c>403 forbidden</c> or <c>409 widens_access</c> with the gains.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">Where it goes.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}/move")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Move(Guid key, MoveFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var form = RequestAccess.Form!;
        var folderKey = request.FolderKey == Guid.Empty ? null : request.FolderKey;
        if (folderKey is { } target && await folders.GetAsync(target, cancellationToken) is null)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
        }

        var move = new AccessMove(PermissionTarget.Form, form.Key, form.IsRestricted, form.FolderKey, folderKey);
        if (await LibraryMoves.RefusalAsync(access, authorization, User, move, request.WidenAccess, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        return await ChangedAsync(await forms.MoveAsync(key, request.ConcurrencyVersion, folderKey, RequestAccess.User.Key, cancellationToken), cancellationToken);
    }

    /// <summary>Puts a form in the trash on its own; the website answers it as missing until a restore.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The version it is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/trash")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Trash(Guid key, FormChangeRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ChangedAsync(await forms.TrashAsync(key, request.ConcurrencyVersion, RequestAccess.User.Key, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Takes a form out of the trash, back into its folder, or to the root when that folder is in the trash. A restore to
    /// the root needs what a move there needs; when the folder goes to the trash or comes back meanwhile, the restore is
    /// refused with <c>landing_changed</c>.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The version it is based on, and the confirmation.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/restore")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid key, RestoreFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var form = RequestAccess.Form!;
        if (await forms.GetRestoreLandingAsync(key, cancellationToken) is not { } landing)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var move = new AccessMove(PermissionTarget.Form, form.Key, form.IsRestricted, form.FolderKey, landing.FolderKey);
        if (landing.FolderKey != form.FolderKey && await LibraryMoves.RefusalAsync(access, authorization, User, move, request.WidenAccess, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        return await ChangedAsync(await forms.RestoreAsync(key, request.ConcurrencyVersion, RequestAccess.User.Key, landing, cancellationToken), cancellationToken);
    }

    /// <summary>Deletes a form in the trash for good, with its entries, their files and its versions.</summary>
    /// <param name="key">The form.</param>
    /// <param name="concurrencyVersion">The form's concurrency version that the delete is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderLibraryService.ToDelete, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(LibraryDeletionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid key, [FromQuery, BindRequired] int concurrencyVersion, CancellationToken cancellationToken = default) =>
        await forms.DeleteAsync(key, concurrencyVersion, RequestAccess.User.Key, cancellationToken) is { } deleted
            ? Ok(LibraryDeletionResponseModel.Of(deleted))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);

    private async Task<IActionResult> ChangedAsync(FormInfo? form, CancellationToken cancellationToken) =>
        form is null ? FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form) : Ok(await ModelAsync(form, cancellationToken));

    private async Task<FormSavedResponseModel> SavedAsync(FormVersionResult result, CancellationToken cancellationToken) =>
        new() { Form = await ModelAsync(result.Form, cancellationToken), Version = result.Version };

    private async Task<FormResponseModel> ModelAsync(FormInfo form, CancellationToken cancellationToken) =>
        FormResponseModel.Of(form, await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(form), cancellationToken));
}
