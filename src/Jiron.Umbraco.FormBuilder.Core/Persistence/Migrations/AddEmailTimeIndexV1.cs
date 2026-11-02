using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 7 (<see cref="FormBuilderMigrationPlan.StateEmailTimeIndex"/>): the index that counts the last hour's mail to visitors
/// (ADR-0159). The count holds what it reads until its unit ends, so it must read that hour and not 30 days of mail. It writes
/// its names out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class AddEmailTimeIndexV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEmail_createdUtc_recipientKey";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (IndexExists(IndexName))
        {
            return;
        }

        var index = SqlSyntax.GetQuotedName(IndexName);
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.Email);
        var created = SqlSyntax.GetQuotedColumnName("createdUtc");
        var key = SqlSyntax.GetQuotedColumnName("recipientKey");
        await Database.ExecuteAsync(Sql($"CREATE INDEX {index} ON {table} ({created}, {key})").SQL);
    }
}
