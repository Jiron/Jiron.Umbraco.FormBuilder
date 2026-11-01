using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 6 (<see cref="FormBuilderMigrationPlan.StateFileScanOrder"/>): the index of the files waiting for a scan also tells
/// those an entry holds from the uploads no entry took (ADR-0148), so a flood of uploads does not cost the search for the
/// others. It writes its names out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class ChangeFileScanIndexV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEntryFile_scanState_entryId_id";

    private const string OldIndexName = "IX_jfbEntryFile_scanState_id";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (IndexExists(OldIndexName))
        {
            Delete.Index(OldIndexName).OnTable(FormBuilderTables.EntryFile).Do();
        }

        if (IndexExists(IndexName))
        {
            return;
        }

        var index = SqlSyntax.GetQuotedName(IndexName);
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.EntryFile);
        var state = SqlSyntax.GetQuotedColumnName("scanState");
        var entry = SqlSyntax.GetQuotedColumnName("entryId");
        var id = SqlSyntax.GetQuotedColumnName("id");
        await Database.ExecuteAsync(Sql($"CREATE INDEX {index} ON {table} ({state}, {entry}, {id})").SQL);
    }
}
