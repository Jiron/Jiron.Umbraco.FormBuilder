using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// A part of the retention job (ADR-0045): it removes what its rules allow in bounded, idempotent units until it is done
/// or the deadline passes, and leaves the rest to the next run.
/// </summary>
internal interface IFormBuilderRetentionPart
{
    /// <summary>The part's name, for the log.</summary>
    string Name { get; }

    /// <summary>Runs the part until it is done or <paramref name="deadline"/> has passed.</summary>
    Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken);
}

/// <summary>
/// The retention job (D8, ADR-0007, ADR-0045): one server of the cluster runs it every hour. It runs only while Umbraco
/// runs and no Jiron Form Builder migration is pending, then the retention parts in the order they were registered,
/// within <see cref="Budget"/>, because Umbraco's runner holds every other distributed job of the server while it waits.
/// A part that fails is logged, and the next one runs.
/// </summary>
internal sealed partial class FormBuilderRetentionJob(
    IRuntimeState runtimeState,
    IFormBuilderMigrationState migrationState,
    IEnumerable<IFormBuilderRetentionPart> parts,
    TimeProvider timeProvider,
    ILogger<FormBuilderRetentionJob> logger)
    : IDistributedBackgroundJob
{
    /// <summary>The job's name in Umbraco's job table.</summary>
    public const string JobName = "Jiron.FormBuilder.Retention";

    /// <summary>How long one run works at most, so that it returns within ten seconds (ADR-0007).</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    private readonly IReadOnlyList<IFormBuilderRetentionPart> _parts = parts.ToList();

    /// <inheritdoc />
    public string Name => JobName;

    /// <inheritdoc />
    public TimeSpan Period => TimeSpan.FromHours(1);

    /// <inheritdoc />
    public Task ExecuteAsync() => ExecuteAsync(CancellationToken.None);

    /// <inheritdoc />
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        if (runtimeState.Level != RuntimeLevel.Run || await migrationState.IsPendingAsync(cancellationToken))
        {
            return;
        }

        var deadline = timeProvider.GetUtcNow() + Budget;
        foreach (var part in _parts)
        {
            if (timeProvider.GetUtcNow() >= deadline)
            {
                return;
            }

            try
            {
                await part.RunAsync(deadline, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogPartFailed(logger, part.Name, Period, LoggedException.Of(exception));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder's retention of {Part} failed; it runs again in {Period}")]
    private static partial void LogPartFailed(ILogger logger, string part, TimeSpan period, Exception exception);
}
