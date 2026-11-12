using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Samples;
using Jiron.Umbraco.FormBuilder.Samples.Blob;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.StorageProviders.AzureBlob.IO;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Files;

/// <summary>
/// The blob store sample as a site gets it (ROADMAP 15.2, ADR-0006, ADR-0234): the store's contract
/// (<see cref="FileStoreContract"/>) against the local storage emulator, through Umbraco's official Azure Blob provider,
/// in a container of each test's own below the root path the site sets; the blobs named by the id's shard, a save's
/// claim the only thing beside a file being written, and the container private.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.None)]
[NonParallelizable]
public sealed class BlobFileStoreTests : FileStoreContract
{
    private const string RootPath = "uploads";

    private static StorageEmulator? _emulator;
    private string _container = null!;

    private static StorageEmulator Emulator => _emulator ?? throw new InvalidOperationException("The emulator has not started.");

    [OneTimeSetUp]
    public static async Task StartTheEmulator() => _emulator = await StorageEmulator.StartAsync();

    [OneTimeTearDown]
    public static async Task StopTheEmulator()
    {
        if (_emulator is not null)
        {
            await _emulator.DisposeAsync();
            _emulator = null;
        }
    }

    [Test]
    public void TheSite_StoresThroughTheProvidersContainer_WithItsRootPath()
    {
        var options = GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AzureBlobFileSystemOptions>>().Get(BlobFileStore.FileSystemName);

        Assert.Multiple(() =>
        {
            Assert.That(Store, Is.InstanceOf<BlobFileStore>());
            Assert.That((options.ContainerName, options.ContainerRootPath), Is.EqualTo((_container, RootPath)));
        });
    }

    [Test]
    public async Task TheContainer_IsPrivate_AndAStoredFileIsNoAddressAnyoneCanRead()
    {
        var id = FormBuilderFileId.New();
        await Store.SaveAsync(id, new MemoryStream([1, 2, 3]));
        var container = Container();
        var access = await container.GetAccessPolicyAsync();
        using var anonymous = new HttpClient();
        using var read = await anonymous.GetAsync(new Uri(Emulator.BlobEndpoint, $"{_container}/{StoredName(id)}"));

        Assert.Multiple(() =>
        {
            Assert.That(access.Value.BlobPublicAccess, Is.EqualTo(PublicAccessType.None));
            Assert.That((int)read.StatusCode, Is.AnyOf(401, 403, 404), "nobody reads it without the account's key");
        });
    }

    [Test]
    public async Task AContainerThatLetsAnyoneReadIt_IsRefused_AndHoldsNothing()
    {
        await Container().CreateAsync(PublicAccessType.Blob);

        var refused = Assert.ThrowsAsync<InvalidOperationException>(async () => await Store.SaveAsync(FormBuilderFileId.New(), new MemoryStream([1, 2, 3])));

        Assert.Multiple(async () =>
        {
            Assert.That(refused!.Message, Does.Contain("lets anyone read its blobs"));
            Assert.That(await StoredAsync(), Is.Empty, "no upload lands where an address opens it");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        _container = "jfb-" + Guid.NewGuid().ToString("N");
        builder.AddFormBuilderSample(FormBuilderSamples.BlobStore);
        builder.Services.Configure<AzureBlobFileSystemOptions>(BlobFileStore.FileSystemName, options =>
        {
            options.ConnectionString = Emulator.ConnectionString;
            options.ContainerName = _container;
            options.ContainerRootPath = RootPath;
        });
    }

    /// <inheritdoc />
    protected override async Task<IReadOnlyList<string>> StoredAsync()
    {
        var container = Container();
        if (!await container.ExistsAsync())
        {
            return [];
        }

        var names = new List<string>();
        await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: null, cancellationToken: CancellationToken.None))
        {
            names.Add(blob.Name);
        }

        return names;
    }

    /// <inheritdoc />
    /// <remarks>Below the root path, in the shard of the id's first two characters.</remarks>
    protected override string StoredName(string id) => $"{RootPath}/{id[..2]}/{id}";

    /// <inheritdoc />
    /// <remarks>Only the save's claim on its id beside it; no blob of the id before the commit.</remarks>
    protected override Task ExpectWhileWritingAsync(string id, IReadOnlyList<string> stored)
    {
        Assert.That(stored, Is.EqualTo(new[] { StoredName(id) + BlobFileStore.ClaimSuffix }), "only the claim");
        return Task.CompletedTask;
    }

    private BlobContainerClient Container() => new BlobServiceClient(Emulator.ConnectionString).GetBlobContainerClient(_container);
}
