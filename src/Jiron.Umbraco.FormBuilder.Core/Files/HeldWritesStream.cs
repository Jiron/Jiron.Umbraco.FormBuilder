namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// A response as a ZIP writer needs it: the writer writes some of its small records synchronously even in its asynchronous calls,
/// which a request may not. Those wait in memory for the next asynchronous write, in their place, so nothing blocks a thread and
/// the files themselves still stream. It only writes, and its owner calls <see cref="FlushAsync(CancellationToken)"/> last.
/// </summary>
/// <param name="inner">The response, which the owner closes.</param>
internal sealed class HeldWritesStream(Stream inner) : Stream
{
    private readonly MemoryStream _held = new();

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException("A response has no length yet.");

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException("A response only goes forward.");
        set => throw new NotSupportedException("A response only goes forward.");
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => _held.Write(buffer, offset, count);

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => _held.Write(buffer);

    /// <inheritdoc />
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await SendHeldAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await SendHeldAsync(cancellationToken);
        await inner.FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("A response is only written.");

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("A response only goes forward.");

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException("A response only goes forward.");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _held.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task SendHeldAsync(CancellationToken cancellationToken)
    {
        if (_held.Length > 0)
        {
            await inner.WriteAsync(_held.GetBuffer().AsMemory(0, (int)_held.Length), cancellationToken);
            _held.SetLength(0);
        }
    }
}
