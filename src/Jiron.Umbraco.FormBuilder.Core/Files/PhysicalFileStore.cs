using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The default file store (ADR-0006, ADR-0041): files in <see cref="FormBuilderFilesOptions.Root"/>, outside the web
/// root, as <c>&lt;root&gt;/&lt;first two characters&gt;/&lt;id&gt;</c> without an extension. A save writes
/// <c>&lt;id&gt;.partial</c>, which no reader opens, flushes it to the disk and renames it when it is complete; a rename
/// within a folder is atomic, so a reader finds a whole file or none. A delete renames the file to
/// <c>&lt;id&gt;.deleted-&lt;random&gt;</c> before it deletes it, so the id reads as missing at once, even on a system
/// that keeps a file a download holds open under its name until the download ends.
/// </summary>
internal sealed partial class PhysicalFileStore(IOptions<FormBuilderOptions> options, IHostFolders folders, ILogger<PhysicalFileStore> logger) : IFormBuilderFileStore, IFileStoreLeftovers
{
    /// <summary>What a save writes to until the file is complete.</summary>
    public const string PartialSuffix = ".partial";

    /// <summary>What a delete renames a file to, followed by 16 random hex characters, before it deletes it.</summary>
    public const string DeletedInfix = ".deleted-";

    private const int BufferSize = 81920;

    /// <summary>The full path of the folder the files are in.</summary>
    public string Root { get; } = FileStoreRoot.Resolve(options.Value.Files.Root, folders);

    /// <inheritdoc />
    public async Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead)
        {
            throw new ArgumentException("The content of a file to store must be readable.", nameof(content));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var path = PathOf(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            throw new IOException("A file is stored under this id already.");
        }

        // A new file only: when another save of the id holds its partial file, this one fails here and leaves it alone.
        // Unbuffered, because the copy has a buffer of its own.
        var partial = path + PartialSuffix;
        var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 0, FileOptions.Asynchronous);
        try
        {
            await using (file)
            {
                await content.CopyToAsync(file, BufferSize, cancellationToken);

                // On the disk before the rename, so a crash cannot leave a file under its id that misses bytes. The system
                // has no asynchronous way to do this, so the save blocks once, for as long as the disk takes.
                RandomAccess.FlushToDisk(file.SafeFileHandle);
            }

            File.Move(partial, path, overwrite: false);
        }
        catch
        {
            Discard(partial);
            throw;
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // Shared for renaming and deleting too, so a delete during a download succeeds and the download reads on.
            Stream stream = new FileStream(PathOf(id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathOf(id);
        var deleted = path + DeletedInfix + FormBuilderFileId.New()[..16];
        try
        {
            File.Move(path, deleted, overwrite: false);
        }
        catch (FileNotFoundException)
        {
            // Nothing is stored under the id.
            return Task.CompletedTask;
        }

        try
        {
            File.Delete(deleted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The id reads as missing already; retention sweeps the bytes (ADR-0041).
            LogDeletedKept(logger, exception.GetType().Name);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Folders()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        return [.. Directory.EnumerateDirectories(Root).Select(Path.GetFileName).OfType<string>().Where(IsFolderName).Order(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public IReadOnlyList<StoredFileInfo> FilesIn(string folder)
    {
        CheckFolder(folder);
        var path = Path.Combine(Root, folder);
        if (!Directory.Exists(path))
        {
            return [];
        }

        var found = new List<StoredFileInfo>();
        foreach (var file in new DirectoryInfo(path).EnumerateFiles())
        {
            if (KindOf(file.Name, folder) is { } kind)
            {
                found.Add(new StoredFileInfo(file.Name, kind, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
            }
        }

        return found;
    }

    /// <inheritdoc />
    public void DeleteLeftover(string folder, string name)
    {
        CheckFolder(folder);
        if (KindOf(name, folder) is null)
        {
            throw new ArgumentException("The name is none of the store's files.", nameof(name));
        }

        try
        {
            File.Delete(Path.Combine(Root, folder, name));
        }
        catch (DirectoryNotFoundException)
        {
            // The folder went with its last file.
        }
    }

    /// <summary>
    /// What a name in a folder of the store is: a stored file (its id), a save's partial file, or a deleted file's bytes; null
    /// for any other name, which the store never writes and the sweep leaves alone.
    /// </summary>
    internal static StoredFileKind? KindOf(string name, string folder)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length < FormBuilderFileId.Length || !FormBuilderFileId.IsValid(name[..FormBuilderFileId.Length]) || !name.StartsWith(folder, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = name.AsSpan(FormBuilderFileId.Length);
        if (rest.IsEmpty)
        {
            return StoredFileKind.Stored;
        }

        if (rest.SequenceEqual(PartialSuffix))
        {
            return StoredFileKind.Partial;
        }

        return rest.StartsWith(DeletedInfix) && rest.Length == DeletedInfix.Length + 16 && IsLowerHex(rest[DeletedInfix.Length..])
            ? StoredFileKind.Deleted
            : null;
    }

    private static bool IsFolderName(string name) => name.Length == 2 && IsLowerHex(name);

    private static bool IsLowerHex(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckFolder(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (!IsFolderName(folder))
        {
            throw new ArgumentException("A folder of the store is named by two lowercase hex characters.", nameof(folder));
        }
    }

    private string PathOf(string id) => Path.Combine(Root, id[..2], id);

    // Best effort: the failure that got here is the one the caller hears of, and retention sweeps what stays (ADR-0041).
    private void Discard(string partial)
    {
        try
        {
            File.Delete(partial);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogPartialKept(logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not delete the partial file of a failed save ({Failure}); retention deletes it later")]
    private static partial void LogPartialKept(ILogger logger, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not delete the bytes of a deleted file ({Failure}); retention deletes them later")]
    private static partial void LogDeletedKept(ILogger logger, string failure);
}
