using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Umbraco.StorageProviders.AzureBlob.IO;

namespace Jiron.Umbraco.FormBuilder.Samples.Blob;

/// <summary>
/// The blob store sample (docs/extending.md, ADR-0006): the uploaded files in a private container of Azure Blob Storage
/// that the site registers through Umbraco's official provider as <see cref="FileSystemName"/>, for a site of several
/// servers or one without a lasting disk. It keeps the store's contract:
/// <list type="bullet">
/// <item>a file is staged in blocks of 1 MB as its bytes arrive and committed only at the end, so a save that is cut off,
/// fails or is cancelled leaves no blob, and a reader never sees one being written;</item>
/// <item>a save first claims its id with an empty blob beside it, made only where none is, so a second save of an id that
/// is being written or stored is refused and leaves the first alone; the commit itself never replaces a blob;</item>
/// <item>a read is one streamed download of the blob as it is, which a delete does not cut off.</item>
/// </list>
/// The blobs are named <c>&lt;xx&gt;/&lt;id&gt;</c> by the id's first two characters, below the container's root path, which the
/// site sets so that no virtual path is in the names. Nothing serves the container: files go out only through the
/// management API's downloads. A container that already lets anyone read its blobs is refused at the first save or read,
/// so no upload lands where an address alone would open it (16.1's review).
/// </summary>
/// <param name="provider">The provider's file systems.</param>
public sealed class BlobFileStore(IAzureBlobFileSystemProvider provider) : IFormBuilderFileStore
{
    /// <summary>The name of the file system the site registers for the files.</summary>
    public const string FileSystemName = "JironFormBuilder";

    /// <summary>What the name of a save's claim on its id ends with.</summary>
    public const string ClaimSuffix = ".saving";

    /// <summary>The size of a block.</summary>
    public const int BlockBytes = 1024 * 1024;

    private Task? _container;

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
        await ContainerAsync(cancellationToken);
        var blob = BlobOf(id);
        var claim = BlobOf(id, ClaimSuffix);
        try
        {
            await claim.UploadAsync(BinaryData.Empty, new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } }, cancellationToken);
        }
        catch (RequestFailedException failed) when (IsTaken(failed))
        {
            throw new IOException("A file of this id is being stored.", failed);
        }

        try
        {
            if (await blob.ExistsAsync(cancellationToken))
            {
                throw new IOException("A file is stored under this id already.");
            }

            await blob.UploadAsync(
                content,
                new BlobUploadOptions
                {
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                    TransferOptions = new StorageTransferOptions { InitialTransferSize = BlockBytes, MaximumTransferSize = BlockBytes, MaximumConcurrency = 1 },
                },
                cancellationToken);
        }
        catch (RequestFailedException failed) when (IsTaken(failed))
        {
            throw new IOException("A file is stored under this id already.", failed);
        }
        finally
        {
            // The claim goes whatever came of the save; a claim a stopped server left keeps only its own id.
            await claim.DeleteIfExistsAsync(cancellationToken: CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        await ContainerAsync(cancellationToken);
        try
        {
            var download = await BlobOf(id).DownloadStreamingAsync(cancellationToken: cancellationToken);
            return download.Value.Content;
        }
        catch (RequestFailedException failed) when (failed.Status == 404)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        await ContainerAsync(cancellationToken);
        await BlobOf(id).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    /// <summary>The name of an id's blob below the container's root path.</summary>
    /// <param name="id">The file's id.</param>
    /// <returns>The name.</returns>
    public static string NameOf(string id)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        return $"{id[..2]}/{id}";
    }

    private BlobClient BlobOf(string id, string suffix = "") => provider.GetFileSystem(FileSystemName).GetBlobClient(NameOf(id) + suffix);

    // The container, made private once where it is missing, and refused where it lets anyone read it.
    private Task ContainerAsync(CancellationToken cancellationToken)
    {
        var made = _container;
        if (made is { IsCompletedSuccessfully: true })
        {
            return made;
        }

        made = PrivateAsync(BlobOf(FormBuilderFileId.New()).GetParentBlobContainerClient(), cancellationToken);
        _container = made;
        return made;
    }

    private static async Task PrivateAsync(BlobContainerClient container, CancellationToken cancellationToken)
    {
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);
        var access = await container.GetAccessPolicyAsync(cancellationToken: cancellationToken);
        if (access.Value.BlobPublicAccess != PublicAccessType.None)
        {
            throw new InvalidOperationException($"The container '{container.Name}' lets anyone read its blobs; make it private before Jiron Form Builder stores visitors' files in it.");
        }
    }

    // Another blob holds the name: a claim or a stored file.
    private static bool IsTaken(RequestFailedException failed) =>
        failed.Status is 409 or 412 || failed.ErrorCode is "BlobAlreadyExists" or "ConditionNotMet";
}
