using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>Where a run of an action is (ADR-0007).</summary>
internal enum ActionRunState
{
    /// <summary>Waiting for its next attempt, or for its first one.</summary>
    Pending,

    /// <summary>Claimed: an attempt holds its lease.</summary>
    Running,

    /// <summary>Delivered; final.</summary>
    Succeeded,

    /// <summary>Failed for good; final until a retry from the log.</summary>
    Failed,

    /// <summary>
    /// Never run: an earlier run of its batch stopped the rest, which a retry of that run from the log undoes, or the
    /// change that queued it was undone.
    /// </summary>
    Skipped,

    /// <summary>An attempt was cut off or its lease ran out: it may have been delivered. Only a retry from the log runs it again.</summary>
    Uncertain,
}

/// <summary>A run of one action to queue for an entry (D6).</summary>
/// <param name="ActionKey">The action's key in the definition, not empty.</param>
/// <param name="ActionType">The action type's alias, 1 to 128 characters without spaces or control characters.</param>
/// <param name="Trigger">What started the action.</param>
/// <param name="SortOrder">The action's position among the form's actions, 0 or more: the order of section 4.4.</param>
/// <param name="StopOnFailure">Whether the later runs of its batch are skipped when this one fails for good.</param>
internal sealed record NewActionRun(
    Guid ActionKey,
    string ActionType,
    ActionTrigger Trigger,
    int SortOrder,
    bool StopOnFailure);

/// <summary>A stored run of an action.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="EntryId">The entry it runs for.</param>
/// <param name="FormVersionId">The version whose action it runs.</param>
/// <param name="ActionKey">The action's key.</param>
/// <param name="ActionType">The action type's alias.</param>
/// <param name="Trigger">What started it.</param>
/// <param name="SortOrder">The action's position.</param>
/// <param name="StopOnFailure">Whether a failure for good skips the later runs of its batch.</param>
/// <param name="State">Where it is.</param>
/// <param name="Attempts">The attempts claimed so far; a retry from the log starts them over.</param>
/// <param name="NextAttemptUtc">When it may be claimed; null once it is final.</param>
/// <param name="ChangeKey">The change that queued it with its batch; this repository always sets it.</param>
/// <param name="LockedUntilUtc">When the lease of its claim ends, while an attempt holds it or it is uncertain.</param>
/// <param name="LockedBy">The token of its last claim, kept in a final state; null while it waits for a claim.</param>
/// <param name="LastError">Why its last attempt did not succeed, or why it was skipped.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="StartedUtc">When its first attempt started, since a retry from the log, which starts the attempts over.</param>
/// <param name="FinishedUtc">When it reached a final state.</param>
internal sealed record StoredActionRun(
    long Id,
    long EntryId,
    int FormVersionId,
    Guid ActionKey,
    string ActionType,
    ActionTrigger Trigger,
    int SortOrder,
    bool StopOnFailure,
    ActionRunState State,
    int Attempts,
    DateTimeOffset? NextAttemptUtc,
    Guid? ChangeKey,
    DateTimeOffset? LockedUntilUtc,
    string? LockedBy,
    DeliveryError? LastError,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc)
{
    /// <summary>When its latest attempt started; null before its first, after a retry from the log until its next, and for a run whose attempts ran before step 10 (ADR-0195).</summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }
}

/// <summary>An entry's action runs counted by where they are, for its row in a list (ADR-0207).</summary>
/// <param name="Delivered">The runs that succeeded.</param>
/// <param name="Failed">The runs that failed for good.</param>
/// <param name="Uncertain">The runs whose outcome is uncertain.</param>
/// <param name="Waiting">The runs that wait for an attempt or hold one.</param>
/// <param name="Skipped">The runs that were never run.</param>
internal readonly record struct ActionRunTally(int Delivered, int Failed, int Uncertain, int Waiting, int Skipped)
{
    /// <summary>Every run.</summary>
    public int Total => Delivered + Failed + Uncertain + Waiting + Skipped;
}

/// <summary>
/// The action outbox in <c>jfbActionRun</c> (D6, ADR-0007, ADR-0040). Every state change is one conditional statement
/// that names the run and its state, and for an attempt's outcome the token of its claim; a change that finds the run
/// otherwise changes nothing. The runs one change queues for an entry are a batch: they run in their order, and one that
/// stops the rest stops the rest of its batch. The processor (8.1) owns the policy: the attempts, the back-off and the
/// lease.
/// </summary>
internal interface IActionRunRepository
{
    /// <summary>
    /// Queues a batch of runs for an entry, in the caller's unit of work when there is one (the submission's, D6): the
    /// runs that <paramref name="changeKey"/> (the submission, a confirmation, a user's or a rule's change) starts, due
    /// after <paramref name="wait"/> (the undo window of ADR-0023, or none). The runs as stored, by position, or null when
    /// the entry does not exist; throws <see cref="FormBuilderConflictException"/> (<see cref="FormBuilderConflict.DuplicateKey"/>)
    /// when the entry already has runs of that change, since a change queues one batch for an entry.
    /// </summary>
    Task<IReadOnlyList<StoredActionRun>?> AddAsync(long entryId, int formVersionId, Guid changeKey, IReadOnlyList<NewActionRun> runs, TimeSpan wait = default, CancellationToken cancellationToken = default);

    /// <summary>The run with this id, or null.</summary>
    Task<StoredActionRun?> GetAsync(long runId, CancellationToken cancellationToken = default);

    /// <summary>An entry's runs, in the order they were queued.</summary>
    Task<IReadOnlyList<StoredActionRun>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>The runs of these entries counted by where they are, by entry; an entry without runs is left out.</summary>
    /// <param name="entryIds">The entries, as many as a page of a list holds.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyDictionary<long, ActionRunTally>> TallyOfEntriesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of up to <paramref name="take"/> runs that are due and that no earlier run of their batch holds back, the
    /// longest due first: the runs a pass can claim.
    /// </summary>
    Task<IReadOnlyList<long>> DueAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a run for an attempt for <paramref name="claimant"/> (8.1: the server and its worker): one statement that
    /// finds it pending, due and not held back, and writes a new token of the claimant's name and 128 random bits, the
    /// lease's end and one more attempt. The run as claimed, its token in <see cref="StoredActionRun.LockedBy"/>, or null
    /// when the claim is not the caller's. No two claims get the same token, so a late finisher never records over a
    /// later attempt.
    /// </summary>
    Task<StoredActionRun?> ClaimAsync(long runId, string claimant, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Records a delivered attempt: <see cref="ActionRunState.Succeeded"/>. False when the token no longer holds the run.</summary>
    Task<bool> SucceedAsync(long runId, string token, CancellationToken cancellationToken = default);

    /// <summary>Records a failed attempt to be tried again after <paramref name="delay"/>. False when the token no longer holds the run.</summary>
    Task<bool> RetryAsync(long runId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets a claimed run go without an attempt, to be claimed again after <paramref name="delay"/>: nothing was sent, as
    /// when its entry could not be read, so the claim's attempt is given back and the run still waits for its first, as a
    /// mail's release does (ADR-0252). False when the token no longer holds the run.
    /// </summary>
    Task<bool> ReleaseAsync(long runId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failure for good: <see cref="ActionRunState.Failed"/>, and when the run stops the rest, the later pending
    /// runs of its batch <see cref="ActionRunState.Skipped"/> with the code <see cref="DeliveryError.Stopped"/> in the
    /// same unit. False when the token no longer holds the run.
    /// </summary>
    Task<bool> FailAsync(long runId, string token, DeliveryError error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an attempt that was cut off and may have been delivered: <see cref="ActionRunState.Uncertain"/>, keeping
    /// the token. False when the token no longer holds the run.
    /// </summary>
    Task<bool> CutOffAsync(long runId, string token, DeliveryError error, CancellationToken cancellationToken = default);

    /// <summary>Turns the running runs whose lease ran out into <see cref="ActionRunState.Uncertain"/>, keeping their tokens; how many.</summary>
    Task<int> ExpireLeasesAsync(CancellationToken cancellationToken = default);

    /// <summary>How many runs have failed for good since a time, which the health check reads (ADR-0165).</summary>
    /// <param name="since">The earliest time a counted run finished.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<int> CountFailedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a run from the deliveries log (section 4.4): from the state the user saw, <see cref="ActionRunState.Failed"/>
    /// or <see cref="ActionRunState.Uncertain"/>, back to pending and due now, with its attempts at 0, their times and its
    /// token cleared; a failed run that stopped the rest brings back the runs of its batch it skipped. The run as it is then, or
    /// null when it does not exist; throws <see cref="FormBuilderConcurrencyException"/> when it was no longer in that
    /// state.
    /// </summary>
    Task<StoredActionRun?> RequeueAsync(long runId, ActionRunState expected, CancellationToken cancellationToken = default);

    /// <summary>
    /// Undoes a user's change (ADR-0023): its runs that are still pending without an attempt become skipped with the code
    /// <see cref="DeliveryError.Undone"/>; how many.
    /// </summary>
    Task<int> SkipChangeAsync(Guid changeKey, CancellationToken cancellationToken = default);

    /// <summary>The same for one entry of a change, whose later move replaces what the change had waiting for it.</summary>
    Task<int> SkipChangeOfEntryAsync(Guid changeKey, long entryId, CancellationToken cancellationToken = default);

    /// <summary>Skips every run of an entry that has not started, as the entry reached a state they no longer fit (ADR-0162).</summary>
    Task<int> SkipUnstartedOfEntryAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets the error messages of an entry's runs, which a server may have answered with what it was sent, and keeps their
    /// codes (ADR-0225); how many runs had one.
    /// </summary>
    Task<int> ForgetErrorMessagesAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>How many runs a change queued, which an Undo holds against how many it could still call back.</summary>
    Task<int> CountOfChangeAsync(Guid changeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The last run of each of a form's actions (ADR-0195): the newest that has been tried or has settled, of an entry of
    /// the form, so a run that waits for its first attempt never hides the one before; an action without one is left
    /// out.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="actionKeys">Its actions, at most <see cref="Outbox.MaxTake"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoredActionRun>> LastOfActionsAsync(int formId, IReadOnlyCollection<Guid> actionKeys, CancellationToken cancellationToken = default);
}
