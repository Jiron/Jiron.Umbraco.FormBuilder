namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>What a name in the default file store is (ADR-0041, ADR-0222).</summary>
internal enum StoredFileKind
{
    /// <summary>A stored file, under its id.</summary>
    Stored,

    /// <summary>A save's partial file, which a save that never ended leaves.</summary>
    Partial,

    /// <summary>A deleted file's bytes, which a delete that could not finish leaves.</summary>
    Deleted,
}

/// <summary>A file in a folder of the default store.</summary>
/// <param name="Name">Its name in the folder.</param>
/// <param name="Kind">What it is.</param>
/// <param name="LastWriteUtc">When it was last written; a rename keeps it.</param>
internal sealed record StoredFileInfo(string Name, StoredFileKind Kind, DateTimeOffset LastWriteUtc);

/// <summary>
/// What the default file store shows of itself, so retention can sweep what a dead process left in it (ADR-0041,
/// ADR-0222): its folders, the files in each, and a delete of a file by its name there. A store of the site's own keeps
/// its bytes elsewhere and sweeps them its own way: it has no folders to show.
/// </summary>
internal interface IFileStoreLeftovers
{
    /// <summary>The store's folders, by their names in order.</summary>
    IReadOnlyList<string> Folders();

    /// <summary>The files of one folder the store writes; any other name is left out.</summary>
    /// <param name="folder">The folder's name.</param>
    /// <exception cref="ArgumentException">The name is none of the store's folders.</exception>
    IReadOnlyList<StoredFileInfo> FilesIn(string folder);

    /// <summary>Deletes a file of a folder by its name; one that is gone is no error.</summary>
    /// <param name="folder">The folder's name.</param>
    /// <param name="name">The file's name, one the store writes.</param>
    /// <exception cref="ArgumentException">The folder or the name is none the store writes.</exception>
    void DeleteLeftover(string folder, string name);
}
