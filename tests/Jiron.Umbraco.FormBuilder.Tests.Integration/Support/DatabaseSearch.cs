using Jiron.Umbraco.FormBuilder.Core.Persistence;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Searches every Jiron Form Builder table for a text, as the privacy tests do after an erase or once a value kept only until
/// the actions have run is gone (ADR-0009, ADR-0223, ADR-0225): a table added later is searched too.
/// </summary>
internal static class DatabaseSearch
{
    /// <summary>Each column, as <c>table.column</c>, that holds <paramref name="text"/> in any case in any row.</summary>
    /// <param name="database">The units of work.</param>
    /// <param name="text">The text.</param>
    public static async Task<IReadOnlyList<string>> TablesHoldingAsync(FormBuilderDatabase database, string text)
    {
        ArgumentNullException.ThrowIfNull(database);
        var found = new List<string>();
        foreach (var table in FormBuilderTables.All)
        {
            var rows = await database.ReadAsync(db => db.FetchListAsync<dynamic>($"SELECT * FROM {new SqlNames(db).T(table)}"));
            foreach (IDictionary<string, object?> row in rows)
            {
                found.AddRange(row.Where(cell => cell.Value is string value && value.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(cell => $"{table}.{cell.Key}"));
            }
        }

        return [.. found.Distinct()];
    }
}
