using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A row of a folder's list (ADR-0166): the folder or form, and its entries of the last days.</summary>
/// <param name="Item">The folder or form, as the user sees it.</param>
/// <param name="RecentEntries">
/// The entries of the last <see cref="LibraryLists.RecentDays"/> days that the user may view: a form's, or those of the
/// forms below a folder, its folders' included; null for a form whose entries the user may not view, and for a folder
/// with no such form below it.
/// </param>
internal sealed record LibraryRow(LibraryItem Item, long? RecentEntries);

/// <summary>A field the header search found (ADR-0166).</summary>
/// <param name="Field">The field and its form.</param>
/// <param name="FolderName">The name of the form's folder, or null at the root.</param>
internal sealed record FieldMatch(FoundField Field, string? FolderName);

/// <summary>
/// The library's lists beyond the tree (ADR-0166): a folder's rows with their recent entries, the fields the header search
/// finds, and what a form holds that its permanent delete takes. Each holds only what the user may see, filtered in the
/// database, as the tree does (ADR-0048).
/// </summary>
internal sealed class LibraryLists(
    IFormBuilderLibraryService library,
    IFolderRepository folders,
    IFormRepository forms,
    IEntryRepository entries,
    IFormBuilderAccessService access,
    TimeProvider time)
{
    /// <summary>How many days a folder's list counts entries back.</summary>
    public const int RecentDays = 30;

    /// <summary>
    /// The items directly in a folder (null: the root) that the user sees, as the tree lists them, each with its recent
    /// entries. Null when the folder does not exist.
    /// </summary>
    public async Task<Page<LibraryRow>?> RowsAsync(AccessUser user, Guid? folderKey, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (await library.ListChildrenAsync(user, folderKey, page, cancellationToken) is not { } items)
        {
            return null;
        }

        var all = await folders.ListAllAsync(cancellationToken);
        var trashed = folderKey is { } key && all.FirstOrDefault(folder => folder.Key == key)?.TrashedUtc is not null;
        var below = items.Items
            .Where(item => item.Kind == LibraryItemKind.Folder)
            .ToDictionary(item => item.Key, item => Subtree(all, item.Key, trashed));
        var formKeys = items.Items.Where(item => item.Kind == LibraryItemKind.Form).Select(item => item.Key).ToList();

        // The forms of the page and those below its folders whose entries the user may view, in one list: the page's forms
        // sit in the listed folder, never below it, so none counts twice.
        var viewed = await AllAsync(
            new FormFilter
            {
                AllFolders = true,
                Trashed = trashed,
                Within = new FormScope([.. below.Values.SelectMany(ids => ids)], formKeys),
                Visibility = await access.GetFormVisibilityAsync(user, FormBuilderPermissions.ViewEntries, cancellationToken),
            },
            below.Count + formKeys.Count == 0,
            cancellationToken);
        var counts = viewed.Count == 0 ? new Dictionary<int, long>() : await entries.CountSinceAsync([.. viewed.Select(form => form.Id)], time.GetUtcNow().AddDays(-RecentDays), cancellationToken);
        long Count(StoredForm form) => counts.GetValueOrDefault(form.Id);

        var rows = items.Items.Select(item =>
        {
            if (item.Kind == LibraryItemKind.Form)
            {
                return new LibraryRow(item, viewed.FirstOrDefault(form => form.Key == item.Key) is { } form ? Count(form) : null);
            }

            var inside = viewed.Where(form => form.FolderId is { } folderId && below[item.Key].Contains(folderId)).ToList();
            return new LibraryRow(item, inside.Count == 0 ? null : inside.Sum(Count));
        });
        return new Page<LibraryRow>([.. rows], items.Total);
    }

    /// <summary>
    /// The fields of the live versions of the forms outside the trash that the user sees whose label or alias contains the
    /// text (ADR-0166), by label, each with its form's folder.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="text"/> is empty or only white space.</exception>
    public async Task<Page<FieldMatch>> SearchFieldsAsync(AccessUser user, string text, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var found = await forms.SearchFieldsAsync(text, await access.GetFormVisibilityAsync(user, cancellationToken: cancellationToken), page, cancellationToken);
        var names = found.Items.Any(field => field.FolderId is not null)
            ? (await folders.ListAllAsync(cancellationToken)).ToDictionary(folder => folder.Id, folder => folder.Name)
            : [];
        return new Page<FieldMatch>([.. found.Items.Select(field => new FieldMatch(field, field.FolderId is { } id ? names.GetValueOrDefault(id) : null))], found.Total);
    }

    /// <summary>The entries and uploaded files of a form, which its permanent delete takes; null when it does not exist.</summary>
    public async Task<EntryHoldings?> HoldingsAsync(Guid formKey, CancellationToken cancellationToken = default) =>
        await forms.GetAsync(formKey, cancellationToken) is { } form ? await entries.CountHeldAsync([form.Id], cancellationToken) : null;

    // A folder and the folders below it in the same state of the trash: the folders whose forms its row counts.
    private static HashSet<int> Subtree(IReadOnlyList<StoredFolder> all, Guid key, bool trashed)
    {
        var children = all.Where(folder => folder.ParentId is not null && folder.TrashedUtc is not null == trashed).ToLookup(folder => folder.ParentId!.Value);
        var found = all.Where(folder => folder.Key == key).Select(folder => folder.Id).ToHashSet();
        var next = found.ToList();
        for (var depth = 1; next.Count > 0 && depth <= FolderRepository.MaxDepth; depth++)
        {
            next = [.. next.SelectMany(id => children[id]).Select(folder => folder.Id)];
            found.UnionWith(next);
        }

        return found;
    }

    // Every form the filter allows, a page at a time; none when there is nothing to ask for.
    private async Task<List<StoredForm>> AllAsync(FormFilter filter, bool nothing, CancellationToken cancellationToken)
    {
        var found = new List<StoredForm>();
        while (!nothing)
        {
            var page = await forms.ListAsync(filter, new PageRequest(found.Count, PageRequest.MaxTake), cancellationToken);
            found.AddRange(page.Items);
            if (page.Items.Count < PageRequest.MaxTake)
            {
                break;
            }
        }

        return found;
    }
}
