using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The answers that must be unique in their form (uniquePerForm, ADR-0091): a value is taken when an entry that holds its
// answers has a value at the same field that equals it as entries filter. A number or a date compares by its column; a
// text by its key, sought on the value index and kept by a LIKE without wildcards in a binary collation (as the entries
// query builder compares keys), and whole where its key is cut.
internal sealed partial class EntryRepository
{
    /// <inheritdoc />
    public Task<IReadOnlySet<AnswerValue>> GetTakenAnswersAsync(int formId, IReadOnlyCollection<AnswerValue> values, CancellationToken cancellationToken = default)
    {
        var asked = Compared(values);
        if (asked.Count == 0)
        {
            return Task.FromResult<IReadOnlySet<AnswerValue>>(new HashSet<AnswerValue>());
        }

        return database.ReadAsync<IReadOnlySet<AnswerValue>>(
            db =>
            {
                var n = new SqlNames(db);
                return TakenAsync(db, n, asked, (a, value, key) => EarlyHolders(n, a, formId, value, key));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<AnswerValue>> FindTakenAnswersAsync(long entryId, IReadOnlyCollection<AnswerValue> values, CancellationToken cancellationToken = default)
    {
        var asked = Compared(values);
        if (asked.Count == 0)
        {
            return Task.FromResult<IReadOnlySet<AnswerValue>>(new HashSet<AnswerValue>());
        }

        return database.WriteAsync<IReadOnlySet<AnswerValue>>(
            async db =>
            {
                // The first write: the form's row, which every write that changes whose answers count locks too.
                var n = new SqlNames(db);
                await LockFormsAsync(db, n, [entryId]);
                var formId = await db.ScalarAsync<int?>($"SELECT {n.C("formId")} FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("id")} = @0", entryId);
                return formId is { } form ? await TakenAsync(db, n, asked, (a, value, key) => UnitHolders(n, a, form, entryId, value, key)) : new HashSet<AnswerValue>();
            },
            cancellationToken);
    }

    // The asked values that the entries the held clause names hold, each with one statement: few fields of a form are
    // unique.
    private static async Task<IReadOnlySet<AnswerValue>> TakenAsync(IUmbracoDatabase db, SqlNames n, IReadOnlyList<AnswerValue> asked, Func<SqlArgs, AnswerValue, string?, string> holders)
    {
        var taken = new HashSet<AnswerValue>();
        foreach (var value in asked)
        {
            var a = new SqlArgs();
            var (key, cut) = value.Text is { } text ? (EntryTextKey.For(text), EntryTextKey.MayBeCut(EntryTextKey.For(text))) : (null, false);
            var held = holders(a, value, key);
            if (!cut)
            {
                if (await db.ScalarAsync<int>($"SELECT CASE WHEN EXISTS (SELECT 1 FROM {held}) THEN 1 ELSE 0 END", a.Values) == 1)
                {
                    taken.Add(value);
                }

                continue;
            }

            // A cut key stands for every text that starts with it: the stored texts with that key, compared whole.
            var whole = EntryTextKey.Whole(value.Text!);
            var texts = await db.FetchListAsync<string>($"SELECT o.{n.C("text")} FROM {held}", a.Values);
            if (texts.Any(stored => string.Equals(EntryTextKey.Whole(stored), whole, StringComparison.Ordinal)))
            {
                taken.Add(value);
            }
        }

        return taken;
    }

    // The holders of a value while a submission's answers are read: without locks, as the unit asks again.
    internal static string EarlyHolders(SqlNames n, SqlArgs a, int formId, AnswerValue value, string? key) =>
        Holders(n, a, formId, except: null, value, key, locking: false);

    // The holders of a value inside the unit that stored the entry: every table read with a lock, so a writer at the same
    // moment waits on SQL Server (ADR-0036), and the entry itself left out.
    internal static string UnitHolders(SqlNames n, SqlArgs a, int formId, long entryId, AnswerValue value, string? key) =>
        Holders(n, a, formId, entryId, value, key, locking: true);

    // The values of the entries that hold their answers, at the value's field, that equal it by its column or its key: a
    // FROM clause whose arguments join the statement's.
    private static string Holders(SqlNames n, SqlArgs a, int formId, long? except, AnswerValue value, string? key, bool locking)
    {
        var hint = locking ? n.LockingRead : string.Empty;
        var at = $"f.{n.C("formId")} = {a.Add(formId)} AND f.{n.C("fieldKey")} = {a.Add(value.FieldKey)}";
        var match = (value.Number, value.Date) switch
        {
            ({ } number, _) => $"o.{n.C("number")} = {a.Add(number)}",
            (_, { } date) => $"o.{n.C("date")} = {a.Add(date)}",
            _ => $"o.{n.C("textKey")} = {a.Add(key)} AND o.{n.C("textKey")}{n.BinaryCollation} LIKE {a.Add(SqlNames.Escape(key!))} {SqlNames.EscapeClause}",
        };
        var holding = Holding(n, "e", a);
        var other = except is { } id ? $" AND e.{n.C("id")} <> {a.Add(id)}" : string.Empty;
        return $"{n.T(FormBuilderTables.EntryValue)} o{hint} " +
            $"INNER JOIN {n.T(FormBuilderTables.FormField)} f{hint} ON f.{n.C("id")} = o.{n.C("fieldId")} " +
            $"INNER JOIN {n.T(FormBuilderTables.Entry)} e{hint} ON e.{n.C("id")} = o.{n.C("entryId")} " +
            $"WHERE {at} AND {match} AND {holding}{other}";
    }

    // The values asked for, each once; refused when one cannot be compared.
    private static List<AnswerValue> Compared(IReadOnlyCollection<AnswerValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        foreach (var value in values)
        {
            var columns = (value.Text is null ? 0 : 1) + (value.Number is null ? 0 : 1) + (value.Date is null ? 0 : 1);
            if (value.FieldKey == Guid.Empty || columns != 1 || value.Text is { Length: 0 } || value.Number is { } number && !double.IsFinite(number))
            {
                throw new ArgumentException("A value has a field key and one of a text that is not empty, a finite number and a date.", nameof(values));
            }
        }

        return [.. values.Distinct()];
    }
}
