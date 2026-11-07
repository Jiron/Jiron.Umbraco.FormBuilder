using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>What a delete takes, or would take.</summary>
/// <param name="Entries">The entries: those of the form that exist.</param>
/// <param name="Files">Their uploaded files.</param>
internal sealed record EntryDeletion(int Entries, int Files);

/// <summary>
/// Deletes entries a user chose, for good (ADR-0207): in one unit, each with its answers, files, events, notes and
/// action runs, the places it held given back and the mail it still had waiting dropped, with an audit entry for every
/// <see cref="AuditDetails.MaxListLength"/> of them that names their numbers. Their files' bytes go after the unit, and
/// each raises <see cref="EntryDeletedNotification"/>.
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

    /// <summary>The most entries one delete takes, as many as one change of state moves.</summary>
    public const int MaxEntries = Submissions.EntryStateChanges.MaxEntries;

    /// <summary>What a delete of these entries would take, for the user to confirm: the entries and their files.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries, 1 to <see cref="MaxEntries"/>; each counts once.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryDeletion> PlanAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, CancellationToken cancellationToken = default)
    {
        var (_, found) = await FindAsync(formKey, entryKeys, cancellationToken);
        return found.Count == 0
            ? new EntryDeletion(0, 0)
            : new EntryDeletion(found.Count, (await entries.ListFilesAsync([.. found.Select(entry => entry.Id)], cancellationToken)).Count);
    }

    /// <summary>Deletes entries of a form: those that are gone or of another form are left out.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries, 1 to <see cref="MaxEntries"/>; each counts once.</param>
    /// <param name="userKey">The user who deletes them.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <returns>What it deleted.</returns>
    public async Task<EntryDeletion> DeleteAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, Guid userKey, CancellationToken cancellationToken = default)
    {
        var (form, found) = await FindAsync(formKey, entryKeys, cancellationToken);
        if (form is null || found.Count == 0)
        {
            return new EntryDeletion(0, 0);
        }

        IReadOnlyList<StoredEntryFile> gone = [];
        var deleted = await database.WriteAsync(
            async _ =>
            {
                var ids = found.Select(entry => entry.Id).ToList();
                gone = await entries.ListFilesAsync(ids, cancellationToken);
                await emails.DeleteUnsentAsync(ids, cancellationToken);
                var count = await entries.DeleteAsync(ids, cancellationToken);
                foreach (var chunk in found.Chunk(AuditDetails.MaxListLength))
                {
                    await audit.AddAsync(FormAudit.Entry(DeletedAction, form, userKey, new AuditDetails().Add("entries", chunk.Length).AddNumbers("numbers", chunk.Select(entry => (long)entry.Number))), cancellationToken);
                }

                return count;
            },
            cancellationToken);

        await settlement.DeleteBytesAsync(gone);
        var files = gone.Count;
        var now = time.GetUtcNow();
        await entryEvents.PublishEachAsync(found.Select<StoredEntry, Func<bool, EntryDeletedNotification>>(entry => on => new EntryDeletedNotification
        {
            EntryKey = entry.Key,
            EntryNumber = entry.Number,
            State = StoredNames.Of(entry.State),
            FormAlias = form.Alias,
            OccurredUtc = now,
            FiresWebhooks = on,
        }).ToList());
        return new EntryDeletion(deleted, files);
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
