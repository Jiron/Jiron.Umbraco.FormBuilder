using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Holds rows until the unit ends (ADR-0036, ADR-0038): an update that changes nothing but takes the rows' write locks,
/// so they cannot change or go away meanwhile, and units that hold the same row take turns. A unit that has nothing to
/// write before it reads makes a hold its first statement, as a write's first statement must be a write (ADR-0007).
/// </summary>
internal static class RowHolds
{
    /// <summary>
    /// Holds the rows of <paramref name="table"/> that <paramref name="where"/> finds, with its arguments
    /// <paramref name="args"/>, and returns how many it holds.
    /// </summary>
    public static Task<int> HoldAsync(IUmbracoDatabase db, SqlNames n, string table, string where, params object?[] args) =>
        db.ExecAsync($"UPDATE {n.T(table)} SET {n.C("updatedUtc")} = {n.C("updatedUtc")} WHERE {where}", args);

    /// <summary>
    /// Holds an entry's row, so the entry cannot go away before a new row's foreign key to it is checked. False when the
    /// entry does not exist.
    /// </summary>
    public static async Task<bool> HoldEntryAsync(IUmbracoDatabase db, SqlNames n, long entryId) =>
        await HoldAsync(db, n, FormBuilderTables.Entry, $"{n.C("id")} = @0", entryId) == 1;

    /// <summary>
    /// Holds a form's row, so units that touch what belongs to the form take turns and always lock the form first.
    /// False when the form does not exist.
    /// </summary>
    public static async Task<bool> HoldFormAsync(IUmbracoDatabase db, SqlNames n, int formId) =>
        await HoldAsync(db, n, FormBuilderTables.Form, $"{n.C("id")} = @0", formId) == 1;
}
