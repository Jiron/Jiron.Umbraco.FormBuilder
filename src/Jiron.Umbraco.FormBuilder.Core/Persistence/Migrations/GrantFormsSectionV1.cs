using Umbraco.Cms.Infrastructure.Migrations;
using UmbracoConstants = Umbraco.Cms.Core.Constants;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 3 (<see cref="FormBuilderMigrationPlan.StateGrantSection"/>): grants the Form Builder section to Umbraco's
/// administrators group, once (ADR-0013). A <c>jfbKeyValue</c> flag records the grant, so a rerun never grants again
/// and an administrator's later removal stands. When the group does not exist, the step warns and grants nothing.
/// </summary>
/// <remarks>
/// It writes <c>umbracoUserGroup2App</c> directly, as Umbraco's own migrations do: the user group service checks
/// permissions and publishes notifications that may fail inside a migration. The user group cache is refreshed on
/// every server once the plan has run (<see cref="FormBuilderMigrationsExecutedHandler"/>). Other groups get the
/// section from an administrator in Umbraco's Users section; the Access settings list each group that lacks it (ADR-0218).
/// It writes before it reads (27.1): the grant and then the flag are each one statement that checks what it needs as it
/// writes, so beside a second process on the same SQLite file it waits its turn for the write lock instead of holding a
/// read its write can never get past; only the log's words are read afterwards.
/// </remarks>
internal sealed class GrantFormsSectionV1(IMigrationContext context, TimeProvider timeProvider) : AsyncMigrationBase(context)
{
    /// <summary>The flag's key in <c>jfbKeyValue</c>; its value lists the groups that were granted the section.</summary>
    public const string GrantedFlag = "forms-section-granted";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        var flags = SqlSyntax.GetQuotedTableName(FormBuilderTables.KeyValue);
        var groups = SqlSyntax.GetQuotedTableName(UmbracoConstants.DatabaseSchema.Tables.UserGroup);
        var grants = SqlSyntax.GetQuotedTableName(UmbracoConstants.DatabaseSchema.Tables.UserGroup2App);
        var key = SqlSyntax.GetQuotedColumnName("key");
        var value = SqlSyntax.GetQuotedColumnName("value");
        var updated = SqlSyntax.GetQuotedColumnName("updatedUtc");
        var id = SqlSyntax.GetQuotedColumnName("id");
        var groupAlias = SqlSyntax.GetQuotedColumnName("userGroupAlias");
        var groupId = SqlSyntax.GetQuotedColumnName("userGroupId");
        var app = SqlSyntax.GetQuotedColumnName("app");
        var alias = UmbracoConstants.Security.AdminGroupAlias;
        var unflagged = $"NOT EXISTS (SELECT 1 FROM {flags} f WHERE f.{key} = @0)";

        // The administrators get the section unless the flag says it was granted before, or they have it already.
        await Database.ExecuteAsync(Sql(
            $"INSERT INTO {grants} ({groupId}, {app}) SELECT g.{id}, @1 FROM {groups} g WHERE g.{groupAlias} = @2 AND {unflagged} "
            + $"AND NOT EXISTS (SELECT 1 FROM {grants} a WHERE a.{groupId} = g.{id} AND a.{app} = @1)",
            GrantedFlag,
            FormBuilderConstants.SectionAlias,
            alias));

        // The flag, naming the group when there is one, so a rerun never grants again and an administrator's later removal stands.
        var flagged = await Database.ExecuteAsync(Sql(
            $"INSERT INTO {flags} ({key}, {value}, {updated}) SELECT @0, COALESCE((SELECT g.{groupAlias} FROM {groups} g WHERE g.{groupAlias} = @1), @2), @3 WHERE {unflagged}",
            GrantedFlag,
            alias,
            string.Empty,
            timeProvider.GetUtcNow().UtcDateTime));
        if (flagged == 0)
        {
            FormBuilderMigrationLog.SectionGrantedBefore(Logger);
        }
        else if (await Database.ExecuteScalarAsync<string?>(Sql($"SELECT {value} FROM {flags} WHERE {key} = @0", GrantedFlag)) is { Length: > 0 } granted)
        {
            FormBuilderMigrationLog.SectionGranted(Logger, granted);
        }
        else
        {
            FormBuilderMigrationLog.GroupMissing(Logger, alias);
        }
    }
}
