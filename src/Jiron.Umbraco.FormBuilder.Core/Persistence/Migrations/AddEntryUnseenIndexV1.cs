using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 11 (<see cref="FormBuilderMigrationPlan.StateEntryUnseenIndex"/>): the index that finds the entries nobody opened
/// yet, newest first, across forms (ADR-0209): the Inbox lists them first, and its badge counts them. It writes its names
/// out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class AddEntryUnseenIndexV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEntry_firstSeenUtc_submittedUtc";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (IndexExists(IndexName))
        {
            return;
        }

        var index = SqlSyntax.GetQuotedName(IndexName);
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.Entry);
        var seen = SqlSyntax.GetQuotedColumnName("firstSeenUtc");
        var submitted = SqlSyntax.GetQuotedColumnName("submittedUtc");
        await Database.ExecuteAsync(Sql($"CREATE INDEX {index} ON {table} ({seen}, {submitted})").SQL);
    }
}
