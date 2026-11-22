using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// The host's own start-up seeding and whether it has finished: one per host. Each seeder is announced when the host is
/// composed (<see cref="StartupSeedingExtensions.AddStartupSeeder"/>) and reports its end once it has run, also when it
/// failed or had nothing to do. The seeding has finished when every announced seeder has reported; with none announced
/// it has finished from the start. Umbraco's start-up work waits for it on a test host
/// (<see cref="FormBuilderTestingOptions.StartupWorkAtOnce"/>), so that work never writes beside the seeding.
/// </summary>
public sealed partial class StartupSeeding
{
    private readonly object _gate = new();
    private readonly HashSet<string> _pending;
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ILogger<StartupSeeding> _logger;
    private List<Func<Task>>? _waiting = [];

    /// <summary>Creates the registry of the announced seeders.</summary>
    public StartupSeeding(IEnumerable<StartupSeeder> announced, ILogger<StartupSeeding> logger)
    {
        ArgumentNullException.ThrowIfNull(announced);
        _logger = logger;
        _pending = new HashSet<string>(announced.Select(seeder => seeder.Name), StringComparer.Ordinal);
        if (_pending.Count == 0)
        {
            _waiting = null;
            _finished.SetResult();
        }
    }

    /// <summary>Completes when every announced seeder has reported; it never fails.</summary>
    public Task Finished => _finished.Task;

    /// <summary>True once every announced seeder has reported.</summary>
    public bool IsFinished => _finished.Task.IsCompleted;

    /// <summary>
    /// Reports that the seeder of this name has run. A name that was not announced, or a second report, changes
    /// nothing. The last report runs the work registered with <see cref="WhenFinished"/>, in the order it came.
    /// </summary>
    public void Report(string name)
    {
        List<Func<Task>> waiting;
        lock (_gate)
        {
            if (!_pending.Remove(name) || _pending.Count > 0 || _waiting is null)
            {
                return;
            }

            waiting = _waiting;
            _waiting = null;
        }

        _finished.SetResult();
        foreach (var work in waiting)
        {
            _ = RunAsync(work);
        }
    }

    /// <summary>
    /// Starts the work once the seeding has finished, and at once when it has. The work runs once and is not awaited;
    /// a failure is logged.
    /// </summary>
    public void WhenFinished(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        lock (_gate)
        {
            if (_waiting is not null)
            {
                _waiting.Add(work);
                return;
            }
        }

        _ = RunAsync(work);
    }

    private async Task RunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception exception)
        {
            LogFailed(_logger, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder test host: start-up work after the seeding failed")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

/// <summary>A seeder of the host's start-up seeding, by its name; registered once for each seeder.</summary>
/// <param name="Name">The name the seeder reports with.</param>
public sealed record StartupSeeder(string Name);

/// <summary>Announces the host's start-up seeders.</summary>
public static class StartupSeedingExtensions
{
    /// <summary>
    /// Announces a seeder of the host's start-up seeding, which reports <paramref name="name"/> to
    /// <see cref="StartupSeeding"/> when it has run, and registers the registry once.
    /// </summary>
    public static IServiceCollection AddStartupSeeder(this IServiceCollection services, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        services.AddSingleton(new StartupSeeder(name));
        return services.AddStartupSeeding();
    }

    /// <summary>Registers the registry of the host's start-up seeding once, with no seeder of its own.</summary>
    public static IServiceCollection AddStartupSeeding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<StartupSeeding>();
        return services;
    }
}
