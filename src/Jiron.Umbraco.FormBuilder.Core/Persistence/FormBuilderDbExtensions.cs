using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// NPoco's async calls take a <see cref="Sql"/> object or an argument array without <c>params</c>. These build the
/// statement from text and arguments (numbered <c>@0</c>, <c>@1</c> and so on) and call the async overloads with the running
/// unit's cancellation token (<see cref="CurrentToken"/>), so a cancelled request stops its queries too.
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

    private static Sql<ISqlContext> Build(IUmbracoDatabase db, string sql, object?[] args) => db.SqlContext.Sql(sql, args!);
}
