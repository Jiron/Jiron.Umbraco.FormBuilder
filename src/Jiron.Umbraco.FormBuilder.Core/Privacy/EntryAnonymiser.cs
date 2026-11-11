using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>What an anonymise took.</summary>
/// <param name="Entries">The entries anonymised.</param>
/// <param name="Files">Their files, which went.</param>
/// <param name="Held">The entries a legal hold keeps, which it left as they were.</param>
internal sealed record EntryAnonymised(int Entries, int Files, int Held);

/// <summary>
/// Anonymises entries for a person who asked (ADR-0223): each stays with its form, number, state and times, so counts and
/// statistics keep it, and everything else about the person goes in one unit per form: every answer, which gives its places
/// and unique answers back (ADR-0090, ADR-0091); its files and, after the unit, their bytes; its search text, IP address,
/// context, page, member key and resume token; its notes; all its mail; its action runs' errors; and the details of its
/// edit and forward events. Its timeline gains an <see cref="AnonymisedEvent"/>, and an audit entry for every
/// <see cref="AuditDetails.MaxListLength"/> of them names their numbers. An entry a legal hold keeps stays as it is.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="entries">The entries.</param>
/// <param name="people">What else an entry holds of a person.</param>
/// <param name="events">The timelines.</param>
/// <param name="emails">The mail outbox.</param>
/// <param name="audit">The audit log.</param>
/// <param name="settlement">Deletes the files' bytes.</param>
/// <param name="time">The clock.</param>
internal sealed class EntryAnonymiser(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IPersonDataRepository people,
    IEntryEventRepository events,
    IEmailRepository emails,
    IAuditRepository audit,
    ActionSettlement settlement,
    TimeProvider time)
{
    /// <summary>The kind of the event an anonymised entry gains.</summary>
    public const string AnonymisedEvent = "anonymised";

    /// <summary>The audit action of an anonymise; its details name the entries' numbers.</summary>
    public const string AnonymisedAction = "entry.anonymised";

    /// <summary>The kinds of event whose details may hold a person's answers or address.</summary>
    public static readonly IReadOnlyList<string> PersonalEvents = [EntryEdits.EditedEvent, EntryForwards.ForwardedEvent];

    /// <summary>Anonymises entries of a form; those of another form, drafts and those a hold keeps are left out.</summary>
    /// <param name="form">The form.</param>
    /// <param name="entryIds">The entries, at most <see cref="EntryDeletions.MaxEntries"/>.</param>
    /// <param name="userKey">The user who anonymises them; null when Umbraco does, for a member it deleted (ADR-0231).</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<EntryAnonymised> AnonymiseAsync(StoredForm form, IReadOnlyCollection<long> entryIds, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count > EntryDeletions.MaxEntries)
        {
            throw new ArgumentException($"One anonymise takes at most {EntryDeletions.MaxEntries} entries.", nameof(entryIds));
        }

        if (entryIds.Count == 0)
        {
            return new EntryAnonymised(0, 0, 0);
        }

        IReadOnlyList<StoredEntryFile> gone = [];
        var held = 0;
        var done = await database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await RowHolds.HoldFormAsync(db, n, form.Id);
                var ids = entryIds.Distinct().ToList();
                await RowHolds.HoldAsync(db, n, FormBuilderTables.Entry, $"{n.C("id")} IN ({SqlArgs.Placeholders(0, ids.Count)})", [.. ids.Cast<object?>()]);
                var now = time.GetUtcNow();
                var rows = (await entries.ListForRetentionAsync(ids, cancellationToken)).Where(row => row.FormId == form.Id && row.State != EntryState.Draft).ToList();
                held = rows.Count(row => EntryDeletions.IsHeld(row.HoldUntilUtc, now));
                var taken = rows.Where(row => !EntryDeletions.IsHeld(row.HoldUntilUtc, now)).ToList();
                if (taken.Count == 0)
                {
                    return 0;
                }

                var chosen = taken.Select(row => row.Id).ToList();
                var values = await entries.ListValuesAsync(chosen, cancellationToken);
                foreach (var row in taken)
                {
                    // Every answer goes, and with it the places and unique answers it held.
                    var fields = values.Where(value => value.EntryId == row.Id).Select(value => value.FieldId).Distinct().ToList();
                    await entries.ReplaceValuesAsync(row.Id, fields, [], string.Empty, cancellationToken);
                }

                // The files once no value names them.
                gone = await entries.ListFilesAsync(chosen, cancellationToken);
                foreach (var file in gone)
                {
                    await entries.DeleteFileAsync(file.Id, file.EntryId, cancellationToken);
                }

                await people.ScrubAsync(chosen, PersonalEvents, cancellationToken);
                await emails.DeleteAllAsync(chosen, cancellationToken);
                foreach (var row in taken)
                {
                    await events.AddAsync(new NewEntryEvent(row.Id, AnonymisedEvent, userKey), cancellationToken);
                }

                foreach (var chunk in taken.Chunk(AuditDetails.MaxListLength))
                {
                    await audit.AddAsync(FormAudit.Entry(AnonymisedAction, form, userKey, new AuditDetails().Add("entries", chunk.Length).AddNumbers("numbers", chunk.Select(row => (long)row.Number))), cancellationToken);
                }

                return taken.Count;
            },
            cancellationToken);
        if (done > 0)
        {
            await settlement.DeleteBytesAsync(gone);
        }

        return new EntryAnonymised(done, done > 0 ? gone.Count : 0, held);
    }
}
