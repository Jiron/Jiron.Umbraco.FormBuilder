using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

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
/// <param name="documents">Reads a version's definition.</param>
/// <param name="settlement">Lets go of what no action can use any more (ADR-0158).</param>
internal sealed class EntrySpamFolder(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IEntryEventRepository events,
    IActionRunRepository runs,
    IDailyStatRepository stats,
    IFormRepository forms,
    FormDocuments documents,
    ActionSettlement settlement)
{
    /// <summary>The kind of the event that records the move.</summary>
    public const string NotSpamEvent = "notSpam";

    /// <summary>How long the actions a backoffice user's move starts wait, so Undo can still call them back (ADR-0023).</summary>
    public static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Marks an entry in Spam as no spam; null when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it is no longer in Spam.
    /// </summary>
    /// <param name="key">The entry's key.</param>
    /// <param name="userKey">The backoffice user who marked it, whose actions wait out the undo window; null for the system.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<StoredEntry?> MarkAsNotSpamAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        if (await entries.GetAsync(key, cancellationToken) is not { } entry)
        {
            return null;
        }

        var version = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken);
        var definition = version is null ? null : documents.Readable(version);
        var unverified = definition?.Settings.Submission.Verification is not null && entry.VerifiedUtc is null;
        var timeline = await events.ListForEntryAsync(entry.Id, cancellationToken);
        var queued = unverified || definition is null ? [] : SubmissionEntries.Runs(definition, SubmissionEntries.ChosenActions(timeline));
        var change = Guid.NewGuid();
        IReadOnlyList<StoredEntryFile> gone = [];
        var taken = await database.WriteAsync(
            async _ =>
            {
                gone = [];
                var moved = await entries.ChangeStateAsync(entry.Id, EntryState.Spam, unverified ? EntryState.Unverified : EntryState.Submitted, userKey, cancellationToken);
                if (moved is null)
                {
                    return null;
                }

                await events.AddAsync(new NewEntryEvent(moved.Id, NotSpamEvent, userKey, change), cancellationToken);
                if (queued.Count > 0)
                {
                    await runs.AddAsync(moved.Id, moved.FormVersionId, change, queued, userKey is null ? default : UndoWindow, cancellationToken);
                }

                await stats.IncrementAsync(moved.FormId, [new DailyStatCount(SubmissionEntries.SubmissionsMetric)], cancellationToken);

                // No action may run for it now: what was kept for one goes in this unit. A user's move can still be undone, so
                // that waits with it (8.5, ADR-0023).
                if (queued.Count == 0 && definition is not null && userKey is null)
                {
                    gone = await settlement.SettleInUnitAsync(moved.Id, definition, cancellationToken);
                }

                return moved;
            },
            cancellationToken);
        await settlement.DeleteBytesAsync(gone);
        return taken;
    }
}
