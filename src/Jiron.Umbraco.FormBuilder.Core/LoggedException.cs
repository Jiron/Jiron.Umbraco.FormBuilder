using System.Data.Common;
using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>
/// An exception as a background loop logs it (ADR-0226): its type, the codes a database gives, and where it was thrown,
/// but never its message, which may quote a value it could not store, such as a truncated answer or a duplicate key. Its
/// inner exceptions are logged the same way.
/// </summary>
internal sealed class LoggedException : Exception
{
    private readonly string? _stackTrace;

    public LoggedException()
    {
    }

    public LoggedException(string message)
        : base(message)
    {
    }

    public LoggedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private LoggedException(Exception exception)
        : base(Describe(exception), exception.InnerException is { } inner ? Of(inner) : null)
    {
        _stackTrace = exception.StackTrace;
        HResult = exception.HResult;
    }

    /// <inheritdoc />
    public override string? StackTrace => _stackTrace ?? base.StackTrace;

    /// <summary>What to log in place of an exception: the exception as this class holds it.</summary>
    /// <param name="exception">The exception caught.</param>
    public static Exception Of(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception as LoggedException ?? new LoggedException(exception);
    }

    private static string Describe(Exception exception) => exception is DbException database
        ? string.Create(CultureInfo.InvariantCulture, $"{exception.GetType().FullName} (error {database.ErrorCode}, SQL state {database.SqlState ?? "none"}); its message is not logged")
        : $"{exception.GetType().FullName}; its message is not logged";
}
