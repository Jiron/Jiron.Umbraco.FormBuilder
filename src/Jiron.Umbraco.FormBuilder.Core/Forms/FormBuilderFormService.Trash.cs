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
        changes.EnsureOwnUnit(OwnUnit);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion, inTrash: true);
        if (form.TrashedUtc is not null)
        {
            return await documents.InfoAsync(form, cancellationToken);
        }

        await changes.CancelableAsync(new FormTrashingNotification(form.Key, form.Alias, form.Name, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            () => forms.TrashAsync(form.Id, expectedConcurrencyVersion, userKey, cancellationToken),
            stored => stored is null ? [] : [FormDocuments.Entry(TrashedAction, stored, userKey, new AuditDetails())],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        var info = await documents.InfoAsync(changed, cancellationToken);
        await changes.PublishedAsync(new FormTrashedNotification(info.Key, info.Alias, info.Name, userKey), Subject(info.Key));
        return info;
    }

    /// <inheritdoc />
    public async Task<RestoreLanding?> GetRestoreLandingAsync(Guid key, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        var folder = form.FolderId is { } folderId ? await folders.GetByIdAsync(folderId, cancellationToken) : null;
        return form.TrashedUtc is null ? new RestoreLanding(folder?.Key) : Landing(folder);
    }

    /// <inheritdoc />
    public async Task<FormInfo?> RestoreAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, RestoreLanding? landing = null, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion, inTrash: true);
        if (form.TrashedUtc is null)
        {
            return await documents.InfoAsync(form, cancellationToken);
        }

        // Back into its folder while that is outside the trash, otherwise to the root; there, or where the caller checked
        // it lands, and the write refuses another landing.
        var folder = form.FolderId is { } folderId ? await folders.GetByIdAsync(folderId, cancellationToken) : null;
        var lands = landing ?? Landing(folder);
        int? landingId = lands.FolderKey is null ? null : folder is not null && folder.Key == lands.FolderKey ? folder.Id : throw LandingChanged();
        await changes.CancelableAsync(new FormRestoringNotification(form.Key, form.Alias, form.Name, lands.FolderKey, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            async () =>
            {
                if (await forms.RestoreAsync(form.Id, expectedConcurrencyVersion, landingId, userKey, cancellationToken) is not { } stored)
                {
                    return null;
                }

                var folderKey = stored.FolderId is { } id ? (await folders.GetByIdAsync(id, cancellationToken))?.Key : null;
                return new Restored(stored, folderKey);
            },
            restored => restored is null ? [] : [FormDocuments.Entry(RestoredAction, restored.Form, userKey, new AuditDetails().Add("folder", restored.FolderKey))],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        var (row, back) = changed;
        await changes.PublishedAsync(new FormRestoredNotification(row.Key, row.Alias, row.Name, back, userKey), Subject(row.Key));
        return FormDocuments.Info(row, back);
    }

    // A restore goes back into the folder while that is outside the trash, otherwise to the root.
    private static RestoreLanding Landing(StoredFolder? folder) => new(folder is { TrashedUtc: null } ? folder.Key : null);

    private static FormBuilderConflictException LandingChanged() =>
        new(FormBuilderConflict.LandingChanged, "Where it lands changed since it was checked: its folder went to the trash or came back meanwhile. Check again, then restore it.");

    /// <inheritdoc />
    public async Task<FormDeletion?> DeleteAsync(Guid key, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await forms.GetAsync(key, cancellationToken) is not { } form)
        {
            return null;
        }

        FormChecks.Current(form, expectedConcurrencyVersion, inTrash: true);
        LibraryTrash.CheckInside(form.TrashedUtc, FormChecks.Item);
        await changes.CancelableAsync(new FormDeletingNotification(form.Key, form.Alias, form.Name, userKey, new EventMessages()));
        FormDeletion? removed;
        try
        {
            removed = await removal.DeleteAsync(form, userKey, cancellationToken);
        }
        finally
        {
            // Its grants go in its last unit; the snapshot drops whether the delete finished or not (ADR-0048).
            access.Changed();
        }

        if (removed is null)
        {
            return null;
        }

        await changes.PublishedAsync(new FormDeletedNotification(form.Key, form.Alias, form.Name, removed.Entries, removed.Files, userKey), Subject(form.Key));
        return removed;
    }

    // A restored form and the folder it went back to.
    private sealed record Restored(StoredForm Form, Guid? FolderKey);
}
