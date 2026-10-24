using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

// The trash and the permanent delete of one form (ADR-0047).
internal sealed partial class FormBuilderFormService
{
    /// <summary>The audit action of a form's trash.</summary>
    public const string TrashedAction = "form.trashed";

    /// <summary>The audit action of a form's restore.</summary>
    public const string RestoredAction = "form.restored";

    /// <inheritdoc />
    public async Task<FormInfo?> TrashAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion, inTrash: true);
        if (form.TrashedUtc is not null)
        {
            return await InfoAsync(form, cancellationToken);
        }

        await CancelableAsync(new FormTrashingNotification(form.Key, form.Alias, form.Name, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                var stored = await forms.TrashAsync(form.Id, expectedConcurrencyVersion, userKey, cancellationToken);
                if (stored is not null)
                {
                    await audit.AddAsync(Entry(TrashedAction, stored, userKey, new AuditDetails()), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        var info = await InfoAsync(changed, cancellationToken);
        await PublishedAsync(new FormTrashedNotification(info.Key, info.Alias, info.Name, userKey));
        return info;
    }

    /// <inheritdoc />
    public async Task<FormInfo?> RestoreAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion, inTrash: true);
        if (form.TrashedUtc is null)
        {
            return await InfoAsync(form, cancellationToken);
        }

        // Back into its folder while that is outside the trash, otherwise to the root.
        var folder = form.FolderId is { } folderId ? await folders.GetByIdAsync(folderId, cancellationToken) : null;
        await CancelableAsync(new FormRestoringNotification(form.Key, form.Alias, form.Name, folder is { TrashedUtc: null } ? folder.Key : null, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                if (await forms.RestoreAsync(form.Id, expectedConcurrencyVersion, userKey, cancellationToken) is not { } stored)
                {
                    return null;
                }

                var folderKey = stored.FolderId is { } id ? (await folders.GetByIdAsync(id, cancellationToken))?.Key : null;
                await audit.AddAsync(Entry(RestoredAction, stored, userKey, new AuditDetails().Add("folder", folderKey)), cancellationToken);
                return new Restored(stored, folderKey);
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        var (row, back) = changed;
        await PublishedAsync(new FormRestoredNotification(row.Key, row.Alias, row.Name, back, userKey));
        return Info(row, back);
    }

    /// <inheritdoc />
    public async Task<FormDeletion?> DeleteAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        CheckCurrent(form, expectedConcurrencyVersion, inTrash: true);
        if (form.TrashedUtc is null)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.NotTrashed, "Only a form in the trash is deleted; put it in the trash first.");
        }

        await CancelableAsync(new FormDeletingNotification(form.Key, form.Alias, form.Name, userKey, new EventMessages()));
        if (await removal.DeleteAsync(form, userKey, cancellationToken) is not { } removed)
        {
            return null;
        }

        await PublishedAsync(new FormDeletedNotification(form.Key, form.Alias, form.Name, removed.Entries, removed.Files, userKey));
        return new FormDeletion(form.Key, removed.Entries, removed.Files);
    }

    // A restored form and the folder it went back to.
    private sealed record Restored(StoredForm Form, Guid? FolderKey);
}
