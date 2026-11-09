using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 14 (<see cref="FormBuilderMigrationPlan.StateEntryFileNameIndex"/>): the index of the files' names in the file store,
/// which retention's sweep of the store looks a folder's names up on, to find the bytes no row names any more (ADR-0222).
/// It writes its names out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class AddEntryFileNameIndexV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEntryFile_storedName";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (IndexExists(IndexName))
        {
            return;
        }

        var index = SqlSyntax.GetQuotedName(IndexName);
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.EntryFile);
        var name = SqlSyntax.GetQuotedColumnName("storedName");
        await Database.ExecuteAsync(Sql($"CREATE INDEX {index} ON {table} ({name})").SQL);
    }
}
