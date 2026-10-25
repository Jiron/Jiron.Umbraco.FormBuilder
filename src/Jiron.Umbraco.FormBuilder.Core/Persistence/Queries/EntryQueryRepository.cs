using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>
/// Lists one form's entries for the entries screen and the exports (ADR-0003, ADR-0039): filtered, searched and
/// sorted, a page at a time, within a time budget.
/// </summary>
internal interface IEntryQueryRepository
{
    /// <summary>
    /// One page of the entries <paramref name="query"/> asks for, after <paramref name="after"/> or from the start, and
    /// how many match when <paramref name="count"/> asks.
    /// </summary>
    /// <param name="query">Which entries, in which order.</param>
    /// <param name="after">The cursor of the previous page (<see cref="EntryPage.Next"/>), or none for the first.</param>
    /// <param name="take">The page's size: 1 to <see cref="PageRequest.MaxTake"/>.</param>
    /// <param name="count">Whether to count the matches too.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <exception cref="ArgumentException">A query, cursor or page size the builder cannot answer as asked.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget (<c>query_timeout</c>).</exception>
    Task<EntryPage> ListAsync(EntryQuery query, EntryCursor? after, int take, bool count, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class EntryQueryRepository : IEntryQueryRepository
{
    private readonly FormBuilderDatabase _database;
    private readonly TimeSpan _budget;

    /// <summary>The repository, with the default budget.</summary>
    public EntryQueryRepository(FormBuilderDatabase database)
        : this(database, FormBuilderQueryBudget.Default)
    {
    }

    /// <summary>The repository with another budget, for the tests of the budget itself.</summary>
    internal EntryQueryRepository(FormBuilderDatabase database, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);
        _database = database;
        _budget = budget;
    }

    /// <inheritdoc />
    public Task<EntryPage> ListAsync(EntryQuery query, EntryCursor? after, int take, bool count, CancellationToken cancellationToken = default)
    {
        EntryQuerySql.Check(query, after, take);
        return _database.QueryAsync(db => ListAsync(db, query, after, take, count), _budget, cancellationToken);
    }

    private static async Task<EntryPage> ListAsync(IUmbracoDatabase db, EntryQuery query, EntryCursor? after, int take, bool count)
    {
        var n = new SqlNames(db);
        var sql = new EntryQuerySql(n, query, await LongTextsAsync(db, n, query));
        var limit = take + 1;
        EntryCount? total = null;
        List<EntryPositionRow> rows;
        if (count && sql.Searches && query.Sort == EntrySort.Newest && after is null)
        {
            // One pass (ADR-0003): the newest 1,001 matches hold the first page and the count.
            var newest = await FetchAsync<EntryPositionRow>(db, sql.BySubmission(after: null, EntryQuerySql.SearchCountLimit + 1));
            total = Counted(newest.Count, search: true);
            rows = [.. newest.Take(limit)];
        }
        else
        {
            rows = await PositionsAsync(db, sql, query.Sort, after, limit);
        }

        if (count && total is null)
        {
            var statement = sql.Count();
            total = Counted(await db.ScalarAsync<int>(statement.Text, statement.Args), sql.Searches);
        }

        var more = rows.Count > take;
        if (more)
        {
            rows.RemoveRange(take, rows.Count - take);
        }

        return new EntryPage(await LoadAsync(db, n, rows), more ? Cursor(query.Sort, rows[^1]) : null, total);
    }

    // The positions of the next entries: by submission, or the walk of a value sort and then its tail.
    private static async Task<List<EntryPositionRow>> PositionsAsync(IUmbracoDatabase db, EntryQuerySql sql, EntrySort sort, EntryCursor? after, int limit)
    {
        if (!sort.ByField)
        {
            return await FetchAsync<EntryPositionRow>(db, sql.BySubmission(after, limit));
        }

        var rows = new List<EntryPositionRow>(limit);
        if (after is null || after.Value is not null)
        {
            rows.AddRange(await FetchAsync<EntryPositionRow>(db, sql.Walk(after, limit)));
            if (rows.Count == limit)
            {
                return rows;
            }

            // The walk ran out on this page: the tail follows from its start.
            after = null;
        }

        rows.AddRange(await FetchAsync<EntryPositionRow>(db, sql.Tail(after, limit - rows.Count)));
        return rows;
    }

    // For each value of a text filter whose key may be cut, the stored texts of that key that equal it whole.
    private static async Task<Dictionary<(int Filter, int Value), IReadOnlyList<string>>> LongTextsAsync(IUmbracoDatabase db, SqlNames n, EntryQuery query)
    {
        var found = new Dictionary<(int Filter, int Value), IReadOnlyList<string>>();
        var read = new Dictionary<(int FieldId, string Key), List<string>>();
        foreach (var (filter, value, fieldId, key, whole) in EntryQuerySql.LongValues(query))
        {
            if (!read.TryGetValue((fieldId, key), out var texts))
            {
                texts = await FetchAsync<string>(db, EntryQuerySql.LongTexts(n, fieldId, key));
                read[(fieldId, key)] = texts;
            }

            found[(filter, value)] = [.. texts.Where(text => string.Equals(EntryTextKey.Whole(text), whole, StringComparison.Ordinal))];
        }

        return found;
    }

    // The page's entries by id, in the order of their positions; one deleted meanwhile is left out.
    private static async Task<IReadOnlyList<StoredEntry>> LoadAsync(IUmbracoDatabase db, SqlNames n, List<EntryPositionRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var loaded = (await FetchAsync<EntryDto>(db, EntryQuerySql.Load(n, [.. rows.Select(row => row.Id)]))).ToDictionary(row => row.Id);
        return [.. rows.Where(row => loaded.ContainsKey(row.Id)).Select(row => EntryRepository.FromDto(loaded[row.Id]))];
    }

    private static EntryCursor Cursor(EntrySort sort, EntryPositionRow last) => new(
        sort,
        last.Id,
        DateTime.SpecifyKind(last.SubmittedUtc, DateTimeKind.Utc),
        last.ValueId is { } valueId
            ? new EntryValuePosition(valueId, last.TextKey, last.Number, last.Date is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null)
            : null);

    private static EntryCount Counted(int matches, bool search) =>
        search && matches > EntryQuerySql.SearchCountLimit ? new EntryCount(EntryQuerySql.SearchCountLimit, More: true) : new EntryCount(matches, More: false);

    private static Task<List<T>> FetchAsync<T>(IUmbracoDatabase db, SqlStatement statement) => db.FetchListAsync<T>(statement.Text, statement.Args);
}
