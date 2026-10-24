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
internal sealed partial class PhysicalFileStore(IOptions<FormBuilderFilesOptions> options, IHostFolders folders, ILogger<PhysicalFileStore> logger) : IFormBuilderFileStore
{
    /// <summary>What a save writes to until the file is complete.</summary>
    public const string PartialSuffix = ".partial";

    /// <summary>What a delete renames a file to, followed by 16 random hex characters, before it deletes it.</summary>
    public const string DeletedInfix = ".deleted-";

    private const int BufferSize = 81920;

    /// <summary>The full path of the folder the files are in.</summary>
    public string Root { get; } = FileStoreRoot.Resolve(options.Value.Root, folders);

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
