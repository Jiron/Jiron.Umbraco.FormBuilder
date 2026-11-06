using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The last run of each action of a form's draft, for its card in the Actions view (ROADMAP 10.2f, ADR-0195). It needs
/// <c>design</c> on the form, as the view does, and names no entry and no answer: a run is its action, its state, its
/// attempts and its failure's code and message.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/action/last-run")]
[ApiExplorerSettings(GroupName = "Action type")]
internal sealed class ActionLastRunController(ActionLastRuns lastRuns) : FormBuilderManagementApiController
{
    /// <summary>The last run of each action of the draft that has one.</summary>
    /// <param name="key">The form.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(ActionLastRunsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid key, CancellationToken cancellationToken = default) =>
        await lastRuns.ListAsync(key, cancellationToken) is { } runs
            ? Ok(new ActionLastRunsResponseModel { Runs = [.. runs.Select(ActionLastRunResponseModel.Of)] })
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
}

/// <summary>The last runs of a form's actions (ADR-0195).</summary>
internal sealed record ActionLastRunsResponseModel
{
    /// <summary>One for each action of the draft that has run.</summary>
    public required IReadOnlyList<ActionLastRunResponseModel> Runs { get; init; }
}

/// <summary>An action's last run, as its card says it: no entry and no answer.</summary>
internal sealed record ActionLastRunResponseModel
{
    /// <summary>The action's key.</summary>
    public required Guid ActionKey { get; init; }

    /// <summary>Where it is: <c>running</c>, <c>pending</c> (tried and failed, to be tried again), <c>succeeded</c>, <c>failed</c>, <c>skipped</c> or <c>uncertain</c>.</summary>
    public required string State { get; init; }

    /// <summary>The attempts so far.</summary>
    public required int Attempts { get; init; }

    /// <summary>When its first attempt started.</summary>
    public DateTimeOffset? FirstAttemptUtc { get; init; }

    /// <summary>When its latest attempt started; null for a run whose attempts ran before the site kept it.</summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    /// <summary>When it is tried next, for a <c>pending</c> run only: another is not tried by the queue again.</summary>
    public DateTimeOffset? NextAttemptUtc { get; init; }

    /// <summary>When it reached a final state.</summary>
    public DateTimeOffset? FinishedUtc { get; init; }

    /// <summary>Why its last attempt did not succeed, as a code: the type's, such as <c>http_503</c>, or the queue's.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Why, in the type's words, without personal data.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The model of a stored run, without its id and its entry.</summary>
    public static ActionLastRunResponseModel Of(StoredActionRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new ActionLastRunResponseModel
        {
            ActionKey = run.ActionKey,
            State = StoredNames.Of(run.State),
            Attempts = run.Attempts,
            FirstAttemptUtc = run.StartedUtc,
            LastAttemptUtc = run.LastAttemptUtc,
            // A run cut off or still running keeps the time it was due at, which is past.
            NextAttemptUtc = run.State == ActionRunState.Pending ? run.NextAttemptUtc : null,
            FinishedUtc = run.FinishedUtc,
            ErrorCode = run.LastError?.Code,
            ErrorMessage = run.LastError?.Message,
        };
    }
}
