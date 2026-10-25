using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

/// <summary>A statement with its arguments, which the text names as <c>@0</c>, <c>@1</c> and so on.</summary>
/// <param name="Text">The statement.</param>
/// <param name="Args">Its arguments, one per placeholder in the text.</param>
internal sealed record SqlStatement(string Text, object?[] Args);

/// <summary>What a list's shape reads for each entry: its id and its position, never the whole entry (ADR-0039).</summary>
internal sealed class EntryPositionRow
{
    /// <summary>The entry.</summary>
    public long Id { get; set; }

    /// <summary>Its submission time as stored.</summary>
    public DateTime SubmittedUtc { get; set; }

    /// <summary>In the walk of a value sort, the value row; otherwise null.</summary>
    public long? ValueId { get; set; }

    /// <summary>In the walk of a text sort, the value's text key.</summary>
    public string? TextKey { get; set; }

    /// <summary>In the walk of a number sort, the value's number.</summary>
    public double? Number { get; set; }

    /// <summary>In the walk of a date sort, the value's date.</summary>
    public DateTime? Date { get; set; }
}

/// <summary>
/// The entries query builder (ADR-0003, ADR-0039): the statements of one <see cref="EntryQuery"/>, in the shapes
/// ADR-0003 measured and only those. Field filters are <c>IN</c> subqueries, pages go on from a keyset, a sort by a
/// value walks the value index, and a search counts up to 1,000 in one pass.
/// </summary>
internal sealed class EntryQuerySql
{
    /// <summary>The most field filters one query holds.</summary>
    public const int MaxFilters = 10;

    /// <summary>The most values one text filter compares with.</summary>
    public const int MaxValuesPerFilter = 50;

    /// <summary>The most parameters one statement takes: SQL Server takes 2,100.</summary>
    public const int MaxParameters = 2000;

    /// <summary>The most matches a search counts exactly; beyond, it says "more than 1,000" (ADR-0003).</summary>
    public const int SearchCountLimit = 1000;

    /// <summary>The most entries one page holds.</summary>
    public const int MaxTake = PageRequest.MaxTake;

    private readonly SqlNames _n;
    private readonly EntryQuery _query;
    private readonly IReadOnlyDictionary<(int Filter, int Value), IReadOnlyList<string>> _longTexts;
    private readonly string _entry;
    private readonly string _values;
    private readonly string _term;

    /// <summary>The statements of <paramref name="query"/>, which <see cref="Check"/> accepted.</summary>
    /// <param name="n">The names of the database.</param>
    /// <param name="query">The query.</param>
    /// <param name="longTexts">
    /// For each value of a text filter whose key may be cut (<see cref="LongValues"/>), the stored texts that equal it
    /// once cleaned and folded; a value without any matches no entry.
    /// </param>
    public EntryQuerySql(SqlNames n, EntryQuery query, IReadOnlyDictionary<(int Filter, int Value), IReadOnlyList<string>>? longTexts = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        _n = n;
        _query = query;
        _longTexts = longTexts ?? new Dictionary<(int Filter, int Value), IReadOnlyList<string>>();
        _entry = n.T(FormBuilderTables.Entry);
        _values = n.T(FormBuilderTables.EntryValue);
        _term = query.Search is null ? string.Empty : EntrySearchText.Normalize(query.Search);
    }

    /// <summary>Whether the query looks for a term: then its count stops at <see cref="SearchCountLimit"/>.</summary>
    public bool Searches => _term.Length > 0;

    /// <summary>
    /// The values of text filters whose key may be cut (ADR-0038): their stored texts are read and compared whole
    /// first (<see cref="LongTexts"/>), because the key holds only the first 256 code units.
    /// </summary>
    public static IEnumerable<(int Filter, int Value, int FieldId, string Key, string Whole)> LongValues(EntryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        for (var i = 0; i < query.Filters.Count; i++)
        {
            if (query.Filters[i] is not EntryTextFilter { Match: EntryTextMatch.OneOf or EntryTextMatch.NoneOf } text)
            {
                continue;
            }

            for (var j = 0; j < text.Values.Count; j++)
            {
                var key = EntryTextKey.For(text.Values[j]);
                if (EntryTextKey.MayBeCut(key))
                {
                    yield return (i, j, text.FieldId, key, EntryTextKey.Whole(text.Values[j]));
                }
            }
        }
    }

    /// <summary>The distinct stored texts of a field whose key is <paramref name="key"/>, each as it was stored.</summary>
    public static SqlStatement LongTexts(SqlNames n, int fieldId, string key)
    {
        var a = new Args();
        var textKey = n.C("textKey");
        return a.Statement(
            $"SELECT DISTINCT v.{n.C("text")}{n.BinaryCollation} AS {n.C("Text")} FROM {n.T(FormBuilderTables.EntryValue)} v WHERE v.{n.C("fieldId")} = {a.Add(fieldId)} AND v.{textKey} = {a.Add(key)} AND v.{textKey}{n.BinaryCollation} = {a.Add(key)}");
    }

    /// <summary>The whole rows of the entries <paramref name="ids"/> names, in no order.</summary>
    public static SqlStatement Load(SqlNames n, IReadOnlyList<long> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Count, MaxTake);
        var a = new Args();
        return a.Statement($"SELECT * FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("id")} IN ({a.List(ids.Cast<object?>())})");
    }

    /// <summary>A page by submission: the next <paramref name="limit"/> positions after <paramref name="after"/>.</summary>
    public SqlStatement BySubmission(EntryCursor? after, int limit) => ByDate(after, limit, tail: false);

    /// <summary>
    /// The tail of a value sort: the entries without a value in the sort's column, newest first, after
    /// <paramref name="after"/> when the cursor stands in the tail.
    /// </summary>
    public SqlStatement Tail(EntryCursor? after, int limit) => ByDate(after, limit, tail: true);

    /// <summary>
    /// The walk of a value sort: the field's values in the order of their index, every entry once at its first answer,
    /// after <paramref name="after"/> when the cursor stands in the walk.
    /// </summary>
    public SqlStatement Walk(EntryCursor? after, int limit)
    {
        var sort = _query.Sort;
        var a = new Args();
        var column = C(SortColumn(sort.Column));
        var (id, entryId, fieldId, submitted) = (C("id"), C("entryId"), C("fieldId"), C("submittedUtc"));
        var (row, item) = (C("rowIndex"), C("valueIndex"));

        // The arguments go in the order of their placeholders.
        var field = a.Add(sort.FieldId);

        // An earlier answer of the same entry and field wins, by row and then by item: one seek on (entryId, fieldId).
        var first = $"NOT EXISTS (SELECT 1 FROM {_values} vw WHERE vw.{entryId} = v.{entryId} AND vw.{fieldId} = {a.Add(sort.FieldId)} AND vw.{column} IS NOT NULL AND (vw.{row} < v.{row} OR (vw.{row} = v.{row} AND vw.{item} < v.{item})))";

        // The unary plus keeps the walk on the value index: with planner statistics, SQLite would otherwise reach the
        // values through the entry ids of a filter (v.entryId IN ...) and sort them in a temporary b-tree. CROSS JOIN keeps
        // the values the outer loop, as SQLite documents, should a later planner choose another order.
        var where = $"e.{id} = +v.{entryId} AND v.{fieldId} = {field} AND v.{column} IS NOT NULL AND {first} AND {Where(a)}";
        if (after?.Value is { } position)
        {
            where += ValueKeyset(a, column, position, after.EntryId, sort.Descending);
        }

        var order = sort.Descending ? "DESC" : "ASC";
        return a.Statement(
            $"SELECT v.{id} AS {C("ValueId")}, e.{id} AS {C("Id")}, e.{submitted} AS {C("SubmittedUtc")}, v.{column} AS {C(PositionColumn(sort.Column))} FROM {_values} v CROSS JOIN {_entry} e WHERE {where} ORDER BY v.{column} {order}, v.{entryId} {order} {_n.Page(0, limit)}");
    }

    /// <summary>
    /// How many entries match: exactly, or for a search the newest <see cref="SearchCountLimit"/> + 1 at most.
    /// </summary>
    public SqlStatement Count()
    {
        var a = new Args();
        var where = Where(a);
        var (id, submitted) = (C("id"), C("submittedUtc"));
        return Searches
            ? a.Statement($"SELECT COUNT(*) FROM (SELECT e.{id} AS {C("Id")} FROM {_entry} e WHERE {where} ORDER BY e.{submitted} DESC, e.{id} DESC {_n.Page(0, SearchCountLimit + 1)}) c")
            : a.Statement($"SELECT COUNT(*) FROM {_entry} e WHERE {where}");
    }

    /// <summary>Refuses a query, cursor or page size the builder cannot answer as asked.</summary>
    /// <exception cref="ArgumentException">What is wrong, by parameter.</exception>
    public static void Check(EntryQuery query, EntryCursor? after, int take)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxTake);
        Require(query.FormId > 0, "A query names its form.", nameof(query));
        Require(query.States is not null && query.States.All(Enum.IsDefined), "The states are entry states.", nameof(query));
        Require(Enum.IsDefined(query.Tests), "The test entries are left out, included or the only ones.", nameof(query));
        Require(IsUtc(query.SubmittedFrom) && IsUtc(query.SubmittedBefore), "Submission times are UTC.", nameof(query));
        Require(query.SubmittedFrom is null || query.SubmittedBefore is null || query.SubmittedFrom < query.SubmittedBefore, "A submission range starts before it ends.", nameof(query));
        Require(query.Filters is not null && query.Filters.Count <= MaxFilters, $"A query holds at most {MaxFilters} field filters.", nameof(query));
        foreach (var filter in query.Filters!)
        {
            CheckFilter(filter, nameof(query));
        }

        Require(query.Search is null || EntrySearchText.Normalize(query.Search).Length <= EntrySearchText.MaxTermLength, $"A search term holds at most {EntrySearchText.MaxTermLength} characters.", nameof(query));
        CheckSort(query.Sort, nameof(query));
        if (after is not null)
        {
            CheckCursor(query.Sort, after);
        }
    }

    private static void CheckFilter(EntryFieldFilter filter, string parameter)
    {
        Require(filter is not null && filter.FieldId > 0, "A field filter names a field of the catalogue.", parameter);
        switch (filter)
        {
            case EntryTextFilter text:
                Require(Enum.IsDefined(text.Match), "A text filter compares one of, none of, or a prefix.", parameter);
                Require(text.Values is { Count: > 0 and <= MaxValuesPerFilter }, $"A text filter compares with 1 to {MaxValuesPerFilter} values.", parameter);
                Require(text.Values!.All(value => value is not null && EntryTextKey.Whole(value).Length > 0), "A text filter's values are not empty.", parameter);
                Require(
                    text.Match != EntryTextMatch.StartsWith || (text.Values.Count == 1 && EntryTextKey.Whole(text.Values[0]).Length < EntryTextKey.MaxLength - 1),
                    $"A prefix is one value of at most {EntryTextKey.MaxLength - 2} characters once cleaned and folded.",
                    parameter);
                break;
            case EntryNumberFilter number:
                Require(number.Min is not null || number.Max is not null, "A number range has a bound.", parameter);
                Require(number.Min is null || number.Max is null || number.Min <= number.Max, "A number range starts at or below its end.", parameter);
                break;
            case EntryDateFilter date:
                Require(date.From is not null || date.Before is not null, "A date range has a bound.", parameter);
                Require(IsUtc(date.From) && IsUtc(date.Before), "A date range is UTC.", parameter);
                Require(date.From is null || date.Before is null || date.From < date.Before, "A date range starts before it ends.", parameter);
                break;
            case EntryCheckedFilter or EntryAnsweredFilter:
                break;
            default:
                throw new ArgumentException("The query builder does not know this kind of field filter.", parameter);
        }
    }

    private static void CheckSort(EntrySort sort, string parameter)
    {
        Require(sort is not null && Enum.IsDefined(sort.Column), "A query is sorted by submission, or by a field's text, number or date.", parameter);
        Require(sort!.ByField ? sort.FieldId > 0 : sort.FieldId == 0, "A value sort names its field, and a sort by submission none.", parameter);
    }

    private static void CheckCursor(EntrySort sort, EntryCursor after)
    {
        Require(after.Sort == sort, "A cursor goes on only with the sort it came from.", nameof(after));
        Require(after.EntryId > 0 && after.SubmittedUtc.Kind == DateTimeKind.Utc, "A cursor names an entry and its UTC submission time.", nameof(after));
        if (after.Value is not { } position)
        {
            return;
        }

        Require(sort.ByField && position.ValueId > 0, "Only a value sort's walk has a value position.", nameof(after));
        Require(
            sort.Column switch
            {
                EntrySortColumn.Text => position.Text is not null,
                EntrySortColumn.Number => position.Number is { } number && double.IsFinite(number),
                _ => position.Date is { Kind: DateTimeKind.Utc },
            },
            "A value position holds a value of the sort's column.",
            nameof(after));
    }

    private static bool IsUtc(DateTime? time) => time is not { } value || value.Kind == DateTimeKind.Utc;

    private static void Require(bool condition, string message, string parameter)
    {
        if (!condition)
        {
            throw new ArgumentException(message, parameter);
        }
    }

    private static string SortColumn(EntrySortColumn column) => column switch
    {
        EntrySortColumn.Text => "textKey",
        EntrySortColumn.Number => "number",
        EntrySortColumn.Date => "date",
        _ => throw new ArgumentOutOfRangeException(nameof(column)),
    };

    private static string PositionColumn(EntrySortColumn column) => column switch
    {
        EntrySortColumn.Text => nameof(EntryPositionRow.TextKey),
        EntrySortColumn.Number => nameof(EntryPositionRow.Number),
        _ => nameof(EntryPositionRow.Date),
    };

    private string C(string column) => _n.C(column);

    private SqlStatement ByDate(EntryCursor? after, int limit, bool tail)
    {
        var a = new Args();
        var (id, submitted) = (C("id"), C("submittedUtc"));
        var descending = tail || _query.Sort.Descending;
        var where = Where(a);
        if (tail)
        {
            where += $" AND e.{id} NOT IN (SELECT vt.{C("entryId")} FROM {_values} vt WHERE vt.{C("fieldId")} = {a.Add(_query.Sort.FieldId)} AND vt.{C(SortColumn(_query.Sort.Column))} IS NOT NULL)";
        }

        if (after is not null && (!tail || after.Value is null))
        {
            // Column against column: the cursor's own row says where it stood, exact also on SQL Server's DATETIME. Only
            // a row of the form counts, so a cursor never places a page by another form's entry.
            string At() => $"{_n.IfNull}((SELECT ec.{submitted} FROM {_entry} ec WHERE ec.{id} = {a.Add(after.EntryId)} AND ec.{C("formId")} = {a.Add(_query.FormId)}), {a.Add(after.SubmittedUtc)})";
            var (edge, beyond) = descending ? ("<=", "<") : (">=", ">");
            where += $" AND e.{submitted} {edge} {At()} AND (e.{submitted} {beyond} {At()} OR e.{id} {beyond} {a.Add(after.EntryId)})";
        }

        var order = descending ? "DESC" : "ASC";
        return a.Statement($"SELECT e.{id} AS {C("Id")}, e.{submitted} AS {C("SubmittedUtc")} FROM {_entry} e WHERE {where} ORDER BY e.{submitted} {order}, e.{id} {order} {_n.Page(0, limit)}");
    }

    private string ValueKeyset(Args a, string column, EntryValuePosition position, long entryId, bool descending)
    {
        object? fallback = _query.Sort.Column switch
        {
            EntrySortColumn.Text => position.Text,
            EntrySortColumn.Number => position.Number,
            _ => position.Date,
        };

        // The cursor's value row says where the walk stood; only a value of the sort's field counts, so a cursor never
        // places a page by another field's answer.
        string At() => $"{_n.IfNull}((SELECT vc.{column} FROM {_values} vc WHERE vc.{C("id")} = {a.Add(position.ValueId)} AND vc.{C("fieldId")} = {a.Add(_query.Sort.FieldId)}), {a.Add(fallback)})";
        var (edge, beyond) = descending ? ("<=", "<") : (">=", ">");
        return $" AND v.{column} {edge} {At()} AND (v.{column} {beyond} {At()} OR v.{C("entryId")} {beyond} {a.Add(entryId)})";
    }

    // The conditions on the entry row e, the field filters included.
    private string Where(Args a)
    {
        var parts = new List<string> { $"e.{C("formId")} = {a.Add(_query.FormId)}" };
        var states = _query.States.Distinct().ToList();
        if (states.Count > 0 && states.Count < Enum.GetValues<EntryState>().Length)
        {
            parts.Add($"e.{C("state")} IN ({a.List(states.Select(state => (object?)StoredNames.Of(state)))})");
        }

        if (_query.Tests != EntryTestFilter.Include)
        {
            parts.Add($"e.{C("isTest")} = {a.Add(_query.Tests == EntryTestFilter.Only)}");
        }

        if (_query.SubmittedFrom is { } from)
        {
            parts.Add($"e.{C("submittedUtc")} >= {a.Add(from)}");
        }

        if (_query.SubmittedBefore is { } before)
        {
            parts.Add($"e.{C("submittedUtc")} < {a.Add(before)}");
        }

        if (Searches)
        {
            parts.Add($"e.{C("searchText")}{_n.BinaryCollation} LIKE {a.Add("%" + SqlNames.Escape(_term) + "%")} {SqlNames.EscapeClause}");
        }

        for (var i = 0; i < _query.Filters.Count; i++)
        {
            parts.Add(Filter(a, i, _query.Filters[i]));
        }

        return string.Join(" AND ", parts);
    }

    // One field filter, on the alias vf: the entry's id in (or, for the opposite, not in) the ids of the entries whose
    // values match.
    private string Filter(Args a, int index, EntryFieldFilter filter)
    {
        // The field's argument comes first, as its placeholder does.
        string In(bool negate, Func<string>? condition)
        {
            var field = a.Add(filter.FieldId);
            var rest = condition is null ? string.Empty : " AND " + condition();
            return $"e.{C("id")} {(negate ? "NOT IN" : "IN")} (SELECT vf.{C("entryId")} FROM {_values} vf WHERE vf.{C("fieldId")} = {field}{rest})";
        }

        return filter switch
        {
            EntryTextFilter { Match: EntryTextMatch.OneOf } text => In(negate: false, () => OneOf(a, index, text)),
            EntryTextFilter { Match: EntryTextMatch.NoneOf } text => In(negate: true, () => OneOf(a, index, text)),
            EntryTextFilter text => In(negate: false, () => StartsWith(a, text.Values[0])),
            EntryNumberFilter number => In(negate: false, () => Range(a, "number", Bound(number.Min), number.MinInclusive, Bound(number.Max), number.MaxInclusive)),
            EntryDateFilter date => In(negate: false, () => Range(a, "date", date.From, lowInclusive: true, date.Before, highInclusive: false)),
            EntryCheckedFilter check => In(!check.Checked, () => $"vf.{C("bool")} = {a.Add(true)}"),
            EntryAnsweredFilter answered => In(!answered.Answered, condition: null),
            _ => throw new ArgumentException("The query builder does not know this kind of field filter.", nameof(filter)),
        };
    }

    // A value's key equals one of the filter's keys: sought with the column's collation, then kept by a LIKE without
    // wildcards in a binary collation. Unlike = and IN, LIKE pads nothing on SQL Server, where a cut key can end in
    // spaces that a shorter key would otherwise equal.
    private string OneOf(Args a, int index, EntryTextFilter filter)
    {
        var (key, bin) = (C("textKey"), _n.BinaryCollation);
        var parts = new List<string>();
        var keys = filter.Values.Select(EntryTextKey.For).Where(value => !EntryTextKey.MayBeCut(value)).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count > 0)
        {
            var seek = a.List(keys);
            var exact = string.Join(" OR ", keys.Select(value => $"vf.{key}{bin} LIKE {a.Add(SqlNames.Escape(value))} {SqlNames.EscapeClause}"));
            parts.Add($"(vf.{key} IN ({seek}) AND ({exact}))");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var j = 0; j < filter.Values.Count; j++)
        {
            var cut = EntryTextKey.For(filter.Values[j]);
            if (EntryTextKey.MayBeCut(cut) && seen.Add(EntryTextKey.Whole(filter.Values[j])) && _longTexts.TryGetValue((index, j), out var texts) && texts.Count > 0)
            {
                // The texts are the stored ones that equal the value whole; texts that differ by trailing spaces
                // alone, which SQL Server's IN would equate, clean to the same text.
                parts.Add($"(vf.{key} = {a.Add(cut)} AND vf.{C("text")}{bin} IN ({a.List(texts)}))");
            }
        }

        return parts.Count switch
        {
            0 => "1 = 0",
            1 => parts[0],
            _ => "(" + string.Join(" OR ", parts) + ")",
        };
    }

    // A value's key starts with the prefix, by its code units. SQLite's LIKE ignores collations and folds ASCII letters
    // only, which folded keys no longer differ by, so its LIKE decides exactly and, on the NOCASE column, seeks the
    // prefix on the index. On SQL Server a collation may read letters as one (Danish aa, Czech ch) and miss a key that
    // starts with the prefix, so only a binary LIKE decides there, over the field's values.
    private string StartsWith(Args a, string prefix)
    {
        var key = C("textKey");
        var pattern = a.Add(SqlNames.Escape(EntryTextKey.For(prefix)) + "%");
        return _n.IsSqlite
            ? $"vf.{key} LIKE {pattern} {SqlNames.EscapeClause}"
            : $"vf.{key}{_n.BinaryCollation} LIKE {pattern} {SqlNames.EscapeClause}";
    }

    // A bound as the double a submitted number with the same digits is stored as: parsed, which rounds correctly,
    // where a conversion from decimal can miss by one unit in the last place.
    private static double? Bound(decimal? value) =>
        value is { } bound ? double.Parse(bound.ToString(CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture) : null;

    private string Range(Args a, string column, object? low, bool lowInclusive, object? high, bool highInclusive)
    {
        var parts = new List<string>(2);
        if (low is not null)
        {
            parts.Add($"vf.{C(column)} {(lowInclusive ? ">=" : ">")} {a.Add(low)}");
        }

        if (high is not null)
        {
            parts.Add($"vf.{C(column)} {(highInclusive ? "<=" : "<")} {a.Add(high)}");
        }

        return string.Join(" AND ", parts);
    }

    // The arguments of one statement, numbered as they are added; one per placeholder, as NPoco binds them.
    private sealed class Args
    {
        private readonly List<object?> _values = [];

        public string Add(object? value)
        {
            _values.Add(value);
            return "@" + (_values.Count - 1).ToString(CultureInfo.InvariantCulture);
        }

        public string List(IEnumerable<object?> values) => string.Join(", ", values.Select(Add));

        public SqlStatement Statement(string text) =>
            _values.Count > MaxParameters
                ? throw new ArgumentException($"The query names more values than one statement takes ({_values.Count}, at most {MaxParameters}).")
                : new SqlStatement(text, [.. _values]);
    }
}
