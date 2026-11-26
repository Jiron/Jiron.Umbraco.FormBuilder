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
/// <remarks>
/// One statement writes every missing row and reads nothing before it (27.1): a step that counted first and then
/// inserted held a read beside a second process writing the same SQLite file, which its insert could then never get
/// past, so Umbraco retried it for minutes. Writing first, it waits its turn for the write lock as any write does, on
/// SQLite and SQL Server alike.
/// </remarks>
internal sealed class CreateDefaultSettingsV1(IMigrationContext context, TimeProvider timeProvider) : AsyncMigrationBase(context)
{
    /// <summary>The scope of the values every site shares.</summary>
    public const string GlobalScope = "global";

    /// <summary>The backoffice sections of ADR-0008. Access is stored in <c>jfbPermission</c> instead.</summary>
    internal static readonly IReadOnlyList<string> Sections = ["general", "email", "protection", "files", "privacy", "integrations"];

    /// <inheritdoc />
    protected override async Task MigrateAsync()
    {
        var table = SqlSyntax.GetQuotedTableName(FormBuilderTables.Setting);
        string C(string column) => SqlSyntax.GetQuotedColumnName(column);

        // @0 the scope, @1 the empty values, @2 the time, then each section's name as a row of its own.
        var sections = string.Join(" UNION ALL ", Sections.Select((_, index) => $"SELECT @{index + 3} AS {C("section")}"));
        object[] arguments = [GlobalScope, "{}", timeProvider.GetUtcNow().UtcDateTime, .. Sections];
        var added = await Database.ExecuteAsync(Sql(
            $"INSERT INTO {table} ({C("scope")}, {C("section")}, {C("schemaVersion")}, {C("version")}, {C("json")}, {C("updatedUtc")}) "
            + $"SELECT @0, s.{C("section")}, 1, 1, @1, @2 FROM ({sections}) s "
            + $"WHERE NOT EXISTS (SELECT 1 FROM {table} t WHERE t.{C("scope")} = @0 AND t.{C("section")} = s.{C("section")})",
            arguments));

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
