using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Files;

/// <summary>
/// The physical file store as a site gets it (ADR-0006, ADR-0041): in the configured folder outside the web root, a file
/// per id in its shard, stored whole or not at all. A save that fails, is cut off or is cancelled leaves nothing behind;
/// a reader never sees a file that is still being written; a second save never replaces a file; a deleted id reads as
/// missing at once; and what cannot be deleted is logged without its path and left for retention.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.None)]
public sealed class PhysicalFileStoreTests : UmbracoIntegrationTest
{
    private string _root = null!;

    private IFormBuilderFileStore Store => GetRequiredService<IFormBuilderFileStore>();

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
    public async Task ASave_StoresTheStreamFromItsPosition_InTheIdsShard_AndLeavesTheStreamOpen()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var id = FormBuilderFileId.New();
        await using var content = new MemoryStream([0xEF, 0xBB, .. bytes]) { Position = 2 };

        await Store.SaveAsync(id, content);

        Assert.Multiple(async () =>
        {
            Assert.That(content.CanRead, Is.True, "the caller's stream stays open");
            Assert.That(Files(), Is.EqualTo(new[] { Path.Combine(_root, id[..2], id) }), "one file, in its shard, without an extension");
            Assert.That(await ReadAsync(id), Is.EqualTo(bytes));
        });
    }

    [Test]
    public async Task ASave_ReadsTheStreamAsynchronously()
    {
        // A request body refuses synchronous reads (ADR-0006), and so does this stream.
        var bytes = RandomNumberGenerator.GetBytes(100_000);
        var id = FormBuilderFileId.New();

        await Store.SaveAsync(id, new ScriptedStream(bytes, bytes.Length, _ => Task.CompletedTask));

        Assert.That(await ReadAsync(id), Is.EqualTo(bytes));
    }

    [Test]
    public async Task AnEmptyFile_IsStored()
    {
        var id = FormBuilderFileId.New();

        await Store.SaveAsync(id, new MemoryStream());

        Assert.That(await ReadAsync(id), Is.Empty);
    }

    [Test]
    public async Task WhatIsNotStored_ReadsAsNull_AndDeletesWithoutError()
    {
        var stored = FormBuilderFileId.New();
        await Store.SaveAsync(stored, new MemoryStream([1, 2, 3]));
        var sameShard = stored[..2] + FormBuilderFileId.New()[2..];

        Assert.Multiple(async () =>
        {
            Assert.That(await Store.OpenReadAsync(FormBuilderFileId.New()), Is.Null);
            Assert.That(await Store.OpenReadAsync(sameShard), Is.Null, "in a shard that exists");
            Assert.That(() => Store.DeleteAsync(NewShard(stored)), Throws.Nothing, "in a shard that does not exist");
            Assert.That(() => Store.DeleteAsync(sameShard), Throws.Nothing);
            Assert.That(await ReadAsync(stored), Is.EqualTo(new byte[] { 1, 2, 3 }), "the other file of the shard stays");
        });
    }

    [Test]
    public async Task ADelete_RemovesTheFile_WhileADownloadReadsOn()
    {
        var bytes = RandomNumberGenerator.GetBytes(200_000);
        var id = FormBuilderFileId.New();
        await Store.SaveAsync(id, new MemoryStream(bytes));
        await using var download = (await Store.OpenReadAsync(id))!;
        var first = new byte[1000];
        await download.ReadExactlyAsync(first);

        await Store.DeleteAsync(id);
        var whileOpen = Files();
        var rest = new MemoryStream();
        await download.CopyToAsync(rest);

        Assert.Multiple(async () =>
        {
            Assert.That(await Store.OpenReadAsync(id), Is.Null, "gone for every later read");
            Assert.That(whileOpen, Has.None.EqualTo(Path.Combine(_root, id[..2], id)), "no file under the id while the download reads on");
            Assert.That(first.Concat(rest.ToArray()), Is.EqualTo(bytes), "the open download read it whole");
        });
        await download.DisposeAsync();
        Assert.That(Files(), Is.Empty);
    }

    [Test]
    public async Task ASaveCutOff_LeavesNothingBehind_AndTheIdCanBeSavedAfterwards()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var id = FormBuilderFileId.New();
        var reset = new IOException("JFB_TEST the connection was reset");

        var thrown = Assert.ThrowsAsync<IOException>(() => Store.SaveAsync(id, new ScriptedStream(bytes, 200_000, _ => throw reset)));

        Assert.Multiple(async () =>
        {
            Assert.That(thrown, Is.SameAs(reset), "the caller hears of the failure itself");
            Assert.That(Files(), Is.Empty, "no file and no partial file");
            Assert.That(await Store.OpenReadAsync(id), Is.Null);
        });
        await Store.SaveAsync(id, new MemoryStream(bytes));
        Assert.That(await ReadAsync(id), Is.EqualTo(bytes));
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

    [Test]
    public async Task ACancelledSave_LeavesNothingBehind()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        using var cancel = new CancellationTokenSource();
        var id = FormBuilderFileId.New();

        Assert.Multiple(async () =>
        {
            Assert.That(
                () => Store.SaveAsync(id, new ScriptedStream(bytes, 100_000, _ => cancel.CancelAsync()), cancel.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Files(), Is.Empty, "cancelled in the middle");
            Assert.That(() => Store.SaveAsync(id, new MemoryStream(bytes), cancel.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(Files(), Is.Empty, "cancelled before it started");
            Assert.That(await Store.OpenReadAsync(id), Is.Null);
        });
    }

    [Test]
    public async Task ASecondSave_OfAStoredId_IsRefused_AndTheFileStays()
    {
        var id = FormBuilderFileId.New();
        await Store.SaveAsync(id, new MemoryStream([1, 2, 3]));

        Assert.That(() => Store.SaveAsync(id, new MemoryStream([4, 5, 6])), Throws.InstanceOf<IOException>());
        Assert.Multiple(async () =>
        {
            Assert.That(await ReadAsync(id), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(Files(), Has.Count.EqualTo(1), "no partial file left");
        });
    }

    [Test]
    public async Task WhileASaveWrites_ReadersFindNothing_AndASecondSaveOfTheIdIsRefused_LeavingTheFirstAlone()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var id = FormBuilderFileId.New();
        var halfway = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var goOn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Store.SaveAsync(id, new ScriptedStream(bytes, 150_000, async _ =>
        {
            halfway.SetResult();
            await goOn.Task;
        }));
        await halfway.Task;

        try
        {
            Assert.Multiple(async () =>
            {
                Assert.That(Files(), Is.EqualTo(new[] { Path.Combine(_root, id[..2], id + PhysicalFileStore.PartialSuffix) }), "only the partial file");
                Assert.That(await Store.OpenReadAsync(id), Is.Null, "a reader never sees a file being written");
                Assert.That(() => Store.SaveAsync(id, new MemoryStream([4, 5, 6])), Throws.InstanceOf<IOException>(), "a second save");
                Assert.That(Files(), Has.Count.EqualTo(1), "the second save left the first one's partial file alone");
                Assert.That(Warnings(), Is.Empty, "the second save tried to delete nothing");
            });
        }
        finally
        {
            goOn.SetResult();
        }

        await first;
        Assert.That(await ReadAsync(id), Is.EqualTo(bytes), "the first save completed");
    }

    [Test]
    public void IdsThatAreNotFileIds_AndUnreadableStreams_AreRefused()
    {
        var bad = new[] { "../../../../web.config", Path.Combine("..", "x"), "0123456789ABCDEF0123456789ABCDEF", "0123", string.Empty, null! };
        var id = FormBuilderFileId.New();

        Assert.Multiple(() =>
        {
            foreach (var wrong in bad)
            {
                Assert.That(() => Store.SaveAsync(wrong, new MemoryStream([1])), Throws.InstanceOf<ArgumentException>(), wrong);
                Assert.That(() => Store.OpenReadAsync(wrong), Throws.InstanceOf<ArgumentException>(), wrong);
                Assert.That(() => Store.DeleteAsync(wrong), Throws.InstanceOf<ArgumentException>(), wrong);
            }

            Assert.That(() => Store.SaveAsync(id, null!), Throws.ArgumentNullException);
            Assert.That(() => Store.SaveAsync(id, new WriteOnlyStream()), Throws.ArgumentException);
            Assert.That(Files(), Is.Empty);
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

    // A file id in another shard than this one's.
    private static string NewShard(string id) => (id[0] == '0' ? "1" : "0") + FormBuilderFileId.New()[1..];

    private List<CapturedLog> Warnings() =>
        GetRequiredService<LogCapture>().Entries.Where(log => log.Category == typeof(PhysicalFileStore).FullName && log.Level == LogLevel.Warning).ToList();

    private List<string> Files() =>
        Directory.Exists(_root) ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList() : [];

    private async Task<byte[]> ReadAsync(string id)
    {
        await using var stream = await Store.OpenReadAsync(id);
        Assert.That(stream, Is.Not.Null, "stored");
        var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// A request body as a store meets it: read only asynchronously, giving its bytes up to <c>breakAt</c>, then running
    /// <c>atBreak</c> once (which may throw, cancel or wait), then the rest.
    /// </summary>
    private sealed class ScriptedStream(byte[] bytes, int breakAt, Func<CancellationToken, Task> atBreak) : Stream
    {
        private int _position;
        private bool _broke;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_position == breakAt && !_broke)
            {
                _broke = true;
                await atBreak(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var end = _broke ? bytes.Length : breakAt;
            var count = Math.Min(buffer.Length, end - _position);
            bytes.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Synchronous reads are not allowed.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
