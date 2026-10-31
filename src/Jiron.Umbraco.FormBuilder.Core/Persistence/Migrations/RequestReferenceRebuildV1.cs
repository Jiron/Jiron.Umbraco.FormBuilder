using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 4 (<see cref="FormBuilderMigrationPlan.StateRequestReferenceRebuild"/>): asks for a rebuild of the "used on"
/// index (ADR-0017, ADR-0131), which the reference job then runs in bounded parts: a site that installs the package, or
/// had content saved while the index was not kept, gets every page that picks a form. It only stores the request in
/// <c>jfbKeyValue</c>, a walk from the first document, and leaves one that is there as it is; reading content here
/// would need Umbraco's services inside a migration.
/// </summary>
internal sealed class RequestReferenceRebuildV1(IMigrationContext context, TimeProvider timeProvider) : AsyncMigrationBase(context)
{
    /// <summary>The request's key in <c>jfbKeyValue</c>; its value is where the walk goes on (ADR-0131).</summary>
    public const string RebuildKey = "references-rebuild";

    /// <summary>The value of a walk that starts from the first document.</summary>
    public const string FromStart = "0";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.KeyValue);
        var key = SqlSyntax.GetQuotedColumnName("key");
        var value = SqlSyntax.GetQuotedColumnName("value");
        var updated = SqlSyntax.GetQuotedColumnName("updatedUtc");
        await Database.ExecuteAsync(
            Sql($"INSERT INTO {table} ({key}, {value}, {updated}) SELECT @0, @1, @2 WHERE NOT EXISTS (SELECT 1 FROM {table} WHERE {key} = @0)", RebuildKey, FromStart, timeProvider.GetUtcNow().UtcDateTime));
    }
}
