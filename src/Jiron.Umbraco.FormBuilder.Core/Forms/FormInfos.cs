using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A form as the form services answer with it (ADR-0044, ADR-0053): its stored row, with its folder's key.</summary>
internal sealed class FormInfos(IFolderRepository folders)
{
    /// <summary>A form as the services answer with it, with the key of the folder it is in, which this reads.</summary>
    public async Task<FormInfo> InfoAsync(StoredForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Info(form, form.FolderId is { } folderId ? (await folders.GetByIdAsync(folderId, cancellationToken))?.Key : null);
    }

    /// <summary>A form as the services answer with it, in the folder with <paramref name="folderKey"/> (null: the root).</summary>
    public static FormInfo Info(StoredForm form, Guid? folderKey)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new FormInfo(
            form.Key,
            form.Alias,
            form.Name,
            folderKey,
            form.ConcurrencyVersion,
            form.PublishedVersionId is not null,
            form.PublicRevision,
            form.IsRestricted,
            form.TrashedUtc,
            form.CreatedUtc,
            form.UpdatedUtc,
            form.CreatedByUserKey,
            form.UpdatedByUserKey);
    }
}
