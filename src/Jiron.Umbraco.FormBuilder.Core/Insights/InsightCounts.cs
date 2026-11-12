using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Insights;

/// <summary>One sum of this server's beacons: a form's metric and dimension on a UTC day.</summary>
/// <param name="Form">The form's key.</param>
/// <param name="Day">The UTC day the beacons came on.</param>
/// <param name="Metric">What they counted.</param>
/// <param name="Dimension">What they counted it by; empty for nothing.</param>
internal readonly record struct InsightKey(Guid Form, DateOnly Day, string Metric, string Dimension);

/// <summary>
/// This server's beacons not yet written (ADR-0211), added up in memory per form, UTC day, metric and dimension, which
/// <see cref="InsightWriter"/> writes to <c>jfbDailyStat</c>. At most <see cref="MaxPending"/> sums wait: a beacon that
/// would start another is dropped, so no flood of beacons grows the server's memory.
/// </summary>
/// <param name="time">The clock that gives a beacon its day.</param>
internal sealed class InsightCounts(TimeProvider time)
{
    /// <summary>The most sums that wait to be written.</summary>
    public const int MaxPending = 100_000;

    private readonly Lock _lock = new();
    private Dictionary<InsightKey, long> _pending = [];

    /// <summary>How many sums wait.</summary>
    public int Pending
    {
        get
        {
            lock (_lock)
            {
                return _pending.Count;
            }
        }
    }

    /// <summary>Adds one to a form's metric and dimension of today; false when it was dropped, as too many sums wait.</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="metric">The metric (<see cref="InsightMetrics"/>).</param>
    /// <param name="dimension">The dimension; empty for nothing.</param>
    public bool Add(Guid form, string metric, string dimension = "")
    {
        var key = new InsightKey(form, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime), metric, dimension);
        lock (_lock)
        {
            if (_pending.TryGetValue(key, out var sum))
            {
                _pending[key] = sum + 1;
                return true;
            }

            if (_pending.Count >= MaxPending)
            {
                return false;
            }

            _pending[key] = 1;
            return true;
        }
    }

    /// <summary>Every sum that waits, which no longer waits.</summary>
    public IReadOnlyDictionary<InsightKey, long> Take()
    {
        lock (_lock)
        {
            var taken = _pending;
            _pending = [];
            return taken;
        }
    }

    /// <summary>Puts back sums that could not be written, added to those that came meanwhile, as far as room is left.</summary>
    /// <param name="sums">The sums.</param>
    public void Restore(IEnumerable<KeyValuePair<InsightKey, long>> sums)
    {
        ArgumentNullException.ThrowIfNull(sums);
        lock (_lock)
        {
            foreach (var (key, value) in sums)
            {
                if (_pending.TryGetValue(key, out var sum))
                {
                    _pending[key] = sum + value;
                }
                else if (_pending.Count < MaxPending)
                {
                    _pending[key] = value;
                }
            }
        }
    }
}

/// <summary>
/// Writes this server's waiting beacons to <c>jfbDailyStat</c> (ADR-0211): each form's sums of a day in units of at most
/// <see cref="DailyStatRepository.MaxCountsPerCall"/>, each holding the form's row only as long as it writes. A form that is
/// gone takes its sums with it; a unit that fails puts its sums back for the next write.
/// </summary>
/// <param name="counts">The waiting sums.</param>
/// <param name="forms">The forms, whose rows the sums are written to.</param>
/// <param name="stats">The daily statistics.</param>
/// <param name="logger">Where a failed write is told.</param>
internal sealed partial class InsightWriter(InsightCounts counts, IFormRepository forms, IDailyStatRepository stats, ILogger<InsightWriter> logger)
{
    /// <summary>Writes every waiting sum; how many were written.</summary>
    /// <param name="cancellationToken">Cancels the writes; what they did not write waits again.</param>
    public async Task<int> FlushAsync(CancellationToken cancellationToken = default)
    {
        var taken = counts.Take();
        var written = 0;
        foreach (var form in taken.GroupBy(sum => sum.Key.Form))
        {
            try
            {
                if (await forms.GetAsync(form.Key, cancellationToken) is not { } row)
                {
                    continue;
                }

                foreach (var day in form.GroupBy(sum => sum.Key.Day))
                {
                    foreach (var unit in day.Chunk(DailyStatRepository.MaxCountsPerCall))
                    {
                        try
                        {
                            await stats.IncrementOnAsync(row.Id, day.Key, [.. unit.Select(sum => new DailyStatCount(sum.Key.Metric, sum.Key.Dimension, Math.Min(sum.Value, DailyStatRepository.MaxBy)))], cancellationToken);
                            written += unit.Length;
                        }
                        catch (Exception exception) when (exception is not OutOfMemoryException)
                        {
                            counts.Restore(unit);
                            if (exception is not OperationCanceledException)
                            {
                                LogWriteFailed(logger, LoggedException.Of(exception));
                            }
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The form's row could not be read: all its sums wait again.
                counts.Restore(form);
                if (exception is not OperationCanceledException)
                {
                    LogWriteFailed(logger, LoggedException.Of(exception));
                }
            }
        }

        return written;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not write insights; they wait for the next write")]
    private static partial void LogWriteFailed(ILogger logger, LoggedException exception);
}

/// <summary>
/// Writes this server's waiting beacons every <see cref="Period"/> while Umbraco runs and no migration is pending, and once
/// more when the host stops (ADR-0211), so a server that stops in order loses none and one that crashes at most a period's.
/// </summary>
/// <param name="services">Where it finds the writer and the runtime's state when the first period ends.</param>
/// <param name="time">The clock of the periods.</param>
/// <param name="logger">Where a failed write is told.</param>
internal sealed partial class InsightFlusher(IServiceProvider services, TimeProvider time, ILogger<InsightFlusher> logger) : BackgroundService
{
    /// <summary>How often the waiting beacons are written.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await FlushAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Period, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host stops; StopAsync writes what waits.
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<InsightCounts>().Pending == 0)
        {
            return;
        }

        try
        {
            if (services.GetRequiredService<IRuntimeState>().Level == RuntimeLevel.Run && !await services.GetRequiredService<IFormBuilderMigrationState>().IsPendingAsync(cancellationToken))
            {
                await services.GetRequiredService<InsightWriter>().FlushAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            LogFlushFailed(logger, LoggedException.Of(exception));
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's write of insights failed; the next period tries again")]
    private static partial void LogFlushFailed(ILogger logger, LoggedException exception);
}
