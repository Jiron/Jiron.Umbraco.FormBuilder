using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 5 (<see cref="FormBuilderMigrationPlan.StateFileScanIndex"/>): the index that finds the files waiting for a scan
/// (ADR-0144) without reading every file of the site. It writes the statement out by hand, as a step that has shipped never
/// changes, so it does not depend on any class that may.
/// </summary>
internal sealed class AddFileScanIndexV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEntryFile_scanState_id";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (IndexExists(IndexName))
        {
            return;
        }

        var index = SqlSyntax.GetQuotedName(IndexName);
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.EntryFile);
        var state = SqlSyntax.GetQuotedColumnName("scanState");
        var id = SqlSyntax.GetQuotedColumnName("id");
        await Database.ExecuteAsync(Sql($"CREATE INDEX {index} ON {table} ({state}, {id})").SQL);
    }
}
