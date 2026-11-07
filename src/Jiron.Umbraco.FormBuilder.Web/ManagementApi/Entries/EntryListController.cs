using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// A form's entries list (IA 6.1, ADR-0207): what its view opens with, its pages, the user's columns, and the bulk bar's
/// delete and re-run. A page holds no sensitive answer; reading needs <c>viewEntries</c>, deleting <c>deleteEntries</c>
/// and running actions again <c>manageActions</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/entries")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class EntryListController(EntryLists lists, EntryUserSettings settings, EntryDeletions deletions, EntryActionReruns reruns) : FormBuilderManagementApiController
{
    /// <summary>What the form's Entries view opens with: its fields, the user's columns and filter sets.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("view")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryListViewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> View(Guid key, CancellationToken cancellationToken = default)
    {
        if (await lists.ViewAsync(RequestAccess.User.Key, key, cancellationToken) is not { } view)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        return Ok(new EntryListViewResponseModel
        {
            Fields = [.. view.Fields.Select(EntryListMapping.Model)],
            Columns = view.Columns,
            FilterSets = [.. view.FilterSets.Select(EntryListMapping.Model)],
        });
    }

    /// <summary>A page of the form's entries: newest first unless sorted, 50 at a time, with the count on asking.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">Which entries, the columns and the page.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryListPageResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List(Guid key, EntryListRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<DefinitionProblem>();
        var filter = EntryListMapping.Filter(request.Filter, "filter", problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var page = await lists.ListAsync(key, new EntryListRequest(filter) { Columns = request.Columns ?? [], After = request.After, Count = request.Count }, cancellationToken);
        return page is null ? FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form) : Ok(EntryListMapping.Model(page));
    }

    /// <summary>Keeps the user's columns for the form, the same on every device.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The columns.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("columns")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Columns(Guid key, EntryColumnsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var columns = request.Columns ?? [];
        await lists.CheckColumnsAsync(key, columns, cancellationToken);
        await settings.SetColumnsAsync(RequestAccess.User.Key, key, columns, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Deletes entries of the form for good, with their answers, files and timeline: without <c>confirm</c>, says what
    /// would go, for the dialog that asks first.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The entries, and whether to delete them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("delete")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.DeleteEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryDeleteResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Delete(Guid key, EntryDeleteRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Entries is not { Count: > 0 and <= EntryDeletions.MaxEntries })
        {
            return FormBuilderProblems.EntriesInvalid();
        }

        var deletion = request.Confirm
            ? await deletions.DeleteAsync(key, request.Entries, RequestAccess.User.Key, cancellationToken)
            : await deletions.PlanAsync(key, request.Entries, cancellationToken);
        return Ok(new EntryDeleteResponseModel { Entries = deletion.Entries, Files = deletion.Files, Deleted = request.Confirm && deletion.Entries > 0 });
    }

    /// <summary>
    /// Runs entries' actions again: without <c>confirm</c>, says what would run; with it, queues them as one change.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The entries, and whether to run them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("run-actions")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageActions, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryRerunResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunActions(Guid key, EntryRerunRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Entries is not { Count: > 0 and <= EntryActionReruns.MaxEntries })
        {
            return FormBuilderProblems.EntriesInvalid();
        }

        var rerun = request.Confirm
            ? await reruns.RerunAsync(key, request.Entries, RequestAccess.User.Key, cancellationToken)
            : await reruns.PlanAsync(key, request.Entries, cancellationToken);
        return Ok(new EntryRerunResponseModel
        {
            Actions = [.. rerun.Actions.Select(action => new EntryRerunActionResponseModel { Key = action.Key, Type = action.Type, Name = action.Name, Trigger = EntryListMapping.NameOf(action.Trigger) })],
            Entries = rerun.Entries,
            LeftOut = rerun.LeftOut,
            Queued = request.Confirm && rerun.Entries > 0,
        });
    }
}

/// <summary>
/// The user's filter sets (ADR-0207), the same on every device: one of a form needs <c>viewEntries</c> on it, and keeps
/// only a filter the form can answer.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/entry/filter-set")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class EntryFilterSetController(EntryLists lists, EntryUserSettings settings, IFormBuilderFormService forms, IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    /// <summary>Saves a filter set: a new one, or the one with this key.</summary>
    /// <param name="setKey">The set's key, which the client makes for a new one.</param>
    /// <param name="request">Its name, form and filter.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{setKey:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryFilterSetResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Save(Guid setKey, EntryFilterSetRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Form is { } formKey
            && (await forms.GetAsync(formKey, cancellationToken) is not { } form
                || (await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(form), cancellationToken) & FormBuilderPermissions.ViewEntries) == FormBuilderPermissions.None))
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var problems = new List<DefinitionProblem>();
        var filter = EntryListMapping.Filter(request.Filter, "filter", problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        if (!await lists.CheckAsync(request.Form, filter, cancellationToken))
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var saved = await settings.SaveFilterSetAsync(RequestAccess.User.Key, new EntryFilterSet(setKey, request.Name ?? string.Empty, request.Form, filter), cancellationToken);
        return Ok(EntryListMapping.Model(saved));
    }

    /// <summary>Deletes a filter set; one that is gone already is deleted too, and the answer says so.</summary>
    /// <param name="setKey">The set's key.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{setKey:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryFilterSetDeleteResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Delete(Guid setKey, CancellationToken cancellationToken = default) =>
        Ok(new EntryFilterSetDeleteResponseModel { Deleted = await settings.DeleteFilterSetAsync(RequestAccess.User.Key, setKey, cancellationToken) });
}
