using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>Quotes table and column names for the current database, so raw SQL works on SQLite and SQL Server.</summary>
internal readonly struct SqlNames(ISqlSyntaxProvider syntax)
{
    /// <summary>The character that escapes a LIKE wildcard; use it with <see cref="EscapeClause"/>.</summary>
    public const char EscapeCharacter = '\\';

    /// <summary>The ESCAPE clause that goes with <see cref="Escape"/>.</summary>
    public const string EscapeClause = @"ESCAPE '\'";

    /// <summary>The names of the given database.</summary>
    public SqlNames(IUmbracoDatabase database)
        : this(database.SqlContext.SqlSyntax)
    {
    }

    /// <summary>Whether the database is SQLite.</summary>
    public bool IsSqlite => syntax.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>A quoted table name.</summary>
    public string T(string table) => syntax.GetQuotedTableName(table);

    /// <summary>A quoted column name.</summary>
    public string C(string column) => syntax.GetQuotedColumnName(column);

    /// <summary>
    /// The hint for the <c>NOT EXISTS</c> check of an insert that must happen once (ADR-0036): on SQL Server
    /// <c>WITH (UPDLOCK, HOLDLOCK)</c>, which locks the checked range until the transaction ends, so a concurrent
    /// insert waits and then finds the row, also under read-committed snapshot; empty on SQLite, whose writers the gate
    /// serialises.
    /// </summary>
    public string InsertOnceHint => IsSqlite ? string.Empty : " WITH (UPDLOCK, HOLDLOCK)";

    /// <summary>
    /// The start of a statement with recursive common table expressions: SQLite wants <c>WITH RECURSIVE</c>, SQL Server
    /// knows only <c>WITH</c>.
    /// </summary>
    public string WithRecursive => IsSqlite ? "WITH RECURSIVE" : "WITH";

    /// <summary>
    /// The paging clause after an <c>ORDER BY</c>: <c>LIMIT n OFFSET m</c> on SQLite, <c>OFFSET m ROWS FETCH NEXT n ROWS
    /// ONLY</c> on SQL Server. Both numbers are written as literals, after a check, so they never need a parameter.
    /// </summary>
    public string Page(long skip, int take)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return IsSqlite
            ? FormattableString.Invariant($"LIMIT {take} OFFSET {skip}")
            : FormattableString.Invariant($"OFFSET {skip} ROWS FETCH NEXT {take} ROWS ONLY");
    }

    /// <summary>
    /// <paramref name="text"/> with the wildcard characters of LIKE escaped (also SQL Server's <c>[</c>); use it with
    /// <see cref="EscapeClause"/>.
    /// </summary>
    public static string Escape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var escape = EscapeCharacter.ToString();
        return text.Replace(escape, escape + escape, StringComparison.Ordinal)
            .Replace("%", escape + "%", StringComparison.Ordinal)
            .Replace("_", escape + "_", StringComparison.Ordinal)
            .Replace("[", escape + "[", StringComparison.Ordinal);
    }
}
