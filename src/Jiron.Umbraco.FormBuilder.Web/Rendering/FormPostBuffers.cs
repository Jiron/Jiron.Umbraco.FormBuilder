using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The bytes posts without JavaScript hold in this server's temporary files while they are read (16.1's security review).
/// A file is written there before the surface action checks anything else, so the bytes are counted as they arrive: each
/// client may hold its share of what may wait for an entry (<c>Files:MaxWaitingBytesPerAddress</c>) and all of them
/// together what may wait (<c>Files:MaxWaitingBytes</c>). As with what waits (ADR-0148), a read goes on while the count is
/// below the limit, so it passes it by one read at most; then the file is left out as at a full site. The files are this
/// server's own, so the count is too, and it ends with the request.
/// </summary>
/// <param name="options">The host configuration with the limits; 0 for either leaves it out.</param>
internal sealed class FormPostBuffers(IOptions<FormBuilderOptions> options)
{
    private readonly Dictionary<string, long> _clients = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private long _total;

    /// <summary>The bytes held now, by every client together.</summary>
    public long Held
    {
        get
        {
            lock (_lock)
            {
                return _total;
            }
        }
    }

    /// <summary>What one request holds for its client; disposing it gives every byte back.</summary>
    /// <param name="client">The client: its server key's name or its address, as the rate limits name it.</param>
    public Holding Start(string client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new Holding(this, client);
    }

    // Counts bytes the client's request has read, while neither its count nor the server's has reached its limit.
    private bool TryCount(string client, long bytes)
    {
        var files = options.Value.Files;
        lock (_lock)
        {
            var own = _clients.GetValueOrDefault(client);
            if ((files.MaxWaitingBytesPerAddress > 0 && own >= files.MaxWaitingBytesPerAddress)
                || (files.MaxWaitingBytes > 0 && _total >= files.MaxWaitingBytes))
            {
                return false;
            }

            _clients[client] = own + bytes;
            _total += bytes;
            return true;
        }
    }

    private void Release(string client, long bytes)
    {
        lock (_lock)
        {
            var left = _clients.GetValueOrDefault(client) - bytes;
            if (left > 0)
            {
                _clients[client] = left;
            }
            else
            {
                // A client that holds nothing has no entry, so the count never grows with addresses that came once.
                _clients.Remove(client);
            }

            _total -= bytes;
        }
    }

    /// <summary>What one request holds for its client.</summary>
    internal sealed class Holding : IDisposable
    {
        private readonly FormPostBuffers _buffers;
        private readonly string _client;
        private long _held;
        private bool _released;

        internal Holding(FormPostBuffers buffers, string client)
        {
            _buffers = buffers;
            _client = client;
        }

        /// <summary>Counts bytes just read; false, counting nothing, once the client or the server holds its limit.</summary>
        /// <param name="bytes">The bytes.</param>
        public bool TryCount(long bytes)
        {
            ObjectDisposedException.ThrowIf(_released, this);
            if (bytes <= 0)
            {
                return true;
            }

            if (!_buffers.TryCount(_client, bytes))
            {
                return false;
            }

            _held += bytes;
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                _buffers.Release(_client, _held);
            }
        }
    }
}

/// <summary>
/// A part's body that counts what it reads against its request's holding, and ends early once that is refused, saying so
/// (<see cref="Refused"/>): the reader that drains it then holds a cut file, which is let go.
/// </summary>
/// <param name="inner">The part's body.</param>
/// <param name="holding">The request's holding.</param>
internal sealed class HeldReadStream(Stream inner, FormPostBuffers.Holding holding) : Stream
{
    /// <summary>Whether a read was refused, so the body ended before the part did.</summary>
    public bool Refused { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("A post's file is read asynchronously.");

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Refused)
        {
            return 0;
        }

        var read = await inner.ReadAsync(buffer, cancellationToken);
        if (holding.TryCount(read))
        {
            return read;
        }

        Refused = true;
        return 0;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
