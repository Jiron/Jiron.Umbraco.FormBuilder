using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseModelDefinitions;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 1 (<see cref="FormBuilderMigrationPlan.StateCreateSchema"/>): every table of schema v1 (section 4.5, ADR-0003,
/// ADR-0033), from the frozen snapshot DTOs nested in this class. Idempotent: a table, a foreign key added after its
/// table and a filtered index are each created only when missing.
/// </summary>
/// <remarks>
/// <para>
/// The snapshots describe the tables exactly as this step leaves them and are never edited: schema v1 is frozen since
/// 4.8i, and <c>SchemaV1FreezeTests</c> pins them (ADR-0054). Later steps get snapshots of their own; repositories use
/// separate product DTOs. Conventions: columns in camelCase, a single-column primary key is <c>id</c>, indexes
/// <c>IX_&lt;table&gt;_&lt;columns&gt;</c>, foreign keys <c>FK_&lt;table&gt;_&lt;referencedTable&gt;</c> (plus the column
/// when a table has two to one table), times in UTC, enums as short text, hashes as lowercase hex, and every string with
/// an explicit length or <c>NVARCHAR(MAX)</c>.
/// </para>
/// <para>
/// On SQLite the foreign keys are part of <c>CREATE TABLE</c>, which may name a table that does not exist yet. On SQL
/// Server Umbraco adds them right after their table, so a key that refers to a table created later (the form's draft
/// and published versions) is added once every table exists. Both databases end with the same keys.
/// </para>
/// </remarks>
internal sealed partial class CreateSchemaV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>Every table with its snapshot, in creation order (<see cref="FormBuilderTables.SchemaV1"/>).</summary>
    internal static readonly IReadOnlyList<(string Name, Type Dto)> Tables =
    [
        (FormBuilderTables.KeyValue, typeof(KeyValueDto)),
        (FormBuilderTables.Folder, typeof(FolderDto)),
        (FormBuilderTables.Form, typeof(FormDto)),
        (FormBuilderTables.FormVersion, typeof(FormVersionDto)),
        (FormBuilderTables.FormField, typeof(FormFieldDto)),
        (FormBuilderTables.Entry, typeof(EntryDto)),
        (FormBuilderTables.EntryFile, typeof(EntryFileDto)),
        (FormBuilderTables.EntryValue, typeof(EntryValueDto)),
        (FormBuilderTables.ConsentText, typeof(ConsentTextDto)),
        (FormBuilderTables.EntryEvent, typeof(EntryEventDto)),
        (FormBuilderTables.EntryNote, typeof(EntryNoteDto)),
        (FormBuilderTables.ActionRun, typeof(ActionRunDto)),
        (FormBuilderTables.Email, typeof(EmailDto)),
        (FormBuilderTables.OptionCapacity, typeof(OptionCapacityDto)),
        (FormBuilderTables.OptionList, typeof(OptionListDto)),
        (FormBuilderTables.Theme, typeof(ThemeDto)),
        (FormBuilderTables.EmailLayout, typeof(EmailLayoutDto)),
        (FormBuilderTables.FormTemplate, typeof(FormTemplateDto)),
        (FormBuilderTables.UserSetting, typeof(UserSettingDto)),
        (FormBuilderTables.Permission, typeof(PermissionDto)),
        (FormBuilderTables.Setting, typeof(SettingDto)),
        (FormBuilderTables.Reference, typeof(ReferenceDto)),
        (FormBuilderTables.AuditEntry, typeof(AuditEntryDto)),
        (FormBuilderTables.DailyStat, typeof(DailyStatDto)),
    ];

    /// <summary>
    /// The indexes over a column that is often NULL, created with SQL of their own because the annotations cannot say
    /// <c>WHERE</c>. Both databases support them (section 4.5): a unique one then holds any number of rows without a value.
    /// </summary>
    internal static readonly IReadOnlyList<FilteredIndex> FilteredIndexes =
    [
        // A challenge makes one entry per form (ADR-0012); entries made without one have none.
        new("IX_jfbEntry_formId_challengeId", FormBuilderTables.Entry, ["formId", "challengeId"], "challengeId", Unique: true),

        // A resume link finds its draft (8.6); other entries have no token.
        new("IX_jfbEntry_resumeTokenHash", FormBuilderTables.Entry, ["resumeTokenHash"], "resumeTokenHash", Unique: true),

        // Deleting a file checks the values that refer to it; only file fields have one (ADR-0033).
        new("IX_jfbEntryValue_fileId", FormBuilderTables.EntryValue, ["fileId"], "fileId", Unique: false),
    ];

    /// <summary>
    /// The tables as this step creates them on a database whose provider does (or does not) put foreign keys into
    /// <c>CREATE TABLE</c>, each with the foreign keys that wait until every table exists.
    /// </summary>
    internal static IReadOnlyList<TablePlan> Plan(ISqlSyntaxProvider syntax, bool foreignKeysInCreateTable)
    {
        ArgumentNullException.ThrowIfNull(syntax);
        var created = new HashSet<string>(StringComparer.Ordinal);
        var plan = new List<TablePlan>(Tables.Count);
        foreach (var (name, dto) in Tables)
        {
            var definition = DefinitionFactory.GetTableDefinition(dto, syntax);
            if (!string.Equals(definition.Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The snapshot {dto.Name} describes {definition.Name}, not {name}.");
            }

            created.Add(name);
            var deferred = foreignKeysInCreateTable
                ? []
                : definition.ForeignKeys.Where(key => key.PrimaryTable is { } referenced && !created.Contains(referenced)).ToList();
            foreach (var key in deferred)
            {
                definition.ForeignKeys.Remove(key);
            }

            plan.Add(new TablePlan(name, definition, deferred));
        }

        return plan;
    }

    /// <summary>Whether the provider writes foreign keys into <c>CREATE TABLE</c> (SQLite) rather than after it.</summary>
    internal static bool PutsForeignKeysInCreateTable(ISqlSyntaxProvider syntax) =>
        syntax.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    /// <inheritdoc />
    protected override Task MigrateAsync() => CreateAsync(Database, SqlSyntax, TableExists, IndexExists, Logger);

    /// <summary>
    /// Creates every table, deferred foreign key and filtered index that does not exist yet. The step runs it on its
    /// database; the SQL Server DDL test runs it on a recorder, so the reviewed snapshot is what this code issues.
    /// </summary>
    internal static async Task CreateAsync(
        IDatabase database,
        ISqlSyntaxProvider syntax,
        Func<string, bool> tableExists,
        Func<string, bool> indexExists,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(syntax);
        ArgumentNullException.ThrowIfNull(tableExists);
        ArgumentNullException.ThrowIfNull(indexExists);
        var deferred = new List<ForeignKeyDefinition>();
        foreach (var table in Plan(syntax, PutsForeignKeysInCreateTable(syntax)))
        {
            if (tableExists(table.Name))
            {
                FormBuilderMigrationLog.TableExists(logger, table.Name);
                continue;
            }

            syntax.HandleCreateTable(database, table.Definition);
            deferred.AddRange(table.Deferred);
            FormBuilderMigrationLog.TableCreated(logger, table.Name);
        }

        // Only the keys of tables created in this run: an existing table already has its keys.
        foreach (var key in deferred)
        {
            await database.ExecuteAsync(new Sql(syntax.Format(key)));
            FormBuilderMigrationLog.ForeignKeyAdded(logger, key.ForeignTable ?? string.Empty, key.Name ?? string.Empty);
        }

        foreach (var index in FilteredIndexes)
        {
            if (indexExists(index.Name))
            {
                FormBuilderMigrationLog.IndexExists(logger, index.Table, index.Name);
                continue;
            }

            await database.ExecuteAsync(new Sql(index.Sql(syntax)));
            FormBuilderMigrationLog.IndexCreated(logger, index.Table, index.Name);
        }
    }

    /// <summary>One table of the step: its definition, without the foreign keys in <paramref name="Deferred"/>.</summary>
    internal sealed record TablePlan(string Name, TableDefinition Definition, IReadOnlyList<ForeignKeyDefinition> Deferred);

    /// <summary>An index over the rows where <paramref name="NotNullColumn"/> has a value.</summary>
    internal sealed record FilteredIndex(string Name, string Table, IReadOnlyList<string> Columns, string NotNullColumn, bool Unique)
    {
        /// <summary>The <c>CREATE INDEX</c> statement, quoted for the database.</summary>
        public string Sql(ISqlSyntaxProvider syntax)
        {
            ArgumentNullException.ThrowIfNull(syntax);
            var columns = string.Join(", ", Columns.Select(syntax.GetQuotedColumnName));
            return $"CREATE {(Unique ? "UNIQUE " : string.Empty)}INDEX {syntax.GetQuotedName(Name)} ON {syntax.GetQuotedTableName(Table)} ({columns}) " +
                $"WHERE {syntax.GetQuotedColumnName(NotNullColumn)} IS NOT NULL";
        }
    }
}
