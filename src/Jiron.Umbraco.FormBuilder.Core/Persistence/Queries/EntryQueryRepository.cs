using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>
/// Lists one form's entries for the entries screen and the exports (ADR-0003, ADR-0039): filtered, searched and
/// sorted, a page at a time, within a time budget; and summarises their answers for the Insights view (ADR-0212).
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

    /// <summary>
    /// The answers of the entries <paramref name="query"/> holds, summarised in the database (ADR-0212), in one read within
    /// the time budget: how many entries match, and for the fields <paramref name="fields"/> names what each is summarised
    /// by, with the newest <paramref name="texts"/> answers of each free-text field.
    /// </summary>
    /// <param name="query">Which entries; its sort is not read.</param>
    /// <param name="fields">The fields, by what each is summarised by.</param>
    /// <param name="texts">How many free-text answers each field's first page holds: 1 to <see cref="PageRequest.MaxTake"/>.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <exception cref="ArgumentException">A query the builder cannot answer as asked.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget (<c>query_timeout</c>).</exception>
    Task<AnswerSummaryRows> SummarizeAsync(EntryQuery query, AnswerSummaryFields fields, int texts, CancellationToken cancellationToken = default);

    /// <summary>A free-text field's answers in the entries <paramref name="query"/> holds, after <paramref name="after"/>, newest entry first.</summary>
    /// <param name="query">Which entries; its sort is not read.</param>
    /// <param name="fieldId">The field.</param>
    /// <param name="after">Where the page before ended; none for the first page.</param>
    /// <param name="take">How many: 1 to <see cref="PageRequest.MaxTake"/>.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <exception cref="ArgumentException">A query the builder cannot answer as asked.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget (<c>query_timeout</c>).</exception>
    Task<AnswerTextPage> TextsAsync(EntryQuery query, int fieldId, AnswerTextCursor? after, int take, CancellationToken cancellationToken = default);
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
        return BudgetedAsync(db => ListAsync(db, query, after, take, count), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AnswerSummaryRows> SummarizeAsync(EntryQuery query, AnswerSummaryFields fields, int texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        EntryQuerySql.Check(query, after: null, texts);
        return BudgetedAsync(db => SummarizeAsync(db, query, fields, texts), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AnswerTextPage> TextsAsync(EntryQuery query, int fieldId, AnswerTextCursor? after, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fieldId);
        EntryQuerySql.Check(query, after: null, take);
        return BudgetedAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await TextsAsync(db, new EntryQuerySql(n, query, await LongTextsAsync(db, n, query)), fieldId, after, take);
            },
            cancellationToken);
    }

    // Every read of the builder: one read within the time budget, and no other unit of work.
    private Task<T> BudgetedAsync<T>(Func<IUmbracoDatabase, Task<T>> read, CancellationToken cancellationToken) => _database.QueryAsync(read, _budget, cancellationToken);

    private static async Task<AnswerSummaryRows> SummarizeAsync(IUmbracoDatabase db, EntryQuery query, AnswerSummaryFields fields, int texts)
    {
        var n = new SqlNames(db);
        var sql = new EntryQuerySql(n, query, await LongTextsAsync(db, n, query));
        var total = sql.Total();
        var entries = await db.ScalarAsync<long>(total.Text, total.Args);
        var answered = (await GroupedAsync(db, fields.Answered, sql.Answered)).ToDictionary(row => row.FieldId, row => row.Total);
        var optionRows = await GroupedAsync(db, fields.Options, sql.Options);
        var options = optionRows.Select(row => new AnswerOptionCount(row.FieldId, row.TextKey ?? string.Empty, row.Total, row.Positions ?? 0, row.Sample ?? row.TextKey ?? string.Empty)).ToList();
        foreach (var field in optionRows.GroupBy(row => row.FieldId))
        {
            answered[field.Key] = field.Sum(row => row.Firsts ?? 0);
        }

        // A grid's answers, each entry or row once whichever of its rows it answered.
        foreach (var (grid, rowIds) in fields.Grids)
        {
            var statement = sql.GridAnswered([.. rowIds.Distinct().Take(EntryQuerySql.MaxSummaryFields)]);
            answered[grid] = await db.ScalarAsync<long>(statement.Text, statement.Args);
        }

        var numbers = new Dictionary<int, AnswerNumbers>();
        foreach (var row in await GroupedAsync(db, fields.Numbers, sql.Numbers))
        {
            // The middle one, or the two in the middle of an even count.
            var middle = await FetchAsync<double>(db, sql.Middle(row.FieldId, (row.Total - 1) / 2, row.Total % 2 == 0 ? 2 : 1));
            answered[row.FieldId] = row.Total;
            numbers[row.FieldId] = new AnswerNumbers(row.Total, row.Sum ?? 0, row.Lowest ?? 0, row.Highest ?? 0, middle.Count == 0 ? 0 : middle.Average(), row.Low ?? 0, row.Middle ?? 0, row.High ?? 0);
        }

        var ticked = new Dictionary<int, long>();
        foreach (var row in await GroupedAsync(db, fields.Ticked, sql.Ticked))
        {
            answered[row.FieldId] = row.Total;
            ticked[row.FieldId] = row.Ticks ?? 0;
        }

        var pages = new Dictionary<int, AnswerTextPage>();
        foreach (var field in fields.Texts.Distinct())
        {
            pages[field] = await TextsAsync(db, sql, field, after: null, texts);
        }

        return new AnswerSummaryRows(entries, answered, options, numbers, ticked, pages);
    }

    // One grouped statement for each chunk of the fields the database takes in one list; none for no field.
    private static async Task<List<AnswerSummaryRow>> GroupedAsync(IUmbracoDatabase db, IReadOnlyCollection<int> fieldIds, Func<IReadOnlyCollection<int>, SqlStatement> statement)
    {
        var rows = new List<AnswerSummaryRow>();
        foreach (var chunk in fieldIds.Distinct().Chunk(EntryQuerySql.MaxSummaryFields))
        {
            rows.AddRange(await FetchAsync<AnswerSummaryRow>(db, statement(chunk)));
        }

        return rows;
    }

    // A page of a field's texts, and where the next starts when one more came.
    private static async Task<AnswerTextPage> TextsAsync(IUmbracoDatabase db, EntryQuerySql sql, int fieldId, AnswerTextCursor? after, int take)
    {
        var rows = await FetchAsync<AnswerTextRow>(db, sql.Texts(fieldId, after, take + 1));
        var page = rows.Take(take).ToList();
        var texts = page.Select(row => new AnswerText(row.EntryKey, new DateTimeOffset(DateTime.SpecifyKind(row.SubmittedUtc, DateTimeKind.Utc)), row.RowIndex, row.Text ?? string.Empty)).ToList();
        return new AnswerTextPage(texts, rows.Count > take ? new AnswerTextCursor(page[^1].EntryId, DateTime.SpecifyKind(page[^1].SubmittedUtc, DateTimeKind.Utc), page[^1].RowIndex) : null);
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

    // The positions of the next entries: by submission, the unseen and then the seen ones, or the walk of a value sort and
    // then its tail.
    private static async Task<List<EntryPositionRow>> PositionsAsync(IUmbracoDatabase db, EntryQuerySql sql, EntrySort sort, EntryCursor? after, int limit)
    {
        if (sort.Column == EntrySortColumn.Unseen)
        {
            return await UnseenFirstAsync(db, sql, after, limit);
        }

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

    // Unseen first (ADR-0209): the entries nobody opened yet, then, once they run out on a page, the others from their start.
    // An entry opened while the list pages moves to the second part, where it shows again; none is ever skipped.
    private static async Task<List<EntryPositionRow>> UnseenFirstAsync(IUmbracoDatabase db, EntryQuerySql sql, EntryCursor? after, int limit)
    {
        var rows = new List<EntryPositionRow>(limit);
        if (after is null || !after.Seen)
        {
            rows.AddRange(await FetchAsync<EntryPositionRow>(db, sql.Unseen(after, limit, seen: false)));
            if (rows.Count == limit)
            {
                return rows;
            }

            after = null;
        }

        var seen = await FetchAsync<EntryPositionRow>(db, sql.Unseen(after, limit - rows.Count, seen: true));
        foreach (var row in seen)
        {
            row.Seen = true;
        }

        rows.AddRange(seen);
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
            : null,
        last.Seen);

    private static EntryCount Counted(int matches, bool search) =>
        search && matches > EntryQuerySql.SearchCountLimit ? new EntryCount(EntryQuerySql.SearchCountLimit, More: true) : new EntryCount(matches, More: false);

    private static Task<List<T>> FetchAsync<T>(IUmbracoDatabase db, SqlStatement statement) => db.FetchListAsync<T>(statement.Text, statement.Args);
}
