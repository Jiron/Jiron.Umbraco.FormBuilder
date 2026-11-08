using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 13 of the plan (ADR-0215): <c>jfbFormLibraryUse</c> learns the themes forms name and the email layouts their email
/// actions name, filled for the forms there are from their draft and live versions. Its reading of a stored definition is
/// frozen here, as a migration's must be; a unit test holds it to the model's on the full fixture and on generated
/// definitions. Run twice, it writes the same rows: it replaces the rows of its two kinds.
/// </summary>
/// <param name="context">The migration's context.</param>
internal sealed class AddThemeAndLayoutUsesV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    // The namespace of the themes' keys, as FormLibraryUses.ThemeKey had it when this step was written.
    private static readonly Guid ThemeNamespace = new("4b2c86f1-0d37-4e9a-a5c3-71e98d0f2b64");

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        var uses = SqlSyntax.GetQuotedTableName(FormBuilderTables.FormLibraryUse);
        var forms = SqlSyntax.GetQuotedTableName(FormBuilderTables.Form);
        var versions = SqlSyntax.GetQuotedTableName(FormBuilderTables.FormVersion);
        string C(string column) => SqlSyntax.GetQuotedColumnName(column);
        await Database.ExecuteAsync(Sql($"DELETE FROM {uses} WHERE {C("itemKind")} IN (@0, @1)", "theme", "emailLayout"));
        var rows = await Database.FetchAsync<VersionRow>(Sql(
            $"""
            SELECT f.{C("id")} AS {C("formId")}, v.{C("kind")} AS {C("kind")}, v.{C("definition")} AS {C("definition")}
            FROM {forms} f INNER JOIN {versions} v ON v.{C("id")} = f.{C("draftVersionId")} OR v.{C("id")} = f.{C("publishedVersionId")}
            """));
        foreach (var row in rows)
        {
            foreach (var (kind, key, count) in UsesOf(row.Definition))
            {
                await Database.InsertAsync(new AddFormLibraryUsesV1.FormLibraryUseDto { FormId = row.FormId, VersionKind = row.Kind, ItemKind = kind, ItemKey = key, Uses = count });
            }
        }
    }

    /// <summary>
    /// The theme a stored definition names and the layouts its email actions name, each with how many actions: what the
    /// model's <c>FormLibraryUses</c> finds of those two kinds, in its order. A text that is not a definition gives none.
    /// </summary>
    /// <param name="definition">The stored JSON.</param>
    internal static IReadOnlyList<(string Kind, Guid Key, int Count)> UsesOf(string definition)
    {
        try
        {
            using var document = JsonDocument.Parse(definition);
            var root = document.RootElement;
            var found = new List<(string Kind, Guid Key, int Count)>();
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("settings", out var settings) && settings.ValueKind == JsonValueKind.Object
                && settings.TryGetProperty("appearance", out var appearance) && appearance.ValueKind == JsonValueKind.Object
                && appearance.TryGetProperty("theme", out var theme) && theme.ValueKind == JsonValueKind.String
                && theme.GetString() is { } alias && !string.IsNullOrWhiteSpace(alias))
            {
                found.Add(("theme", ThemeKey(alias), 1));
            }

            var layouts = new SortedDictionary<Guid, int>();
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array)
            {
                foreach (var action in actions.EnumerateArray())
                {
                    if (action.ValueKind == JsonValueKind.Object
                        && action.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "email"
                        && action.TryGetProperty("settings", out var own) && own.ValueKind == JsonValueKind.Object
                        && own.TryGetProperty("layout", out var layout) && layout.ValueKind == JsonValueKind.String
                        && Guid.TryParse(layout.GetString(), out var key))
                    {
                        layouts[key] = layouts.GetValueOrDefault(key) + 1;
                    }
                }
            }

            found.AddRange(layouts.Select(each => ("emailLayout", each.Key, each.Value)));
            return found;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static Guid ThemeKey(string alias)
    {
        var hash = SHA256.HashData([.. ThemeNamespace.ToByteArray(bigEndian: true), .. Encoding.UTF8.GetBytes(alias.Trim().ToLowerInvariant())]);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x80);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }

    // A form's draft or live version as the backfill reads it.
    [NPoco.ExplicitColumns]
    private sealed class VersionRow
    {
        [NPoco.Column("formId")]
        public int FormId { get; set; }

        [NPoco.Column("kind")]
        public string Kind { get; set; } = string.Empty;

        [NPoco.Column("definition")]
        public string Definition { get; set; } = string.Empty;
    }
}
