using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 2 (<see cref="FormBuilderMigrationPlan.StateDefaultSettings"/>): the global row of each backoffice settings
/// section of ADR-0008, at schema version 1 and version 1, holding no values (ADR-0033). Defaults live in code, and a
/// missing property means "use the default", so these rows never freeze a default; they give every later save of a
/// global section a row and a version to check. Idempotent: a section that already has its global row is skipped.
/// </summary>
internal sealed class CreateDefaultSettingsV1(IMigrationContext context, TimeProvider timeProvider) : AsyncMigrationBase(context)
{
    /// <summary>The scope of the values every site shares.</summary>
    public const string GlobalScope = "global";

    /// <summary>The backoffice sections of ADR-0008. Access is stored in <c>jfbPermission</c> instead.</summary>
    internal static readonly IReadOnlyList<string> Sections = ["general", "email", "protection", "files", "privacy", "integrations"];

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var added = 0;
        foreach (var section in Sections)
        {
            var existing = await Database.ExecuteScalarAsync<int>(
                Sql().SelectCount().From<SettingDto>().Where<SettingDto>(row => row.Scope == GlobalScope && row.Section == section));
            if (existing > 0)
            {
                continue;
            }

            await Database.InsertAsync(new SettingDto
            {
                Scope = GlobalScope,
                Section = section,
                SchemaVersion = 1,
                Version = 1,
                Json = "{}",
                UpdatedUtc = now,
            });
            added++;
        }

        FormBuilderMigrationLog.DefaultSettings(Logger, added);
    }

    // Frozen snapshot of jfbSetting as of step 2. Never edit.
    [TableName(FormBuilderTables.Setting)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class SettingDto
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("scope")]
        public string Scope { get; set; } = string.Empty;

        [Column("section")]
        public string Section { get; set; } = string.Empty;

        [Column("schemaVersion")]
        public int SchemaVersion { get; set; }

        [Column("version")]
        public int Version { get; set; }

        [Column("json")]
        public string Json { get; set; } = "{}";

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        [Column("updatedByUserKey")]
        public Guid? UpdatedByUserKey { get; set; }
    }
}
