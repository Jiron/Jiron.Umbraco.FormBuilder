using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Searches the whole audit log for an entry's values (ADR-0041): every step that records actions about entries proves
/// with it that none of the values reached the log, in any column, also as an escape in the details' JSON. Keys are left
/// out of the search, so a value of digits never matches inside one. It finds a value only as it is written, ignoring
/// case: a value split up or written another way would pass, so the guard stays the rule that details never take a value
/// of an entry.
/// </summary>
internal static partial class AuditLogSearch
{
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Asserts that the log holds none of <paramref name="values"/>, ignoring case, and that it holds something.</summary>
    public static async Task AssertHoldsNoneOfAsync(FormBuilderDatabase database, IEnumerable<string> values)
    {
        var (rows, text) = await ReadAsync(database);

        Assert.That(rows, Is.Not.Zero, "the log was searched");
        Assert.That(Find(text, values), Is.Empty, "the audit log holds a value");
    }

    /// <summary>Those of <paramref name="values"/> that the log holds, ignoring case and keys, in the order given.</summary>
    public static async Task<IReadOnlyList<string>> FindAsync(FormBuilderDatabase database, IEnumerable<string> values) =>
        Find((await ReadAsync(database)).Text, values);

    private static List<string> Find(string text, IEnumerable<string> values) =>
        values.Where(value => text.Contains(value, StringComparison.OrdinalIgnoreCase)).ToList();

    private static async Task<(int Rows, string Text)> ReadAsync(FormBuilderDatabase database)
    {
        var rows = await database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.FetchListAsync<AuditEntryDto>($"SELECT * FROM {n.T(FormBuilderTables.AuditEntry)}");
            });
        var text = string.Join(
            '\n',
            rows.Select(row => string.Join('\t', row.Action, row.EntityType, row.EntityKey, row.UserKey, row.Details, JsonNode.Parse(row.Details)!.ToJsonString(Unescaped))));
        return (rows.Count, Key().Replace(text, "{key}"));
    }

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Key();
}
