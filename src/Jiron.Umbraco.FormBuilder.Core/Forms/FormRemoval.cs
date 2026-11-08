using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Deletes a form in the trash for good (ADR-0047), for the form service and the folder service: its entries in
/// batches, each a unit of work that holds the form's row while the form is in the trash at the version the editor
/// confirmed; then the uploads that wait for an entry, in batches the same way; then one unit with the form's own rows,
/// its grants, the references to it, its daily statistics and what users chose for it, and the audit entry. The bytes of the deleted files go
/// after each unit committed, so a unit that rolls back never loses a file (ADR-0041). A delete that stops part way
/// leaves the form in the trash with fewer entries, and deleting it again finishes the job; the first batch that
/// deletes anything records <see cref="DeletingAction"/> in its unit, so a delete that stops is on record too. It
/// publishes nothing; the services announce what they delete.
/// </summary>
internal sealed partial class FormRemoval(
    IFormRepository forms,
    IEntryRepository entries,
    IEmailRepository emails,
    IPermissionRepository permissions,
    IReferenceRepository references,
    IDailyStatRepository stats,
    EntryUserSettings userSettings,
    IAuditRepository audit,
    FormBuilderDatabase database,
    IFormBuilderFileStore files,
    ILogger<FormRemoval> logger)
{
    /// <summary>The audit action of a form's permanent delete.</summary>
    public const string DeletedAction = "form.deleted";

    /// <summary>
    /// The audit action of the first batch of a delete that deletes anything before its last unit (ADR-0047, amended in
    /// 4.8c): the batches commit one by one, so a delete that stops part way has deleted entries already, and the log
    /// says so. A delete whose last unit is its first to delete anything records only <see cref="DeletedAction"/>.
    /// </summary>
    public const string DeletingAction = "form.deleting";

    /// <summary>
    /// How many entries one unit deletes: with their values, a unit locks a few thousand rows at most, below the point
    /// where SQL Server locks a whole table (ADR-0041).
    /// </summary>
    public const int EntriesPerBatch = 100;

    /// <summary>How many uploads that wait for an entry one unit deletes.</summary>
    public const int UploadsPerBatch = 100;

    /// <summary>How often the delete goes back for entries that arrived after its last batch, before it gives up.</summary>
    public const int Attempts = 3;

    /// <summary>
    /// Deletes a form in the trash at the concurrency version of <paramref name="form"/>, with everything of it. Null
    /// when it is gone already.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">The form changed meanwhile; what is deleted stays deleted.</exception>
    /// <exception cref="FormBuilderConflictException">
    /// The form is outside the trash (<c>not_trashed</c>), or entries kept arriving (<c>in_use</c>).
    /// </exception>
    public async Task<FormDeletion?> DeleteAsync(StoredForm form, Guid? userKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        long entryCount = 0;
        long fileCount = 0;
        var deleting = new NewAuditEntry(DeletingAction, FormAudit.EntityType, form.Key, userKey);
        var recorded = false;
        for (var attempt = 1; ; attempt++)
        {
            if (!await InBatchesAsync(DeleteEntriesAsync) || !await InBatchesAsync(DeleteUploadsAsync))
            {
                return null;
            }

            try
            {
                return await DeleteRowAsync(form, entryCount, fileCount, userKey, cancellationToken) ? new FormDeletion(form.Key, entryCount, fileCount) : null;
            }
            catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.InUse && attempt < Attempts)
            {
                // An entry came in after the last batch: the next round deletes it too.
            }
        }

        // Runs batches until one lists nothing, deleting the bytes of the files of each; false when the form is gone. The
        // first batch that lists anything records the delete in its unit.
        async Task<bool> InBatchesAsync(Func<StoredForm, NewAuditEntry?, CancellationToken, Task<Batch?>> next)
        {
            while (await next(form, recorded ? null : deleting, cancellationToken) is { } batch)
            {
                if (batch.Listed == 0)
                {
                    return true;
                }

                recorded = true;
                entryCount += batch.Entries;
                fileCount += batch.Files.Count;
                await DeleteBytesAsync(batch.Files);
            }

            return false;
        }
    }

    // One batch of entries, with their unsent mail and, when it is the first, the delete's record; null when the form is
    // gone. The hold refuses a form that left the trash or changed since the editor confirmed, and keeps new entries
    // out until the unit ends.
    private Task<Batch?> DeleteEntriesAsync(StoredForm form, NewAuditEntry? record, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async _ =>
            {
                if (await forms.HoldTrashedAsync(form.Id, form.ConcurrencyVersion, cancellationToken) is null)
                {
                    return null;
                }

                var ids = await entries.ListIdsAsync(form.Id, EntriesPerBatch, cancellationToken);
                if (ids.Count == 0)
                {
                    return new Batch(0, 0, []);
                }

                await RecordAsync(record, cancellationToken);
                var stored = await entries.ListFilesAsync(ids, cancellationToken);
                await emails.DeleteUnsentAsync(ids, cancellationToken);
                return new Batch(ids.Count, await entries.DeleteAsync(ids, cancellationToken), stored);
            },
            cancellationToken);

    // One batch of the uploads that wait for an entry and, when it is the first, the delete's record; null when the form
    // is gone. While the unit holds the form, no entry takes an upload and none comes in, so each one listed is deleted.
    private Task<Batch?> DeleteUploadsAsync(StoredForm form, NewAuditEntry? record, CancellationToken cancellationToken) =>
        database.WriteAsync<Batch?>(
            async _ =>
            {
                if (await forms.HoldTrashedAsync(form.Id, form.ConcurrencyVersion, cancellationToken) is null)
                {
                    return null;
                }

                var uploads = await entries.ListUploadsAsync(form.Id, UploadsPerBatch, cancellationToken);
                if (uploads.Count > 0)
                {
                    await RecordAsync(record, cancellationToken);
                }

                var deleted = new List<StoredEntryFile>();
                foreach (var upload in uploads)
                {
                    if (await entries.DeleteFileAsync(upload.Id, null, cancellationToken))
                    {
                        deleted.Add(upload);
                    }
                }

                return new Batch(uploads.Count, 0, deleted);
            },
            cancellationToken);

    // The form's own rows, then what names it without a foreign key; the row goes before its statistics, so a count that
    // waited for the row counts nothing (ADR-0041). False when the form is gone.
    private Task<bool> DeleteRowAsync(StoredForm form, long entryCount, long fileCount, Guid? userKey, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async _ =>
            {
                if (!await forms.DeleteAsync(form.Id, form.ConcurrencyVersion, cancellationToken))
                {
                    return false;
                }

                await permissions.DeleteForTargetAsync(PermissionTarget.Form, form.Key, cancellationToken);
                await references.DeleteFormAsync(form.Id, cancellationToken);
                await stats.DeleteFormAsync(form.Id, cancellationToken);
                await userSettings.ForgetFormAsync(form.Key, cancellationToken);
                var details = new AuditDetails().Add("entries", entryCount).Add("files", fileCount);
                await audit.AddAsync(new NewAuditEntry(DeletedAction, FormAudit.EntityType, form.Key, userKey, details), cancellationToken);
                return true;
            },
            cancellationToken);

    private async Task RecordAsync(NewAuditEntry? record, CancellationToken cancellationToken)
    {
        if (record is not null)
        {
            await audit.AddAsync(record, cancellationToken);
        }
    }

    // After the commit, whatever the caller's cancellation says: the rows are gone, so their bytes go too. What cannot be
    // deleted is logged without its path and left for retention (13.1).
    private async Task DeleteBytesAsync(IReadOnlyList<StoredEntryFile> deleted)
    {
        foreach (var file in deleted)
        {
            try
            {
                await files.DeleteAsync(file.StoredName, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogBytesKept(logger, file.Key, exception.GetType().Name);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not delete the bytes of the deleted file {FileKey} ({Failure}); retention deletes them later")]
    private static partial void LogBytesKept(ILogger logger, Guid fileKey, string failure);

    // A batch of entries or uploads: how many rows it listed, how many entries went, and the files whose rows went.
    private sealed record Batch(int Listed, int Entries, IReadOnlyList<StoredEntryFile> Files);
}
