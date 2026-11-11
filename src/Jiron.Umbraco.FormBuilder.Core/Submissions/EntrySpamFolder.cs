using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// Takes an entry out of Spam (section 4.4, ADR-0012, ADR-0140): it moves to Submitted and queues the <c>submitted</c>
/// actions the rules chose for it when it came in, or, on a form with email verification and an address not confirmed yet,
/// to Unverified, which queues none (8.5 sends its mail). The move takes its places again, and the screen that offers it
/// asks first when an option has filled up meanwhile (11.2). All of it is one unit of work.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The timeline.</param>
/// <param name="runs">The action runs.</param>
/// <param name="stats">The daily counts.</param>
/// <param name="forms">The forms' versions, whose actions the entry was read with.</param>
/// <param name="audit">The audit log, for a user's move.</param>
/// <param name="documents">Reads a version's definition.</param>
/// <param name="settlement">Lets go of what no action can use any more (ADR-0158).</param>
/// <param name="entryEvents">Tells of an entry that is submitted now.</param>
/// <param name="retention">Gives an entry that leaves Spam its retention time (ADR-0222).</param>
/// <param name="wake">Asks for a pass of the worker, for a confirmation mail.</param>
internal sealed class EntrySpamFolder(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IEntryEventRepository events,
    IActionRunRepository runs,
    IDailyStatRepository stats,
    IFormRepository forms,
    IAuditRepository audit,
    FormDocuments documents,
    ActionSettlement settlement,
    EntryEvents entryEvents,
    EntryRetention retention,
    OutboxWake wake)
{
    /// <summary>The kind of the event that records the move.</summary>
    public const string NotSpamEvent = EntryEventRepository.NotSpamKind;

    /// <summary>How long the actions a backoffice user's move starts wait, so Undo can still call them back (ADR-0023).</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);

    /// <summary>Whether a move out of Spam counts a submission: not when the entry was counted once, before someone marked it as spam.</summary>
    /// <param name="timeline">The entry's events.</param>
    /// <param name="before">The event of the move, whose earlier events decide.</param>
    internal static bool CountsAsNew(IReadOnlyList<StoredEntryEvent> timeline, long before)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        return !timeline.Any(item => item.Id < before && item.Kind == EntryStateChanges.ChangedEvent);
    }

    /// <summary>
    /// Marks an entry in Spam as no spam; null when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it is no longer in Spam.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="userKey">The backoffice user who marked it, whose actions wait out the undo window; null for the system.</param>
    /// <param name="change">The change it is part of; null for one of its own.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<StoredEntry?> MarkAsNotSpamAsync(Guid key, Guid? userKey, Guid? change = null, CancellationToken cancellationToken = default)
    {
        if (await entries.GetAsync(key, cancellationToken) is not { } entry)
        {
            return null;
        }

        var form = await forms.GetByIdAsync(entry.FormId, cancellationToken);
        var version = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken);
        var definition = version is null ? null : documents.Readable(version);
        var unverified = definition?.Settings.Submission.Verification is not null && entry.VerifiedUtc is null;
        var timeline = await events.ListForEntryAsync(entry.Id, cancellationToken);
        var ran = await runs.ListForEntryAsync(entry.Id, cancellationToken);
        var queued = unverified || definition is null
            ? []
            : SubmissionEntries.Runs(definition, SubmissionEntries.ChosenActions(timeline), SubmissionEntries.ArrivalTriggers(definition, verified: entry.VerifiedUtc is not null))
                .Where(run => !ran.Any(earlier => earlier.ActionKey == run.ActionKey && earlier.Trigger == run.Trigger && earlier.State != ActionRunState.Skipped))
                .ToList();
        var arrives = unverified ? EntryState.Unverified : definition is null ? EntryState.Submitted : SubmissionEntries.ArrivalState(definition);
        var changeKey = change ?? Guid.NewGuid();
        var terms = await retention.TermsAsync(entry.FormId, cancellationToken);
        IReadOnlyList<StoredEntryFile> gone = [];
        var taken = await database.WriteAsync(
            async _ =>
            {
                gone = [];
                var moved = await entries.ChangeStateAsync(entry.Id, EntryState.Spam, arrives, userKey, cancellationToken);
                if (moved is null)
                {
                    return null;
                }

                moved = await retention.StampAsync(moved, terms, cancellationToken);

                await events.AddAsync(new NewEntryEvent(moved.Id, NotSpamEvent, userKey, changeKey, EntryStateChanges.Details(EntryState.Spam, arrives), userKey is null ? null : UndoWindow), cancellationToken);
                if (queued.Count > 0)
                {
                    await runs.AddAsync(moved.Id, moved.FormVersionId, changeKey, queued, userKey is null ? default : UndoWindow, cancellationToken);
                }

                // A user's move counts once it is past its Undo, which the counts have no way to take back.
                if (userKey is null && CountsAsNew(timeline, long.MaxValue))
                {
                    await stats.IncrementAsync(moved.FormId, [new DailyStatCount(SubmissionEntries.SubmissionsMetric)], cancellationToken);
                }

                if (userKey is { } user)
                {
                    await audit.AddAsync(new NewAuditEntry(EntryStateChanges.ChangedAction, FormAudit.EntityType, form?.Key, user, EntryStateChanges.Audited(moved.Key, EntryState.Spam, arrives)), cancellationToken);
                }

                // A system's move asks for the confirmation mail at once; a user's waits with the rest, for the queue of entry changes.
                if (unverified && userKey is null)
                {
                    await events.AddAsync(new NewEntryEvent(moved.Id, EntryVerification.RequestedEvent, DueIn: TimeSpan.Zero), cancellationToken);
                    await events.AddAsync(new NewEntryEvent(moved.Id, EntryVerification.ExpiresEvent, DueIn: EntryVerification.TimeOf(definition!)), cancellationToken);
                }

                // No action may run for it now: what was kept for one goes in this unit. A user's move can still be undone, so
                // that waits with it, for the queue of entry changes (ADR-0023, ADR-0162).
                if (queued.Count == 0 && definition is not null && userKey is null)
                {
                    gone = await settlement.SettleInUnitAsync(moved.Id, definition, cancellationToken);
                }

                return moved;
            },
            cancellationToken);
        await settlement.DeleteBytesAsync(gone);
        if (unverified)
        {
            wake.Wake();
        }

        if (userKey is null && taken?.State is EntryState.Submitted or EntryState.Approved && form is not null)
        {
            await entryEvents.TellArrivedAsync(taken, form.Alias, EntryState.Spam);
        }
        else if (userKey is null && taken?.State is EntryState.Unverified && form is not null)
        {
            await entryEvents.TellMovedAsync(taken, form.Alias, EntryState.Spam);
        }

        return taken;
    }
}
