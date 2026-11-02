using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// An entry's action runs and their retry (section 4.4, ADR-0040, ADR-0158): reading them needs <c>viewEntries</c> on the form,
/// and a retry <c>manageActions</c>. A retry of an uncertain run needs an explicit confirmation, because its first attempt may
/// already have been delivered.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/entry/{entryKey:guid}/action-run")]
[ApiExplorerSettings(GroupName = "Entry action run")]
internal sealed class EntryActionRunController(ActionRunRetries retries) : FormBuilderManagementApiController
{
    /// <summary>An entry's runs, in the order they were queued.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(IReadOnlyList<ActionRunResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid key, Guid entryKey, CancellationToken cancellationToken = default) =>
        await retries.ListAsync(key, entryKey, cancellationToken) is { } runs
            ? Ok(runs.Select(ActionRunResponseModel.Of).ToList())
            : FormBuilderProblems.ActionRunNotFound();

    /// <summary>Queues a run that failed or is uncertain again, with its attempts starting over.</summary>
    /// <param name="key">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="runId">The run.</param>
    /// <param name="request">Whether the user confirmed that an uncertain run may go out a second time.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{runId:long}/retry")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageActions, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(ActionRunResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid key, Guid entryKey, long runId, ActionConfirmationRequestModel? request = null, CancellationToken cancellationToken = default)
    {
        var retry = await retries.RetryAsync(key, entryKey, runId, request?.Confirmed == true, RequestAccess.User.Key, cancellationToken);
        return retry.Status switch
        {
            ActionRunRetryStatus.Queued => Ok(ActionRunResponseModel.Of(retry.Run!)),
            ActionRunRetryStatus.NeedsConfirmation => FormBuilderProblems.ActionRetryNeedsConfirmation(),
            ActionRunRetryStatus.NotRetryable => FormBuilderProblems.ActionRunNotRetryable(),
            _ => FormBuilderProblems.ActionRunNotFound(),
        };
    }
}

/// <summary>A run of an action, as the entry's timeline and the deliveries log show it.</summary>
internal sealed record ActionRunResponseModel
{
    /// <summary>The run.</summary>
    public required long Id { get; init; }

    /// <summary>The action's key.</summary>
    public required Guid ActionKey { get; init; }

    /// <summary>The action type's alias.</summary>
    public required string ActionType { get; init; }

    /// <summary>What started it.</summary>
    public required string Trigger { get; init; }

    /// <summary>Where it is: <c>pending</c>, <c>running</c>, <c>succeeded</c>, <c>failed</c>, <c>skipped</c> or <c>uncertain</c>.</summary>
    public required string State { get; init; }

    /// <summary>The attempts claimed so far.</summary>
    public required int Attempts { get; init; }

    /// <summary>When it may be claimed; null once it is final.</summary>
    public DateTimeOffset? NextAttemptUtc { get; init; }

    /// <summary>Why its last attempt did not succeed, as a code.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Why, in words.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>When it was queued.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>When its first attempt started.</summary>
    public DateTimeOffset? StartedUtc { get; init; }

    /// <summary>When it reached a final state.</summary>
    public DateTimeOffset? FinishedUtc { get; init; }

    /// <summary>The model of a stored run.</summary>
    public static ActionRunResponseModel Of(StoredActionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new ActionRunResponseModel
        {
            Id = run.Id,
            ActionKey = run.ActionKey,
            ActionType = run.ActionType,
            Trigger = StoredNames.Of(run.Trigger),
            State = StoredNames.Of(run.State),
            Attempts = run.Attempts,
            NextAttemptUtc = run.NextAttemptUtc,
            ErrorCode = run.LastError?.Code,
            ErrorMessage = run.LastError?.Message,
            CreatedUtc = run.CreatedUtc,
            StartedUtc = run.StartedUtc,
            FinishedUtc = run.FinishedUtc,
        };
    }
}
