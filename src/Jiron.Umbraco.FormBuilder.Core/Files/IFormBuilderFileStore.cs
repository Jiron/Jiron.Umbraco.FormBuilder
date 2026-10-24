namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// Where the bytes of uploaded files are kept (D15, ADR-0006, ADR-0041). The default keeps them in a folder outside the
/// web root (<see cref="FormBuilderFilesOptions.Root"/>); a site that keeps them elsewhere, for example in blob storage
/// through Umbraco's official provider, registers its own implementation in a composer that runs after Jiron Form
/// Builder's. The rows that describe the files are Jiron Form Builder's: a store keeps only bytes under ids.
/// </summary>
/// <remarks>
/// Ids are <see cref="FormBuilderFileId"/>s, 128 random bits as 32 lowercase hex characters, never derived from a file's
/// name. Every method refuses any other id with an <see cref="ArgumentException"/>, so an id never names a path.
/// </remarks>
public interface IFormBuilderFileStore
{
    /// <summary>
    /// Stores <paramref name="content"/>, read from its position to its end, under <paramref name="id"/>: all of it, or
    /// nothing when anything fails (the stream, the storage or the cancellation), in which case the exception reaches the
    /// caller. The stream stays open: the caller owns it.
    /// </summary>
    /// <param name="id">A <see cref="FormBuilderFileId"/>.</param>
    /// <param name="content">The bytes to store, readable.</param>
    /// <param name="cancellationToken">Cancels the save, which then stores nothing.</param>
    /// <exception cref="ArgumentException">The id is not a file id, or the stream cannot be read.</exception>
    /// <exception cref="IOException">
    /// A file is stored under the id already, or another save is writing it. A save never replaces a file: the other one
    /// stays as it is.
    /// </exception>
    Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// The file stored under <paramref name="id"/>, from its first byte, for the caller to dispose; null when nothing is
    /// stored under it. Never a file that a save is still writing.
    /// </summary>
    /// <param name="id">A <see cref="FormBuilderFileId"/>.</param>
    /// <param name="cancellationToken">Cancels opening the file.</param>
    /// <exception cref="ArgumentException">The id is not a file id.</exception>
    Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Deletes the file stored under <paramref name="id"/>; an id with nothing stored under it is no error.</summary>
    /// <param name="id">A <see cref="FormBuilderFileId"/>.</param>
    /// <param name="cancellationToken">Cancels the delete before it starts.</param>
    /// <exception cref="ArgumentException">The id is not a file id.</exception>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
