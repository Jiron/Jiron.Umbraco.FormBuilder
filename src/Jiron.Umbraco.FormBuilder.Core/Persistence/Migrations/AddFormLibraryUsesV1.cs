using System.Data;
using System.Text.Json;
using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseModelDefinitions;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 12 (<see cref="FormBuilderMigrationPlan.StateFormLibraryUses"/>): <c>jfbFormLibraryUse</c>, what of the library each
/// form's draft and live version use (ADR-0214), for the library's "used by" and its delete guards; then the rows of the
/// forms that exist, read from their versions as this step reads them, which never changes: the option lists their fields
/// take their options from. It writes its names out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class AddFormLibraryUsesV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index that finds an item's forms.</summary>
    public const string IndexName = "IX_jfbFormLibraryUse_itemKind_itemKey";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (TableExists(FormBuilderTables.FormLibraryUse))
        {
            return;
        }

        SqlSyntax.HandleCreateTable(Database, DefinitionFactory.GetTableDefinition(typeof(FormLibraryUseDto), SqlSyntax));

        var forms = SqlSyntax.GetQuotedTableName(FormBuilderTables.Form);
        var versions = SqlSyntax.GetQuotedTableName(FormBuilderTables.FormVersion);
        string C(string column) => SqlSyntax.GetQuotedColumnName(column);
        var rows = await Database.FetchAsync<VersionRow>(Sql(
            $"""
            SELECT f.{C("id")} AS {C("formId")}, v.{C("kind")} AS {C("kind")}, v.{C("definition")} AS {C("definition")}
            FROM {forms} f INNER JOIN {versions} v ON v.{C("id")} = f.{C("draftVersionId")} OR v.{C("id")} = f.{C("publishedVersionId")}
            """));
        foreach (var row in rows)
        {
            foreach (var (list, uses) in ListsOf(row.Definition))
            {
                await Database.InsertAsync(new FormLibraryUseDto { FormId = row.FormId, VersionKind = row.Kind, ItemKind = "optionList", ItemKey = list, Uses = uses });
            }
        }
    }

    /// <summary>
    /// The option lists a stored definition's fields take their options from, with how many fields take each: every field
    /// of every group of every page and the fields inside it, whose <c>options</c> have <c>source</c> <c>list</c> and a
    /// <c>list</c>. A definition that does not read gives none, as it names nothing a form could show.
    /// </summary>
    internal static IReadOnlyDictionary<Guid, int> ListsOf(string definition)
    {
        var lists = new Dictionary<Guid, int>();
        try
        {
            using var document = JsonDocument.Parse(definition);
            foreach (var page in Items(document.RootElement, "pages"))
            {
                foreach (var group in Items(page, "groups"))
                {
                    Fields(group, lists);
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<Guid, int>();
        }

        return lists;
    }

    private static void Fields(JsonElement parent, Dictionary<Guid, int> lists)
    {
        foreach (var field in Items(parent, "fields"))
        {
            if (field.TryGetProperty("options", out var options)
                && options.ValueKind == JsonValueKind.Object
                && options.TryGetProperty("source", out var source)
                && source.ValueKind == JsonValueKind.String
                && source.GetString() == "list"
                && options.TryGetProperty("list", out var list)
                && list.ValueKind == JsonValueKind.String
                && Guid.TryParse(list.GetString(), out var key))
            {
                lists[key] = lists.GetValueOrDefault(key) + 1;
            }

            Fields(field, lists);
        }
    }

    private static JsonElement[] Items(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array
            ? [.. items.EnumerateArray()]
            : [];

    // A form's draft or live version as the backfill reads it.
    [ExplicitColumns]
    private sealed class VersionRow
    {
        [Column("formId")]
        public int FormId { get; set; }

        [Column("kind")]
        public string Kind { get; set; } = string.Empty;

        [Column("definition")]
        public string Definition { get; set; } = string.Empty;
    }

    /// <summary>
    /// The frozen snapshot of <c>jfbFormLibraryUse</c>. Never edit. One row per form, version (<c>draft</c> or
    /// <c>published</c>), kind of item (<c>optionList</c>) and item; a form deleted for good takes its rows with it.
    /// </summary>
    [TableName(FormBuilderTables.FormLibraryUse)]
    [PrimaryKey(["formId", "versionKind", "itemKind", "itemKey"], AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class FormLibraryUseDto
    {
        [Column("formId")]
        [PrimaryKeyColumn(AutoIncrement = false, Name = "PK_jfbFormLibraryUse", OnColumns = "formId, versionKind, itemKind, itemKey")]
        [ForeignKey(typeof(CreateSchemaV1.FormDto), Name = "FK_jfbFormLibraryUse_jfbForm", OnDelete = Rule.Cascade)]
        public int FormId { get; set; }

        // draft or published.
        [Column("versionKind")]
        [Length(16)]
        public string VersionKind { get; set; } = string.Empty;

        // optionList.
        [Column("itemKind")]
        [Length(16)]
        [Index(IndexTypes.NonClustered, Name = IndexName, ForColumns = "itemKind,itemKey")]
        public string ItemKind { get; set; } = string.Empty;

        [Column("itemKey")]
        public Guid ItemKey { get; set; }

        // How many of the version's elements use the item: fields, for an option list.
        [Column("uses")]
        public int Uses { get; set; }
    }
}
