using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// What a folder's trash or restore takes with it, and what a user lacks on any of it (16.1's security review): a folder
/// goes to the trash with every folder and form below it outside the trash, and comes back with what went with it, so
/// <c>design</c> on the folder alone would let a user take a restricted form offline that they hold nothing on.
/// </summary>
/// <param name="folders">The folders.</param>
/// <param name="forms">The forms.</param>
/// <param name="access">What a user holds on a folder or a form.</param>
internal sealed class FolderTakes(IFolderRepository folders, IFormRepository forms, IFormBuilderAccessService access)
{
    /// <summary>
    /// The permissions of <paramref name="needed"/> the user lacks on something the trash (or, with <paramref name="restore"/>,
    /// the restore) of the folder takes; none when they hold them on all of it, or the folder is gone.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="key">The folder.</param>
    /// <param name="restore">Whether it is the folder's restore, which takes what went to the trash with it.</param>
    /// <param name="needed">The permissions each item needs.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<FormBuilderPermissions> MissingAsync(AccessUser user, Guid key, bool restore, FormBuilderPermissions needed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await folders.ListAllAsync(cancellationToken);
        if (all.FirstOrDefault(folder => folder.Key == key) is not { } top)
        {
            return FormBuilderPermissions.None;
        }

        // As the trash and the restore themselves read it: outside the trash for a trash, the same trash key for a restore.
        var trashKey = top.TrashKey;
        bool Goes(StoredFolder folder) => restore ? trashKey is not null && folder.TrashKey == trashKey : folder.TrashedUtc is null;
        bool FormGoes(StoredForm form) => restore ? trashKey is not null && form.TrashKey == trashKey : form.TrashedUtc is null;
        var below = FormBuilderFolderService.Below(all, top.Id, Goes);
        var taken = (await forms.ListInFoldersAsync(FormBuilderFolderService.Ids(top, below), cancellationToken)).Where(FormGoes);
        var keys = all.ToDictionary(folder => folder.Id, folder => folder.Key);

        var missing = FormBuilderPermissions.None;
        foreach (var folder in below)
        {
            missing |= needed & ~await access.GetForFolderAsync(user, folder.Key, cancellationToken);
        }

        foreach (var form in taken)
        {
            missing |= needed & ~await access.GetForFormAsync(user, FormBuilderFolderService.Target(form, keys), cancellationToken);
        }

        return missing;
    }
}
