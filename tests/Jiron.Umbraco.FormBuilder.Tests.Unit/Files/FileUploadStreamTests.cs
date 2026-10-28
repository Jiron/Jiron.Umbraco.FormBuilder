using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The stream a store reads an upload through (ADR-0087): the first bytes read already, then the rest, counted on every
/// read against the limit and hashed, asynchronous only; a failure to read the request arrives as a read exception of
/// its own, while a cancellation, a limit and a failure already told pass as they are.
/// </summary>
public sealed class FileUploadStreamTests
{
    private static readonly byte[] Head = "%PDF-"u8.ToArray();
    private static readonly byte[] Rest = "1.7 and the rest of it"u8.ToArray();

    [Test]
    public async Task AnUpload_IsItsHeadThenItsRest_CountedAndHashed()
    {
        using var rest = new MemoryStream(Rest);
        using var upload = new FileUploadStream(Head, rest, Head.Length + Rest.Length, hash: true);
        using var read = new MemoryStream();

        await upload.CopyToAsync(read);

        Assert.Multiple(() =>
        {
            Assert.That(read.ToArray(), Is.EqualTo(Head.Concat(Rest)));
            Assert.That(upload.BytesRead, Is.EqualTo(Head.Length + Rest.Length));
            Assert.That(upload.Position, Is.EqualTo(upload.BytesRead));
            Assert.That(upload.IsAtEnd, Is.True);
            Assert.That(upload.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData([.. Head, .. Rest]))));
        });
    }

    [Test]
    public async Task AnUpload_ReadInSingleBytes_IsTheSame()
    {
        using var rest = new MemoryStream(Rest);
        using var upload = new FileUploadStream(Head, rest, 1000, hash: true);
        var bytes = new List<byte>();
        var one = new byte[1];
        while (await upload.ReadAsync(one) == 1)
        {
            bytes.Add(one[0]);
        }

        var empty = await upload.ReadAsync(Memory<byte>.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(Head.Concat(Rest)));
            Assert.That(upload.Sha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData([.. Head, .. Rest]))));
            Assert.That(empty, Is.Zero, "an empty buffer reads nothing");
        });
    }

    [Test]
    public async Task AnUpload_ReadIntoPartOfAnArray_IsTheSame_AndFlushesNothing()
    {
        using var rest = new MemoryStream(Rest);
        using var upload = new FileUploadStream(Head, rest, 1000);
        var buffer = new byte[Head.Length + 2];

#pragma warning disable CA1835 // The overload of arrays is the one under test.
        var read = await upload.ReadAsync(buffer, 2, Head.Length);
#pragma warning restore CA1835
        await upload.FlushAsync();

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(Head.Length));
            Assert.That(buffer.Skip(2), Is.EqualTo(Head));
            Assert.That(upload.BytesRead, Is.EqualTo(Head.Length));
        });
    }

    [Test]
    public void ItsExceptions_TakeTheStandardArguments()
    {
        var inner = new IOException("The connection was reset.");

        Assert.Multiple(() =>
        {
            Assert.That(new FileTooLargeException(10), Has.Property(nameof(FileTooLargeException.Limit)).EqualTo(10).And.Message.Contains("10 bytes"));
            Assert.That(new FileTooLargeException(), Has.Property(nameof(FileTooLargeException.Limit)).Zero.And.Message.EqualTo("The upload is larger than its limit."));
            Assert.That(new FileTooLargeException("Too large."), Has.Message.EqualTo("Too large."));
            Assert.That(new FileTooLargeException("Too large.", inner), Has.InnerException.SameAs(inner));
            Assert.That(new FileUploadReadException(), Has.Message.EqualTo("The upload could not be read from its request."));
            Assert.That(new FileUploadReadException("Cut short."), Has.Message.EqualTo("Cut short."));
            Assert.That(new FileUploadReadException("Cut short.", inner), Has.InnerException.SameAs(inner));
        });
    }

    [Test]
    public void ABytePastTheLimit_IsRefused_NamingTheLimit()
    {
        using var rest = new MemoryStream(Rest);
        using var upload = new FileUploadStream(Head, rest, Head.Length + Rest.Length - 1);

        var refused = Assert.ThrowsAsync<FileTooLargeException>(() => upload.CopyToAsync(Stream.Null));

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Limit, Is.EqualTo(Head.Length + Rest.Length - 1));
            Assert.That(upload.IsAtEnd, Is.False);
        });
    }

    [Test]
    public void AHeadPastTheLimit_IsRefusedOnTheFirstRead()
    {
        using var upload = new FileUploadStream(Head, Stream.Null, Head.Length - 1);

        Assert.ThrowsAsync<FileTooLargeException>(() => upload.ReadAsync(new byte[16]).AsTask());
    }

    [Test]
    public async Task WithoutAHead_ItCountsAWholeRequest()
    {
        using var rest = new MemoryStream(Rest);
        using var request = new FileUploadStream(ReadOnlyMemory<byte>.Empty, rest, Rest.Length);

        await request.CopyToAsync(Stream.Null);

        Assert.That((request.BytesRead, request.IsAtEnd), Is.EqualTo((Rest.Length, true)));
    }

    [Test]
    public async Task TheHash_IsThereOnlyOnceTheEndIsRead_AndOnlyWhenAsked()
    {
        using var rest = new MemoryStream(Rest);
        using var hashed = new FileUploadStream(Head, rest, 1000, hash: true);
        using var plain = new FileUploadStream(Head, Stream.Null, 1000);
        var read = await hashed.ReadAsync(new byte[2]);
        await plain.CopyToAsync(Stream.Null);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(2));
            Assert.That(() => hashed.Sha256, Throws.InvalidOperationException.With.Message.Contains("end"));
            Assert.That(() => plain.Sha256, Throws.InvalidOperationException.With.Message.Contains("does not hash"));
        });
    }

    [Test]
    public void ItIsReadOnce_FromItsStart_AndOnlyAsynchronously()
    {
        using var upload = new FileUploadStream(Head, Stream.Null, 100);

        Assert.Multiple(() =>
        {
            Assert.That((upload.CanRead, upload.CanSeek, upload.CanWrite), Is.EqualTo((true, false, false)));
            Assert.That(() => upload.Read(new byte[4], 0, 4), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => upload.Length, Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => upload.Position = 1, Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => upload.Seek(0, SeekOrigin.Begin), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => upload.SetLength(1), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => upload.Write([1], 0, 1), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => new FileUploadStream(Head, Stream.Null, -1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public async Task AFailureToReadTheRequest_ArrivesAsAReadException_WithItsCause()
    {
        var cut = new IOException("The connection was reset.");
        var malformed = new InvalidDataException("Malformed.");

        var (fromCut, fromMalformed) = (await FailureAsync(cut), await FailureAsync(malformed));

        Assert.Multiple(() =>
        {
            Assert.That(fromCut, Is.InstanceOf<FileUploadReadException>().And.Property(nameof(Exception.InnerException)).SameAs(cut));
            Assert.That(fromMalformed, Is.InstanceOf<FileUploadReadException>().And.Property(nameof(Exception.InnerException)).SameAs(malformed));
        });
    }

    [Test]
    public async Task ACancellationALimitAndAFailureAlreadyTold_PassAsTheyAre()
    {
        Exception[] passing = [new OperationCanceledException(), new TaskCanceledException(), new FileTooLargeException(10), new FileUploadReadException("Told.", new IOException())];
        var thrown = new List<Exception?>();
        foreach (var exception in passing)
        {
            thrown.Add(await FailureAsync(exception));
        }

        Assert.That(thrown, Is.EqualTo(passing));
    }

    // What reading an upload whose rest throws the exception throws.
    private static async Task<Exception?> FailureAsync(Exception thrown)
    {
        using var upload = new FileUploadStream(ReadOnlyMemory<byte>.Empty, new Throwing(thrown), 100);
        try
        {
            _ = await upload.ReadAsync(new byte[4]);
            return null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return exception;
        }
    }

    // A request whose every read fails.
    private sealed class Throwing(Exception exception) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => 0; set => throw new NotSupportedException(); }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromException<int>(exception);

        public override int Read(byte[] buffer, int offset, int count) => throw exception;

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
