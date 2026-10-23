using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>One log entry a test host wrote.</summary>
/// <param name="Category">The logger's category, the full name of its type.</param>
/// <param name="Level">The level.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception, or null.</param>
public sealed record CapturedLog(string Category, LogLevel Level, string Message, Exception? Exception);

/// <summary>
/// The log entries of a test host, for tests that check what is logged (and what never is: personal data, tokens,
/// passwords; ADR-0011). Umbraco owns the logger factory, so capturing replaces <see cref="ILogger{TCategoryName}"/>
/// itself (Tandem's lesson): <c>services.AddLogCapture()</c> in a fixture's builder set-up.
/// </summary>
public sealed class LogCapture
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    /// <summary>Every entry so far, oldest first.</summary>
    public IReadOnlyList<CapturedLog> Entries => _entries.ToList();

    /// <summary>Adds an entry.</summary>
    public void Add(CapturedLog entry) => _entries.Enqueue(entry);
}

/// <summary>A logger that writes to <see cref="LogCapture"/>; every level is on.</summary>
/// <typeparam name="T">The category.</typeparam>
public sealed class CapturingLogger<T>(LogCapture capture) : ILogger<T>
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        capture.Add(new CapturedLog(typeof(T).FullName ?? typeof(T).Name, logLevel, formatter(state, exception), exception));
    }
}

/// <summary>Registers the log capture.</summary>
public static class LogCaptureServiceCollectionExtensions
{
    /// <summary>Replaces every <see cref="ILogger{TCategoryName}"/> with one that writes to a shared <see cref="LogCapture"/>.</summary>
    public static IServiceCollection AddLogCapture(this IServiceCollection services)
    {
        services.AddSingleton<LogCapture>();
        services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
        return services;
    }
}
