using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>Whether an item of the form library is a folder or a form.</summary>
internal enum LibraryItemKind
{
    /// <summary>A folder.</summary>
    Folder,

    /// <summary>A form.</summary>
    Form,
}

/// <summary>A folder or form of the library as one user sees it (ADR-0050).</summary>
/// <param name="Kind">A folder or a form.</param>
/// <param name="Key">Its key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Alias">A form's alias; null for a folder.</param>
/// <param name="ParentKey">The folder it is in, or null at the root.</param>
/// <param name="IsRestricted">Whether only the grants on it and below count below it (ADR-0013).</param>
/// <param name="IsPublished">Whether a form has a published version; false for a folder.</param>
/// <param name="TrashedUtc">When it went to the trash, or null.</param>
/// <param name="HasChildren">Whether a folder holds something the user sees; false for a form.</param>
/// <param name="Permissions">What the user holds on it.</param>
internal sealed record LibraryItem(
    LibraryItemKind Kind,
    Guid Key,
    string Name,
    string? Alias,
    Guid? ParentKey,
    bool IsRestricted,
    bool IsPublished,
    DateTimeOffset? TrashedUtc,
    bool HasChildren,
    FormBuilderPermissions Permissions);

/// <summary>
/// The form library as one user sees it (ADR-0048, ADR-0050): the items of a folder, the trash, and whether the user may
/// delete something for good. Every list filters in the database before paging, and names the user's permissions on
/// each item, so the backoffice never guesses.
/// </summary>
internal interface IFormBuilderLibraryService
{
    /// <summary>
    /// The items directly in a folder (null: the root) that the user sees, its folders first, then its forms by name.
    /// The root holds what is outside the trash, and a folder what shares its place: the items in a folder in the trash
    /// are in the trash too. Null when the folder does not exist.
    /// </summary>
    Task<Page<LibraryItem>?> ListChildrenAsync(AccessUser user, Guid? folderKey, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// The top items of the trash that the user sees: the folders in it whose parent is not, then the forms in it outside
    /// its folders, which went there on their own.
    /// </summary>
    Task<Page<LibraryItem>> ListTrashAsync(AccessUser user, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// The forms outside the trash whose name or alias contains <paramref name="text"/>, that the user sees, by name
    /// (ADR-0051).
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or only white space.</exception>
    Task<Page<LibraryItem>> SearchFormsAsync(AccessUser user, string text, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the user lacks of <c>design</c> and <c>deleteEntries</c> on anything the removal takes (ADR-0050); none when
    /// they may delete it for good.
    /// </summary>
    Task<FormBuilderPermissions> MissingToDeleteAsync(AccessUser user, LibraryRemoval removal, CancellationToken cancellationToken = default);

    /// <summary>Empties the trash of what the user may delete for good, and leaves the rest (ADR-0050).</summary>
    /// <exception cref="FormBuilderConcurrencyException">A form in the trash changed meanwhile; what is deleted stays deleted.</exception>
    /// <exception cref="FormBuilderCancelledException">A handler cancelled the delete.</exception>
    Task<TrashEmptied> EmptyTrashAsync(AccessUser user, Guid? userKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class FormBuilderLibraryService(
    IFolderRepository folders,
    IFormRepository forms,
    IFormBuilderAccessService access,
    IFormBuilderFolderService folderService)
    : IFormBuilderLibraryService
{
    /// <summary>What a permanent delete needs on everything it removes (ADR-0050).</summary>
    public const FormBuilderPermissions ToDelete = FormBuilderPermissions.Design | FormBuilderPermissions.DeleteEntries;

    /// <inheritdoc />
    public async Task<Page<LibraryItem>?> ListChildrenAsync(AccessUser user, Guid? folderKey, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await folders.ListAllAsync(cancellationToken);
        StoredFolder? parent = null;
        if (folderKey is { } key && (parent = all.FirstOrDefault(folder => folder.Key == key)) is null)
        {
            return null;
        }

        var trashed = parent?.TrashedUtc is not null;
        var visibility = await access.GetFolderVisibilityAsync(user, cancellationToken);
        var shown = all
            .Where(folder => folder.ParentId == parent?.Id && folder.TrashedUtc is not null == trashed && visibility.Allows(folder.Key))
            .ToList();
        var filter = new FormFilter { FolderId = parent?.Id, Trashed = trashed, Access = await access.GetFormScopeAsync(user, cancellationToken: cancellationToken) };
        return await PageAsync(user, all, visibility, shown, filter, page, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Page<LibraryItem>> ListTrashAsync(AccessUser user, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var all = await folders.ListAllAsync(cancellationToken);
        var trashedIds = all.Where(folder => folder.TrashedUtc is not null).Select(folder => folder.Id).ToHashSet();
        var visibility = await access.GetFolderVisibilityAsync(user, cancellationToken);
        var shown = all
            .Where(folder => folder.TrashedUtc is not null && (folder.ParentId is not { } parentId || !trashedIds.Contains(parentId)) && visibility.Allows(folder.Key))
            .ToList();
        var filter = new FormFilter
        {
            AllFolders = true,
            Trashed = true,
            OutsideFolderIds = trashedIds,
            Access = await access.GetFormScopeAsync(user, cancellationToken: cancellationToken),
        };
        return await PageAsync(user, all, visibility, shown, filter, page, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Page<LibraryItem>> SearchFormsAsync(AccessUser user, string text, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var keys = (await folders.ListAllAsync(cancellationToken)).ToDictionary(folder => folder.Id, folder => folder.Key);
        var filter = new FormFilter { AllFolders = true, Trashed = false, Search = text, Access = await access.GetFormScopeAsync(user, cancellationToken: cancellationToken) };
        var found = await forms.ListAsync(filter, page, cancellationToken);
        var items = new List<LibraryItem>(found.Items.Count);
        foreach (var form in found.Items)
        {
            items.Add(await FormItemAsync(user, form, keys, cancellationToken));
        }

        return new Page<LibraryItem>(items, found.Total);
    }

    /// <inheritdoc />
    public async Task<FormBuilderPermissions> MissingToDeleteAsync(AccessUser user, LibraryRemoval removal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(removal);
        var missing = FormBuilderPermissions.None;
        foreach (var folderKey in removal.FolderKeys)
        {
            missing |= ToDelete & ~await access.GetForFolderAsync(user, folderKey, cancellationToken);
        }

        foreach (var form in removal.Forms)
        {
            missing |= ToDelete & ~await access.GetForFormAsync(user, form, cancellationToken);
        }

        return missing;
    }

    /// <inheritdoc />
    public Task<TrashEmptied> EmptyTrashAsync(AccessUser user, Guid? userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        return folderService.EmptyTrashAsync(
            async (removal, token) => await MissingToDeleteAsync(user, removal, token) == FormBuilderPermissions.None,
            userKey,
            cancellationToken);
    }

    // The folders first (the whole list is in memory, in the tree's order), then the forms the database pages; each
    // folder says whether it holds something the user sees, in the state of the trash it is in.
    private async Task<Page<LibraryItem>> PageAsync(
        AccessUser user,
        IReadOnlyList<StoredFolder> all,
        FolderVisibility visibility,
        List<StoredFolder> shown,
        FormFilter filter,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var keys = all.ToDictionary(folder => folder.Id, folder => folder.Key);
        var folderPart = shown.Skip((int)Math.Min(page.Skip, shown.Count)).Take(page.Take).ToList();
        var formSkip = Math.Max(0, page.Skip - shown.Count);
        var formTake = page.Take - folderPart.Count;
        var formPage = await forms.ListAsync(filter, new PageRequest(formSkip, Math.Max(1, formTake)), cancellationToken);
        var items = new List<LibraryItem>(page.Take);
        if (folderPart.Count > 0)
        {
            var withForms = await forms.ListFolderIdsWithFormsAsync([.. folderPart.Select(folder => folder.Id)], filter.Trashed == true, filter.Access, cancellationToken);
            foreach (var folder in folderPart)
            {
                var hasFolders = all.Any(child => child.ParentId == folder.Id && child.TrashedUtc is not null == folder.TrashedUtc is not null && visibility.Allows(child.Key));
                items.Add(new LibraryItem(
                    LibraryItemKind.Folder,
                    folder.Key,
                    folder.Name,
                    null,
                    folder.ParentId is { } parentId && keys.TryGetValue(parentId, out var parentKey) ? parentKey : null,
                    folder.IsRestricted,
                    false,
                    folder.TrashedUtc,
                    hasFolders || withForms.Contains(folder.Id),
                    await access.GetForFolderAsync(user, folder.Key, cancellationToken)));
            }
        }

        if (formTake > 0)
        {
            foreach (var form in formPage.Items)
            {
                items.Add(await FormItemAsync(user, form, keys, cancellationToken));
            }
        }

        return new Page<LibraryItem>(items, shown.Count + formPage.Total);
    }

    // A form as the user sees it, named by the key of its folder.
    private async Task<LibraryItem> FormItemAsync(AccessUser user, StoredForm form, Dictionary<int, Guid> folderKeys, CancellationToken cancellationToken)
    {
        var folderKey = form.FolderId is { } folderId && folderKeys.TryGetValue(folderId, out var key) ? key : (Guid?)null;
        return new LibraryItem(
            LibraryItemKind.Form,
            form.Key,
            form.Name,
            form.Alias,
            folderKey,
            form.IsRestricted,
            form.PublishedVersionId is not null,
            form.TrashedUtc,
            false,
            await access.GetForFormAsync(user, new FormAccessTarget(form.Key, folderKey, form.IsRestricted), cancellationToken));
    }
}
