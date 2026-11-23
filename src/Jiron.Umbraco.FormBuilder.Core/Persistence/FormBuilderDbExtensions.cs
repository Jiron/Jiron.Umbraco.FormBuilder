using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// NPoco's async calls take a <see cref="Sql"/> object or an argument array without <c>params</c>. These build the
/// statement from text and arguments (numbered <c>@0</c>, <c>@1</c> and so on) and call the async overloads with the running
/// unit's cancellation token (<see cref="CurrentToken"/>), so a cancelled request stops its queries too. A time goes out
/// within what SQL Server's <c>DATETIME</c> holds (<see cref="Parameter"/>), on both databases.
/// </summary>
internal static class FormBuilderDbExtensions
{
    // Set by FormBuilderDatabase inside each unit of work: it flows into the unit's work and everything it awaits,
    // never back to the caller or into another request.
    private static readonly AsyncLocal<CancellationToken> Token = new();

    /// <summary>The cancellation token of the unit of work running on this flow.</summary>
    internal static CancellationToken CurrentToken
    {
        get => Token.Value;
        set => Token.Value = value;
    }

    /// <summary>Runs a statement; returns the affected row count.</summary>
    public static Task<int> ExecAsync(this IUmbracoDatabase db, string sql, params object?[] args) => db.ExecuteAsync(Build(db, sql, args), CurrentToken);

    /// <summary>Reads one value.</summary>
    public static Task<T> ScalarAsync<T>(this IUmbracoDatabase db, string sql, params object?[] args) => db.ExecuteScalarAsync<T>(Build(db, sql, args), CurrentToken);

    /// <summary>Reads the first row, or null.</summary>
    public static Task<T?> FirstOrNullAsync<T>(this IUmbracoDatabase db, string sql, params object?[] args)
        where T : class => db.FirstOrDefaultAsync<T>(Build(db, sql, args), CurrentToken)!;

    /// <summary>Reads every row.</summary>
    public static Task<List<T>> FetchListAsync<T>(this IUmbracoDatabase db, string sql, params object?[] args) => db.FetchAsync<T>(Build(db, sql, args), CurrentToken);

    /// <summary>Inserts a row and fills its identity.</summary>
    public static Task<object> InsertRowAsync<T>(this IUmbracoDatabase db, T row) => db.InsertAsync(row, CurrentToken);

    /// <summary>
    /// A statement's argument as it goes out: a time before 1753 or after the last moment of 9999 as that edge of SQL
    /// Server's <c>DATETIME</c>, whose parameters hold nothing outside it (ROADMAP 26.1), and anything else as it is. The
    /// tables hold only times inside it (<see cref="FormBuilderDbTime.IsStorable"/>), so a bound beyond it, such as a cursor
    /// or a range a request names, compares with every stored time as that edge does; SQLite gets the same value, so both
    /// databases answer alike. The comparison reads the value as written, whatever its kind, with no conversion between
    /// zones, as the database does: the tables' times are UTC values written as they are (<see cref="FormBuilderDbTime"/>),
    /// and the edge keeps the value's kind. Statements written otherwise carry the clock's time or none.
    /// </summary>
    internal static object? Parameter(object? value) => value switch
    {
        DateTime time when time < FirstTime => DateTime.SpecifyKind(FirstTime, time.Kind),
        DateTime time when time > LastTime => DateTime.SpecifyKind(LastTime, time.Kind),
        _ => value,
    };

    // The first and the last moment of SQL Server's DATETIME, whose steps are 1/300 s.
    private static DateTime FirstTime => new(1753, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static DateTime LastTime => new(9999, 12, 31, 23, 59, 59, 997, DateTimeKind.Unspecified);

    private static Sql<ISqlContext> Build(IUmbracoDatabase db, string sql, object?[] args) => db.SqlContext.Sql(sql, [.. args.Select(arg => Parameter(arg)!)]);
}
