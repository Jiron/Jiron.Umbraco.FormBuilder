using System.Globalization;
using System.Threading.Channels;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// Published inside a unit of work that queued something to send. Umbraco delivers it after the outermost scope commits
/// and never after a rollback (ADR-0007), so a worker it wakes finds the rows.
/// </summary>
internal sealed class OutboxQueuedNotification : INotification;

/// <summary>Wakes this server's worker once what was queued is committed (ADR-0007).</summary>
/// <param name="wake">The worker's wake.</param>
internal sealed class OutboxQueuedHandler(OutboxWake wake) : INotificationHandler<OutboxQueuedNotification>
{
    /// <inheritdoc />
    public void Handle(OutboxQueuedNotification notification) => wake.Wake();
}

/// <summary>
/// What wakes this server's worker (ADR-0007): a bounded channel of one that drops further writes, so wakes add up to one.
/// While sending is paused a wake does nothing, and turning the pause off wakes the worker.
/// </summary>
internal sealed class OutboxWake : IDisposable
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly IOptionsMonitor<FormBuilderOptions> _options;
    private readonly IDisposable? _changes;
    private volatile bool _lastPaused;

    /// <summary>A wake that follows the host's pause switch.</summary>
    /// <param name="options">The host configuration, whose pause switch takes effect at once.</param>
    public OutboxWake(IOptionsMonitor<FormBuilderOptions> options)
    {
        _options = options;
        _changes = options.OnChange(_ => Wake());
    }

    /// <summary>
    /// Whether sending is paused (section 4.4). While a change of the configuration does not pass the options' validation, the
    /// last good answer holds: this runs after a submission's commit, and a site's typo elsewhere must not fail the visitor.
    /// </summary>
    public bool Paused
    {
        get
        {
            try
            {
                return _lastPaused = _options.CurrentValue.Actions.Paused;
            }
            catch (OptionsValidationException)
            {
                return _lastPaused;
            }
        }
    }

    /// <summary>Asks for a pass; nothing while sending is paused.</summary>
    public void Wake()
    {
        if (!Paused)
        {
            _channel.Writer.TryWrite(true);
        }
    }

    /// <summary>Asks for a pass at the end of a wait; the distributed job finds the work too if this server stops first.</summary>
    /// <param name="wait">How long.</param>
    /// <param name="time">The clock.</param>
    public void WakeAfter(TimeSpan wait, TimeProvider time) =>
        _ = Task.Delay(wait, time).ContinueWith(_ => Wake(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>Waits for the next wake.</summary>
    /// <param name="cancellationToken">Cancelled when the host stops.</param>
    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _changes?.Dispose();
}

/// <summary>Something a pass of the worker sends: the action runs, then the mail (ADR-0007).</summary>
internal interface IOutboxQueue
{
    /// <summary>The queue's name, for the log.</summary>
    string Name { get; }

    /// <summary>
    /// One pass over the queue: it marks expired leases and sends what is due, up to its limit, claiming nothing once the
    /// deadline has passed or sending is paused. Whether it left work due.
    /// </summary>
    /// <param name="claimant">The name its claims carry: the server and its worker.</param>
    /// <param name="deadline">When it stops claiming.</param>
    /// <param name="stopping">Cancelled when the host stops.</param>
    Task<bool> PassAsync(string claimant, DateTimeOffset deadline, CancellationToken stopping);
}

/// <summary>
/// One pass of the worker (ADR-0007): the queues in their order, each within <see cref="Budget"/>. It does nothing while sending
/// is paused, while Umbraco does not run or while a migration is pending, and a pass that left work due wakes the worker
/// again. A queue that fails is logged, and the next one runs.
/// </summary>
internal sealed partial class OutboxPass(
    IEnumerable<IOutboxQueue> queues,
    OutboxWake wake,
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    TimeProvider time,
    ILogger<OutboxPass> logger)
{
    /// <summary>
    /// How long a queue of a pass goes on claiming. Each queue has its own time (ADR-0252): runs that wait on a host that never
    /// answers use up theirs, and the mails after them still leave.
    /// </summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(2);

    private readonly string _claimant = $"{Environment.MachineName}/{Guid.NewGuid():N}";

    /// <summary>Runs one pass.</summary>
    /// <param name="stopping">Cancelled when the host stops.</param>
    public async Task RunAsync(CancellationToken stopping)
    {
        if (wake.Paused || runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(stopping))
        {
            return;
        }

        var left = false;
        foreach (var queue in queues)
        {
            if (stopping.IsCancellationRequested)
            {
                break;
            }

            try
            {
                left |= await queue.PassAsync(_claimant, time.GetUtcNow() + Budget, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogQueueFailed(logger, queue.Name, LoggedException.Of(exception));
            }
        }

        if (left)
        {
            wake.Wake();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass over Jiron Form Builder's queue of {Queue} failed; the next wake tries again")]
    private static partial void LogQueueFailed(ILogger logger, string queue, LoggedException exception);
}

/// <summary>
/// This server's worker (ADR-0007): it waits for a wake and runs a pass, so passes never overlap on one server, and servers
/// share the work through their claims. The commit of what was queued wakes it within milliseconds, and the job only makes
/// sure that retries as they fall due, expired leases and what a stopped server left behind are found.
/// </summary>
/// <remarks>
/// It asks for its pass when the first wake comes. The host creates every hosted service before it starts any, and a pass needs the
/// host configuration, which the start-up checks must be the first to refuse, with all that is wrong with it.
/// </remarks>
internal sealed partial class OutboxWorker(OutboxWake wake, IServiceProvider services, ILogger<OutboxWorker> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await wake.WaitAsync(stoppingToken);
                try
                {
                    await services.GetRequiredService<OutboxPass>().RunAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    LogPassFailed(logger, LoggedException.Of(exception));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host stops; an attempt under way records that it was cut off.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A pass of Jiron Form Builder's outbox failed; the next wake tries again")]
    private static partial void LogPassFailed(ILogger logger, LoggedException exception);
}

/// <summary>
/// The safety net of the outbox (D8, ADR-0007): one server of the cluster runs it every minute. It only wakes its server's
/// worker and returns, because Umbraco's runner holds every other distributed job of the server while it waits.
/// </summary>
internal sealed class FormBuilderOutboxJob(OutboxWake wake, JobHeartbeat heartbeat, IRuntimeState runtimeState, IFormBuilderMigrationState migrationState) : IDistributedBackgroundJob
{
    /// <summary>The job's name in Umbraco's job table.</summary>
    public const string JobName = "Jiron.FormBuilder.Outbox";

    /// <inheritdoc />
    public string Name => JobName;

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (runtimeState.Level == RuntimeLevel.Run && !await migrationState.IsPendingAsync(cancellationToken))
        {
            wake.Wake();
            await heartbeat.BeatAsync(cancellationToken);
        }
    }
}

/// <summary>
/// When the jobs last ran (ADR-0165), which the health check reads: one server runs them, and any server may be asked. The
/// row changes at most once in <see cref="BeatEvery"/>, as a job that runs every minute should not write every minute.
/// </summary>
/// <param name="values">The key-value store.</param>
/// <param name="time">The clock.</param>
internal sealed class JobHeartbeat(IKeyValueRepository values, TimeProvider time)
{
    /// <summary>The longest a server waits before it writes the time again.</summary>
    public static readonly TimeSpan BeatEvery = TimeSpan.FromMinutes(5);

    /// <summary>The key of the row.</summary>
    public const string Key = "jfb.jobs.last-run";

    private long _lastBeat;

    /// <summary>When this server started to wait for the jobs, which counts as their last run until they have one.</summary>
    public DateTimeOffset Started { get; } = time.GetUtcNow();

    /// <summary>Writes the time, unless this server did a short while ago.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task BeatAsync(CancellationToken cancellationToken = default)
    {
        var now = time.GetUtcNow();
        if (now.UtcTicks - Interlocked.Read(ref _lastBeat) >= BeatEvery.Ticks)
        {
            await values.SetAsync(Key, now.ToString("O", CultureInfo.InvariantCulture), cancellationToken);
            Interlocked.Exchange(ref _lastBeat, now.UtcTicks);
        }
    }

    /// <summary>
    /// When the jobs last ran on any server, or when this server started if that is later: a site that was idle or down has missed
    /// nothing until a run has had the time to come.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<DateTimeOffset> LastRunAsync(CancellationToken cancellationToken = default) =>
        DateTimeOffset.TryParse(await values.GetAsync(Key, cancellationToken), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last) && last > Started ? last : Started;
}
