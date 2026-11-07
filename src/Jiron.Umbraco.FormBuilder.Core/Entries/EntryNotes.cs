using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>A note on an entry.</summary>
/// <param name="Id">The note.</param>
/// <param name="Author">The backoffice user who wrote it.</param>
/// <param name="Body">What it says.</param>
/// <param name="Created">When it was written.</param>
/// <param name="Edited">When it was last changed, or none.</param>
internal sealed record EntryNote(int Id, Guid Author, string Body, DateTimeOffset Created, DateTimeOffset? Edited);

/// <summary>How a change of a note came out.</summary>
internal enum EntryNoteChange
{
    /// <summary>The note says the new text.</summary>
    Changed,

    /// <summary>The entry or the note does not exist, or they do not belong together.</summary>
    NotFound,

    /// <summary>Someone else wrote the note; only its author changes it.</summary>
    NotYours,
}

/// <summary>
/// The notes on an entry (ADR-0208): visitors never see them, everyone who may open the entry reads them and adds one,
/// and each author changes their own. A note is no change of the entry, so it writes no audit entry.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="notes">The notes.</param>
internal sealed class EntryNotes(IFormRepository forms, IEntryRepository entries, IEntryNoteRepository notes)
{
    /// <summary>The longest note.</summary>
    public const int MaxLength = EntryNoteRepository.MaxBodyLength;

    /// <summary>An entry's notes, newest first; null when the form has no such entry.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyList<EntryNote>?> ListAsync(Guid formKey, Guid entryKey, CancellationToken cancellationToken = default) =>
        await EntryAsync(formKey, entryKey, cancellationToken) is { } entry
            ? [.. (await notes.ListAsync(entry.Id, cancellationToken)).OrderByDescending(note => note.CreatedUtc).ThenByDescending(note => note.Id).Select(Of)]
            : null;

    /// <summary>Adds a note; null when the form has no such entry.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="userKey">Its author.</param>
    /// <param name="body">What it says: 1 to <see cref="MaxLength"/> characters, not only white space.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<EntryNote?> AddAsync(Guid formKey, Guid entryKey, Guid userKey, string body, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        return await EntryAsync(formKey, entryKey, cancellationToken) is { } entry
            && await notes.AddAsync(entry.Id, userKey, body.Trim(), cancellationToken) is { } note
                ? Of(note)
                : null;
    }

    /// <summary>Changes a note its author wrote.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="noteId">The note.</param>
    /// <param name="userKey">Who changes it.</param>
    /// <param name="body">What it says now: 1 to <see cref="MaxLength"/> characters, not only white space.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<(EntryNoteChange Change, EntryNote? Note)> ChangeAsync(Guid formKey, Guid entryKey, int noteId, Guid userKey, string body, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (await EntryAsync(formKey, entryKey, cancellationToken) is not { } entry
            || await notes.GetAsync(noteId, cancellationToken) is not { } note
            || note.EntryId != entry.Id)
        {
            return (EntryNoteChange.NotFound, null);
        }

        if (note.UserKey != userKey)
        {
            return (EntryNoteChange.NotYours, null);
        }

        return await notes.UpdateAsync(noteId, body.Trim(), cancellationToken) is { } changed
            ? (EntryNoteChange.Changed, Of(changed))
            : (EntryNoteChange.NotFound, null);
    }

    private static EntryNote Of(StoredEntryNote note) =>
        new(note.Id, note.UserKey, note.Body, note.CreatedUtc, note.UpdatedUtc > note.CreatedUtc ? note.UpdatedUtc : null);

    private async Task<StoredEntry?> EntryAsync(Guid formKey, Guid entryKey, CancellationToken cancellationToken) =>
        await forms.GetAsync(formKey, cancellationToken) is { } form
        && await entries.GetAsync(entryKey, cancellationToken) is { } entry
        && entry.FormId == form.Id
        && entry.State != EntryState.Draft
            ? entry
            : null;
}
