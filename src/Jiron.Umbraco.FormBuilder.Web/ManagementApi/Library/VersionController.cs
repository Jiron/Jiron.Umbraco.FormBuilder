using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// A form's versions (ADR-0045, ADR-0052): the History, a version with its definition, a comparison of two, a restore
/// into a new draft, pins and comments. Each needs <c>design</c> on the form; a restore that changes the actions also
/// <c>manageActions</c>, as a save does (ADR-0051).
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/versions")]
[ApiExplorerSettings(GroupName = "Version")]
internal sealed class VersionController(IFormBuilderFormVersionService versions, IFormBuilderFormService forms, IFormBuilderAccessService access, FormSensitivityChecks sensitivity, IUserService users) : FormBuilderManagementApiController
{
    /// <summary>A page of the form's versions, newest first: the publishes, and the draft saves too when asked.</summary>
    /// <param name="key">The form.</param>
    /// <param name="includeDrafts">Whether the draft saves are listed too.</param>
    /// <param name="skip">How many versions to pass over.</param>
    /// <param name="take">How many versions to answer with, 1 to 50.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(PagedViewModel<FormVersionEntryResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid key,
        [FromQuery] bool includeDrafts = false,
        [FromQuery, Range(0, int.MaxValue)] int skip = 0,
        [FromQuery, Range(1, FormBuilderFormVersionService.MaxVersionsPerPage)] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (await versions.ListVersionsAsync(key, includeDrafts, new PageRequest(skip, take), cancellationToken) is not { } page)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var names = await UserNames.OfAsync(users, page.Items.Select(item => FormVersionEntryResponseModel.MakerOf(item.Version)));
        return Ok(new PagedViewModel<FormVersionEntryResponseModel> { Total = page.Total, Items = [.. page.Items.Select(item => FormVersionEntryResponseModel.Of(item, names))] });
    }

    /// <summary>A version with its definition document, and why a restore of it would be refused now.</summary>
    /// <param name="key">The form.</param>
    /// <param name="number">The version's number.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{number:int}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormVersionDocumentResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid key, int number, CancellationToken cancellationToken = default) =>
        await versions.GetVersionAsync(key, number, cancellationToken) is { } document
            ? Ok(FormVersionDocumentResponseModel.Of(document))
            : FormBuilderProblems.VersionNotFound();

    /// <summary>What changed from one version to another, element by element, each property before and after.</summary>
    /// <param name="key">The form.</param>
    /// <param name="from">The older version's number.</param>
    /// <param name="to">The newer version's number.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("compare")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormComparisonResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Compare(
        Guid key,
        [FromQuery, BindRequired, Range(1, int.MaxValue)] int from,
        [FromQuery, BindRequired, Range(1, int.MaxValue)] int to,
        CancellationToken cancellationToken = default) =>
        await versions.CompareAsync(key, from, to, cancellationToken) is { } comparison
            ? Ok(FormComparisonResponseModel.Of(comparison))
            : FormBuilderProblems.VersionNotFound();

    /// <summary>
    /// Copies a version's definition into a new draft; the current draft stays in the History. A restore that changes the
    /// actions needs <c>manageActions</c>.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="number">The version's number.</param>
    /// <param name="request">The version of the form it is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{number:int}/restore")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormSavedResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Restore(Guid key, int number, RestoreVersionRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await versions.GetVersionAsync(key, number, cancellationToken) is not { } version)
        {
            return FormBuilderProblems.VersionNotFound();
        }

        if (await forms.GetDraftAsync(key, cancellationToken) is not { } draft)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        // As a save: compared with the draft the restore is based on, after the concurrency check (ADR-0051).
        if (draft.Form.ConcurrencyVersion != request.ConcurrencyVersion)
        {
            throw new FormBuilderConcurrencyException();
        }

        if (version.Definition is { } definition)
        {
            var held = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(draft.Form), cancellationToken);
            if ((held & FormBuilderPermissions.ManageActions) == FormBuilderPermissions.None && DefinitionDiff.ChangesActions(draft.Definition, definition))
            {
                return FormBuilderProblems.Missing(FormBuilderPermissions.ManageActions);
            }

            if (await sensitivity.RefusesAsync(RequestAccess.User, draft.Form, definition, cancellationToken))
            {
                return FormBuilderProblems.Missing(FormBuilderPermissions.ViewSensitive);
            }
        }

        if (await versions.RestoreVersionAsync(key, request.ConcurrencyVersion, number, RequestAccess.User.Key, cancellationToken) is not { } restored)
        {
            return FormBuilderProblems.VersionNotFound();
        }

        var permissions = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(restored.Form), cancellationToken);
        return Ok(new FormSavedResponseModel { Form = FormResponseModel.Of(restored.Form, permissions), Version = restored.Version });
    }

    /// <summary>Pins or unpins a version, so the cleanup keeps it; the form's concurrency version stays.</summary>
    /// <param name="key">The form.</param>
    /// <param name="number">The version's number.</param>
    /// <param name="request">The pin.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{number:int}/pin")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormVersionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pin(Guid key, int number, PinVersionRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await versions.PinVersionAsync(key, number, request.IsPinned, RequestAccess.User.Key, cancellationToken) is { } version
            ? Ok(FormVersionResponseModel.Of(version))
            : FormBuilderProblems.VersionNotFound();
    }

    /// <summary>Sets a version's comment, or clears it; the form's concurrency version stays.</summary>
    /// <param name="key">The form.</param>
    /// <param name="number">The version's number.</param>
    /// <param name="request">The comment.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{number:int}/comment")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormVersionResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Comment(Guid key, int number, CommentVersionRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await versions.CommentVersionAsync(key, number, request.Comment, RequestAccess.User.Key, cancellationToken) is { } version
            ? Ok(FormVersionResponseModel.Of(version))
            : FormBuilderProblems.VersionNotFound();
    }
}
