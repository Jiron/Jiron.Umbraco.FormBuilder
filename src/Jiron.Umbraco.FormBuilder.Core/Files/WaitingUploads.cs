using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The bytes of uploads that wait for an entry, against the host's cap (<c>Files:MaxWaitingBytes</c>, ADR-0148). The table
/// holds the count, so every server of a site sees the same one; each reads it every few seconds, and adds what it stores
/// itself in between, so a burst on one server counts at once.
/// </summary>
internal sealed partial class WaitingUploads(IEntryRepository entries, IOptions<FormBuilderOptions> options, TimeProvider time, ILogger<WaitingUploads> logger)
{
    /// <summary>How long one reading of the table holds.</summary>
    public static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(10);

    private long _bytes;
    private long _readAt;

    /// <summary>Whether another upload may be stored; without a cap always, and the table is not read.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> HasRoomAsync(CancellationToken cancellationToken)
    {
        var cap = options.Value.Files.MaxWaitingBytes;
        if (cap == 0)
        {
            return true;
        }

        // One caller reads; the others go on with the count they have, so a flood costs one query for each period.
        var now = time.GetUtcNow().UtcTicks;
        var last = Interlocked.Read(ref _readAt);
        if (now - last >= ReadEvery.Ticks && Interlocked.CompareExchange(ref _readAt, now, last) == last)
        {
            var bytes = await entries.SumWaitingBytesAsync(cancellationToken);
            Interlocked.Exchange(ref _bytes, bytes);
            if (bytes >= cap)
            {
                LogFull(logger, bytes, cap);
            }
        }

        return Interlocked.Read(ref _bytes) < cap;
    }

    /// <summary>Counts an upload just stored until the next reading.</summary>
    /// <param name="bytes">Its size.</param>
    public void Add(long bytes) => Interlocked.Add(ref _bytes, bytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder refuses uploads for now: {Bytes} bytes wait for an entry, and Files:MaxWaitingBytes is {Cap}")]
    private static partial void LogFull(ILogger logger, long bytes, long cap);
}
