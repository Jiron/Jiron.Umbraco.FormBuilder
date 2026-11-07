namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;

// The statements of the answer summaries (ADR-0212): grouped over the values of the entries the query holds, its own
// conditions as a subquery, so the summaries read the entries the list, the exports and they all mean by one filter. An
// answer is counted by its first value (valueIndex 0), once for an entry or for a row of a repeatable group, and an
// option once for each entry or row that chose it, so nothing needs DISTINCT.
internal sealed partial class EntryQuerySql
{
    /// <summary>The most fields one summary statement names.</summary>
    public const int MaxSummaryFields = SqlArgs.MaxListLength;

    /// <summary>
    /// How many entries match, exactly, whatever the search: the summaries' entries. A list's count stops at
    /// <see cref="SearchCountLimit"/> for a search; this one does not.
    /// </summary>
    public SqlStatement Total()
    {
        var a = new SqlArgs();
        return Statement(a, $"SELECT COUNT(*) FROM {_entry} e WHERE {Where(a)}");
    }

    /// <summary>
    /// For each field, how many answers the matching entries hold: one per entry, or per row of a repeatable group. The
    /// statements of options, numbers and checkboxes count their fields' answers in their own pass.
    /// </summary>
    /// <param name="fieldIds">The fields, at most <see cref="MaxSummaryFields"/>.</param>
    public SqlStatement Answered(IReadOnlyCollection<int> fieldIds) =>
        Grouped(fieldIds, $"COUNT(*) AS {C("Total")}", _ => string.Empty, _ => $"v.{C("valueIndex")} = 0", byKey: false);

    /// <summary>
    /// For each field and stored key (its <c>textKey</c>), how many answers chose it, how many of them chose it first (an
    /// answer's first value, whose sum over the keys is the field's answers), the sum of the places it was chosen at
    /// (<c>valueIndex</c>), which for a ranking is its position less one, and one of its stored texts, to name it by.
    /// </summary>
    /// <param name="fieldIds">The fields, at most <see cref="MaxSummaryFields"/>.</param>
    public SqlStatement Options(IReadOnlyCollection<int> fieldIds) =>
        Grouped(
            fieldIds,
            $"v.{C("textKey")} AS {C("TextKey")}, COUNT(*) AS {C("Total")}, SUM(CASE WHEN v.{C("valueIndex")} = 0 THEN 1 ELSE 0 END) AS {C("Firsts")}, SUM(v.{C("valueIndex")}) AS {C("Positions")}, MAX(v.{C("text")}) AS {C("Sample")}",
            _ => string.Empty,
            _ => $"v.{C("textKey")} IS NOT NULL",
            byKey: true);

    /// <summary>
    /// For each number field, how many answers, their sum, the lowest and the highest, and how many fall in each group of
    /// a 0-to-10 scale: up to 6, 7 and 8, and 9 or more.
    /// </summary>
    /// <param name="fieldIds">The fields, at most <see cref="MaxSummaryFields"/>.</param>
    public SqlStatement Numbers(IReadOnlyCollection<int> fieldIds)
    {
        var number = $"v.{C("number")}";
        return Grouped(
            fieldIds,
            $"COUNT(*) AS {C("Total")}, SUM({number}) AS {C("Sum")}, MIN({number}) AS {C("Lowest")}, MAX({number}) AS {C("Highest")}, "
            + $"SUM(CASE WHEN {number} < 6.5 THEN 1 ELSE 0 END) AS {C("Low")}, "
            + $"SUM(CASE WHEN {number} >= 6.5 AND {number} < 8.5 THEN 1 ELSE 0 END) AS {C("Middle")}, "
            + $"SUM(CASE WHEN {number} >= 8.5 THEN 1 ELSE 0 END) AS {C("High")}",
            _ => string.Empty,
            _ => $"{number} IS NOT NULL",
            byKey: false);
    }

    /// <summary>For each checkbox field, how many answers it has and how many of them are ticked.</summary>
    /// <param name="fieldIds">The fields, at most <see cref="MaxSummaryFields"/>.</param>
    public SqlStatement Ticked(IReadOnlyCollection<int> fieldIds) =>
        Grouped(fieldIds, $"COUNT(*) AS {C("Total")}", a => $"SUM(CASE WHEN v.{C("bool")} = {a.Add(true)} THEN 1 ELSE 0 END) AS {C("Ticks")}", _ => $"v.{C("bool")} IS NOT NULL", byKey: false);

    /// <summary>
    /// How many answers a grid has: the entries, or rows of a repeatable group, that answered any of its rows, each once.
    /// </summary>
    /// <param name="rowIds">The grid's rows, at most <see cref="MaxSummaryFields"/>.</param>
    public SqlStatement GridAnswered(IReadOnlyCollection<int> rowIds)
    {
        ArgumentNullException.ThrowIfNull(rowIds);
        ArgumentOutOfRangeException.ThrowIfZero(rowIds.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rowIds.Count, MaxSummaryFields);
        var a = new SqlArgs();
        var (entryId, rowIndex) = (C("entryId"), C("rowIndex"));
        var rows = a.List(rowIds.Distinct().Select(id => (object?)id));
        return Statement(
            a,
            $"SELECT COUNT(*) FROM (SELECT DISTINCT v.{entryId}, v.{rowIndex} FROM {_values} v WHERE v.{C("fieldId")} IN ({rows}) AND v.{C("valueIndex")} = 0 AND v.{entryId} IN ({Matching(a)})) answered");
    }

    /// <summary>
    /// A number field's answers in their order from <paramref name="skip"/> on, <paramref name="take"/> of them: the one
    /// or two in the middle, the median, on the field's number index.
    /// </summary>
    /// <param name="fieldId">The field.</param>
    /// <param name="skip">How many come first.</param>
    /// <param name="take">How many to read.</param>
    public SqlStatement Middle(int fieldId, long skip, int take)
    {
        var a = new SqlArgs();
        var (field, number) = (a.Add(fieldId), C("number"));
        return Statement(
            a,
            $"SELECT v.{number} FROM {_values} v WHERE v.{C("fieldId")} = {field} AND v.{number} IS NOT NULL AND v.{C("entryId")} IN ({Matching(a)}) ORDER BY v.{number} {_n.Page(skip, take)}");
    }

    /// <summary>
    /// A field's texts, newest entry first and in its rows' order within an entry, after <paramref name="after"/>:
    /// <paramref name="limit"/> of them, walking the entries by submission.
    /// </summary>
    /// <param name="fieldId">The field.</param>
    /// <param name="after">Where the page before ended; none for the first page.</param>
    /// <param name="limit">How many to read.</param>
    public SqlStatement Texts(int fieldId, AnswerTextCursor? after, int limit)
    {
        var a = new SqlArgs();
        var (id, submitted, row) = (C("id"), C("submittedUtc"), C("rowIndex"));
        var where = $"v.{C("fieldId")} = {a.Add(fieldId)} AND v.{C("text")} IS NOT NULL AND {Where(a)}";
        if (after is not null)
        {
            // Column against column, as a list's cursor goes: the cursor's own row says where it stood, and only a row of
            // the query's forms counts, so a cursor never places a page by another form's entry.
            string At() => $"{_n.IfNull}((SELECT ec.{submitted} FROM {_entry} ec WHERE ec.{id} = {a.Add(after.EntryId)} AND {FormCondition(a, "ec")}), {a.Add(after.SubmittedUtc)})";
            where += $" AND e.{submitted} <= {At()} AND (e.{submitted} < {At()} OR e.{id} < {a.Add(after.EntryId)} OR (e.{id} = {a.Add(after.EntryId)} AND v.{row} > {a.Add(after.Row)}))";
        }

        return Statement(
            a,
            $"SELECT e.{C("key")} AS {C("EntryKey")}, e.{id} AS {C("EntryId")}, e.{submitted} AS {C("SubmittedUtc")}, v.{row} AS {C("RowIndex")}, v.{C("text")} AS {C("Text")} "
            + $"FROM {_entry} e INNER JOIN {_values} v ON v.{C("entryId")} = e.{id} WHERE {where} "
            + $"ORDER BY e.{submitted} DESC, e.{id} DESC, v.{row} ASC, v.{C("valueIndex")} ASC {_n.Page(0, limit)}");
    }

    // The ids of the entries the query holds, as a subquery on the alias e.
    private string Matching(SqlArgs a) => $"SELECT e.{C("id")} FROM {_entry} e WHERE {Where(a)}";

    // One row per field, or per field and key, over the values of the matching entries that meet the condition; the
    // select's figures, and those with arguments of their own, which come first as their placeholders do.
    private SqlStatement Grouped(IReadOnlyCollection<int> fieldIds, string select, Func<SqlArgs, string> figures, Func<SqlArgs, string> condition, bool byKey)
    {
        ArgumentNullException.ThrowIfNull(fieldIds);
        ArgumentOutOfRangeException.ThrowIfZero(fieldIds.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fieldIds.Count, MaxSummaryFields);
        var a = new SqlArgs();
        var (fieldId, entryId) = (C("fieldId"), C("entryId"));

        // The arguments go in the order of their placeholders.
        var more = figures(a);
        var fields = a.List(fieldIds.Distinct().Select(id => (object?)id));
        var where = $"v.{fieldId} IN ({fields}) AND {condition(a)} AND v.{entryId} IN ({Matching(a)})";
        var group = byKey ? $"v.{fieldId}, v.{C("textKey")}" : $"v.{fieldId}";
        return Statement(a, $"SELECT v.{fieldId} AS {C("FieldId")}, {select}{(more.Length == 0 ? string.Empty : ", " + more)} FROM {_values} v WHERE {where} GROUP BY {group}");
    }
}
