using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Creates, renames, moves, trashes, restores and deletes the folders of the form library, and empties the trash
/// (ADR-0047), on the rules of the form service (ADR-0044, ADR-0053): every change is a unit of work of its own,
/// refused inside a caller's unit or an Umbraco scope; the checks that need no write, then the cancelable notification,
/// then the unit with the change and its audit entry, then the notification that it happened. Folders have no
/// concurrency version: the last rename or move wins, and a trash or a restore that has nothing to do changes nothing.
/// A missing folder gives null; a refusal throws <see cref="FormBuilderValidationException"/>,
/// <see cref="FormBuilderConflictException"/>, <see cref="FormBuilderConcurrencyException"/> or
/// <see cref="FormBuilderCancelledException"/>, each with a code from <see cref="FormBuilderErrorCodes"/>.
/// </summary>
public interface IFormBuilderFolderService
{
    /// <summary>A folder by key, or null.</summary>
    Task<FolderInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every folder, in the trash or not, by sort order and name: a library holds few folders. With
    /// <paramref name="visibility"/>, only the folders it allows, those that lead to something the user may see
    /// (ADR-0048); without it, every folder, which only Jiron Form Builder's own work asks for.
    /// </summary>
    Task<IReadOnlyList<FolderInfo>> ListAsync(FolderVisibility? visibility = null, CancellationToken cancellationToken = default);

    /// <summary>Creates a folder at the root or in a folder outside the trash.</summary>
    /// <exception cref="FormBuilderValidationException">The name is not allowed.</exception>
    /// <exception cref="FormBuilderConflictException">The parent is missing or in the trash, or the tree would be too deep.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the create.</exception>
    Task<FolderInfo> CreateAsync(NewFolderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Renames a folder outside the trash; the same name writes nothing. Null when it does not exist.</summary>
    /// <exception cref="FormBuilderValidationException">The name is not allowed.</exception>
    /// <exception cref="FormBuilderConflictException">The folder is in the trash.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FolderInfo?> RenameAsync(Guid key, string name, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a folder outside the trash, with everything in it, into another folder outside the trash or to the root.
    /// Null when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">
    /// The folder is in the trash, the target is missing or in the trash, is the folder or below it, or the tree would be
    /// too deep.
    /// </exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FolderInfo?> MoveAsync(Guid key, Guid? parentKey, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a folder in the trash with the folders below it and the forms in them, all with one trash key, so a restore
    /// brings back exactly what went. A folder in the trash already stays as it is. Null when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FolderChange?> TrashAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Where a restore of the folder lands now: its parent while that is outside the trash, otherwise the root; for a
    /// folder outside the trash, its parent. Null when the folder does not exist.
    /// </summary>
    Task<RestoreLanding?> GetRestoreLandingAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes a folder out of the trash, back into its parent or to the root when the parent is in the trash, with the
    /// folders and forms that went to the trash with it. With <paramref name="landing"/>, the landing the caller checked
    /// (<see cref="GetRestoreLandingAsync"/>), it lands there or nowhere. A folder outside the trash stays as it is. Null
    /// when it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">
    /// The restore would land elsewhere than <paramref name="landing"/> or than it found before the write (<c>landing_changed</c>).
    /// </exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FolderChange?> RestoreAsync(Guid key, Guid? userKey, RestoreLanding? landing = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a folder in the trash for good with the folders below it and the forms in them, each form with everything
    /// of it. A delete that stops part way leaves the rest in the trash, and deleting again finishes the job. Null when
    /// it does not exist.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">
    /// The folder is outside the trash (<c>not_trashed</c>), or something below it is (<c>folder_not_empty</c>).
    /// </exception>
    /// <exception cref="FormBuilderConcurrencyException">A form below it changed meanwhile; what is deleted stays deleted.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the delete.</exception>
    Task<LibraryDeletion?> DeleteAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an empty folder outside the trash at once (ADR-0166): one that holds no folder and no form, in the trash or
    /// out, which a user who sees no more of it than its name could not tell. Nothing else goes with it. Null when it does
    /// not exist.
    /// </summary>
    /// <exception cref="FormBuilderConflictException">
    /// The folder is in the trash (<c>trashed</c>), or holds a folder or a form (<c>folder_not_empty</c>).
    /// </exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the delete.</exception>
    Task<LibraryDeletion?> DeleteEmptyAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// What <see cref="DeleteAsync"/> would remove: the folder, the folders below it and the forms in them (ADR-0050).
    /// Null when it does not exist.
    /// </summary>
    Task<DeletionCandidate?> GetDeletionCandidateAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes for good the top items of the trash that <paramref name="include"/> lets go (ADR-0050): each folder at the
    /// top of the trash with everything below it, as a folder's delete does, then each form in the trash outside them on
    /// its own. The notifications name only what goes.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">A form in the trash changed meanwhile; what is deleted stays deleted.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the delete.</exception>
    Task<TrashEmptied> EmptyTrashAsync(Func<DeletionCandidate, CancellationToken, Task<bool>> include, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// What <see cref="EmptyTrashAsync"/> would delete with the same <paramref name="include"/>, deleting nothing
    /// (ADR-0166): its folders and forms, with the entries and files of those forms, and how many top items would stay.
    /// </summary>
    Task<TrashPreview> PreviewEmptyTrashAsync(Func<DeletionCandidate, CancellationToken, Task<bool>> include, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class FormBuilderFolderService(
    IFolderRepository folders,
    IFormRepository forms,
    IEntryRepository entries,
    IPermissionRepository permissions,
    FormRemoval removal,
    AccessSnapshotInvalidator snapshots,
    FormCacheInvalidator formCache,
    FormBuilderChanges<FormBuilderFolderService> changes)
    : IFormBuilderFolderService
{
    /// <summary>The audit entity type of a folder; the item is the folder's key.</summary>
    public const string EntityType = "folder";

    /// <summary>The audit action of a create.</summary>
    public const string CreatedAction = "folder.created";

    /// <summary>The audit action of a rename.</summary>
    public const string RenamedAction = "folder.renamed";

    /// <summary>The audit action of a move.</summary>
    public const string MovedAction = "folder.moved";

    /// <summary>The audit action of a trash.</summary>
    public const string TrashedAction = "folder.trashed";

    /// <summary>The audit action of a restore.</summary>
    public const string RestoredAction = "folder.restored";

    /// <summary>The audit action of a permanent delete, one per folder.</summary>
    public const string DeletedAction = "folder.deleted";

    // What a folder is called in the refusals of the trash check, and what a change names when a handler fails.
    private const string Item = "folder";
    private const string Library = "the form library";
    private const string OwnUnit = "A change to a folder is a unit of work of its own: it tells handlers once it has committed (ADR-0047).";

    /// <inheritdoc />
    public async Task<FolderInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        await folders.GetAsync(key, cancellationToken) is { } folder ? await InfoAsync(folder, cancellationToken) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FolderInfo>> ListAsync(FolderVisibility? visibility = null, CancellationToken cancellationToken = default)
    {
        var all = await folders.ListAllAsync(cancellationToken);
        var keys = all.ToDictionary(folder => folder.Id, folder => folder.Key);
        return all
            .Where(folder => visibility?.Allows(folder.Key) != false)
            .Select(folder => Info(folder, folder.ParentId is { } parentId && keys.TryGetValue(parentId, out var parentKey) ? parentKey : null))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<FolderInfo> CreateAsync(NewFolderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        changes.EnsureOwnUnit(OwnUnit);
        var name = CheckName(request.Name);
        var parent = await LibraryFolders.OutsideTrashAsync(folders, request.ParentKey, cancellationToken);
        var key = Guid.NewGuid();
        await changes.CancelableAsync(new FolderCreatingNotification(key, name, parent?.Key, request.UserKey, new EventMessages()));
        var created = await changes.WriteAsync(
            () => folders.CreateAsync(new NewFolder(key, parent?.Id, name, UserKey: request.UserKey), cancellationToken),
            _ => [Entry(CreatedAction, key, request.UserKey, new AuditDetails().Add("parent", parent?.Key))],
            cancellationToken);
        await PublishedAsync(new FolderCreatedNotification(key, name, parent?.Key, request.UserKey));
        return Info(created, parent?.Key);
    }

    /// <inheritdoc />
    public async Task<FolderInfo?> RenameAsync(Guid key, string name, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        var newName = CheckName(name);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        LibraryTrash.CheckOutside(folder.TrashedUtc, Item);
        if (newName == folder.Name)
        {
            return await InfoAsync(folder, cancellationToken);
        }

        await changes.CancelableAsync(new FolderRenamingNotification(folder.Key, newName, folder.Name, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            () => folders.RenameAsync(folder.Id, newName, userKey, cancellationToken),
            stored => stored is null ? [] : [Entry(RenamedAction, folder.Key, userKey, new AuditDetails())],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        await PublishedAsync(new FolderRenamedNotification(changed.Key, changed.Name, folder.Name, userKey), treeChanged: false);
        return await InfoAsync(changed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FolderInfo?> MoveAsync(Guid key, Guid? parentKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        LibraryTrash.CheckOutside(folder.TrashedUtc, Item);
        var parent = await LibraryFolders.OutsideTrashAsync(folders, parentKey, cancellationToken);
        if (parent?.Id == folder.ParentId)
        {
            return await InfoAsync(folder, cancellationToken);
        }

        var previousParentKey = await KeyOfAsync(folder.ParentId, cancellationToken);
        await changes.CancelableAsync(new FolderMovingNotification(folder.Key, folder.Name, parent?.Key, previousParentKey, userKey, new EventMessages()));
        var changed = await changes.WriteAsync(
            () => folders.MoveAsync(folder.Id, parent?.Id, userKey, cancellationToken),
            stored => stored is null ? [] : [Entry(MovedAction, folder.Key, userKey, new AuditDetails().Add("parent", parent?.Key).Add("previousParent", previousParentKey))],
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        await PublishedAsync(new FolderMovedNotification(changed.Key, changed.Name, parent?.Key, previousParentKey, userKey));
        return Info(changed, parent?.Key);
    }

    /// <inheritdoc />
    public async Task<FolderChange?> TrashAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        if (folder.TrashedUtc is not null)
        {
            return await UnchangedAsync(folder, cancellationToken);
        }

        // What goes with it, as read before the change: nothing below a folder outside the trash is in it, but a form
        // trashed on its own keeps its own key.
        var below = Below(await folders.ListAllAsync(cancellationToken), folder.Id, child => child.TrashedUtc is null);
        var going = (await forms.ListInFoldersAsync(Ids(folder, below), cancellationToken)).Where(form => form.TrashedUtc is null);
        await changes.CancelableAsync(new FolderTrashingNotification(folder.Key, folder.Name, Summaries(below), Summaries(going), userKey, new EventMessages()));
        var changed = await changes.WriteAsync<TreeChange?>(
            async () =>
            {
                if (await folders.TrashAsync(folder.Id, userKey, cancellationToken) is not { TrashKey: { } trashKey } tree)
                {
                    return null;
                }

                var trashed = await forms.TrashInFoldersAsync(tree.Folders.Select(row => row.Id).ToList(), trashKey, userKey, cancellationToken);
                return new TreeChange(tree.Folders, trashed, await KeyOfAsync(tree.Folders[0].ParentId, cancellationToken));
            },
            tree => tree is null ? [] : [Entry(TrashedAction, folder.Key, userKey, new AuditDetails().Add("folders", tree.Folders.Count - 1).Add("forms", tree.Forms.Count))],
            cancellationToken);
        if (changed is null)
        {
            return await folders.GetAsync(key, cancellationToken) is { } current ? await UnchangedAsync(current, cancellationToken) : null;
        }

        FormsChanged(changed.Forms);
        var result = Result(changed);
        await PublishedAsync(new FolderTrashedNotification(folder.Key, result.Folder.Name, result.Folders, result.Forms, userKey));
        return result;
    }

    /// <inheritdoc />
    public async Task<RestoreLanding?> GetRestoreLandingAsync(Guid key, CancellationToken cancellationToken = default)
    {
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        var parent = folder.ParentId is { } parentId ? await folders.GetByIdAsync(parentId, cancellationToken) : null;
        return folder.TrashedUtc is null ? new RestoreLanding(parent?.Key) : LibraryFolders.Landing(parent);
    }

    /// <inheritdoc />
    public async Task<FolderChange?> RestoreAsync(Guid key, Guid? userKey, RestoreLanding? landing = null, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        if (folder.TrashedUtc is null)
        {
            return await UnchangedAsync(folder, cancellationToken);
        }

        // What went to the trash with it, as read before the change: the folders with its key, reached only through
        // such folders, and the forms with its key in them.
        var all = await folders.ListAllAsync(cancellationToken);
        var trashKey = folder.TrashKey;
        var below = Below(all, folder.Id, child => trashKey is not null && child.TrashKey == trashKey);
        var coming = (await forms.ListInFoldersAsync(Ids(folder, below), cancellationToken)).Where(form => trashKey is not null && form.TrashKey == trashKey);
        // Back into its parent while that is outside the trash, otherwise to the root; there, or where the caller checked
        // it lands, and the write refuses another landing.
        var parent = all.FirstOrDefault(row => row.Id == folder.ParentId);
        var lands = landing ?? LibraryFolders.Landing(parent);
        var landingId = LibraryFolders.LandingId(lands, parent, Item);
        await changes.CancelableAsync(new FolderRestoringNotification(folder.Key, folder.Name, lands.FolderKey, Summaries(below), Summaries(coming), userKey, new EventMessages()));
        var changed = await changes.WriteAsync<TreeChange?>(
            async () =>
            {
                if (await folders.RestoreAsync(folder.Id, landingId, userKey, cancellationToken) is not { } tree)
                {
                    return null;
                }

                IReadOnlyList<StoredForm> restored = tree.TrashKey is { } restoredKey
                    ? await forms.RestoreInFoldersAsync(tree.Folders.Select(row => row.Id).ToList(), restoredKey, userKey, cancellationToken)
                    : [];
                return new TreeChange(tree.Folders, restored, await KeyOfAsync(tree.Folders[0].ParentId, cancellationToken));
            },
            tree => tree is null ? [] : [Entry(RestoredAction, folder.Key, userKey, new AuditDetails().Add("parent", tree.ParentKey).Add("folders", tree.Folders.Count - 1).Add("forms", tree.Forms.Count))],
            cancellationToken);
        if (changed is null)
        {
            return await folders.GetAsync(key, cancellationToken) is { } current ? await UnchangedAsync(current, cancellationToken) : null;
        }

        FormsChanged(changed.Forms);
        var result = Result(changed);
        await PublishedAsync(new FolderRestoredNotification(folder.Key, result.Folder.Name, changed.ParentKey, result.Folders, result.Forms, userKey));
        return result;
    }

    /// <inheritdoc />
    public async Task<LibraryDeletion?> DeleteAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        LibraryTrash.CheckInside(folder.TrashedUtc, Item);

        var below = Below(await folders.ListAllAsync(cancellationToken), folder.Id, _ => true);
        var inside = await forms.ListInFoldersAsync(Ids(folder, below), cancellationToken);
        if (below.Any(row => row.TrashedUtc is null) || inside.Any(form => form.TrashedUtc is null))
        {
            throw new FormBuilderConflictException(FormBuilderConflict.FolderNotEmpty, "Something below the folder is outside the trash; move it out or put it in the trash first.");
        }

        await removal.CheckNoHoldsAsync(inside, cancellationToken);
        await changes.CancelableAsync(new FolderDeletingNotification(folder.Key, folder.Name, Summaries(below), Summaries(inside), userKey, new EventMessages()));
        var deleted = await DeletingAsync(() => DeleteTreeAsync(folder, below, inside, userKey, cancellationToken));
        await PublishedAsync(new FolderDeletedNotification(folder.Key, folder.Name, [.. deleted.Folders.Where(summary => summary.Key != folder.Key)], deleted.Forms, userKey));
        return deleted;
    }

    /// <inheritdoc />
    public async Task<LibraryDeletion?> DeleteEmptyAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit(OwnUnit);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        LibraryTrash.CheckOutside(folder.TrashedUtc, Item);
        if ((await folders.ListAllAsync(cancellationToken)).Any(row => row.ParentId == folder.Id) || (await forms.ListInFoldersAsync([folder.Id], cancellationToken)).Count > 0)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.FolderNotEmpty, "The folder holds folders or forms, perhaps in the trash; move or delete them first.");
        }

        await changes.CancelableAsync(new FolderDeletingNotification(folder.Key, folder.Name, [], [], userKey, new EventMessages()));
        var deleted = await DeletingAsync(() => changes.WriteAsync(
            async () =>
            {
                if (!await folders.DeleteEmptyAsync(folder.Id, cancellationToken))
                {
                    return false;
                }

                await permissions.DeleteForTargetAsync(PermissionTarget.Folder, folder.Key, cancellationToken);
                return true;
            },
            gone => gone ? [Entry(DeletedAction, folder.Key, userKey, new AuditDetails())] : [],
            cancellationToken));
        if (!deleted)
        {
            return null;
        }

        await PublishedAsync(new FolderDeletedNotification(folder.Key, folder.Name, [], [], userKey));
        return new LibraryDeletion([new FolderSummary(folder.Key, folder.Name)], [], 0, 0);
    }

    /// <inheritdoc />
    public async Task<DeletionCandidate?> GetDeletionCandidateAsync(Guid key, CancellationToken cancellationToken = default)
    {
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        var all = await folders.ListAllAsync(cancellationToken);
        var below = Below(all, folder.Id, _ => true);
        var inside = await forms.ListInFoldersAsync(Ids(folder, below), cancellationToken);
        var keys = all.ToDictionary(row => row.Id, row => row.Key);
        return new DeletionCandidate([folder.Key, .. below.Select(row => row.Key)], [.. inside.Select(form => Target(form, keys))]);
    }

    /// <inheritdoc />
    public async Task<TrashEmptied> EmptyTrashAsync(Func<DeletionCandidate, CancellationToken, Task<bool>> include, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(include);
        changes.EnsureOwnUnit(OwnUnit);
        var (trees, loose, everything) = await TrashPartsAsync(include, cancellationToken);
        var going = new TrashEmptyingNotification(
            [.. trees.SelectMany(tree => Summaries([tree.Top, .. tree.Below]))],
            [.. trees.SelectMany(tree => Summaries(tree.Forms)), .. Summaries(loose)],
            userKey,
            new EventMessages());
        await changes.CancelableAsync(going);

        var emptied = await DeletingAsync(async () =>
        {
            var deleted = new List<LibraryDeletion>();
            foreach (var tree in trees)
            {
                deleted.Add(await DeleteTreeAsync(tree.Top, tree.Below, tree.Forms, userKey, cancellationToken));
            }

            deleted.Add(await DeleteFormsAsync(loose, userKey, cancellationToken));
            return new LibraryDeletion(
                [.. deleted.SelectMany(part => part.Folders)],
                [.. deleted.SelectMany(part => part.Forms)],
                deleted.Sum(part => part.Entries),
                deleted.Sum(part => part.Files));
        });
        await PublishedAsync(new TrashEmptiedNotification(emptied.Folders, emptied.Forms, userKey));
        return new TrashEmptied(emptied, everything - trees.Count - loose.Count);
    }

    /// <inheritdoc />
    public async Task<TrashPreview> PreviewEmptyTrashAsync(Func<DeletionCandidate, CancellationToken, Task<bool>> include, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(include);
        var (trees, loose, everything) = await TrashPartsAsync(include, cancellationToken);
        List<StoredForm> going = [.. trees.SelectMany(tree => tree.Forms), .. loose];
        var held = going.Count == 0 ? default : await entries.CountHeldAsync([.. going.Select(form => form.Id)], cancellationToken);
        return new TrashPreview(trees.Sum(tree => 1 + tree.Below.Count), going.Count, held.Entries, held.Files, everything - trees.Count - loose.Count);
    }

    // The trash's top items that emptying it takes, as emptying and its preview see them alike: the folders at its top with
    // what is below them, then the forms in it outside its folders, each kept when include takes it; and how many top
    // items there are in all.
    private async Task<(List<TrashTree> Trees, List<StoredForm> Loose, int Everything)> TrashPartsAsync(Func<DeletionCandidate, CancellationToken, Task<bool>> include, CancellationToken cancellationToken)
    {
        var all = await folders.ListAllAsync(cancellationToken);
        var trashedIds = all.Where(folder => folder.TrashedUtc is not null).Select(folder => folder.Id).ToHashSet();
        var trashedForms = await TrashedFormsAsync(cancellationToken);

        // The folders at the top of the trash, each with the folders below it, which are all in the trash too, and the
        // forms in them; then the forms in the trash outside its folders: trashed on their own, at the root or in a
        // folder outside the trash. The notifications name them in the order they go.
        var trees = all.Where(folder => folder.TrashedUtc is not null && (folder.ParentId is not { } parentId || !trashedIds.Contains(parentId)))
            .Select(top =>
            {
                var below = Below(all, top.Id, _ => true);
                var ids = Ids(top, below).ToHashSet();
                return new TrashTree(top, below, [.. trashedForms.Where(form => form.FolderId is { } folderId && ids.Contains(folderId))]);
            })
            .ToList();
        var loose = trashedForms.Where(form => form.FolderId is not { } folderId || !trashedIds.Contains(folderId)).ToList();
        var keys = all.ToDictionary(folder => folder.Id, folder => folder.Key);
        var everything = trees.Count + loose.Count;

        // What a legal hold keeps entries of stays in the trash, counted with what the user may not delete (ADR-0222).
        trees = await KeptAsync(trees, tree => tree.Forms);
        loose = await KeptAsync(loose, form => [form]);
        trees = await WhereAsync(trees, tree => new DeletionCandidate([tree.Top.Key, .. tree.Below.Select(folder => folder.Key)], [.. tree.Forms.Select(form => Target(form, keys))]));
        loose = await WhereAsync(loose, form => new DeletionCandidate([], [Target(form, keys)]));
        return (trees, loose, everything);

        async Task<List<T>> KeptAsync<T>(List<T> items, Func<T, IEnumerable<StoredForm>> formsOf)
        {
            var free = new List<T>();
            foreach (var item in items)
            {
                var held = false;
                foreach (var form in formsOf(item))
                {
                    held = held || await removal.HoldsAsync(form, cancellationToken);
                }

                if (!held)
                {
                    free.Add(item);
                }
            }

            return free;
        }

        async Task<List<T>> WhereAsync<T>(List<T> items, Func<T, DeletionCandidate> candidate)
        {
            var kept = new List<T>();
            foreach (var item in items)
            {
                if (await include(candidate(item), cancellationToken))
                {
                    kept.Add(item);
                }
            }

            return kept;
        }
    }

    // The folders below a folder, nearest first, reached only through folders that pass: a breadth-first walk of the tree
    // read whole, bounded by its depth.
    private static List<StoredFolder> Below(IReadOnlyList<StoredFolder> all, int id, Func<StoredFolder, bool> passes)
    {
        var children = all.Where(folder => folder.ParentId is not null).ToLookup(folder => folder.ParentId!.Value);
        var found = new List<StoredFolder>();
        var next = new List<int> { id };
        for (var depth = 1; next.Count > 0 && depth <= FolderRepository.MaxDepth; depth++)
        {
            var level = next.SelectMany(parentId => children[parentId]).Where(passes).ToList();
            found.AddRange(level);
            next = level.Select(folder => folder.Id).ToList();
        }

        return found;
    }

    private static List<int> Ids(StoredFolder folder, IEnumerable<StoredFolder> below) => [folder.Id, .. below.Select(row => row.Id)];

    private static List<FolderSummary> Summaries(IEnumerable<StoredFolder> list) => list.Select(folder => new FolderSummary(folder.Key, folder.Name)).ToList();

    private static List<FormSummary> Summaries(IEnumerable<StoredForm> list) => list.Select(Summary).ToList();

    private static FormSummary Summary(StoredForm form) => new(form.Key, form.Alias, form.Name);

    private static FormAccessTarget Target(StoredForm form, Dictionary<int, Guid> folderKeys) =>
        new(form.Key, form.FolderId is { } folderId && folderKeys.TryGetValue(folderId, out var folderKey) ? folderKey : null, form.IsRestricted);

    private static FolderInfo Info(StoredFolder folder, Guid? parentKey) => new(
        folder.Key,
        folder.Name,
        parentKey,
        folder.SortOrder,
        folder.IsRestricted,
        folder.TrashedUtc,
        folder.CreatedUtc,
        folder.UpdatedUtc,
        folder.CreatedByUserKey,
        folder.UpdatedByUserKey);

    private static NewAuditEntry Entry(string action, Guid folderKey, Guid? userKey, AuditDetails details) =>
        new(action, EntityType, folderKey, userKey, details);

    private static string CheckName(string name) => LibraryNames.Check(name, "folder", FolderRepository.MaxNameLength);

    // The folder's first row, then the rest nearest first as the notification before the change named them, as the
    // change left them.
    private static FolderChange Result(TreeChange changed) =>
        new(Info(changed.Folders[0], changed.ParentKey), Summaries(Below(changed.Folders, changed.Folders[0].Id, _ => true)), Summaries(changed.Forms));

    private async Task<FolderChange> UnchangedAsync(StoredFolder folder, CancellationToken cancellationToken) =>
        new(await InfoAsync(folder, cancellationToken), [], []);

    // The forms one after another, each with everything of it; a form gone meanwhile is left out.
    private async Task<LibraryDeletion> DeleteFormsAsync(IReadOnlyList<StoredForm> list, Guid? userKey, CancellationToken cancellationToken)
    {
        var gone = new List<FormSummary>();
        long entries = 0;
        long files = 0;
        foreach (var form in list)
        {
            FormDeletion? removed;
            try
            {
                removed = await removal.DeleteAsync(form, userKey, cancellationToken);
            }
            finally
            {
                formCache.Changed(form.Key);
            }

            if (removed is not null)
            {
                gone.Add(Summary(form));
                entries += removed.Entries;
                files += removed.Files;
            }
        }

        return new LibraryDeletion([], gone, entries, files);
    }

    // The forms a trash or a restore of folders moved: what the website kept of them drops on every server at once
    // (ADR-0084), as one change, however many there were.
    private void FormsChanged(IReadOnlyList<StoredForm> moved)
    {
        if (moved.Count > 0)
        {
            formCache.ChangedAll();
        }
    }

    // The forms first, then the folders from the deepest up in one unit, each with its grants and its audit entry. The
    // folders that went are named top first, then nearest first, as the notification before the delete named them.
    private async Task<LibraryDeletion> DeleteTreeAsync(StoredFolder top, IReadOnlyList<StoredFolder> below, IReadOnlyList<StoredForm> inside, Guid? userKey, CancellationToken cancellationToken)
    {
        var formsGone = await DeleteFormsAsync(inside, userKey, cancellationToken);
        List<StoredFolder> deepestFirst = [.. below.AsEnumerable().Reverse(), top];
        var foldersGone = await changes.WriteAsync(
            async () =>
            {
                var gone = new List<StoredFolder>();
                foreach (var folder in deepestFirst)
                {
                    if (await folders.DeleteAsync(folder.Id, cancellationToken))
                    {
                        await permissions.DeleteForTargetAsync(PermissionTarget.Folder, folder.Key, cancellationToken);
                        gone.Add(folder);
                    }
                }

                return gone;
            },
            gone => gone.Select(folder => Entry(DeletedAction, folder.Key, userKey, new AuditDetails())),
            cancellationToken);
        return formsGone with { Folders = Summaries(Enumerable.Reverse(foldersGone)) };
    }

    // Every form in the trash, a page at a time.
    private async Task<List<StoredForm>> TrashedFormsAsync(CancellationToken cancellationToken)
    {
        var filter = new FormFilter { AllFolders = true, Trashed = true };
        var found = new List<StoredForm>();
        while (true)
        {
            var page = await forms.ListAsync(filter, new PageRequest(found.Count, PageRequest.MaxTake), cancellationToken);
            found.AddRange(page.Items);
            if (page.Items.Count < PageRequest.MaxTake)
            {
                return found;
            }
        }
    }

    private async Task<FolderInfo> InfoAsync(StoredFolder folder, CancellationToken cancellationToken) =>
        Info(folder, await KeyOfAsync(folder.ParentId, cancellationToken));

    private async Task<Guid?> KeyOfAsync(int? folderId, CancellationToken cancellationToken) =>
        folderId is { } id ? (await folders.GetByIdAsync(id, cancellationToken))?.Key : null;

    // A permanent delete, which may stop part way: what went before it stopped took its grants with it, so the access
    // snapshot drops then too; a delete that finishes drops it as it publishes (ADR-0048).
    private async Task<T> DeletingAsync<T>(Func<Task<T>> delete)
    {
        try
        {
            return await delete();
        }
        catch
        {
            snapshots.Changed();
            throw;
        }
    }

    // Publishes a notification after a change committed: a handler that fails is logged, and the change stands. Every
    // change of the tree drops the access snapshot first, once, so the handlers already see it (ADR-0048); a rename
    // changes nothing the snapshot holds, which names no folder.
    private async Task PublishedAsync<T>(T notification, bool treeChanged = true)
        where T : INotification
    {
        if (treeChanged)
        {
            snapshots.Changed();
        }

        await changes.PublishedAsync(notification, Library);
    }

    // The folders a trash or a restore changed, the folder first, the forms with them, and where the folder is now.
    private sealed record TreeChange(IReadOnlyList<StoredFolder> Folders, IReadOnlyList<StoredForm> Forms, Guid? ParentKey);

    // A folder at the top of the trash, the folders below it nearest first, and the forms in them.
    private sealed record TrashTree(StoredFolder Top, IReadOnlyList<StoredFolder> Below, IReadOnlyList<StoredForm> Forms);
}
