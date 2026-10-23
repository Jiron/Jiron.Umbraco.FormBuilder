using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

// Frozen snapshots of the settings, access and operations tables as step 1 creates them. Never edit.
internal sealed partial class CreateSchemaV1
{
    // A backoffice user's own choices, the same on every device: columns, filters, export choices, the digest.
    [TableName(FormBuilderTables.UserSetting)]
    [PrimaryKey(["userKey", "name"], AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class UserSettingDto
    {
        [Column("userKey")]
        [PrimaryKeyColumn(AutoIncrement = false, Name = "PK_jfbUserSetting", OnColumns = "userKey, name")]
        public Guid UserKey { get; set; }

        [Column("name")]
        [Length(64)]
        public string Name { get; set; } = string.Empty;

        [Column("json")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Json { get; set; } = "{}";

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    // The grants of Jiron Form Builder's permissions to user groups (ADR-0013).
    [TableName(FormBuilderTables.Permission)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class PermissionDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("userGroupKey")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbPermission_userGroupKey_targetType_targetKey", ForColumns = "userGroupKey,targetType,targetKey")]
        public Guid UserGroupKey { get; set; }

        // Global, Folder or Form.
        [Column("targetType")]
        [Length(16)]
        public string TargetType { get; set; } = string.Empty;

        // Guid.Empty for a global grant: keys never hold NULL (section 4.5).
        [Column("targetKey")]
        public Guid TargetKey { get; set; }

        // Flags.
        [Column("permissions")]
        public int Permissions { get; set; }
    }

    // One backoffice settings section per scope (ADR-0008).
    [TableName(FormBuilderTables.Setting)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class SettingDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        // "global", or the key of a site's root content.
        [Column("scope")]
        [Length(64)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbSetting_scope_section", ForColumns = "scope,section")]
        public string Scope { get; set; } = string.Empty;

        [Column("section")]
        [Length(64)]
        public string Section { get; set; } = string.Empty;

        [Column("schemaVersion")]
        public int SchemaVersion { get; set; }

        // The optimistic version a save sends back.
        [Column("version")]
        public int Version { get; set; }

        // camelCase JSON; a missing property means the default.
        [Column("json")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Json { get; set; } = "{}";

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        // Null when Jiron Form Builder wrote the row itself (this migration).
        [Column("updatedByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? UpdatedByUserKey { get; set; }
    }

    // Where content uses a form: Umbraco's relations link only umbracoNode rows (ADR-0017).
    [TableName(FormBuilderTables.Reference)]
    [PrimaryKey(["formId", "contentKey", "propertyAlias", "culture"], AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class ReferenceDto
    {
        [Column("formId")]
        [PrimaryKeyColumn(AutoIncrement = false, Name = "PK_jfbReference", OnColumns = "formId, contentKey, propertyAlias, culture")]
        public int FormId { get; set; }

        [Column("contentKey")]
        public Guid ContentKey { get; set; }

        [Column("propertyAlias")]
        [Length(255)]
        public string PropertyAlias { get; set; } = string.Empty;

        // "" for an invariant property.
        [Column("culture")]
        [Length(32)]
        public string Culture { get; set; } = string.Empty;

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    // The audit log. Never field values.
    [TableName(FormBuilderTables.AuditEntry)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class AuditEntryDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        [Column("occurredUtc")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbAuditEntry_occurredUtc")]
        public DateTime OccurredUtc { get; set; }

        // Null for Jiron Form Builder's own actions (jobs, retention).
        [Column("userKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? UserKey { get; set; }

        [Column("action")]
        [Length(64)]
        public string Action { get; set; } = string.Empty;

        [Column("entityType")]
        [Length(64)]
        public string EntityType { get; set; } = string.Empty;

        [Column("entityKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbAuditEntry_entityKey")]
        public Guid? EntityKey { get; set; }

        [Column("details")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Details { get; set; } = "{}";
    }

    // Daily counters per form, metric and dimension; new metrics need no migration, and nothing here is personal.
    [TableName(FormBuilderTables.DailyStat)]
    [PrimaryKey(["day", "formId", "metric", "dimension"], AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class DailyStatDto
    {
        // Midnight UTC of the day.
        [Column("day")]
        [PrimaryKeyColumn(AutoIncrement = false, Name = "PK_jfbDailyStat", OnColumns = "day, formId, metric, dimension")]
        public DateTime Day { get; set; }

        [Column("formId")]
        public int FormId { get; set; }

        [Column("metric")]
        [Length(64)]
        public string Metric { get; set; } = string.Empty;

        // "" when the metric has none: keys never hold NULL (section 4.5).
        [Column("dimension")]
        [Length(128)]
        public string Dimension { get; set; } = string.Empty;

        [Column("value")]
        public long Value { get; set; }
    }
}
