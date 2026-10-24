using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The values of entries (ADR-0003): one row per answer, item and repeater row, in the typed column of the field's
// storage kind. A value that is text only also gets its text key (ADR-0005, ADR-0038).
internal sealed partial class EntryRepository
{
    /// <summary>
    /// The most values one insert writes: ten parameters each, so a statement stays at 1,000 parameters, inside SQL
    /// Server's 2,100 and SQLite's limit.
    /// </summary>
    public const int ValuesPerStatement = 100;

    // The columns an insert of values writes, in the order of its parameters.
    private static readonly string[] ValueColumns = ["entryId", "fieldId", "rowIndex", "valueIndex", "text", "textKey", "number", "date", "bool", "fileId"];

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryValue>> ListValuesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var ids = entryIds.Distinct().Order().ToArray();
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredEntryValue>>([]);
        }

        return database.ReadAsync<IReadOnlyList<StoredEntryValue>>(
            async db =>
            {
                var n = new SqlNames(db);
                var values = new List<StoredEntryValue>();

                // The ids are in order and each statement sorts its own, so the statements' rows follow one another.
                foreach (var chunk in ids.Chunk(IdsPerStatement))
                {
                    var rows = await db.FetchListAsync<EntryValueDto>(
                        $"SELECT * FROM {n.T(FormBuilderTables.EntryValue)} WHERE {n.C("entryId")} IN ({Parameters(0, chunk.Length)}) ORDER BY {n.C("entryId")}, {n.C("fieldId")}, {n.C("rowIndex")}, {n.C("valueIndex")}, {n.C("id")}",
                        [.. chunk.Cast<object?>()]);
                    values.AddRange(rows.Select(ValueFromDto));
                }

                return values;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An entry has no concurrency version (schema v1), so a replace is the last writer's, field by field; the timeline
    /// events the service writes with it say who changed what (ADR-0038). A file a replaced value named stays the
    /// entry's file until the service deletes it.
    /// </remarks>
    public Task<StoredEntry?> ReplaceValuesAsync(long entryId, IReadOnlyCollection<int> fieldIds, IReadOnlyList<NewEntryValue> values, string searchText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fieldIds);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(searchText);
        var fields = fieldIds.Distinct().ToArray();
        if (fields.Any(id => id <= 0))
        {
            throw new ArgumentException("A field id is 1 or more.", nameof(fieldIds));
        }

        CheckValues(values, nameof(values));
        if (values.Any(value => !fields.Contains(value.FieldId)))
        {
            throw new ArgumentException("Every value names one of the fields it replaces.", nameof(values));
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var entries = n.T(FormBuilderTables.Entry);

                // The first write, and the entry's existence in one.
                var updated = await db.ExecAsync(
                    $"UPDATE {entries} SET {n.C("searchText")} = @0, {n.C("updatedUtc")} = @1 WHERE {n.C("id")} = @2",
                    searchText,
                    database.UtcNow,
                    entryId);
                if (updated == 0)
                {
                    return null;
                }

                var formId = await db.ScalarAsync<int>($"SELECT {n.C("formId")} FROM {entries} WHERE {n.C("id")} = @0", entryId);
                await CheckFieldsAsync(db, n, formId, fields);
                await CheckOwnFilesAsync(db, n, entryId, values.Where(value => value.FileId is not null).Select(value => value.FileId!.Value).Distinct().ToArray());
                foreach (var chunk in fields.Chunk(IdsPerStatement))
                {
                    await db.ExecAsync(
                        $"DELETE FROM {n.T(FormBuilderTables.EntryValue)} WHERE {n.C("entryId")} = @0 AND {n.C("fieldId")} IN ({Parameters(1, chunk.Length)})",
                        [entryId, .. chunk.Cast<object?>()]);
                }

                await InsertValuesAsync(db, n, entryId, values);
                return await FindAsync(db, n, $"{n.C("id")} = @0", entryId);
            },
            cancellationToken);
    }

    internal static StoredEntryValue ValueFromDto(EntryValueDto d) => new(
        d.Id,
        d.EntryId,
        d.FieldId,
        d.RowIndex,
        d.ValueIndex,
        d.Text,
        d.TextKey,
        d.Number,
        d.Date is { } date ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : null,
        d.Bool,
        d.FileId);

    /// <summary>
    /// Refuses values that name a field outside the form's catalogue (<see cref="FormBuilderConflict.MissingField"/>). A
    /// locking read: a field that a delete removes at the same moment is either gone already or waits for this unit.
    /// </summary>
    private static async Task CheckFieldsAsync(IUmbracoDatabase db, SqlNames n, int formId, int[] fieldIds)
    {
        var found = 0;
        foreach (var chunk in fieldIds.Chunk(IdsPerStatement))
        {
            found += await db.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.FormField)}{n.LockingRead} WHERE {n.C("formId")} = @0 AND {n.C("id")} IN ({Parameters(1, chunk.Length)})",
                [formId, .. chunk.Cast<object?>()]);
        }

        if (found != fieldIds.Length)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.MissingField, "A value names a field that is not in the catalogue of the entry's form.");
        }
    }

    /// <summary>
    /// Refuses file values that name a file of no entry or of another one (<see cref="FormBuilderConflict.MissingFile"/>),
    /// with a locking read like the fields'.
    /// </summary>
    private static async Task CheckOwnFilesAsync(IUmbracoDatabase db, SqlNames n, long entryId, int[] fileIds)
    {
        var found = 0;
        foreach (var chunk in fileIds.Chunk(IdsPerStatement))
        {
            found += await db.ScalarAsync<int>(
                $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.EntryFile)}{n.LockingRead} WHERE {n.C("entryId")} = @0 AND {n.C("id")} IN ({Parameters(1, chunk.Length)})",
                [entryId, .. chunk.Cast<object?>()]);
        }

        if (found != fileIds.Length)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.MissingFile, "A value names a file that is not one of the entry's files.");
        }
    }

    /// <summary>Inserts values, <see cref="ValuesPerStatement"/> rows to a statement.</summary>
    private static async Task InsertValuesAsync(IUmbracoDatabase db, SqlNames n, long entryId, IReadOnlyList<NewEntryValue> values)
    {
        var columns = string.Join(", ", ValueColumns.Select(column => n.C(column)));
        foreach (var chunk in values.Chunk(ValuesPerStatement))
        {
            var args = new List<object?>(chunk.Length * ValueColumns.Length);
            var rows = new List<string>(chunk.Length);
            foreach (var value in chunk)
            {
                rows.Add("(" + Parameters(args.Count, ValueColumns.Length) + ")");
                args.AddRange(
                [
                    entryId,
                    value.FieldId,
                    value.RowIndex,
                    value.ValueIndex,
                    value.Text,
                    value.IsText ? EntryTextKey.For(value.Text!) : null,
                    value.Number,
                    value.Date,
                    value.Bool,
                    value.FileId,
                ]);
            }

            await db.ExecAsync($"INSERT INTO {n.T(FormBuilderTables.EntryValue)} ({columns}) VALUES {string.Join(", ", rows)}", [.. args]);
        }
    }

    /// <summary>
    /// Refuses values the columns cannot hold as meant: a field id below 1, a negative row or item, two typed columns,
    /// no column at all, a number that is not finite, a date that is not UTC, or two values in the same place.
    /// </summary>
    private static void CheckValues(IReadOnlyList<NewEntryValue> values, string parameter)
    {
        var places = new HashSet<(int Field, int Row, int Item)>();
        foreach (var value in values)
        {
            Require(value is not null, "A value is not null.");
            Require(value!.FieldId > 0, "A value's field id is 1 or more.");
            Require(value.RowIndex >= 0 && value.ValueIndex >= 0, "A value's row and item are 0 or more.");
            var typed = (value.Number is null ? 0 : 1) + (value.Date is null ? 0 : 1) + (value.Bool is null ? 0 : 1) + (value.FileId is null ? 0 : 1);
            Require(typed <= 1, "A value fills at most one typed column.");
            Require(typed == 1 || value.Text is not null, "A value holds text or a typed value.");
            Require(value.Number is null || double.IsFinite(value.Number.Value), "A value's number is finite.");
            Require(value.Date is null || value.Date.Value.Kind == DateTimeKind.Utc, "A value's date is UTC.");
            Require(value.FileId is null or > 0, "A value's file id is 1 or more.");
            Require(places.Add((value.FieldId, value.RowIndex, value.ValueIndex)), "Two values are in the same place of the same field.");
        }

        void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, parameter);
            }
        }
    }
}
