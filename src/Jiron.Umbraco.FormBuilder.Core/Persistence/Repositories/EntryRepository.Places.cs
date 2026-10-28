using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The places held on options (section 4.4, ADR-0090): a row of jfbOptionCapacity counts the values of the form's entries
// that hold places at the field that equal the option's value, compared by their code units. The writes of this
// repository that change such a value, or whether its entry holds places, lock the form's row first and change the
// counts in the same unit.
internal sealed partial class EntryRepository
{
    /// <summary>The longest option value a count's column holds, as long as an option's value may be (ADR-0002).</summary>
    public const int MaxOptionValueLength = 200;

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OptionPlace, int>> GetPlacesTakenAsync(int formId, IReadOnlyCollection<OptionPlace> options, CancellationToken cancellationToken = default)
    {
        var asked = Asked(options);
        if (asked.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<OptionPlace, int>>(new Dictionary<OptionPlace, int>());
        }

        return database.ReadAsync<IReadOnlyDictionary<OptionPlace, int>>(
            async db =>
            {
                var n = new SqlNames(db);
                var counts = await CountsAsync(db, n, formId, asked, locking: false);
                foreach (var (option, _) in asked.Where(pair => !counts.ContainsKey(pair.Key)))
                {
                    var a = new SqlArgs();
                    counts[option] = await db.ScalarAsync<int>($"SELECT {Held(n, a, formId, option)}", a.Values);
                }

                return counts;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<OptionPlace, int>> CountPlacesAsync(int formId, IReadOnlyCollection<OptionPlace> options, CancellationToken cancellationToken = default)
    {
        var asked = Asked(options);
        if (asked.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<OptionPlace, int>>(new Dictionary<OptionPlace, int>());
        }

        return database.WriteAsync<IReadOnlyDictionary<OptionPlace, int>>(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.OptionCapacity);
                foreach (var (option, hash) in asked)
                {
                    // A count made once, holding the places held on the option at that moment: a limit set after
                    // entries came in counts them.
                    var a = new SqlArgs();
                    var held = Held(n, a, formId, option);
                    var (form, field, key, value, now) = (a.Add(formId), a.Add(option.FieldKey), a.Add(hash), a.Add(option.Value), a.Add(database.UtcNow));
                    await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("formId")}, {n.C("fieldKey")}, {n.C("valueHash")}, {n.C("optionValue")}, {n.C("taken")}, {n.C("updatedUtc")})
                        SELECT {form}, {field}, {key}, {value}, {held}, {now}
                        WHERE EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)}{n.LockingRead} WHERE {n.C("id")} = {form})
                        AND NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("formId")} = {form} AND {n.C("fieldKey")} = {field} AND {n.C("valueHash")} = {key})
                        """,
                        a.Values);
                }

                return await CountsAsync(db, n, formId, asked, locking: true);
            },
            cancellationToken);
    }

    // Locks the rows of the forms of these entries: the first write of a unit that changes which values hold places, so a
    // form's places change one unit at a time (ADR-0090). A submission locks the same row with its entry counter.
    private static Task<int> LockFormsAsync(IUmbracoDatabase db, SqlNames n, IReadOnlyList<long> entryIds)
    {
        var a = new SqlArgs();
        var number = n.C("lastEntryNumber");
        return db.ExecAsync(
            $"UPDATE {n.T(FormBuilderTables.Form)} SET {number} = {number} WHERE {n.C("id")} IN (SELECT e.{n.C("formId")} FROM {n.T(FormBuilderTables.Entry)} e WHERE e.{n.C("id")} IN ({a.List(entryIds.Cast<object?>())}))",
            a.Values);
    }

    // Adds the values of these entries to the counts of the options they match, or takes them away; with holdingOnly,
    // only the values of those of the entries that hold places now (ADR-0090). An entry's values match a count when they
    // are at its field and equal its option's value by their code units, as the choice types compare an answer with
    // their options; the key compares exactly, and so does the text here, in a binary collation.
    private Task<int> AdjustPlacesAsync(IUmbracoDatabase db, SqlNames n, IReadOnlyList<long> entryIds, bool add, bool holdingOnly)
    {
        var a = new SqlArgs();
        var (table, entries, bin) = (n.T(FormBuilderTables.OptionCapacity), n.T(FormBuilderTables.Entry), n.BinaryCollation);
        var ids = a.List(entryIds.Cast<object?>());
        var whose = holdingOnly ? $"IN (SELECT h.{n.C("id")} FROM {entries} h WHERE h.{n.C("id")} IN ({ids}) AND {Holding(n, "h", a)})" : $"IN ({ids})";
        var matching =
            $"FROM {n.T(FormBuilderTables.EntryValue)} v INNER JOIN {n.T(FormBuilderTables.FormField)} f ON f.{n.C("id")} = v.{n.C("fieldId")} " +
            $"WHERE v.{n.C("entryId")} {whose} AND f.{n.C("formId")} = {table}.{n.C("formId")} AND f.{n.C("fieldKey")} = {table}.{n.C("fieldKey")} " +
            $"AND v.{n.C("text")}{bin} = {table}.{n.C("optionValue")}{bin}";
        var taken = n.C("taken");
        return db.ExecAsync(
            $"""
            UPDATE {table} SET {taken} = {taken} {(add ? "+" : "-")} (SELECT COUNT(*) {matching}), {n.C("updatedUtc")} = {a.Add(database.UtcNow)}
            WHERE {table}.{n.C("formId")} IN (SELECT e.{n.C("formId")} FROM {entries} e WHERE e.{n.C("id")} IN ({ids})) AND EXISTS (SELECT 1 {matching})
            """,
            a.Values);
    }

    // The counts these options have, by option; an option without one is left out.
    private static async Task<Dictionary<OptionPlace, int>> CountsAsync(IUmbracoDatabase db, SqlNames n, int formId, Dictionary<OptionPlace, string> asked, bool locking)
    {
        var byKey = asked.ToDictionary(pair => (pair.Key.FieldKey, pair.Value), pair => pair.Key);
        var counts = new Dictionary<OptionPlace, int>();
        foreach (var chunk in asked.Keys.Select(option => option.FieldKey).Distinct().Chunk(SqlArgs.MaxListLength))
        {
            var a = new SqlArgs();
            var rows = await db.FetchListAsync<OptionCapacityDto>(
                $"SELECT * FROM {n.T(FormBuilderTables.OptionCapacity)}{(locking ? n.LockingRead : string.Empty)} WHERE {n.C("formId")} = {a.Add(formId)} AND {n.C("fieldKey")} IN ({a.List(chunk.Cast<object?>())})",
                a.Values);
            foreach (var row in rows)
            {
                if (byKey.TryGetValue((row.FieldKey, row.ValueHash), out var option))
                {
                    counts[option] = row.Taken;
                }
            }
        }

        return counts;
    }

    // The places held on an option, counted from the values: a subquery whose arguments join the statement's. It seeks
    // the value index by the field and the text key, which an option's value never fills (it is at most 200 characters),
    // and keeps the values whose text is the value itself.
    private static string Held(SqlNames n, SqlArgs a, int formId, OptionPlace option) =>
        $"(SELECT COUNT(*) FROM {n.T(FormBuilderTables.EntryValue)} v INNER JOIN {n.T(FormBuilderTables.FormField)} f ON f.{n.C("id")} = v.{n.C("fieldId")} " +
        $"INNER JOIN {n.T(FormBuilderTables.Entry)} e ON e.{n.C("id")} = v.{n.C("entryId")} " +
        $"WHERE f.{n.C("formId")} = {a.Add(formId)} AND f.{n.C("fieldKey")} = {a.Add(option.FieldKey)} AND v.{n.C("textKey")} = {a.Add(EntryTextKey.For(option.Value))} " +
        $"AND v.{n.C("text")}{n.BinaryCollation} = {a.Add(option.Value)} AND {Holding(n, "e", a)})";

    // The condition that an entry, by its alias, holds places: it counts toward the entry limit.
    private static string Holding(SqlNames n, string alias, SqlArgs a) =>
        $"{alias}.{n.C("isTest")} = {a.Add(false)} AND {alias}.{n.C("state")} IN ({a.List(EntryLimits.CountingStates.Select(state => (object?)StoredNames.Of(state)))})";

    // The options asked for, each once, with its key; refused when one cannot be counted.
    private static Dictionary<OptionPlace, string> Asked(IReadOnlyCollection<OptionPlace> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var asked = new Dictionary<OptionPlace, string>();
        foreach (var option in options)
        {
            if (option.FieldKey == Guid.Empty || option.Value is not { Length: > 0 and <= MaxOptionValueLength })
            {
                throw new ArgumentException($"An option has a field key and a value of 1 to {MaxOptionValueLength} characters.", nameof(options));
            }

            asked.TryAdd(option, OptionValueHash.For(option.Value));
        }

        return asked;
    }
}
