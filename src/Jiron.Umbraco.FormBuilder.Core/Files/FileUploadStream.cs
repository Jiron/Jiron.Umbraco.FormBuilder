using System.Security.Cryptography;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// An upload as a store reads it (ADR-0006, ADR-0087): the first bytes already read for its checks, then the rest of
/// the request, counted on every read against a limit, past which a read throws <see cref="FileTooLargeException"/>,
/// and hashed with SHA-256 when asked. A failure to read the rest arrives as <see cref="FileUploadReadException"/>, so
/// that a store's own failures stay apart from the request's. It reads asynchronously only, as a request body may only
/// be read. With nothing read already, it counts a whole request.
/// </summary>
/// <param name="head">The bytes read already, which come first.</param>
/// <param name="rest">The rest, which the caller owns and disposes.</param>
/// <param name="limit">The most bytes it gives, 0 or more.</param>
/// <param name="hash">Whether to compute the SHA-256 of the bytes it gives.</param>
internal sealed class FileUploadStream(ReadOnlyMemory<byte> head, Stream rest, long limit, bool hash = false) : Stream
{
    private readonly IncrementalHash? _hash = hash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
    private readonly long _limit = limit >= 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit), limit, "A limit is 0 or more bytes.");
    private int _headRead;
    private string? _sha256;

    /// <summary>How many bytes it gave so far.</summary>
    public long BytesRead { get; private set; }

    /// <summary>Whether a read found the end of the bytes.</summary>
    public bool IsAtEnd { get; private set; }

    /// <summary>The SHA-256 of the bytes in lowercase hex, once a read found their end.</summary>
    /// <exception cref="InvalidOperationException">It does not hash, or the end has not been read.</exception>
    public string Sha256 => _sha256 ?? throw new InvalidOperationException(_hash is null ? "This stream does not hash." : "The end has not been read yet.");

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException("An upload's length is known once it is read.");

    /// <inheritdoc />
    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException("An upload is read once, from its start.");
    }

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.IsEmpty)
        {
            return 0;
        }

        int read;
        if (_headRead < head.Length)
        {
            read = Math.Min(buffer.Length, head.Length - _headRead);
            head.Slice(_headRead, read).CopyTo(buffer);
            _headRead += read;
        }
        else
        {
            read = await ReadSourceAsync(rest, buffer, cancellationToken);
        }

        if (read == 0)
        {
            IsAtEnd = true;
            _sha256 ??= _hash is null ? null : Convert.ToHexStringLower(_hash.GetHashAndReset());
            return 0;
        }

        BytesRead += read;
        if (BytesRead > _limit)
        {
            throw new FileTooLargeException(_limit);
        }

        _hash?.AppendData(buffer.Span[..read]);
        return read;
    }

    /// <summary>
    /// Reads from a request's bytes: a failure other than a cancellation, a limit or one already told arrives as
    /// <see cref="FileUploadReadException"/>.
    /// </summary>
    /// <param name="source">The request's bytes.</param>
    /// <param name="buffer">Where the bytes go.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<int> ReadSourceAsync(Stream source, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(buffer, cancellationToken);
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or FileTooLargeException or FileUploadReadException or OutOfMemoryException))
        {
            throw new FileUploadReadException("The upload could not be read from its request.", exception);
        }
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("An upload is read asynchronously, as its request is.");

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("An upload is read once, from its start.");

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("An upload is only read.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("An upload is only read.");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>An upload went past its limit (ADR-0006): nothing of it is kept.</summary>
public sealed class FileTooLargeException : IOException
{
    /// <summary>An upload went past <paramref name="limit"/> bytes.</summary>
    /// <param name="limit">The limit.</param>
    public FileTooLargeException(long limit)
        : base($"The upload is larger than {limit} bytes.") => Limit = limit;

    /// <summary>An upload went past its limit.</summary>
    public FileTooLargeException()
        : base("The upload is larger than its limit.")
    {
    }

    /// <summary>An upload went past its limit.</summary>
    /// <param name="message">What happened.</param>
    public FileTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>An upload went past its limit.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">What caused it.</param>
    public FileTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The limit in bytes; 0 when it was not given.</summary>
    public long Limit { get; }
}

/// <summary>
/// An upload's request could not be read (ADR-0087): it was malformed, cut short, or the server refused its size; the
/// inner exception says which. Nothing of the upload is kept.
/// </summary>
public sealed class FileUploadReadException : IOException
{
    /// <summary>An upload's request could not be read.</summary>
    public FileUploadReadException()
        : base("The upload could not be read from its request.")
    {
    }

    /// <summary>An upload's request could not be read.</summary>
    /// <param name="message">What happened.</param>
    public FileUploadReadException(string message)
        : base(message)
    {
    }

    /// <summary>An upload's request could not be read.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">What the request's stream threw.</param>
    public FileUploadReadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
