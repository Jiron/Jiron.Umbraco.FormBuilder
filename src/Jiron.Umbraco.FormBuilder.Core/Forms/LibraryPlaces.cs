using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>Where a form stands in the library: whether it is in the trash, and the folders it lies in, its own first.</summary>
/// <param name="Form">The form's key.</param>
/// <param name="Trashed">Whether the form is in the trash.</param>
/// <param name="Folders">The keys of its folder and of the folders above it, its own first; empty at the root.</param>
internal sealed record LibraryPlace(Guid Form, bool Trashed, IReadOnlyList<Guid> Folders);

/// <summary>
/// The places of forms in the library, read synchronously for Umbraco's synchronous value validators (ADR-0129), which
/// must not wait on a task: the forms asked for and the library's folders, in one scope that writes nothing. A key that
/// names no form has no place.
/// </summary>
/// <param name="scopes">Umbraco's scopes, whose database the read uses.</param>
internal sealed class LibraryPlaces(IScopeProvider scopes)
{
    /// <summary>The places of the forms with the given keys that the library holds, by key.</summary>
    /// <param name="forms">The forms' keys.</param>
    public IReadOnlyDictionary<Guid, LibraryPlace> Of(IReadOnlyCollection<Guid> forms)
    {
        ArgumentNullException.ThrowIfNull(forms);
        if (forms.Count == 0)
        {
            return new Dictionary<Guid, LibraryPlace>();
        }

        using var scope = scopes.CreateScope(autoComplete: true);
        var db = scope.Database;
        var n = new SqlNames(db);
        var rows = db.Fetch<FormDto>($"SELECT * FROM {n.T(FormBuilderTables.Form)} WHERE {n.C("key")} IN (@0)", forms.Distinct().ToList());
        var folders = db.Fetch<FolderDto>($"SELECT * FROM {n.T(FormBuilderTables.Folder)}").ToDictionary(folder => folder.Id);
        return rows.ToDictionary(row => row.Key, row => new LibraryPlace(row.Key, row.TrashedUtc is not null, Above(row.FolderId, folders)));
    }

    // The folder and those above it, bounded by the tree's depth so a broken parent chain cannot loop.
    private static List<Guid> Above(int? id, Dictionary<int, FolderDto> folders)
    {
        var keys = new List<Guid>();
        while (id is { } at && keys.Count < Persistence.Repositories.FolderRepository.MaxDepth && folders.TryGetValue(at, out var folder))
        {
            keys.Add(folder.Key);
            id = folder.ParentId;
        }

        return keys;
    }
}
