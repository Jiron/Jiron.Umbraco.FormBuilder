using System.Data.Common;
using NPoco;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// Counts the parameters of every statement a database runs while it is one of its interceptors: how a test shows that
/// a list of ids goes into statements SQL Server accepts (at most 2,100 parameters), on SQLite too.
/// </summary>
/// <param name="statements">Receives the parameter count of each statement, in order.</param>
internal sealed class ParameterCounter(List<int> statements) : IExecutingInterceptor
{
    /// <inheritdoc />
    public void OnExecutingCommand(IDatabase database, DbCommand cmd) => statements.Add(cmd.Parameters.Count);

    /// <inheritdoc />
    public void OnExecutedCommand(IDatabase database, DbCommand cmd)
    {
    }
}

/// <summary>Records the text of every statement a database runs while it is one of its interceptors, in order.</summary>
internal sealed class CommandLog : IExecutingInterceptor
{
    /// <summary>The statements' texts.</summary>
    public List<string> Texts { get; } = [];

    /// <inheritdoc />
    public void OnExecutingCommand(IDatabase database, DbCommand cmd) => Texts.Add(cmd.CommandText);

    /// <inheritdoc />
    public void OnExecutedCommand(IDatabase database, DbCommand cmd)
    {
    }
}

/// <summary>
/// Records the text and the parameter values of every statement a database runs while it is one of its interceptors, in
/// order: how a test explains the statements a repository really issues.
/// </summary>
internal sealed class StatementRecorder : IExecutingInterceptor
{
    /// <summary>The statements, each with its parameter values in order.</summary>
    public List<(string Text, object?[] Values)> Statements { get; } = [];

    /// <inheritdoc />
    public void OnExecutingCommand(IDatabase database, DbCommand cmd) =>
        Statements.Add((cmd.CommandText, [.. cmd.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value is DBNull ? null : parameter.Value)]));

    /// <inheritdoc />
    public void OnExecutedCommand(IDatabase database, DbCommand cmd)
    {
    }
}

/// <summary>
/// Changes the text of the statements that contain <paramref name="from"/> before they run: how a test lifts a
/// statement's own condition, to see what the database does with a writer that got past it at the same moment (on SQL
/// Server, two servers), or what the repository does when its update finds the row moved on.
/// </summary>
/// <param name="from">The text to replace, the first time it occurs in a statement.</param>
/// <param name="to">What replaces it.</param>
internal sealed class CommandRewriter(string from, string to) : IExecutingInterceptor
{
    /// <summary>How many statements were changed.</summary>
    public int Rewritten { get; private set; }

    /// <inheritdoc />
    public void OnExecutingCommand(IDatabase database, DbCommand cmd)
    {
        var at = cmd.CommandText.IndexOf(from, StringComparison.Ordinal);
        if (at >= 0)
        {
            cmd.CommandText = string.Concat(cmd.CommandText.AsSpan(0, at), to, cmd.CommandText.AsSpan(at + from.Length));
            Rewritten++;
        }
    }

    /// <inheritdoc />
    public void OnExecutedCommand(IDatabase database, DbCommand cmd)
    {
    }
}
