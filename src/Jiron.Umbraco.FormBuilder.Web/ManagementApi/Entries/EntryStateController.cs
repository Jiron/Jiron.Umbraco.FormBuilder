using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>Move entries to a state.</summary>
public sealed class EntryStateRequestModel
{
    /// <summary>The entries, at most 500.</summary>
    public IReadOnlyList<Guid> Entries { get; init; } = [];

    /// <summary>The state: <c>submitted</c>, <c>approved</c>, <c>rejected</c> or <c>spam</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Whether the user agreed to overbook options and double unique answers by moving entries back (ADR-0208).</summary>
    public bool Overbook { get; init; }
}

/// <summary>What a change of state did.</summary>
public sealed class EntryStateResponseModel
{
    /// <summary>The change, which Undo names.</summary>
    public required Guid ChangeKey { get; init; }

    /// <summary>Until when it can be undone; null when it moved nothing.</summary>
    public required DateTimeOffset? UndoUntilUtc { get; init; }

    /// <summary>The entries it moved, with the state each reached.</summary>
    public required IReadOnlyList<EntryStateMovedResponseModel> Moved { get; init; }

    /// <summary>The entries it could not move: gone, already there, or in a state it does not leave that way.</summary>
    public required IReadOnlyList<Guid> Unmoved { get; init; }

    /// <summary>What moving entries back would overbook or double, which the user must agree to first; then nothing moved.</summary>
    public EntryMoveAsksResponseModel? Asks { get; init; }
}

/// <summary>An entry a change moved.</summary>
public sealed class EntryStateMovedResponseModel
{
    /// <summary>The entry.</summary>
    public required Guid Key { get; init; }

    /// <summary>The state it reached, which approval at once can make another than the one asked for.</summary>
    public required string State { get; init; }
}

/// <summary>Undo a change.</summary>
public sealed class EntryStateUndoRequestModel
{
    /// <summary>The change.</summary>
    public Guid ChangeKey { get; init; }
}

/// <summary>
/// A change of entries' state by a user, and its Undo (section 4.4, ADR-0023, ADR-0162). What the change starts waits out the undo
/// window the answer names, and both need <c>editEntries</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/entry-state")]
[ApiExplorerSettings(GroupName = "Entry state")]
internal sealed class EntryStateController(EntryStateChanges changes) : FormBuilderManagementApiController
{
    /// <summary>Moves entries of the form to a state.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The entries and the state.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.EditEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(EntryStateResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Change(Guid key, EntryStateRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (StateOf(request.State) is not { } state || request.Entries.Count is 0 or > EntryStateChanges.MaxEntries)
        {
            return FormBuilderProblems.EntryStateInvalid();
        }

        var change = await changes.ChangeAsync(key, request.Entries, state, RequestAccess.User.Key, request.Overbook, cancellationToken);
        return Ok(new EntryStateResponseModel
        {
            ChangeKey = change.ChangeKey,
            UndoUntilUtc = change.UndoUntilUtc,
            Moved = [.. change.Moved.Select(entry => new EntryStateMovedResponseModel { Key = entry.Key, State = StoredNames.Of(entry.State) })],
            Unmoved = change.Unmoved,
            Asks = change.Asks is { } asks ? EntryPanelMapping.Asks(asks) : null,
        });
    }

    /// <summary>Undoes a change inside its window.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The change.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("undo")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.EditEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Undo(Guid key, EntryStateUndoRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await changes.UndoAsync(key, request.ChangeKey, RequestAccess.User.Key, cancellationToken) switch
        {
            EntryUndoStatus.Done => NoContent(),
            EntryUndoStatus.TooLate => FormBuilderProblems.EntryUndoTooLate(),
            _ => FormBuilderProblems.EntryChangeNotFound(),
        };
    }

    private static EntryState? StateOf(string state) => state switch
    {
        "submitted" => EntryState.Submitted,
        "approved" => EntryState.Approved,
        "rejected" => EntryState.Rejected,
        "spam" => EntryState.Spam,
        _ => null,
    };
}
