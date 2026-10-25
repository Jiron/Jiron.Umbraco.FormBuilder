using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Where forms and folders go in the library (ADR-0047, ADR-0053), for the form and folder services: into a folder
/// outside the trash or to the root, and from the trash back into the folder they were in while that is outside the
/// trash, otherwise to the root.
/// </summary>
internal static class LibraryFolders
{
    /// <summary>A folder a form or folder may go into, which is outside the trash; null for the root.</summary>
    /// <exception cref="FormBuilderConflictException">The folder is missing or in the trash (<c>folder_missing</c>).</exception>
    public static async Task<StoredFolder?> LiveAsync(IFolderRepository folders, Guid? folderKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);
        if (folderKey is not { } key)
        {
            return null;
        }

        return await folders.GetAsync(key, cancellationToken) is { TrashedUtc: null } folder
            ? folder
            : throw new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The folder does not exist or is in the trash.");
    }

    /// <summary>
    /// Where a restore lands now: back into <paramref name="folder"/>, the folder it was in, while that is outside the
    /// trash, otherwise at the root.
    /// </summary>
    public static RestoreLanding Landing(StoredFolder? folder) => new(folder is { TrashedUtc: null } ? folder.Key : null);

    /// <summary>
    /// The id of the folder a restore lands in as the caller checked it (<paramref name="lands"/>), for the write, which
    /// refuses another landing; null for the root. A restore lands only in <paramref name="folder"/>, the folder the
    /// <paramref name="item"/> was in, or at the root.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">The check named another folder (<c>landing_changed</c>).</exception>
    public static int? LandingId(RestoreLanding lands, StoredFolder? folder, string item)
    {
        ArgumentNullException.ThrowIfNull(lands);
        return lands.FolderKey is null ? null
            : folder is not null && folder.Key == lands.FolderKey ? folder.Id
            : throw LibraryTrash.LandingChanged(item);
    }
}
