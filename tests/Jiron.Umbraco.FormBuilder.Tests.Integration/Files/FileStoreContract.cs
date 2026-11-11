using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Files;

/// <summary>
/// The contract of every <see cref="IFormBuilderFileStore"/> (ADR-0006, ADR-0041), which each store's fixture runs against
/// the store a site gets: a file is stored whole from its stream's position, read only asynchronously, or not at all; a
/// save that is cut off or cancelled leaves nothing behind and the id can be saved afterwards; a reader never sees a file
/// that is still being written; a second save never replaces a file, also while the first writes, which completes;
/// what is not stored reads as null and deletes without error; a delete removes the file at once while an open download
/// reads on; and ids that are not file ids and unreadable streams are refused.
/// </summary>
public abstract class FileStoreContract : UmbracoIntegrationTest
{
    /// <summary>The store the site gets.</summary>
    protected IFormBuilderFileStore Store => GetRequiredService<IFormBuilderFileStore>();

    [Test]
    public async Task ASave_StoresTheStreamFromItsPosition_UnderItsId_AndLeavesTheStreamOpen()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        var id = FormBuilderFileId.New();
        await using var content = new MemoryStream([0xEF, 0xBB, .. bytes]) { Position = 2 };

        await Store.SaveAsync(id, content);

        Assert.Multiple(async () =>
        {
            Assert.That(content.CanRead, Is.True, "the caller's stream stays open");
            Assert.That(await StoredAsync(), Is.EqualTo(new[] { StoredName(id) }), "one file, under its id's name");
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
        var whileOpen = await StoredAsync();
        var rest = new MemoryStream();
        await download.CopyToAsync(rest);

        Assert.Multiple(async () =>
        {
            Assert.That(await Store.OpenReadAsync(id), Is.Null, "gone for every later read");
            Assert.That(whileOpen, Has.None.EqualTo(StoredName(id)), "nothing under the id while the download reads on");
            Assert.That(first.Concat(rest.ToArray()), Is.EqualTo(bytes), "the open download read it whole");
        });
        await download.DisposeAsync();
        Assert.That(await StoredAsync(), Is.Empty);
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
            Assert.That(await StoredAsync(), Is.Empty, "no file and nothing partial");
            Assert.That(await Store.OpenReadAsync(id), Is.Null);
        });
        await Store.SaveAsync(id, new MemoryStream(bytes));
        Assert.That(await ReadAsync(id), Is.EqualTo(bytes));
    }

    [Test]
    public async Task ACancelledSave_LeavesNothingBehind()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        using var cancel = new CancellationTokenSource();
        var id = FormBuilderFileId.New();

        Assert.That(
            () => Store.SaveAsync(id, new ScriptedStream(bytes, 100_000, _ => cancel.CancelAsync()), cancel.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(await StoredAsync(), Is.Empty, "cancelled in the middle");
        Assert.That(() => Store.SaveAsync(id, new MemoryStream(bytes), cancel.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(async () =>
        {
            Assert.That(await StoredAsync(), Is.Empty, "cancelled before it started");
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
            Assert.That(await StoredAsync(), Has.Count.EqualTo(1), "nothing partial left");
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
            var writing = await StoredAsync();
            Assert.Multiple(async () =>
            {
                Assert.That(writing, Has.None.EqualTo(StoredName(id)), "nothing under the id while it is written");
                await ExpectWhileWritingAsync(id, writing);
                Assert.That(await Store.OpenReadAsync(id), Is.Null, "a reader never sees a file being written");
                Assert.That(() => Store.SaveAsync(id, new MemoryStream([4, 5, 6])), Throws.InstanceOf<IOException>(), "a second save");
                Assert.That(await StoredAsync(), Is.EqualTo(writing), "the second save left the first one's work alone");
                ExpectAfterTheSecondSave();
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
    public async Task IdsThatAreNotFileIds_AndUnreadableStreams_AreRefused()
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
        });
        Assert.That(await StoredAsync(), Is.Empty);
    }

    /// <summary>The names of everything the store holds, its own work in progress too, in no particular order.</summary>
    protected abstract Task<IReadOnlyList<string>> StoredAsync();

    /// <summary>The one name a stored file of the id has among <see cref="StoredAsync"/>.</summary>
    /// <param name="id">The file's id.</param>
    protected abstract string StoredName(string id);

    /// <summary>What the store holds besides while a save of the id writes; nothing more by default.</summary>
    /// <param name="id">The id being written.</param>
    /// <param name="stored">What the store holds meanwhile.</param>
    protected virtual Task ExpectWhileWritingAsync(string id, IReadOnlyList<string> stored) => Task.CompletedTask;

    /// <summary>What a refused second save of an id being written leaves; nothing more by default.</summary>
    protected virtual void ExpectAfterTheSecondSave()
    {
    }

    /// <summary>A file's bytes, read through the store.</summary>
    /// <param name="id">The file's id.</param>
    protected async Task<byte[]> ReadAsync(string id)
    {
        await using var stream = await Store.OpenReadAsync(id);
        Assert.That(stream, Is.Not.Null, "stored");
        var copy = new MemoryStream();
        await stream!.CopyToAsync(copy);
        return copy.ToArray();
    }

    // A file id in another shard than this one's.
    private static string NewShard(string id) => (id[0] == '0' ? "1" : "0") + FormBuilderFileId.New()[1..];

    /// <summary>
    /// A request body as a store meets it: read only asynchronously, giving its bytes up to <c>breakAt</c>, then running
    /// <c>atBreak</c> once (which may throw, cancel or wait), then the rest.
    /// </summary>
    protected sealed class ScriptedStream(byte[] bytes, int breakAt, Func<CancellationToken, Task> atBreak) : Stream
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
