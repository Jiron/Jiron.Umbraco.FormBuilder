using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// Starts Umbraco's hosted service of the distributed jobs once the test host's own start-up seeding has finished
/// (<see cref="StartupSeeding"/>), and stops it with the host (ROADMAP 24.1). With no delay before its jobs, Umbraco's
/// service registers them at once and runs the first five seconds later; beside a seeding that is still writing, that
/// job's write and the seeding's waited for each other on SQLite and the host hung at start-up.
/// <see cref="StartupMarkerComposer"/> puts this service in place of Umbraco's when
/// <c>JironFormBuilderTesting:StartupWorkAtOnce</c> is on. It owns the one instance of Umbraco's service, made only
/// when the seeding has finished, and uses it only as an <see cref="IHostedService"/>; a host that stops before the
/// seeding has finished never starts it.
/// </summary>
public sealed partial class UmbracoJobsAfterSeeding : IHostedService, IDisposable
{
    private readonly StartupSeeding _seeding;
    private readonly Func<IHostedService> _createUmbracos;
    private readonly ILogger<UmbracoJobsAfterSeeding> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task _run = Task.CompletedTask;
    private IHostedService? _umbracos;

    /// <summary>Creates the service; <paramref name="createUmbracos"/> makes Umbraco's service when the seeding has finished.</summary>
    public UmbracoJobsAfterSeeding(StartupSeeding seeding, Func<IHostedService> createUmbracos, ILogger<UmbracoJobsAfterSeeding> logger)
    {
        _seeding = seeding;
        _createUmbracos = createUmbracos;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // The host's start does not wait for the seeding, which runs only once the host has started.
        _run = RunAsync(_stopping.Token);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await _run.WaitAsync(cancellationToken);
        if (_umbracos is { } umbracos)
        {
            await umbracos.StopAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        (_umbracos as IDisposable)?.Dispose();
        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken stopping)
    {
        try
        {
            await _seeding.Finished.WaitAsync(stopping);
            var umbracos = _createUmbracos();
            _umbracos = umbracos;
            await umbracos.StartAsync(stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The host stopped first.
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(_logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder test host: Umbraco's distributed jobs did not start after the seeding")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
