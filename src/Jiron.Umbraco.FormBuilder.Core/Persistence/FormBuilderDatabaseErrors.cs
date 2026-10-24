using System.Data.Common;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// What a database error means for Jiron Form Builder (ADR-0007, ADR-0036): whether another attempt may get past it,
/// whether it is a unique key, and how to name it in a log without its message. Core references neither database
/// client, so errors are recognised by their type's name and their error number.
/// </summary>
internal static class FormBuilderDatabaseErrors
{
    /// <summary>
    /// SQLite's "busy" and "locked": the statement could not take the lock, nothing of it was written, and the scope
    /// rolls the rest of the transaction back.
    /// </summary>
    private static readonly int[] SqliteBusy = [5, 6];

    /// <summary>
    /// SQL Server's deadlock victim (1205) and lock timeout (1222): the server rolled the transaction back, or the scope
    /// does when the error ends the work.
    /// </summary>
    private static readonly int[] SqlServerLocks = [1205, 1222];

    /// <summary>
    /// SQL Server's lost connections and Azure SQL's busy or moving databases, and the command timeout (-2). A read may
    /// run again; a write may not, because the connection may have dropped while its commit was on the way.
    /// </summary>
    private static readonly int[] SqlServerConnections = [-2, 20, 64, 233, 4060, 10053, 10054, 10060, 10928, 10929, 40143, 40197, 40501, 40613, 49918, 49919, 49920];

    /// <summary>SQLite's constraint error (19); its extended code says which constraint.</summary>
    private const int SqliteConstraint = 19;

    /// <summary>SQLite's extended codes for a unique index (2067) and a primary key (1555).</summary>
    private static readonly int[] SqliteUnique = [2067, 1555];

    /// <summary>SQL Server's duplicate key in a unique index (2601) and in a unique or primary key constraint (2627).</summary>
    private static readonly int[] SqlServerUnique = [2601, 2627];

    /// <summary>
    /// True when <paramref name="exception"/> (or an exception inside it) is a database error that another attempt of
    /// the whole unit of work may get past: lock conflicts always, lost connections and timeouts only for reads.
    /// </summary>
    public static bool IsTransient(Exception exception, bool write)
    {
        // A read stopped at its budget would only run into the budget again (ADR-0039).
        if (exception is FormBuilderQueryTimeoutException)
        {
            return false;
        }

        foreach (var database in DatabaseErrors(exception))
        {
            switch (database.GetType().Name)
            {
                case "SqliteException" when Code(database, "SqliteErrorCode") is { } sqlite:
                    return SqliteBusy.Contains(sqlite);
                case "SqlException" when Code(database, "Number") is { } number:
                    return SqlServerLocks.Contains(number) || (!write && SqlServerConnections.Contains(number));
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="exception"/> (or an exception inside it) refused a row that a unique key already holds.</summary>
    public static bool IsUniqueViolation(Exception exception)
    {
        foreach (var database in DatabaseErrors(exception))
        {
            switch (database.GetType().Name)
            {
                case "SqliteException" when Code(database, "SqliteErrorCode") == SqliteConstraint:
                    return Code(database, "SqliteExtendedErrorCode") is not { } extended || SqliteUnique.Contains(extended);
                case "SqlException" when Code(database, "Number") is { } number:
                    return SqlServerUnique.Contains(number);
            }
        }

        return false;
    }

    /// <summary>
    /// The error's type and code, for a log: never its message, which may quote the values of the statement.
    /// </summary>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        foreach (var database in DatabaseErrors(exception))
        {
            var code = Code(database, "SqliteExtendedErrorCode") ?? Code(database, "SqliteErrorCode") ?? Code(database, "Number");
            return code is { } number ? $"{database.GetType().Name} {number}" : database.GetType().Name;
        }

        return exception.GetType().Name;
    }

    private static IEnumerable<DbException> DatabaseErrors(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException database)
            {
                yield return database;
            }
        }
    }

    private static int? Code(DbException exception, string property) =>
        exception.GetType().GetProperty(property)?.GetValue(exception) is int code ? code : null;
}
