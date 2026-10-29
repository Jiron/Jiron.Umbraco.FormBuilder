using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Kestrel's cap on a request's body, which the test server does not have: each request gets the feature with Kestrel's
/// default cap, its body is counted as it is read, and a read past the cap fails as Kestrel's does. The cap the app last
/// set and the bytes the last request read of its body are kept for the tests to read.
/// </summary>
internal sealed class BodyCap : IStartupFilter
{
    /// <summary>Kestrel's default cap on a request's body.</summary>
    public const long KestrelDefault = 30_000_000;

    /// <summary>The cap the app last gave a request, or null while none did.</summary>
    public long? LastSet { get; private set; }

    /// <summary>The bytes the last request read of its body.</summary>
    public long LastRead { get; private set; }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, following) =>
        {
            LastRead = 0;
            var body = new CappedBody(context.Request.Body, this);
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(body);
            context.Request.Body = body;
            await following(context);
        });
        next(app);
    };

    // The body and its cap in one, as Kestrel's: the cap can be changed until the body is first read.
    private sealed class CappedBody(Stream inner, BodyCap owner) : Stream, IHttpMaxRequestBodySizeFeature
    {
        private long? _most = KestrelDefault;
        private long _read;
        private bool _started;

        public bool IsReadOnly => _started;

        public long? MaxRequestBodySize
        {
            get => _most;
            set
            {
                if (_started)
                {
                    throw new InvalidOperationException("The body is already being read.");
                }

                _most = value;
                owner.LastSet = value;
            }
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Counted(inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Counted(await inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Counted(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        // Past the cap a read fails, and every read after it, as Kestrel fails them.
        private int Counted(int count)
        {
            _started = true;
            _read += count;
            owner.LastRead = _read;
            return _most is { } most && _read > most
                ? throw new BadHttpRequestException("The request body is too large.", StatusCodes.Status413PayloadTooLarge)
                : count;
        }
    }
}
