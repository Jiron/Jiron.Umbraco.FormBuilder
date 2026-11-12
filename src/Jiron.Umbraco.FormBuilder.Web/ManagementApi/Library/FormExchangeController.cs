using System.Net.Mime;
using System.Text;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// Forms that come from other forms, templates and files, and the files forms go out as (ADR-0046, ADR-0166, ADR-0220):
/// duplicate, copy actions, create from a template, import and its check, replace and its check, export. A new form needs
/// <c>design</c> where it lands, and <c>manageActions</c> there too when it comes with actions, as a draft save that adds
/// actions does; copying actions needs <c>design</c> on the source and <c>design</c> and <c>manageActions</c> on the target;
/// a replace needs <c>design</c> on the form, and <c>manageActions</c> too when it changes the draft's actions, as a draft
/// save does; an export needs <c>design</c> on the form, whose actions it carries.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form")]
[ApiExplorerSettings(GroupName = "Form")]
internal sealed class FormExchangeController(
    IFormBuilderFormExchangeService exchange,
    IFormBuilderFormService forms,
    IFormBuilderAccessService access,
    FormTemplateGallery gallery,
    FormDefinitionSerializer serializer,
    ActionGrantChecks grants)
    : FormBuilderManagementApiController
{
    /// <summary>
    /// Makes a copy of a form's draft in its folder, with new keys, with or without its actions; entries, versions and
    /// grants stay with the form, so the copy takes its folder's access (ADR-0046). Answers <c>201</c> with the copy.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The copy's name, and whether it gets the actions.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/duplicate")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Duplicate(Guid key, DuplicateFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folderKey = RequestAccess.Form!.FolderKey;
        if (await NewFormRefusalAsync(folderKey, request.IncludeActions, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var copy = await exchange.DuplicateAsync(key, new DuplicateFormRequest(request.Name, request.IncludeActions, FolderKey: folderKey, UserKey: RequestAccess.User.Key), cancellationToken);
        return copy is null ? FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form) : await CreatedAsync(copy, cancellationToken);
    }

    /// <summary>
    /// Appends copies of a form's actions to another form's draft (ADR-0046): the fields they name map by alias, and the
    /// answer lists each reference and merge tag the target has nothing to match, which the copy keeps until the editor
    /// fixes it.
    /// </summary>
    /// <param name="key">The form the actions come from.</param>
    /// <param name="request">The target, its version and the actions.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/copy-actions")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(ActionsCopiedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CopyActions(Guid key, CopyActionsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The target as the user sees it: one they hold nothing on does not exist for them.
        var target = await forms.GetAsync(request.TargetKey, cancellationToken);
        var held = target is null ? FormBuilderPermissions.None : await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(target), cancellationToken);
        if (held == FormBuilderPermissions.None)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        foreach (var needed in new[] { FormBuilderPermissions.Design, FormBuilderPermissions.ManageActions })
        {
            if ((held & needed) == FormBuilderPermissions.None)
            {
                return FormBuilderProblems.Missing(needed);
            }
        }

        var copied = await exchange.CopyActionsAsync(new CopyActionsRequest(key, request.TargetKey, request.ConcurrencyVersion, request.ActionKeys, RequestAccess.User.Key), cancellationToken);
        if (copied is null)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var form = FormResponseModel.Of(copied.Target.Form, await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(copied.Target.Form), cancellationToken));
        return Ok(new ActionsCopiedResponseModel
        {
            Target = new FormSavedResponseModel { Form = form, Version = copied.Target.Version },
            Actions = [.. copied.Actions.Select(CopiedActionResponseModel.Of)],
        });
    }

    /// <summary>
    /// Creates a form from a template of the gallery, with new keys and the new-form defaults (ADR-0046); answers
    /// <c>201</c> with it. A template that is neither built in nor saved here answers <c>404 template_not_found</c>.
    /// </summary>
    /// <param name="request">The template, the form's name and its folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("from-template")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "folderKey")]
    [ProducesResponseType(typeof(FormResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> FromTemplate(CreateFormFromTemplateRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var templateKey = request.TemplateKey!.Value;
        if (await gallery.FindAsync(templateKey, cancellationToken) is not { } template)
        {
            return FormBuilderProblems.TemplateNotFound();
        }

        if (await NewFormRefusalAsync(request.FolderKey, template.Definition.Actions.Count > 0, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var form = await exchange.CreateFromTemplateAsync(new TemplateFormRequest(templateKey, request.Name, FolderKey: request.FolderKey, UserKey: RequestAccess.User.Key), cancellationToken);
        return form is null ? FormBuilderProblems.TemplateNotFound() : await CreatedAsync(form, cancellationToken);
    }

    /// <summary>
    /// What importing an export file would make, making nothing (ADR-0166): what it holds, and the notes the import would
    /// give as this site and the user stand now. A file the import refuses answers <c>400</c> with its problems.
    /// </summary>
    /// <param name="request">The file, and the folder it would go to.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("import/check")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "folderKey")]
    [ProducesResponseType(typeof(ImportCheckResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckImport(ImportFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ImportRefusalAsync(request, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var check = await exchange.CheckImportAsync(new ImportFormRequest(request.Json, request.Name, FolderKey: request.FolderKey, UserKey: RequestAccess.User.Key), cancellationToken);
        return Ok(ImportCheckResponseModel.Of(check));
    }

    /// <summary>
    /// Creates a form from an export file as a new draft with new keys (ADR-0046); answers <c>201</c> with it and what the
    /// import changed or could not resolve.
    /// </summary>
    /// <param name="request">The file, the form's name and its folder.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("import")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Folder, Key = "folderKey")]
    [ProducesResponseType(typeof(FormImportedResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Import(ImportFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ImportRefusalAsync(request, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var imported = await exchange.ImportAsync(new ImportFormRequest(request.Json, request.Name, FolderKey: request.FolderKey, UserKey: RequestAccess.User.Key), cancellationToken);
        Response.Headers[Constants.Headers.GeneratedResource] = imported.Form.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, new FormImportedResponseModel
        {
            Form = await ModelAsync(imported.Form, cancellationToken),
            Notes = [.. imported.Notes.Select(ImportNoteResponseModel.Of)],
        });
    }

    /// <summary>
    /// What replacing a form's draft with an export file would save, saving nothing (ADR-0220): what the file holds, and the
    /// notes the replace would give. A file the replace refuses answers <c>400</c> with its problems.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The file.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/replace/check")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(ImportCheckResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckReplace(Guid key, ReplaceCheckRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ReplaceRefusalAsync(key, request.Json, null, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var check = await exchange.CheckReplaceAsync(new ImportFormRequest(request.Json, UserKey: RequestAccess.User.Key), cancellationToken);
        return Ok(ImportCheckResponseModel.Of(check));
    }

    /// <summary>
    /// Replaces a form's draft with an export file's definition, keeping the file's keys (ADR-0220): the form keeps its name,
    /// alias, folder, access, entries and History, and the published version stays until someone publishes. Answers with the
    /// form, the draft version it wrote and what the replace changed or could not resolve.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The file and the form's concurrency version.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/replace")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormReplacedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Replace(Guid key, ReplaceFormRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ReplaceRefusalAsync(key, request.Json, request.ConcurrencyVersion, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        var replaced = await exchange.ReplaceAsync(new ReplaceFormRequest(key, request.Json, request.ConcurrencyVersion, RequestAccess.User.Key), cancellationToken);
        return replaced is null
            ? FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form)
            : Ok(new FormReplacedResponseModel
            {
                Form = await ModelAsync(replaced.Result.Form, cancellationToken),
                Version = replaced.Result.Version,
                Notes = [.. replaced.Notes.Select(ImportNoteResponseModel.Of)],
            });
    }

    /// <summary>The export file of a form's draft, or of one of its versions, with the option lists it names (ADR-0046).</summary>
    /// <param name="key">The form.</param>
    /// <param name="version">A version's number, or none for the draft.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/export")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, MediaTypeNames.Application.Json)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(Guid key, [FromQuery] int? version = null, CancellationToken cancellationToken = default) =>
        await exchange.ExportAsync(key, version, cancellationToken) is { } export
            ? File(Encoding.UTF8.GetBytes(export.Json), MediaTypeNames.Application.Json, export.FileName)
            : version is null ? FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form) : FormBuilderProblems.VersionNotFound();

    // What a new form needs where it lands, beyond what the declaration checked: design there, and manageActions there too
    // when it comes with actions (ADR-0166).
    private async Task<IActionResult?> NewFormRefusalAsync(Guid? folderKey, bool withActions, CancellationToken cancellationToken)
    {
        var held = await access.GetForFolderAsync(RequestAccess.User, folderKey, cancellationToken);
        if ((held & FormBuilderPermissions.Design) == FormBuilderPermissions.None)
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.Design);
        }

        return withActions && (held & FormBuilderPermissions.ManageActions) == FormBuilderPermissions.None
            ? FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions)
            : null;
    }

    // A file with actions needs manageActions in its folder, and none of its actions may hold a secret as itself, as a draft
    // save refuses; a file that does not read is the import's to refuse, with its problems.
    private async Task<IActionResult?> ImportRefusalAsync(ImportFormRequestModel request, CancellationToken cancellationToken)
    {
        var envelope = FormExportFormat.Read(request.Json, serializer).Envelope;
        if (await NewFormRefusalAsync(request.FolderKey, envelope?.Definition.Actions.Count > 0, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        return envelope is not null && grants.LiteralSecrets(null, envelope.Definition) is { Count: > 0 } literal
            ? throw new FormBuilderValidationException(literal)
            : null;
    }

    // A replace that changes the draft's actions needs manageActions on the form, as a draft save does; compared with the
    // draft it is based on, so a stale replace names no permission for someone else's change. A file that does not read is
    // the replace's to refuse, with its problems.
    private async Task<IActionResult?> ReplaceRefusalAsync(Guid key, string json, int? concurrencyVersion, CancellationToken cancellationToken)
    {
        if (await forms.GetDraftAsync(key, cancellationToken) is not { } draft)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        if (concurrencyVersion is { } expected && draft.Form.ConcurrencyVersion != expected)
        {
            throw new FormBuilderConcurrencyException();
        }

        if (FormExportFormat.Read(json, serializer).Envelope is not { } envelope)
        {
            return null;
        }

        var held = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(draft.Form), cancellationToken);
        if ((held & FormBuilderPermissions.ManageActions) == FormBuilderPermissions.None && DefinitionDiff.ChangesActions(draft.Definition, envelope.Definition))
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
        }

        if (await grants.RefusalAsync(RequestAccess.User, FormAccessTarget.Of(draft.Form), draft.Definition, envelope.Definition, sensitive: true, cancellationToken) is { } refusal)
        {
            return refusal;
        }

        return grants.LiteralSecrets(draft.Definition, envelope.Definition) is { Count: > 0 } literal
            ? throw new FormBuilderValidationException(literal)
            : null;
    }

    private async Task<IActionResult> CreatedAsync(FormInfo form, CancellationToken cancellationToken)
    {
        Response.Headers[Constants.Headers.GeneratedResource] = form.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, await ModelAsync(form, cancellationToken));
    }

    private async Task<FormResponseModel> ModelAsync(FormInfo form, CancellationToken cancellationToken) =>
        FormResponseModel.Of(form, await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(form), cancellationToken));
}
