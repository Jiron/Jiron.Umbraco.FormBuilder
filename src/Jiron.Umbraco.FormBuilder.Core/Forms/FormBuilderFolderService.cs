using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// Creates, renames, moves, trashes, restores and deletes the folders of the form library, and empties the trash
/// (ADR-0047), on the rules of the form service (ADR-0044): every change is a unit of work of its own, refused inside a
/// caller's unit or an Umbraco scope; the checks that need no write, then the cancelable notification, then the unit with
/// the change and its audit entry, then the notification that it happened. Folders have no concurrency version: the
/// last rename or move wins, and a trash or a restore that has nothing to do changes nothing. A missing folder gives
/// null; a refusal throws <see cref="FormBuilderValidationException"/>, <see cref="FormBuilderConflictException"/>,
/// <see cref="FormBuilderConcurrencyException"/> or <see cref="FormBuilderCancelledException"/>, each with a code from
/// <see cref="FormBuilderErrorCodes"/>.
/// </summary>
internal interface IFormBuilderFolderService
{
    /// <summary>A folder by key, or null.</summary>
    Task<FolderInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>Every folder, in the trash or not, by sort order and name: a library holds few folders.</summary>
    Task<IReadOnlyList<FolderInfo>> ListAsync(CancellationToken cancellationToken = default);

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
    /// Takes a folder out of the trash, back into its parent or to the root when the parent is in the trash, with the
    /// folders and forms that went to the trash with it. A folder outside the trash stays as it is. Null when it does not
    /// exist.
    /// </summary>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the change.</exception>
    Task<FolderChange?> RestoreAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default);

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
    /// Deletes every folder and form in the trash for good: the folders at the top of the trash as a folder's delete
    /// does, then the forms in the trash outside them.
    /// </summary>
    /// <exception cref="FormBuilderConcurrencyException">A form in the trash changed meanwhile; what is deleted stays deleted.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the delete.</exception>
    Task<LibraryDeletion> EmptyTrashAsync(Guid? userKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed partial class FormBuilderFolderService(
    IFolderRepository folders,
    IFormRepository forms,
    IPermissionRepository permissions,
    IAuditRepository audit,
    FormBuilderDatabase database,
    FormRemoval removal,
    IEventAggregator eventAggregator,
    ILogger<FormBuilderFolderService> logger)
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

    /// <inheritdoc />
    public async Task<FolderInfo?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        await folders.GetAsync(key, cancellationToken) is { } folder ? await InfoAsync(folder, cancellationToken) : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<FolderInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await folders.ListAllAsync(cancellationToken);
        var keys = all.ToDictionary(folder => folder.Id, folder => folder.Key);
        return all.Select(folder => Info(folder, folder.ParentId is { } parentId && keys.TryGetValue(parentId, out var parentKey) ? parentKey : null)).ToList();
    }

    /// <inheritdoc />
    public async Task<FolderInfo> CreateAsync(NewFolderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureOwnUnit();
        var name = CheckName(request.Name);
        var parent = await LiveParentAsync(request.ParentKey, cancellationToken);
        var key = Guid.NewGuid();
        await CancelableAsync(new FolderCreatingNotification(key, name, parent?.Key, request.UserKey, new EventMessages()));
        var created = await database.WriteAsync(
            async _ =>
            {
                var stored = await folders.CreateAsync(new NewFolder(key, parent?.Id, name, UserKey: request.UserKey), cancellationToken);
                await audit.AddAsync(Entry(CreatedAction, key, request.UserKey, new AuditDetails().Add("parent", parent?.Key)), cancellationToken);
                return stored;
            },
            cancellationToken);
        await PublishedAsync(new FolderCreatedNotification(key, name, parent?.Key, request.UserKey));
        return Info(created, parent?.Key);
    }

    /// <inheritdoc />
    public async Task<FolderInfo?> RenameAsync(Guid key, string name, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        var newName = CheckName(name);
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        CheckOutsideTrash(folder);
        if (newName == folder.Name)
        {
            return await InfoAsync(folder, cancellationToken);
        }

        await CancelableAsync(new FolderRenamingNotification(folder.Key, newName, folder.Name, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                var stored = await folders.RenameAsync(folder.Id, newName, userKey, cancellationToken);
                if (stored is not null)
                {
                    await audit.AddAsync(Entry(RenamedAction, folder.Key, userKey, new AuditDetails()), cancellationToken);
                }

                return stored;
            },
            cancellationToken);
        if (changed is null)
        {
            return null;
        }

        await PublishedAsync(new FolderRenamedNotification(changed.Key, changed.Name, folder.Name, userKey));
        return await InfoAsync(changed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<FolderInfo?> MoveAsync(Guid key, Guid? parentKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        CheckOutsideTrash(folder);
        var parent = await LiveParentAsync(parentKey, cancellationToken);
        if (parent?.Id == folder.ParentId)
        {
            return await InfoAsync(folder, cancellationToken);
        }

        var previousParentKey = await KeyOfAsync(folder.ParentId, cancellationToken);
        await CancelableAsync(new FolderMovingNotification(folder.Key, folder.Name, parent?.Key, previousParentKey, userKey, new EventMessages()));
        var changed = await database.WriteAsync(
            async _ =>
            {
                var stored = await folders.MoveAsync(folder.Id, parent?.Id, userKey, cancellationToken);
                if (stored is not null)
                {
                    var details = new AuditDetails().Add("parent", parent?.Key).Add("previousParent", previousParentKey);
                    await audit.AddAsync(Entry(MovedAction, folder.Key, userKey, details), cancellationToken);
                }

                return stored;
            },
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
        EnsureOwnUnit();
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
        await CancelableAsync(new FolderTrashingNotification(folder.Key, folder.Name, Summaries(below), Summaries(going), userKey, new EventMessages()));
        var changed = await database.WriteAsync<TreeChange?>(
            async _ =>
            {
                if (await folders.TrashAsync(folder.Id, userKey, cancellationToken) is not { TrashKey: { } trashKey } tree)
                {
                    return null;
                }

                var trashed = await forms.TrashInFoldersAsync(tree.Folders.Select(row => row.Id).ToList(), trashKey, userKey, cancellationToken);
                var details = new AuditDetails().Add("folders", tree.Folders.Count - 1).Add("forms", trashed.Count);
                await audit.AddAsync(Entry(TrashedAction, folder.Key, userKey, details), cancellationToken);
                return new TreeChange(tree.Folders, trashed, await KeyOfAsync(tree.Folders[0].ParentId, cancellationToken));
            },
            cancellationToken);
        if (changed is null)
        {
            return await folders.GetAsync(key, cancellationToken) is { } current ? await UnchangedAsync(current, cancellationToken) : null;
        }

        var result = Result(changed);
        await PublishedAsync(new FolderTrashedNotification(folder.Key, result.Folder.Name, result.Folders, result.Forms, userKey));
        return result;
    }

    /// <inheritdoc />
    public async Task<FolderChange?> RestoreAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
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
        var parent = all.FirstOrDefault(row => row.Id == folder.ParentId);
        await CancelableAsync(new FolderRestoringNotification(folder.Key, folder.Name, parent is { TrashedUtc: null } ? parent.Key : null, Summaries(below), Summaries(coming), userKey, new EventMessages()));
        var changed = await database.WriteAsync<TreeChange?>(
            async _ =>
            {
                if (await folders.RestoreAsync(folder.Id, userKey, cancellationToken) is not { } tree)
                {
                    return null;
                }

                IReadOnlyList<StoredForm> restored = tree.TrashKey is { } restoredKey
                    ? await forms.RestoreInFoldersAsync(tree.Folders.Select(row => row.Id).ToList(), restoredKey, userKey, cancellationToken)
                    : [];
                var parentKey = await KeyOfAsync(tree.Folders[0].ParentId, cancellationToken);
                var details = new AuditDetails().Add("parent", parentKey).Add("folders", tree.Folders.Count - 1).Add("forms", restored.Count);
                await audit.AddAsync(Entry(RestoredAction, folder.Key, userKey, details), cancellationToken);
                return new TreeChange(tree.Folders, restored, parentKey);
            },
            cancellationToken);
        if (changed is null)
        {
            return await folders.GetAsync(key, cancellationToken) is { } current ? await UnchangedAsync(current, cancellationToken) : null;
        }

        var result = Result(changed);
        await PublishedAsync(new FolderRestoredNotification(folder.Key, result.Folder.Name, changed.ParentKey, result.Folders, result.Forms, userKey));
        return result;
    }

    /// <inheritdoc />
    public async Task<LibraryDeletion?> DeleteAsync(Guid key, Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
        if (await folders.GetAsync(key, cancellationToken) is not { } folder)
        {
            return null;
        }

        if (folder.TrashedUtc is null)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.NotTrashed, "Only a folder in the trash is deleted; put it in the trash first.");
        }

        var below = Below(await folders.ListAllAsync(cancellationToken), folder.Id, _ => true);
        var inside = await forms.ListInFoldersAsync(Ids(folder, below), cancellationToken);
        if (below.Any(row => row.TrashedUtc is null) || inside.Any(form => form.TrashedUtc is null))
        {
            throw new FormBuilderConflictException(FormBuilderConflict.FolderNotEmpty, "Something below the folder is outside the trash; move it out or put it in the trash first.");
        }

        await CancelableAsync(new FolderDeletingNotification(folder.Key, folder.Name, Summaries(below), Summaries(inside), userKey, new EventMessages()));
        var deleted = await DeleteTreeAsync(folder, below, inside, userKey, cancellationToken);
        await PublishedAsync(new FolderDeletedNotification(folder.Key, folder.Name, [.. deleted.Folders.Where(summary => summary.Key != folder.Key)], deleted.Forms, userKey));
        return deleted;
    }

    /// <inheritdoc />
    public async Task<LibraryDeletion> EmptyTrashAsync(Guid? userKey, CancellationToken cancellationToken = default)
    {
        EnsureOwnUnit();
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
        var going = new TrashEmptyingNotification(
            [.. trees.SelectMany(tree => Summaries([tree.Top, .. tree.Below]))],
            [.. trees.SelectMany(tree => Summaries(tree.Forms)), .. Summaries(loose)],
            userKey,
            new EventMessages());
        await CancelableAsync(going);

        var deleted = new List<LibraryDeletion>();
        foreach (var tree in trees)
        {
            deleted.Add(await DeleteTreeAsync(tree.Top, tree.Below, tree.Forms, userKey, cancellationToken));
        }

        deleted.Add(await DeleteFormsAsync(loose, userKey, cancellationToken));
        var emptied = new LibraryDeletion(
            [.. deleted.SelectMany(part => part.Folders)],
            [.. deleted.SelectMany(part => part.Forms)],
            deleted.Sum(part => part.Entries),
            deleted.Sum(part => part.Files));
        await PublishedAsync(new TrashEmptiedNotification(emptied.Folders, emptied.Forms, userKey));
        return emptied;
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

    // Only a restore or a delete applies to a folder in the trash.
    private static void CheckOutsideTrash(StoredFolder folder)
    {
        if (folder.TrashedUtc is not null)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.Trashed, "The folder is in the trash; restore it first.");
        }
    }

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
            if (await removal.DeleteAsync(form, userKey, cancellationToken) is { } removed)
            {
                gone.Add(Summary(form));
                entries += removed.Entries;
                files += removed.Files;
            }
        }

        return new LibraryDeletion([], gone, entries, files);
    }

    // The forms first, then the folders from the deepest up in one unit, each with its grants and its audit entry. The
    // folders that went are named top first, then nearest first, as the notification before the delete named them.
    private async Task<LibraryDeletion> DeleteTreeAsync(StoredFolder top, IReadOnlyList<StoredFolder> below, IReadOnlyList<StoredForm> inside, Guid? userKey, CancellationToken cancellationToken)
    {
        var formsGone = await DeleteFormsAsync(inside, userKey, cancellationToken);
        List<StoredFolder> deepestFirst = [.. below.AsEnumerable().Reverse(), top];
        var foldersGone = await database.WriteAsync(
            async _ =>
            {
                var gone = new List<StoredFolder>();
                foreach (var folder in deepestFirst)
                {
                    if (await folders.DeleteAsync(folder.Id, cancellationToken))
                    {
                        await permissions.DeleteForTargetAsync(PermissionTarget.Folder, folder.Key, cancellationToken);
                        await audit.AddAsync(Entry(DeletedAction, folder.Key, userKey, new AuditDetails()), cancellationToken);
                        gone.Add(folder);
                    }
                }

                return gone;
            },
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

    // A folder something may go into: outside the trash. Null for the root.
    private async Task<StoredFolder?> LiveParentAsync(Guid? parentKey, CancellationToken cancellationToken)
    {
        if (parentKey is not { } key)
        {
            return null;
        }

        return await folders.GetAsync(key, cancellationToken) is { TrashedUtc: null } parent
            ? parent
            : throw new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The folder does not exist or is in the trash.");
    }

    private void EnsureOwnUnit()
    {
        if (database.InUnitOfWork)
        {
            throw new InvalidOperationException("A change to a folder is a unit of work of its own: it tells handlers once it has committed (ADR-0047).");
        }
    }

    // Publishes a notification before a change: a handler that cancels it stops the change.
    private async Task CancelableAsync<T>(T notification)
        where T : CancelableNotification
    {
        if (await eventAggregator.PublishCancelableAsync(notification))
        {
            throw new FormBuilderCancelledException(notification.Messages);
        }
    }

    // Publishes a notification after a change committed: a handler that fails is logged, and the change stands.
    private async Task PublishedAsync<T>(T notification)
        where T : INotification
    {
        try
        {
            await eventAggregator.PublishAsync(notification, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogHandlerFailed(logger, typeof(T).Name, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of {Notification} failed after a change to the form library committed; the change stands")]
    private static partial void LogHandlerFailed(ILogger logger, string notification, Exception exception);

    // The folders a trash or a restore changed, the folder first, the forms with them, and where the folder is now.
    private sealed record TreeChange(IReadOnlyList<StoredFolder> Folders, IReadOnlyList<StoredForm> Forms, Guid? ParentKey);

    // A folder at the top of the trash, the folders below it nearest first, and the forms in them.
    private sealed record TrashTree(StoredFolder Top, IReadOnlyList<StoredFolder> Below, IReadOnlyList<StoredForm> Forms);
}
