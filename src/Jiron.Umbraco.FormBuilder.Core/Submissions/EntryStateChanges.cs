using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>What came of a change of state.</summary>
/// <param name="ChangeKey">The change, which Undo names.</param>
/// <param name="UndoUntilUtc">Until when a user can undo it; null for a change nobody can undo, or one that moved nothing.</param>
/// <param name="Moved">The entries it moved, as they are now.</param>
/// <param name="Unmoved">The keys of the entries it could not move: gone, of another form, already there, or in a state it does not leave that way.</param>
/// <param name="Asks">What a move back would overbook or double, which the user must agree to first; then nothing moved (ADR-0208).</param>
internal sealed record EntryChange(Guid ChangeKey, DateTimeOffset? UndoUntilUtc, IReadOnlyList<StoredEntry> Moved, IReadOnlyList<Guid> Unmoved, EntryMoveCheck? Asks = null);

/// <summary>What came of an Undo.</summary>
internal enum EntryUndoStatus
{
    /// <summary>Every entry the change moved is back.</summary>
    Done,

    /// <summary>The form has no such change, or a change nobody can undo.</summary>
    NotFound,

    /// <summary>The window is over, it was undone already, or something happened since: nothing changed.</summary>
    TooLate,
}

/// <summary>
/// Moves entries between states (section 4.4, ADR-0023, ADR-0162). Each move is a unit of its own with its timeline event and the runs
/// of its trigger, so one entry a bulk change cannot move leaves the others moved and says so. A backoffice user's change waits out its
/// undo window: the runs carry that time, and the event carries what is left to do once it is over, which
/// <see cref="EntryChangeQueue"/> does. A change by the system runs at once. A later move replaces what an earlier one had waiting.
/// </summary>
internal sealed class EntryStateChanges(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IEntryEventRepository events,
    IActionRunRepository runs,
    IFormRepository forms,
    IAuditRepository audit,
    FormDocuments documents,
    ActionSettlement settlement,
    EntrySpamFolder spam,
    EntryMoveChecks checks,
    EntryEvents entryEvents,
    EntryRetention retention,
    OutboxWake wake,
    TimeProvider time,
    ILogger<EntryStateChanges> logger)
{
    /// <summary>The kind of the event that records a move.</summary>
    public const string ChangedEvent = EntryEventRepository.StateChangedKind;

    /// <summary>The kind of the event that records that a change was undone.</summary>
    public const string UndoneEvent = "undone";

    /// <summary>The audit action of a user's move; its details name the entry, and from and to which state.</summary>
    public const string ChangedAction = "entry.stateChanged";

    /// <summary>The audit action of an Undo.</summary>
    public const string UndoneAction = "entry.stateChangeUndone";

    /// <summary>The most entries one change moves.</summary>
    public const int MaxEntries = 500;

    /// <summary>What a state change's event says, as JSON.</summary>
    /// <param name="from">The state it left.</param>
    /// <param name="to">The state it reached.</param>
    /// <param name="replaced">Whether the move took the place of work still waiting, which an Undo could not bring back.</param>
    public static string Details(EntryState from, EntryState to, bool replaced = false) => replaced
        ? JsonSerializer.Serialize(new { from = StoredNames.Of(from), to = StoredNames.Of(to), replaced })
        : JsonSerializer.Serialize(new { from = StoredNames.Of(from), to = StoredNames.Of(to) });

    /// <summary>Whether a move took the place of work still waiting, so nothing puts things back as they were.</summary>
    /// <param name="move">The event.</param>
    public static bool Replaced(StoredEntryEvent move)
    {
        ArgumentNullException.ThrowIfNull(move);
        using var details = JsonDocument.Parse(move.Details);
        return details.RootElement.TryGetProperty("replaced", out var replaced) && replaced.GetBoolean();
    }

    /// <summary>What a user's move writes to the audit log.</summary>
    /// <param name="entry">The entry.</param>
    /// <param name="from">The state it left.</param>
    /// <param name="to">The state it reached.</param>
    public static AuditDetails Audited(Guid entry, EntryState from, EntryState to) =>
        new AuditDetails().Add("entry", entry).AddIdentifier("from", StoredNames.Of(from)).AddIdentifier("to", StoredNames.Of(to));

    /// <summary>A value of a move's event.</summary>
    /// <param name="move">The event.</param>
    /// <param name="name"><c>from</c> or <c>to</c>.</param>
    public static string DetailOf(StoredEntryEvent move, string name)
    {
        ArgumentNullException.ThrowIfNull(move);
        using var details = JsonDocument.Parse(move.Details!);
        return details.RootElement.GetProperty(name).GetString()!;
    }

    /// <summary>Moves entries of a form to a state, as <see cref="ChangeAsync(Guid, IReadOnlyList{Guid}, EntryState, Guid?, bool, CancellationToken)"/> without agreeing to overbook.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries; each counts once.</param>
    /// <param name="to">The state.</param>
    /// <param name="userKey">The backoffice user who asks, whose change waits for its undo window; null for the system.</param>
    /// <param name="cancellationToken">Cancels what has not started.</param>
    public Task<EntryChange> ChangeAsync(Guid formKey, IReadOnlyList<Guid> entryKeys, EntryState to, Guid? userKey, CancellationToken cancellationToken = default) =>
        ChangeAsync(formKey, entryKeys, to, userKey, overbook: false, cancellationToken);

    /// <summary>
    /// Moves entries of a form to a state. A user's move of entries back into a state that holds places and unique answers
    /// first reads what it would overbook or double, and moves nothing until the user agrees (ADR-0208).
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries; each counts once.</param>
    /// <param name="to">The state.</param>
    /// <param name="userKey">The backoffice user who asks, whose change waits for its undo window; null for the system.</param>
    /// <param name="overbook">Whether the user agreed to overbook options and double unique answers.</param>
    /// <param name="cancellationToken">Cancels what has not started.</param>
    public async Task<EntryChange> ChangeAsync(Guid formKey, IReadOnlyList<Guid> entryKeys, EntryState to, Guid? userKey, bool overbook, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        if (entryKeys.Count > MaxEntries)
        {
            throw new ArgumentException($"One change moves at most {MaxEntries} entries.", nameof(entryKeys));
        }

        // Before the first move, so the time the answer promises is one the server still honours for all of them.
        var started = time.GetUtcNow();
        var change = Guid.NewGuid();
        var moved = new List<StoredEntry>();
        var unmoved = new List<Guid>();
        var form = await forms.GetAsync(formKey, cancellationToken);
        if (form is not null && userKey is not null && !overbook && await AsksAsync(form, entryKeys, to, cancellationToken) is { } asks)
        {
            return new EntryChange(change, null, [], [], asks);
        }

        var terms = form is null ? null : await retention.TermsAsync(form, cancellationToken);
        foreach (var key in entryKeys.Distinct())
        {
            var entry = form is null ? null : await entries.GetAsync(key, cancellationToken);
            var result = entry is null || entry.FormId != form!.Id
                ? null
                : await MoveAsync(entry, form, terms!, to, userKey, change, cancellationToken);
            if (result is null)
            {
                unmoved.Add(key);
            }
            else
            {
                moved.Add(result);
            }
        }

        if (moved.Count == 0 || userKey is null)
        {
            return new EntryChange(change, null, moved, unmoved);
        }

        wake.WakeAfter(EntrySpamFolder.UndoWindow, time);
        var undoable = !(await events.ListForChangeAsync(change, cancellationToken)).Any(item => item.Kind == ChangedEvent && Replaced(item));
        return new EntryChange(change, undoable ? started + EntrySpamFolder.UndoWindow : null, moved, unmoved);
    }

    /// <summary>
    /// What a user's move of entries to a state would overbook or double, which the user must agree to first; null when it
    /// asks nothing. It moves nothing, so a move across forms can ask once for all of them before any moves (ADR-0209).
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries; each counts once.</param>
    /// <param name="to">The state.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryMoveCheck?> AsksAsync(Guid formKey, IReadOnlyList<Guid> entryKeys, EntryState to, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        return await forms.GetAsync(formKey, cancellationToken) is { } form ? await AsksAsync(form, entryKeys, to, cancellationToken) : null;
    }

    // What moving the form's entries back out of Rejected or Spam into a state that holds places would overbook or double.
    private async Task<EntryMoveCheck?> AsksAsync(StoredForm form, IReadOnlyList<Guid> entryKeys, EntryState to, CancellationToken cancellationToken)
    {
        if (!EntryLimits.CountingStates.Contains(to))
        {
            return null;
        }

        var coming = new List<StoredEntry>();
        foreach (var key in entryKeys.Distinct())
        {
            if (await entries.GetAsync(key, cancellationToken) is { } entry && entry.FormId == form.Id && entry.State is EntryState.Rejected or EntryState.Spam)
            {
                coming.Add(entry);
            }
        }

        return await checks.MoveAsync(form, coming, cancellationToken) is { IsEmpty: false } asks ? asks : null;
    }

    /// <summary>Whether an entry is in a state now.</summary>
    /// <param name="key">The entry.</param>
    /// <param name="state">The state.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> IsInAsync(Guid key, EntryState state, CancellationToken cancellationToken = default) =>
        (await entries.GetAsync(key, cancellationToken))?.State == state;

    /// <summary>
    /// Undoes a user's change inside its window: each entry goes back, its runs are skipped, and nothing it left to do is done. It changes
    /// nothing at all when any of that is no longer so: an entry moved on since, or a run or the queue has started.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="changeKey">The change.</param>
    /// <param name="userKey">The user who asks.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<EntryUndoStatus> UndoAsync(Guid formKey, Guid changeKey, Guid userKey, CancellationToken cancellationToken = default)
    {
        var timeline = await events.ListForChangeAsync(changeKey, cancellationToken);
        var moves = timeline.Where(item => item.Kind is ChangedEvent or EntrySpamFolder.NotSpamEvent && item.UserKey is not null).ToList();
        if (moves.Count == 0 || await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return EntryUndoStatus.NotFound;
        }

        var found = new List<(StoredEntryEvent Event, StoredEntry Entry)>();
        foreach (var move in moves)
        {
            if (await entries.GetByIdAsync(move.EntryId, cancellationToken) is not { } entry || entry.FormId != form.Id)
            {
                return EntryUndoStatus.NotFound;
            }

            found.Add((move, entry));
        }

        if (timeline.Any(item => item.Kind == UndoneEvent)
            || moves.Any(move => time.GetUtcNow() >= move.OccurredUtc + EntrySpamFolder.UndoWindow)
            || moves.Any(Replaced)
            || found.Any(item => StoredNames.Parse<EntryState>(DetailOf(item.Event, "to")) != item.Entry.State))
        {
            return EntryUndoStatus.TooLate;
        }

        var terms = await retention.TermsAsync(form, cancellationToken);
        try
        {
            await database.WriteAsync(
                async db =>
                {
                    await RowHolds.HoldFormAsync(db, new SqlNames(db), form.Id);

                    // The queue claims what a move left to do, and a run's claim counts as started: past the window, however close the clocks were.
                    // The counts come from the statements that call them back, so one claimed between a read and a write counts too.
                    if (await events.CancelDueAsync(changeKey, cancellationToken) != moves.Count
                        || await runs.SkipChangeAsync(changeKey, cancellationToken) != await runs.CountOfChangeAsync(changeKey, cancellationToken))
                    {
                        throw new UndoTooLateException();
                    }

                    foreach (var (move, entry) in found)
                    {
                        var before = StoredNames.Parse<EntryState>(DetailOf(move, "from"));
                        if (await entries.ChangeStateAsync(entry.Id, entry.State, before, userKey, cancellationToken) is { } back)
                        {
                            await retention.StampAsync(back, terms, cancellationToken);
                        }

                        await events.AddAsync(new NewEntryEvent(entry.Id, UndoneEvent, userKey, changeKey, Details(entry.State, before)), cancellationToken);
                        await audit.AddAsync(new NewAuditEntry(UndoneAction, FormAudit.EntityType, form.Key, userKey, Audited(entry.Key, entry.State, before)), cancellationToken);
                    }

                    return true;
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is UndoTooLateException or FormBuilderConcurrencyException)
        {
            return EntryUndoStatus.TooLate;
        }

        return EntryUndoStatus.Done;
    }

    // Where an entry may go by hand or by a rule. Taking it out of Spam has its own path, and an address waiting to be confirmed is
    // not approved around that.
    private static bool CanMove(EntryState from, EntryState to) => (from, to) switch
    {
        (EntryState.Draft or EntryState.Spam, _) => false,
        (EntryState.Unverified, EntryState.Spam) => true,
        (EntryState.Unverified, _) => false,
        _ when from == to => false,
        _ => to is EntryState.Submitted or EntryState.Approved or EntryState.Rejected or EntryState.Spam,
    };

    private async Task<StoredEntry?> MoveAsync(StoredEntry entry, StoredForm form, RetentionTerms terms, EntryState to, Guid? userKey, Guid change, CancellationToken cancellationToken)
    {
        if (entry.State == EntryState.Spam && to == EntryState.Submitted)
        {
            try
            {
                return await spam.MarkAsNotSpamAsync(entry.Key, userKey, change, cancellationToken);
            }
            catch (FormBuilderConcurrencyException)
            {
                return null;
            }
        }

        if (!CanMove(entry.State, to))
        {
            return null;
        }

        var definition = await DefinitionAsync(entry, cancellationToken);
        var trigger = to switch
        {
            EntryState.Approved => ActionTrigger.Approved,
            EntryState.Rejected => ActionTrigger.Rejected,
            _ => (ActionTrigger?)null,
        };
        var queued = definition is null || trigger is null
            ? []
            : SubmissionEntries.Runs(definition, SubmissionEntries.ChosenActions(await events.ListForEntryAsync(entry.Id, cancellationToken)), trigger.Value);
        IReadOnlyList<StoredEntryFile> gone = [];
        StoredEntry? moved;
        try
        {
            moved = await database.WriteAsync(
                async _ =>
                {
                    gone = [];
                    var row = await entries.ChangeStateAsync(entry.Id, entry.State, to, userKey, cancellationToken);
                    if (row is null)
                    {
                        return null;
                    }

                    row = await retention.StampAsync(row, terms, cancellationToken);

                    var replaced = await ReplaceWaitingAsync(row, to, cancellationToken);
                    await events.AddAsync(new NewEntryEvent(row.Id, ChangedEvent, userKey, change, Details(entry.State, to, replaced), userKey is null ? null : EntrySpamFolder.UndoWindow), cancellationToken);
                    if (queued.Count > 0)
                    {
                        await runs.AddAsync(row.Id, row.FormVersionId, change, queued, userKey is null ? default : EntrySpamFolder.UndoWindow, cancellationToken);
                    }

                    if (userKey is { } user)
                    {
                        await audit.AddAsync(new NewAuditEntry(ChangedAction, FormAudit.EntityType, form.Key, user, Audited(row.Key, entry.State, to)), cancellationToken);
                    }
                    else if (queued.Count == 0 && definition is not null)
                    {
                        gone = await settlement.SettleInUnitAsync(row.Id, definition, cancellationToken);
                    }

                    return row;
                },
                cancellationToken);
        }
        catch (FormBuilderConcurrencyException)
        {
            // Someone moved it first: what they did stands, and this change says it did not move it.
            return null;
        }

        await settlement.DeleteBytesAsync(gone);
        if (moved is not null && userKey is null)
        {
            await entryEvents.TellMovedAsync(moved, form.Alias);
        }

        return moved;
    }

    // A change that no longer stands sends nothing: what an earlier move still has waiting goes, and an entry that ends up rejected or
    // in Spam starts nothing it has not started yet. What is already on its way goes on, as it may have arrived. True when anything went,
    // which an Undo of this move could not bring back, so it says it is too late rather than leave an entry without what it waited for.
    private async Task<bool> ReplaceWaitingAsync(StoredEntry entry, EntryState to, CancellationToken cancellationToken)
    {
        var replaced = false;
        foreach (var earlier in await events.CancelMovesDueAsync(entry.Id, cancellationToken))
        {
            replaced = true;
            await runs.SkipChangeOfEntryAsync(earlier, entry.Id, cancellationToken);
        }

        if (to is EntryState.Rejected or EntryState.Spam && await runs.SkipUnstartedOfEntryAsync(entry.Id, cancellationToken) > 0)
        {
            replaced = true;
        }

        return replaced;
    }

    private async Task<FormDefinition?> DefinitionAsync(StoredEntry entry, CancellationToken cancellationToken)
    {
        var version = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken);
        if (version is null)
        {
            EntryStateLog.NoVersion(logger, entry.FormVersionId);
        }

        return version is null ? null : documents.Readable(version);
    }

    private sealed class UndoTooLateException : Exception;
}

/// <summary>What the state changes write to the log.</summary>
internal static partial class EntryStateLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The form version {Version} of an entry could not be read, so a change of its state queued no action")]
    public static partial void NoVersion(ILogger logger, int version);
}

/// <summary>
/// What an entry's events leave for later (ADR-0023, ADR-0162): at the end of a user's undo window the entry events Umbraco's webhooks
/// hear, letting go of what no action can use any more, and an address's confirmation mail; and the mail and the expiry of email
/// verification. Whoever claims an event does its work, so another server or a late Undo finds it gone. A claim that cannot be done
/// yet waits a while instead of holding up what comes after it.
/// </summary>
internal sealed partial class EntryChangeQueue(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IEntryEventRepository events,
    IFormRepository forms,
    IDailyStatRepository stats,
    FormDocuments documents,
    ActionSettlement settlement,
    EntryEvents entryEvents,
    EntryVerification verification,
    EntryDrafts drafts,
    OutboxWake wake,
    TimeProvider time,
    ILogger<EntryChangeQueue> logger) : IOutboxQueue
{
    /// <summary>How many due changes a pass takes.</summary>
    public const int PerPass = 100;

    /// <summary>How long what could not be done now waits for its next try.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public string Name => "entry changes";

    /// <inheritdoc />
    public async Task<bool> PassAsync(string claimant, DateTimeOffset deadline, CancellationToken stopping)
    {
        foreach (var due in await events.DueAsync(PerPass, stopping))
        {
            if (time.GetUtcNow() >= deadline || wake.Paused || stopping.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                await FinishAsync(due, stopping);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
            {
                LogFinishFailed(logger, due.Id, exception);
                await events.DelayDueAsync(due.Id, RetryAfter, CancellationToken.None);
            }
        }

        return !stopping.IsCancellationRequested && (await events.DueAsync(1, stopping)).Count > 0;
    }

    private async Task FinishAsync(StoredEntryEvent due, CancellationToken cancellationToken)
    {
        if (await entries.GetByIdAsync(due.EntryId, cancellationToken) is not { } entry
            || await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form)
        {
            await events.ClaimDueAsync(due.Id, cancellationToken);
            return;
        }

        switch (due.Kind)
        {
            case EntryVerification.RequestedEvent:
                await verification.SendAsync(entry, due, cancellationToken);
                return;
            case EntryVerification.ExpiresEvent:
                await verification.ExpireAsync(entry, due, cancellationToken);
                return;
            case EntryDrafts.ExpiresEvent:
                await drafts.ExpireAsync(entry, due, cancellationToken);
                return;
        }

        var definition = await DefinitionAsync(entry, cancellationToken);
        var counted = due.Kind == EntrySpamFolder.NotSpamEvent && EntrySpamFolder.CountsAsNew(await events.ListForEntryAsync(entry.Id, cancellationToken), due.Id);
        IReadOnlyList<StoredEntryFile> gone = [];
        var reached = StoredNames.Parse<EntryState>(EntryStateChanges.DetailOf(due, "to"));
        var stands = false;
        var claimed = await database.WriteAsync(
            async db =>
            {
                gone = [];
                var names = new SqlNames(db);
                await RowHolds.HoldFormAsync(db, names, entry.FormId);
                await RowHolds.HoldEntryAsync(db, names, entry.Id);
                if (!await events.ClaimDueAsync(due.Id, cancellationToken))
                {
                    return false;
                }

                stands = (await entries.GetByIdAsync(entry.Id, cancellationToken))?.State == reached;

                // In the claim's unit: a claim that went through with no mail and no expiry would leave the entry waiting for good.
                if (stands && reached == EntryState.Unverified && due.Kind == EntrySpamFolder.NotSpamEvent)
                {
                    await verification.RequestAsync(entry, definition, cancellationToken);
                }

                if (counted)
                {
                    await stats.IncrementAsync(entry.FormId, [new DailyStatCount(SubmissionEntries.SubmissionsMetric)], cancellationToken);
                }

                if (definition is not null)
                {
                    gone = await settlement.SettleInUnitAsync(entry.Id, definition, cancellationToken);
                }

                return true;
            },
            cancellationToken);
        await settlement.DeleteBytesAsync(gone);

        // What it reached is told only while it stands; a later move has told or will tell of its own.
        if (!claimed || !stands)
        {
            return;
        }

        if (reached == EntryState.Unverified)
        {
            wake.Wake();
        }
        else if (due.Kind == EntrySpamFolder.NotSpamEvent)
        {
            await entryEvents.TellArrivedAsync(entry, form.Alias);
        }
        else
        {
            await entryEvents.TellMovedAsync(entry, form.Alias);
        }
    }

    // A version that cannot be read any more has nothing to say about what is left: the events still go.
    private async Task<FormDefinition?> DefinitionAsync(StoredEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            var version = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken);
            return version is null ? null : documents.Readable(version);
        }
        catch (FormBuilderValidationException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Finishing the entry change of event {Event} failed; it is tried again later")]
    private static partial void LogFinishFailed(ILogger logger, long @event, Exception exception);
}
