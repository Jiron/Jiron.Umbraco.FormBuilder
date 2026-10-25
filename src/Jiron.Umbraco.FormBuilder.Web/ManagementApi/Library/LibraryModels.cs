using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>A folder or form of the library tree or the trash, as the signed-in user sees it (ADR-0050).</summary>
internal sealed record LibraryItemResponseModel
{
    /// <summary>The folder's or form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary><c>folder</c> or <c>form</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>A form's alias; null for a folder.</summary>
    public string? Alias { get; init; }

    /// <summary>The folder it is in, or null at the root.</summary>
    public Guid? ParentKey { get; init; }

    /// <summary>Whether only the grants on it and below count below it.</summary>
    public required bool IsRestricted { get; init; }

    /// <summary>Whether a form has a published version; false for a folder.</summary>
    public required bool IsPublished { get; init; }

    /// <summary>Whether it is in the trash.</summary>
    public required bool IsTrashed { get; init; }

    /// <summary>Whether a folder holds something the user sees; false for a form.</summary>
    public required bool HasChildren { get; init; }

    /// <summary>The permissions the user holds on it, by their stable names.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The model of an item.</summary>
    public static LibraryItemResponseModel Of(LibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new LibraryItemResponseModel
        {
            Key = item.Key,
            Kind = item.Kind == LibraryItemKind.Folder ? "folder" : "form",
            Name = item.Name,
            Alias = item.Alias,
            ParentKey = item.ParentKey,
            IsRestricted = item.IsRestricted,
            IsPublished = item.IsPublished,
            IsTrashed = item.TrashedUtc is not null,
            HasChildren = item.HasChildren,
            Permissions = PermissionRules.NamesOf(item.Permissions),
        };
    }
}

/// <summary>A folder, with the signed-in user's permissions on it.</summary>
internal sealed record FolderResponseModel
{
    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The folder it is in, or null at the root.</summary>
    public Guid? ParentKey { get; init; }

    /// <summary>Whether only the grants on it and below count below it.</summary>
    public required bool IsRestricted { get; init; }

    /// <summary>When it went to the trash, or null outside it.</summary>
    public DateTimeOffset? TrashedUtc { get; init; }

    /// <summary>When it was created.</summary>
    public required DateTimeOffset CreatedUtc { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>The permissions the user holds on it, by their stable names.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The model of a folder.</summary>
    public static FolderResponseModel Of(FolderInfo folder, FormBuilderPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new FolderResponseModel
        {
            Key = folder.Key,
            Name = folder.Name,
            ParentKey = folder.ParentKey,
            IsRestricted = folder.IsRestricted,
            TrashedUtc = folder.TrashedUtc,
            CreatedUtc = folder.CreatedUtc,
            UpdatedUtc = folder.UpdatedUtc,
            Permissions = PermissionRules.NamesOf(permissions),
        };
    }
}

/// <summary>A folder to create.</summary>
internal sealed record CreateFolderRequestModel
{
    /// <summary>Its name: 1 to 255 characters on one line.</summary>
    public required string Name { get; init; }

    /// <summary>The folder outside the trash to create it in, or null for the root.</summary>
    public Guid? ParentKey { get; init; }
}

/// <summary>A folder's new name.</summary>
internal sealed record RenameFolderRequestModel
{
    /// <summary>The name: 1 to 255 characters on one line.</summary>
    public required string Name { get; init; }
}

/// <summary>Where a folder moves.</summary>
internal sealed record MoveFolderRequestModel
{
    /// <summary>The folder outside the trash to move it into, or null for the root.</summary>
    public Guid? ParentKey { get; init; }

    /// <summary>
    /// Confirms a move that gives user groups more access to the folder, which the answer <c>widens_access</c> listed;
    /// it needs <c>managePermissions</c>.
    /// </summary>
    public bool WidenAccess { get; init; }
}

/// <summary>What a user group would gain from a move.</summary>
/// <param name="UserGroupKey">The Umbraco user group.</param>
/// <param name="Permissions">The permissions it would hold on the folder or form and does not now.</param>
internal sealed record AccessGainResponseModel(Guid UserGroupKey, IReadOnlyList<string> Permissions);

/// <summary>What a trash or a restore took with it: the folders and forms it changed.</summary>
internal sealed record LibraryChangeResponseModel
{
    /// <summary>The folders, the one asked for first.</summary>
    public required IReadOnlyList<Guid> Folders { get; init; }

    /// <summary>The forms.</summary>
    public required IReadOnlyList<Guid> Forms { get; init; }

    /// <summary>The model of a change.</summary>
    public static LibraryChangeResponseModel Of(FolderChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new LibraryChangeResponseModel
        {
            Folders = [change.Folder.Key, .. change.Folders.Select(folder => folder.Key)],
            Forms = [.. change.Forms.Select(form => form.Key)],
        };
    }
}

/// <summary>What a permanent delete removed.</summary>
internal sealed record LibraryDeletionResponseModel
{
    /// <summary>How many folders went.</summary>
    public required int Folders { get; init; }

    /// <summary>How many forms went.</summary>
    public required int Forms { get; init; }

    /// <summary>How many entries went with the forms.</summary>
    public required long Entries { get; init; }

    /// <summary>How many files went with the forms.</summary>
    public required long Files { get; init; }

    /// <summary>The model of a deletion.</summary>
    public static LibraryDeletionResponseModel Of(LibraryDeletion deletion)
    {
        ArgumentNullException.ThrowIfNull(deletion);
        return new LibraryDeletionResponseModel { Folders = deletion.Folders.Count, Forms = deletion.Forms.Count, Entries = deletion.Entries, Files = deletion.Files };
    }
}

/// <summary>What emptying the trash removed, and how many of its top items stayed because the user may not delete them.</summary>
internal sealed record TrashEmptiedResponseModel
{
    /// <summary>What went.</summary>
    public required LibraryDeletionResponseModel Deleted { get; init; }

    /// <summary>How many of the trash's top items stayed: folders with what is below them, and forms on their own.</summary>
    public required int Left { get; init; }
}
