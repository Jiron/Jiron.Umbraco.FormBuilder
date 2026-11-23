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
    /// The same hint for any other check whose answer must hold until the unit ends (ADR-0038): a row that must stay
    /// (the version, field or file an insert names) or rows that must not appear (the values or entries that would keep
    /// a delete from happening). On SQL Server the check becomes a locking read that waits for a writer at the same
    /// moment instead of reading the row's last committed version, so the statement's own condition refuses, never a
    /// foreign key. Empty on SQLite, whose writers the gate serialises.
    /// </summary>
    public string LockingRead => InsertOnceHint;

    /// <summary>
    /// The hint of a queue's claim for the rows it checks (ROADMAP 26.1, ADR-0040): on SQL Server
    /// <c>WITH (UPDLOCK, ROWLOCK)</c>, update locks on the rows it reads, held until the claim commits, row by row and
    /// without the key-range locks of <see cref="LockingRead"/>, which also lock the next key beyond the range read: a
    /// queue's rows are inserted once, so there is no phantom to keep out, and the next key is another claimant's row.
    /// Empty on SQLite, whose writers the gate serialises.
    /// </summary>
    public string QueueCheck => IsSqlite ? string.Empty : " WITH (UPDLOCK, ROWLOCK)";

    /// <summary>
    /// The hint of a queue's list of candidates (ROADMAP 26.1, ADR-0040): on SQL Server
    /// <c>WITH (READPAST, READCOMMITTEDLOCK)</c>, which skips the rows another unit holds a lock on, as SQL Server
    /// documents for queue tables, instead of waiting for them or deadlocking with their claims; a row being claimed is
    /// no candidate. <c>READCOMMITTEDLOCK</c> makes the read one that takes locks whatever the database's setting: with
    /// <c>READ_COMMITTED_SNAPSHOT</c> on, which is Azure SQL's default, a read sees row versions, and SQL Server does not
    /// take <c>READPAST</c> alone there. Empty on SQLite.
    /// </summary>
    public string QueueRead => IsSqlite ? string.Empty : " WITH (READPAST, READCOMMITTEDLOCK)";

    /// <summary>
    /// The collation that compares text by its code units, to append to a column (ADR-0039): <c>COLLATE BINARY</c> on
    /// SQLite, <c>COLLATE Latin1_General_BIN2</c> on SQL Server, whose case-insensitive collation can equate texts that
    /// rules keep apart (a sharp s and <c>ss</c>). A seek uses the column's own collation first, so it stays on the index.
    /// SQLite's <c>LIKE</c> ignores collations and folds ASCII letters, which folded text no longer differs by.
    /// </summary>
    public string BinaryCollation => IsSqlite ? " COLLATE BINARY" : " COLLATE Latin1_General_BIN2";

    /// <summary>
    /// The function that gives its first argument, or its second when the first is null, evaluating each once:
    /// <c>IFNULL</c> on SQLite, <c>ISNULL</c> on SQL Server, where <c>COALESCE</c> runs a subquery twice and can see a row
    /// that went away in between as null (ADR-0039).
    /// </summary>
    public string IfNull => IsSqlite ? "IFNULL" : "ISNULL";

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
