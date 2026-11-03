using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 9 (<see cref="FormBuilderMigrationPlan.StateEntryTimeIndex"/>): the index that finds the newest entries of every form
/// (ADR-0165). The other time indexes start at the form, so a list across forms would sort all of a site's entries for ten.
/// It writes its names out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class AddEntryTimeIndexV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEntry_submittedUtc";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (IndexExists(IndexName))
        {
            return;
        }

        var index = SqlSyntax.GetQuotedName(IndexName);
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.Entry);
        var submitted = SqlSyntax.GetQuotedColumnName("submittedUtc");
        await Database.ExecuteAsync(Sql($"CREATE INDEX {index} ON {table} ({submitted})").SQL);
    }
}
