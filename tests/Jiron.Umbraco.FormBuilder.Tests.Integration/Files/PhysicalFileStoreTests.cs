using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Files;

/// <summary>
/// The physical file store as a site gets it (ADR-0006, ADR-0041): the store's contract (<see cref="FileStoreContract"/>)
/// in the configured folder outside the web root, a file per id in its shard without an extension, written to a partial
/// file beside it; and what cannot be deleted is logged without its path and left for retention.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.None)]
public sealed class PhysicalFileStoreTests : FileStoreContract
{
    private string _root = null!;

    [TearDown]
    public void DeleteTheFolder()
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        // What a test kept from deletion can be deleted again.
        foreach (var entry in Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories))
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }
            else if (Directory.Exists(entry))
            {
                File.SetUnixFileMode(entry, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void TheSite_StoresInTheConfiguredFolder_AndRefusesItsWebRoot()
    {
        var folders = GetRequiredService<IHostFolders>();
        var validation = GetRequiredService<IEnumerable<IValidateOptions<FormBuilderOptions>>>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(Store, Is.InstanceOf<PhysicalFileStore>());
            Assert.That(((PhysicalFileStore)Store).Root, Is.EqualTo(_root));
            Assert.That(folders, Is.InstanceOf<WebHostFolders>(), "Web supplies the site's folders");
            Assert.That(validation.Validate(Options.DefaultName, new FormBuilderOptions { Files = { Root = Path.Combine(folders.WebRoot, "uploads") } }).Failed, Is.True);
            Assert.That(validation.Validate(Options.DefaultName, new FormBuilderOptions()).Succeeded, Is.True, "the default");
        });
    }

    [Test]
    public async Task APartialFileThatCannotBeDeleted_IsLoggedWithoutItsPath_AndTheCallerHearsOfTheFailure()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var id = FormBuilderFileId.New();
        var shard = Path.Combine(_root, id[..2]);
        var partial = Path.Combine(shard, id + PhysicalFileStore.PartialSuffix);
        var reset = new IOException("JFB_TEST the connection was reset");

        var thrown = Assert.ThrowsAsync<IOException>(() => Store.SaveAsync(id, new ScriptedStream(bytes, 150_000, _ =>
        {
            KeepFromDeletion(shard, partial);
            throw reset;
        })));

        var warnings = Warnings();
        Assert.Multiple(async () =>
        {
            Assert.That(thrown, Is.SameAs(reset), "the caller hears of the failure itself, not of the delete");
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Message, Does.Contain("partial file").And.Not.Contain(_root).And.Not.Contain(id), "no path");
            Assert.That(Files(), Is.EqualTo(new[] { partial }), "left for retention");
            Assert.That(await Store.OpenReadAsync(id), Is.Null);
        });
    }

    [Test]
    public async Task AFileThatAppearsWhileASaveWrites_IsKept_AndTheSaveIsRefused()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var id = FormBuilderFileId.New();
        var path = Path.Combine(_root, id[..2], id);

        Assert.That(
            () => Store.SaveAsync(id, new ScriptedStream(bytes, 150_000, token => File.WriteAllBytesAsync(path, [7, 7, 7], token))),
            Throws.InstanceOf<IOException>());

        Assert.Multiple(async () =>
        {
            Assert.That(await ReadAsync(id), Is.EqualTo(new byte[] { 7, 7, 7 }), "the rename never replaces a file");
            Assert.That(Files(), Is.EqualTo(new[] { path }), "no partial file left");
            Assert.That(Warnings(), Is.Empty);
        });
    }

    [Test]
    public async Task AFileWhoseBytesCannotBeDeleted_ReadsAsMissing_AndIsLoggedWithoutItsPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("Only Windows renames a file that it refuses to delete.");
        }

        var id = FormBuilderFileId.New();
        var path = Path.Combine(_root, id[..2], id);
        await Store.SaveAsync(id, new MemoryStream([1, 2, 3]));
        File.SetAttributes(path, FileAttributes.ReadOnly);

        await Store.DeleteAsync(id);

        var warnings = Warnings();
        Assert.Multiple(async () =>
        {
            Assert.That(await Store.OpenReadAsync(id), Is.Null, "the id reads as missing");
            Assert.That(Files(), Has.Count.EqualTo(1).And.All.StartsWith(path + PhysicalFileStore.DeletedInfix), "left for retention");
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0].Message, Does.Contain("deleted file").And.Not.Contain(_root).And.Not.Contain(id), "no path");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.AddComposers();
        builder.Services.AddLogCapture();
        _root = Path.Combine(Path.GetTempPath(), "jfb-files-" + Guid.NewGuid().ToString("N"));
        builder.Services.Configure<FormBuilderOptions>(options => options.Files.Root = _root);
    }

    // Makes a file impossible to delete until the teardown: read-only on Windows, and elsewhere in a folder nobody may
    // change.
    private static void KeepFromDeletion(string folder, string file)
    {
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(file, FileAttributes.ReadOnly);
        }
        else
        {
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
    }

    /// <inheritdoc />
    protected override Task<IReadOnlyList<string>> StoredAsync() => Task.FromResult<IReadOnlyList<string>>(Files());

    /// <inheritdoc />
    /// <remarks>The file of the id in its shard, without an extension.</remarks>
    protected override string StoredName(string id) => Path.Combine(_root, id[..2], id);

    /// <inheritdoc />
    /// <remarks>Only the save's partial file beside it.</remarks>
    protected override Task ExpectWhileWritingAsync(string id, IReadOnlyList<string> stored)
    {
        Assert.That(stored, Is.EqualTo(new[] { Path.Combine(_root, id[..2], id + PhysicalFileStore.PartialSuffix) }), "only the partial file");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>The refused save tried to delete nothing.</remarks>
    protected override void ExpectAfterTheSecondSave() => Assert.That(Warnings(), Is.Empty, "the second save tried to delete nothing");

    private List<CapturedLog> Warnings() =>
        GetRequiredService<LogCapture>().Entries.Where(log => log.Category == typeof(PhysicalFileStore).FullName && log.Level == LogLevel.Warning).ToList();

    private List<string> Files() =>
        Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList() : [];
}
