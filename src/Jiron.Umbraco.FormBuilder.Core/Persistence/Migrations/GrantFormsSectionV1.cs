using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Extensions;
using UmbracoConstants = Umbraco.Cms.Core.Constants;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 3 (<see cref="FormBuilderMigrationPlan.StateGrantSection"/>): grants the Forms section to Umbraco's
/// administrators group, once (ADR-0013). A <c>jfbKeyValue</c> flag records the grant, so a rerun never grants again
/// and an administrator's later removal stands. When the group does not exist, the step warns and grants nothing.
/// </summary>
/// <remarks>
/// It writes <c>umbracoUserGroup2App</c> directly, as Umbraco's own migrations do: the user group service checks
/// permissions and publishes notifications that may fail inside a migration. The user group cache is refreshed on
/// every server once the plan has run (<see cref="FormBuilderMigrationsExecutedHandler"/>). Other groups get the
/// section from code when they receive a Jiron Form Builder permission (12.4a).
/// </remarks>
internal sealed class GrantFormsSectionV1(IMigrationContext context, TimeProvider timeProvider) : AsyncMigrationBase(context)
{
    /// <summary>The flag's key in <c>jfbKeyValue</c>; its value lists the groups that were granted the section.</summary>
    public const string GrantedFlag = "forms-section-granted";

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        if (await Database.ExecuteScalarAsync<int>(Sql().SelectCount().From<KeyValueDto>().Where<KeyValueDto>(flag => flag.Key == GrantedFlag)) > 0)
        {
            FormBuilderMigrationLog.SectionGrantedBefore(Logger);
            return;
        }

        var groups = SqlSyntax.GetQuotedTableName(UmbracoConstants.DatabaseSchema.Tables.UserGroup);
        var grants = SqlSyntax.GetQuotedTableName(UmbracoConstants.DatabaseSchema.Tables.UserGroup2App);
        var id = SqlSyntax.GetQuotedColumnName("id");
        var groupAlias = SqlSyntax.GetQuotedColumnName("userGroupAlias");
        var groupId = SqlSyntax.GetQuotedColumnName("userGroupId");
        var app = SqlSyntax.GetQuotedColumnName("app");
        var alias = UmbracoConstants.Security.AdminGroupAlias;

        var granted = string.Empty;
        var admins = await Database.ExecuteScalarAsync<int?>(Sql($"SELECT {id} FROM {groups} WHERE {groupAlias} = @0", alias));
        if (admins is null)
        {
            FormBuilderMigrationLog.GroupMissing(Logger, alias);
        }
        else
        {
            var has = await Database.ExecuteScalarAsync<int>(
                Sql($"SELECT COUNT(*) FROM {grants} WHERE {groupId} = @0 AND {app} = @1", admins.Value, FormBuilderConstants.SectionAlias));
            if (has == 0)
            {
                await Database.ExecuteAsync(
                    Sql($"INSERT INTO {grants} ({groupId}, {app}) VALUES (@0, @1)", admins.Value, FormBuilderConstants.SectionAlias));
            }

            FormBuilderMigrationLog.SectionGranted(Logger, alias);
            granted = alias;
        }

        await Database.InsertAsync(new KeyValueDto
        {
            Key = GrantedFlag,
            Value = granted,
            UpdatedUtc = timeProvider.GetUtcNow().UtcDateTime,
        });
    }

    // Frozen snapshot of jfbKeyValue as of step 3. Never edit.
    [TableName(FormBuilderTables.KeyValue)]
    [PrimaryKey("key", AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class KeyValueDto
    {
        [Column("key")]
        public string Key { get; set; } = string.Empty;

        [Column("value")]
        public string? Value { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }
}
