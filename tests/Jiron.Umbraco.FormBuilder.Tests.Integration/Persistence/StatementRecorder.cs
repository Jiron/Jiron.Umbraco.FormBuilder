using System.Reflection;
using NPoco;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// An NPoco database that runs nothing: it records the SQL of every <c>Execute</c> and <c>ExecuteAsync</c> call and
/// answers 0, so a migration's statements can be rendered without a server (ADR-0033). Any other call fails, so a
/// statement the recorder would miss shows up as an error instead.
/// </summary>
public class StatementRecorder : DispatchProxy
{
    private readonly List<string> _statements = [];

    /// <summary>The statements in the order they were issued.</summary>
    public IReadOnlyList<string> Statements => _statements;

    /// <summary>A database that records into a new recorder.</summary>
    public static (IDatabase Database, StatementRecorder Recorder) Create()
    {
        var database = Create<IDatabase, StatementRecorder>();
        return (database, (StatementRecorder)(object)database);
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name is not ("Execute" or "ExecuteAsync") || args is not [Sql sql, ..])
        {
            throw new NotSupportedException($"The recorder only records statements; {targetMethod.Name} was called.");
        }

        _statements.Add(sql.SQL);
        return targetMethod.ReturnType == typeof(Task<int>) ? Task.FromResult(0) : 0;
    }
}
