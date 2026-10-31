using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// What a form picker's settings allow (ADR-0129): the forms in the allowed folders and in every folder below them, and
/// the allowed forms wherever they are. A scope that names nothing limits nothing.
/// </summary>
/// <param name="Folders">The allowed folders' keys.</param>
/// <param name="Forms">The allowed forms' keys, at most <see cref="SqlArgs.MaxListLength"/>.</param>
internal sealed record PickerScope(IReadOnlySet<Guid> Folders, IReadOnlySet<Guid> Forms)
{
    /// <summary>The scope that limits nothing: the data type settings' own picker, and a picker without limits.</summary>
    public static PickerScope Everything { get; } = new(FrozenSet<Guid>.Empty, FrozenSet<Guid>.Empty);

    /// <summary>Whether the scope names neither a folder nor a form.</summary>
    public bool LimitsNothing => Folders.Count == 0 && Forms.Count == 0;
}

/// <summary>A folder or form as a form picker shows it: its name and key only (ADR-0013).</summary>
/// <param name="Kind">A folder or a form.</param>
/// <param name="Key">Its key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Alias">A form's alias; null for a folder.</param>
/// <param name="HasChildren">Whether a folder leads to a form the picker may pick; false for a form.</param>
internal sealed record PickerItem(LibraryItemKind Kind, Guid Key, string Name, string? Alias, bool HasChildren);

/// <summary>
/// The library as a form picker in content shows it (ADR-0013, ADR-0133): published forms outside the trash that are not
/// restricted, by themselves or by a folder above them, plus the restricted ones the user sees through a grant; the
/// folders that lead to them; and, for a picker's settings, only what they allow. Names and keys only: never entries,
/// settings or actions. Every list filters and pages in the database (ADR-0036, ADR-0048).
/// </summary>
internal interface IFormBuilderPickerService
{
    /// <summary>
    /// A page of a folder's folders and forms (null: the root, or a scope's allowed folders and forms there), folders
    /// first; null when the folder is not one the picker reaches.
    /// </summary>
    Task<Page<PickerItem>?> ListChildrenAsync(AccessUser user, PickerScope scope, Guid? folderKey, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>A page of the forms the picker may pick whose name or alias contains the text, by name.</summary>
    Task<Page<PickerItem>> SearchAsync(AccessUser user, PickerScope scope, string text, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>The forms of these keys the picker may pick, in the order asked; a key it may not pick is left out.</summary>
    Task<IReadOnlyList<PickerItem>> ListFormsAsync(AccessUser user, PickerScope scope, IReadOnlyCollection<Guid> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// The folders of these keys the user may see outside the trash, in the order asked, whether or not they lead to a form
    /// the picker may pick: the allowed folders a picker's settings name (ADR-0135). A key it may not show is left out.
    /// </summary>
    Task<IReadOnlyList<PickerItem>> ListFoldersAsync(AccessUser user, IReadOnlyCollection<Guid> keys, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class FormBuilderPickerService(IFolderRepository folders, IFormRepository forms, IFormBuilderAccessService access) : IFormBuilderPickerService
{
    /// <summary>The most keys one lookup names.</summary>
    public const int MaxKeys = 100;

    /// <inheritdoc />
    public async Task<Page<PickerItem>?> ListChildrenAsync(AccessUser user, PickerScope scope, Guid? folderKey, PageRequest page, CancellationToken cancellationToken = default)
    {
        var reach = await ReachAsync(user, scope, cancellationToken);
        List<StoredFolder> shown;
        FormFilter filter;
        if (folderKey is { } key)
        {
            if (reach.Folder(key) is not { } folder || !reach.LeadsToForms(folder.Id) || !reach.InScope(folder.Id))
            {
                return null;
            }

            shown = [.. reach.Live.Where(child => child.ParentId == folder.Id && reach.LeadsToForms(child.Id))];
            filter = reach.Filter with { AllFolders = false, FolderId = folder.Id };
        }
        else if (scope.LimitsNothing)
        {
            shown = [.. reach.Live.Where(child => child.ParentId is null && reach.LeadsToForms(child.Id))];
            filter = reach.Filter with { AllFolders = false, FolderId = null };
        }
        else
        {
            // The allowed folders that no other allowed folder holds, and the allowed forms outside them.
            shown = [.. reach.Live.Where(folder => scope.Folders.Contains(folder.Key) && reach.LeadsToForms(folder.Id) && !reach.BelowAllowed(folder))];
            filter = reach.Filter with { AllFolders = true, Keys = [.. scope.Forms], Within = null, OutsideFolderIds = [.. reach.Outside, .. reach.Allowed] };
        }

        return await PageAsync(shown, filter, page, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Page<PickerItem>> SearchAsync(AccessUser user, PickerScope scope, string text, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var reach = await ReachAsync(user, scope, cancellationToken);
        var found = await forms.ListAsync(reach.Filter with { Search = text }, page, cancellationToken);
        return new Page<PickerItem>([.. found.Items.Select(FormItem)], found.Total);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickerItem>> ListFormsAsync(AccessUser user, PickerScope scope, IReadOnlyCollection<Guid> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Count, MaxKeys);
        if (keys.Count == 0)
        {
            return [];
        }

        var reach = await ReachAsync(user, scope, cancellationToken);
        var found = await forms.ListAsync(reach.Filter with { Keys = keys }, PageRequest.First(MaxKeys), cancellationToken);
        var byKey = found.Items.ToDictionary(form => form.Key);
        return [.. keys.Distinct().Where(byKey.ContainsKey).Select(key => FormItem(byKey[key]))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PickerItem>> ListFoldersAsync(AccessUser user, IReadOnlyCollection<Guid> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(keys.Count, MaxKeys);
        if (keys.Count == 0)
        {
            return [];
        }

        var reach = await ReachAsync(user, PickerScope.Everything, cancellationToken);
        return [.. keys.Distinct().Select(reach.Folder).OfType<StoredFolder>().Where(reach.Sees).Select(folder => new PickerItem(LibraryItemKind.Folder, folder.Key, folder.Name, null, reach.LeadsToForms(folder.Id)))];
    }

    private static PickerItem FormItem(StoredForm form) => new(LibraryItemKind.Form, form.Key, form.Name, form.Alias, false);

    // The folders first (in the tree's order, in memory), then the forms the database pages.
    private async Task<Page<PickerItem>> PageAsync(List<StoredFolder> shown, FormFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var folderPart = shown.Skip((int)Math.Min(page.Skip, shown.Count)).Take(page.Take).ToList();
        var formSkip = Math.Max(0, page.Skip - shown.Count);
        var formTake = page.Take - folderPart.Count;
        var formPage = await forms.ListAsync(filter, new PageRequest(formSkip, Math.Max(1, formTake)), cancellationToken);
        var items = new List<PickerItem>(page.Take);
        items.AddRange(folderPart.Select(folder => new PickerItem(LibraryItemKind.Folder, folder.Key, folder.Name, null, true)));
        if (formTake > 0)
        {
            items.AddRange(formPage.Items.Select(FormItem));
        }

        return new Page<PickerItem>(items, shown.Count + formPage.Total);
    }

    // What the picker reaches for this user and scope: the forms' filter, the live folders, and which of them lead to a
    // form it may pick.
    private async Task<Reach> ReachAsync(AccessUser user, PickerScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(scope);
        var all = await folders.ListAllAsync(cancellationToken);
        var byId = all.ToDictionary(folder => folder.Id);

        // A folder in the trash, or below one, holds nothing to pick; one below no restricted folder holds the forms
        // everyone may pick (ADR-0013).
        var outside = all.Where(folder => Chain(folder, byId).Any(above => above.TrashedUtc is not null)).Select(folder => folder.Id).ToHashSet();
        var live = all.Where(folder => !outside.Contains(folder.Id)).ToList();
        var open = live.Where(folder => !Chain(folder, byId).Any(above => above.IsRestricted)).Select(folder => folder.Id);

        var own = await access.GetFormVisibilityAsync(user, cancellationToken: cancellationToken);
        var visibility = own.IsEverything ? own : own with { Root = true, FolderIds = [.. own.FolderIds.Union(open)] };

        var allowed = scope.LimitsNothing
            ? []
            : live.Where(folder => Chain(folder, byId).Any(above => scope.Folders.Contains(above.Key))).Select(folder => folder.Id).ToHashSet();
        var filter = new FormFilter
        {
            AllFolders = true,
            Trashed = false,
            PublishedOnly = true,
            Visibility = visibility,
            OutsideFolderIds = outside,
            Within = scope.LimitsNothing ? null : new FormScope(allowed, [.. scope.Forms]),
        };

        var leads = new HashSet<int>();
        foreach (var id in await forms.ListFolderIdsWithPublishedFormsAsync(visibility, cancellationToken))
        {
            if (byId.TryGetValue(id, out var holder) && !outside.Contains(id))
            {
                leads.UnionWith(Chain(holder, byId).Select(folder => folder.Id));
            }
        }

        return new Reach(scope, live, byId, outside, allowed, leads, visibility, filter);
    }

    // A folder and those above it, its own first, bounded by the tree's depth so a broken parent chain cannot loop.
    private static IEnumerable<StoredFolder> Chain(StoredFolder folder, Dictionary<int, StoredFolder> byId)
    {
        StoredFolder? at = folder;
        for (var depth = 0; at is not null && depth < FolderRepository.MaxDepth; depth++)
        {
            yield return at;
            at = at.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null;
        }
    }

    private sealed record Reach(
        PickerScope Scope,
        IReadOnlyList<StoredFolder> Live,
        Dictionary<int, StoredFolder> ById,
        IReadOnlySet<int> Outside,
        IReadOnlySet<int> Allowed,
        IReadOnlySet<int> Leads,
        FormVisibility Visibility,
        FormFilter Filter)
    {
        public StoredFolder? Folder(Guid key) => Live.FirstOrDefault(folder => folder.Key == key);

        public bool LeadsToForms(int folderId) => Leads.Contains(folderId);

        public bool InScope(int folderId) => Scope.LimitsNothing || Allowed.Contains(folderId);

        // Whether the user may see a live folder: one below no restricted folder, or one a grant of theirs covers.
        public bool Sees(StoredFolder folder) => Visibility.IsEverything || Visibility.FolderIds.Contains(folder.Id);

        // Whether an allowed folder lies below another allowed folder, which shows it already.
        public bool BelowAllowed(StoredFolder folder) => Chain(folder, ById).Skip(1).Any(above => Scope.Folders.Contains(above.Key));
    }
}
