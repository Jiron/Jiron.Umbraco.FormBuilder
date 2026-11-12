using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The availability of every field type of the registry (ADR-0057), for the designer's palette. The types' checks run at
/// once, each on the thread pool and within <see cref="Limit"/>, so a slow or blocking check of a site's type costs the
/// palette that long at most. A check that takes longer, throws or answers null counts as not configured, with
/// <see cref="CheckFailed"/> as its reason, and is logged with the type's alias; the request's cancellation is passed on.
/// </summary>
internal sealed partial class FieldTypeAvailabilityService(FieldTypeRegistry registry, TimeProvider time, ILogger<FieldTypeAvailabilityService> logger)
{
    /// <summary>The longest a type's check may take.</summary>
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    /// <summary>The reason of a type whose check took too long, threw or answered nothing: its setup could not be checked.</summary>
    public const string CheckFailed = "#jfbFieldTypeAvailability_checkFailed";

    /// <summary>The availability of each type of the registry, in the registry's order.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<ImmutableArray<FieldTypeAvailability>> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return [.. await Task.WhenAll(registry.Types.Select(type => CheckAsync(type, cancellationToken)))];
    }

    private async Task<FieldTypeAvailability> CheckAsync(IFormBuilderFieldType type, CancellationToken cancellationToken)
    {
        var alias = type.Declaration.Alias;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Read now: a check the thread pool starts only after the limit gets the token cancelled, while the source that
        // gives it is disposed by then and would throw.
        var token = stop.Token;
        var check = Task.Run(() => type.CheckAvailabilityAsync(token).AsTask(), CancellationToken.None);
        try
        {
            if (await check.WaitAsync(Limit, time, cancellationToken) is { } availability)
            {
                return availability;
            }

            LogNoAnswer(logger, alias);
        }
        catch (TimeoutException)
        {
            await stop.CancelAsync();
            LogTooSlow(logger, alias, Limit.TotalSeconds);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            LogFailed(logger, LoggedException.Of(exception), alias);
        }
        finally
        {
            // A check that goes on without us (too slow, or the request was cancelled) is observed, whatever it ends with.
            if (!check.IsCompleted)
            {
                _ = check.ContinueWith(static ended => _ = ended.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        return FieldTypeAvailability.NotConfigured(CheckFailed);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The field type '{Alias}' answered nothing when Jiron Form Builder asked whether it is configured; the designer shows it as not configured")]
    private static partial void LogNoAnswer(ILogger logger, string alias);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The field type '{Alias}' took more than {Seconds} seconds to say whether it is configured; the designer shows it as not configured")]
    private static partial void LogTooSlow(ILogger logger, string alias, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "The field type '{Alias}' failed to say whether it is configured; the designer shows it as not configured")]
    private static partial void LogFailed(ILogger logger, LoggedException exception, string alias);
}
