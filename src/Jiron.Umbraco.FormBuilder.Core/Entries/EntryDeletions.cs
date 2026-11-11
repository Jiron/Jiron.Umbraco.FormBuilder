using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>What a delete takes, or would take.</summary>
/// <param name="Entries">The entries: those of the form that exist and that no legal hold keeps.</param>
/// <param name="Files">Their uploaded files.</param>
/// <param name="Held">The entries of the form a legal hold keeps, which the delete leaves (ADR-0222).</param>
internal sealed record EntryDeletion(int Entries, int Files, int Held = 0);

/// <summary>
/// Deletes entries for good (ADR-0207, ADR-0222): those a user chose, and those whose retention time has come. Each goes
/// in one unit with its answers, files, events, notes and action runs, the places it held given back and its mail dropped,
/// with an audit entry for every <see cref="AuditDetails.MaxListLength"/> of them that names their numbers. Their files'
/// bytes go after the unit, and each raises <see cref="EntryDeletedNotification"/>. An entry a legal hold keeps stays
/// until its hold ends, whoever deletes.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="emails">The mail outbox.</param>
/// <param name="audit">The audit log.</param>
/// <param name="settlement">Deletes the files' bytes.</param>
/// <param name="entryEvents">Publishes the notifications.</param>
/// <param name="time">The clock.</param>
internal sealed class EntryDeletions(
    FormBuilderDatabase database,
    IFormRepository forms,
    IEntryRepository entries,
    IEmailRepository emails,
    IAuditRepository audit,
    ActionSettlement settlement,
    EntryEvents entryEvents,
    TimeProvider time)
{
    /// <summary>The audit action of a user's delete; its details name the entries' numbers.</summary>
    public const string DeletedAction = "entry.deleted";

    /// <summary>The audit action of retention's delete (ADR-0222); its details name the entries' numbers.</summary>
    public const string PurgedAction = "entry.purged";

    /// <summary>The audit action of the privacy tools' erase (ADR-0223); its details name the entries' numbers.</summary>
    public const string ErasedAction = "entry.erased";

    /// <summary>The most entries one delete takes, as many as one change of state moves.</summary>
    public const int MaxEntries = Submissions.EntryStateChanges.MaxEntries;

    /// <summary>What a delete of these entries would take, for the user to confirm: the entries and their files, and those held.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries, 1 to <see cref="MaxEntries"/>; each counts once.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryDeletion> PlanAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, CancellationToken cancellationToken = default)
    {
        var (_, found) = await FindAsync(formKey, entryKeys, cancellationToken);
        var now = time.GetUtcNow();
        var free = found.Where(entry => !IsHeld(entry.HoldUntilUtc, now)).ToList();
        var files = free.Count == 0 ? 0 : (await entries.ListFilesAsync([.. free.Select(entry => entry.Id)], cancellationToken)).Count;
        return new EntryDeletion(free.Count, files, found.Count - free.Count);
    }

    /// <summary>Deletes entries of a form: those that are gone, of another form or held are left out.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries, 1 to <see cref="MaxEntries"/>; each counts once.</param>
    /// <param name="userKey">The user who deletes them.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <returns>What it deleted, and the entries it left as held.</returns>
    public async Task<EntryDeletion> DeleteAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, Guid userKey, CancellationToken cancellationToken = default)
    {
        var (form, found) = await FindAsync(formKey, entryKeys, cancellationToken);
        if (form is null || found.Count == 0)
        {
            return new EntryDeletion(0, 0);
        }

        return await RemoveAsync(form, [.. found.Select(entry => entry.Id)], userKey, DeletedAction, _ => true, cancellationToken);
    }

    /// <summary>
    /// Deletes entries of a form whose retention time has come (ADR-0222), with all their mail, sent mail too. Each is read
    /// again once its row is held: one a hold keeps by now, or whose time moved past <paramref name="now"/>, stays.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="due">The entries, at most <see cref="MaxEntries"/>, of the form.</param>
    /// <param name="now">The moment their time has come by.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <returns>What it deleted.</returns>
    public Task<EntryDeletion> PurgeAsync(StoredForm form, IReadOnlyCollection<long> due, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(due);
        if (due.Count > MaxEntries)
        {
            throw new ArgumentException($"One delete takes at most {MaxEntries} entries.", nameof(due));
        }

        return RemoveAsync(form, due, null, PurgedAction, row => row.PurgeAfterUtc is { } after && after <= now, cancellationToken);
    }

    /// <summary>
    /// Erases entries of a form for a person who asked (ADR-0223), with all their mail, sent mail too; one a hold keeps
    /// stays.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="entryIds">The entries, at most <see cref="MaxEntries"/>, of the form.</param>
    /// <param name="userKey">The user who erases them; null when Umbraco does, for a member it deleted (ADR-0231).</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <returns>What it deleted, and the entries it left as held.</returns>
    public Task<EntryDeletion> EraseAsync(StoredForm form, IReadOnlyCollection<long> entryIds, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count > MaxEntries)
        {
            throw new ArgumentException($"One delete takes at most {MaxEntries} entries.", nameof(entryIds));
        }

        return RemoveAsync(form, entryIds, userKey, ErasedAction, _ => true, cancellationToken);
    }

    /// <summary>Whether a hold that lasts until <paramref name="until"/> keeps its entry at <paramref name="now"/>.</summary>
    public static bool IsHeld(DateTimeOffset? until, DateTimeOffset now) => until is { } end && end > now;

    // The unit: the form's row and the entries' rows first, then each entry read again, those still to go deleted with
    // their mail, and audited; the bytes and the notifications after the commit.
    private async Task<EntryDeletion> RemoveAsync(StoredForm form, IReadOnlyCollection<long> ids, Guid? userKey, string action, Func<EntryRetentionRow, bool> still, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredEntryFile> gone = [];
        IReadOnlyList<EntryRetentionRow> taken = [];
        var held = 0;
        var allMail = action is PurgedAction or ErasedAction;
        var deleted = await database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await RowHolds.HoldFormAsync(db, n, form.Id);
                var list = SqlArgs.Placeholders(0, ids.Count);
                await RowHolds.HoldAsync(db, n, FormBuilderTables.Entry, $"{n.C("id")} IN ({list})", [.. ids.Cast<object?>()]);
                var now = time.GetUtcNow();
                var rows = (await entries.ListForRetentionAsync(ids, cancellationToken)).Where(row => row.FormId == form.Id).ToList();
                held = rows.Count(row => IsHeld(row.HoldUntilUtc, now));
                taken = [.. rows.Where(row => !IsHeld(row.HoldUntilUtc, now) && still(row))];
                if (taken.Count == 0)
                {
                    return 0;
                }

                var chosen = taken.Select(row => row.Id).ToList();
                gone = await entries.ListFilesAsync(chosen, cancellationToken);
                _ = allMail ? await emails.DeleteAllAsync(chosen, cancellationToken) : await emails.DeleteUnsentAsync(chosen, cancellationToken);
                var count = await entries.DeleteAsync(chosen, cancellationToken);
                foreach (var chunk in taken.Chunk(AuditDetails.MaxListLength))
                {
                    await audit.AddAsync(FormAudit.Entry(action, form, userKey, new AuditDetails().Add("entries", chunk.Length).AddNumbers("numbers", chunk.Select(row => (long)row.Number))), cancellationToken);
                }

                return count;
            },
            cancellationToken);
        if (deleted == 0)
        {
            return new EntryDeletion(0, 0, held);
        }

        await settlement.DeleteBytesAsync(gone);
        var occurred = time.GetUtcNow();
        await entryEvents.PublishEachAsync(taken.Select<EntryRetentionRow, Func<bool, EntryDeletedNotification>>(row => on => new EntryDeletedNotification
        {
            EntryKey = row.Key,
            EntryNumber = row.Number,
            State = StoredNames.Of(row.State),
            FormAlias = form.Alias,
            OccurredUtc = occurred,
            FiresWebhooks = on,
        }).ToList());
        return new EntryDeletion(deleted, gone.Count, held);
    }

    // The form and those of the entries that are its own.
    private async Task<(StoredForm? Form, List<StoredEntry> Found)> FindAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        if (entryKeys.Count > MaxEntries)
        {
            throw new ArgumentException($"One delete takes at most {MaxEntries} entries.", nameof(entryKeys));
        }

        var found = new List<StoredEntry>();
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return (null, found);
        }

        foreach (var key in entryKeys.Distinct())
        {
            if (await entries.GetAsync(key, cancellationToken) is { } entry && entry.FormId == form.Id)
            {
                found.Add(entry);
            }
        }

        return (form, found);
    }
}
