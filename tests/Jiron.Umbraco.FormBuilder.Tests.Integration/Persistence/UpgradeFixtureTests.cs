using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// The upgrade from the released schema, on real data (ROADMAP 16.5, ADR-0240): <c>tests/fixtures/upgrade/schema-v1.json</c>
/// holds the rows a site on the baseline packages (tag <c>baseline/schema-v1</c>) stored through the baseline's own
/// services, written by <c>scripts/install-test.ps1 -Mode upgrade -WriteFixture</c>, and that site's schema. The test runs
/// the baseline's steps, checks that today's first steps still build exactly that schema, puts the rows in as they were,
/// runs the rest of the plan and reads everything back through today's services. It runs in the integration suite, so on
/// SQLite here and on SQL Server in CI. Each later released schema adds a fixture of its own (docs/upgrading.md).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class UpgradeFixtureTests : FormBuilderMigrationTestBase
{
    /// <summary>The baseline's plan ended at its third step.</summary>
    private const int BaselineSteps = 3;

    /// <summary>The rows go in in this order, so each row's parents are there first (forms point at their versions after).</summary>
    private static readonly string[] Tables =
        ["jfbSetting", "jfbFolder", "jfbForm", "jfbFormVersion", "jfbFormField", "jfbEntry", "jfbEntryFile", "jfbEntryValue", "jfbEntryEvent", "jfbAuditEntry"];

    /// <summary>The columns that name a row of another table, and the table whose new ids they take.</summary>
    private static readonly Dictionary<string, string> References = new(StringComparer.Ordinal)
    {
        ["parentId"] = "jfbFolder",
        ["folderId"] = "jfbFolder",
        ["formId"] = "jfbForm",
        ["draftVersionId"] = "jfbFormVersion",
        ["publishedVersionId"] = "jfbFormVersion",
        ["formVersionId"] = "jfbFormVersion",
        ["fieldId"] = "jfbFormField",
        ["entryId"] = "jfbEntry",
        ["fileId"] = "jfbEntryFile",
    };

    [Test]
    public async Task TheBaselinesData_SurvivesTheUpgrade_AndReadsBackThroughTodaysServices()
    {
        using var fixture = JsonDocument.Parse(await File.ReadAllTextAsync(FixturePath()));
        var root = fixture.RootElement;
        await RunAsync(new TestPlan(BaselineSteps));
        Assert.That(StoredState(), Is.EqualTo(root.GetProperty("state").GetString()), "the fixture comes from the baseline's final state");
        var built = Inspect();
        var schema = root.GetProperty("schema");
        Assert.Multiple(() =>
        {
            // Today's first steps are frozen: they still build the baseline's tables, column for column, index for index.
            Assert.That(
                built.Columns.Select(table => $"{table.Key}: {table.Value}").Order(StringComparer.Ordinal),
                Is.EqualTo(schema.GetProperty("columns").EnumerateObject().Select(table => $"{table.Name}: {table.Value.GetString()}").Order(StringComparer.Ordinal)));
            Assert.That(built.Indexes, Is.EqualTo(schema.GetProperty("indexes").EnumerateArray().Select(index => index.GetString())));
        });

        Apply(root.GetProperty("tables"));
        var upgrade = await RunAsync(new TestPlan());

        var tables = root.GetProperty("tables");
        var bookingKey = KeyOf(tables, "jfbForm", "alias", "upgradeBooking");
        var feedbackKey = KeyOf(tables, "jfbForm", "alias", "upgradeFeedback");
        var folders = await GetRequiredService<IFormBuilderFolderService>().ListAsync();
        var forms = GetRequiredService<IFormBuilderFormService>();
        var booking = (await forms.GetDraftAsync(bookingKey))!;
        var feedback = (await forms.GetDraftAsync(feedbackKey))!;
        var live = (await GetRequiredService<IFormBuilderFormVersionService>().GetVersionAsync(bookingKey, booking.PublishedVersion!.Value))!;
        var general = (await GetRequiredService<IFormBuilderSettingsService>().GetAsync<GeneralSettings>()).Value;
        var entries = GetRequiredService<IEntryRepository>();
        var formRows = GetRequiredService<IFormRepository>();
        var seeded = new List<(StoredEntry Entry, Dictionary<string, string?> Answers)>();
        foreach (var row in tables.GetProperty("jfbEntry").EnumerateArray())
        {
            var entry = (await entries.GetAsync(Guid.Parse(row.GetProperty("key").GetString()!)))!;
            var aliases = (await formRows.ListFieldsAsync(entry.FormId)).ToDictionary(field => field.Id, field => field.Values.Alias);
            var answers = (await entries.ListValuesAsync([entry.Id])).ToDictionary(value => aliases[value.FieldId], value => value.Text, StringComparer.Ordinal);
            seeded.Add((entry, answers));
        }

        var first = seeded.Single(entry => entry.Answers.GetValueOrDefault("name") == "Ada Upgrade");
        var file = (await entries.ListFilesAsync(first.Entry.Id)).Single();
        var fileRow = tables.GetProperty("jfbEntryFile").EnumerateArray().Single();

        Assert.Multiple(() =>
        {
            Assert.That(upgrade.Successful, Is.True, upgrade.Exception?.ToString());
            Assert.That(upgrade.InitialState, Is.EqualTo(FormBuilderMigrationPlan.StateGrantSection));
            Assert.That(StoredState(), Is.EqualTo(FinalState));

            // The folders, the forms where the baseline put them, their drafts and versions.
            var events = folders.Single(folder => folder.Name == "Upgrade events");
            Assert.That(folders.Single(folder => folder.Name == "Archive").ParentKey, Is.EqualTo(events.Key));
            Assert.That((booking.Form.FolderKey, booking.Form.Name, booking.Form.IsPublished), Is.EqualTo(((Guid?)events.Key, "Upgrade booking", true)));
            Assert.That((feedback.Form.FolderKey, feedback.Form.IsPublished), Is.EqualTo(((Guid?)null, true)));
            Assert.That(booking.Problems, Is.Empty, "today's reader reads the baseline's draft");
            Assert.That(booking.HasUnpublishedChanges, Is.True);
            Assert.That(Label(booking.Definition!, "message"), Is.EqualTo("Anything else?"), "the draft's change");
            Assert.That(live.Problems, Is.Empty);
            Assert.That(Label(live.Definition!, "message"), Is.EqualTo("Message"), "the published version as it was");
            Assert.That(Aliases(live.Definition!), Does.Contain("phone"), "the second published version");

            // Every entry, its state, its answers as the baseline stored them; the columns of later steps are empty.
            Assert.That(seeded, Has.Count.EqualTo(tables.GetProperty("jfbEntry").GetArrayLength()));
            Assert.That(seeded.Single(entry => entry.Answers.GetValueOrDefault("name") == "Bruno Baseline").Entry.State, Is.EqualTo(EntryState.Approved));
            Assert.That(seeded.Where(entry => entry.Entry.FormId == first.Entry.FormId).Select(entry => entry.Entry.Number).Order(), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(first.Answers, Does.ContainKey("plan").And.ContainKey("guests").And.ContainKey("arrival"));
            Assert.That((first.Answers["guests"], first.Answers["arrival"], first.Answers["newsletter"], first.Answers["room"]), Is.EqualTo(("2", "2026-12-24", "true", "double")));
            Assert.That(first.Entry.SiteKey, Is.Null, "the entry's site, step 15's column, is empty for what came before");
            Assert.That((file.OriginalName, file.FileHash, file.SizeBytes), Is.EqualTo(("plan.pdf", fileRow.GetProperty("fileHash").GetString(), fileRow.GetProperty("sizeBytes").GetInt64())));

            // The general settings the baseline saved, read by today's section.
            Assert.That((general.TimeZone, general.History.DraftDays, general.History.DraftCount), Is.EqualTo(("Europe/Berlin", 120, 40)));
        });
    }

    /// <summary>
    /// Puts the fixture's rows in as the baseline stored them, each with a new id (so the database's own identity columns
    /// hand them out, on SQL Server too) and its references moved along. A form's versions come after the form, so the form
    /// points at them once they are there; the general settings replace the defaults the second step wrote.
    /// </summary>
    private void Apply(JsonElement tables)
    {
        var ids = new Dictionary<string, Dictionary<long, long>>(StringComparer.Ordinal);
        var types = ColumnTypes();
        Write(db =>
        {
            foreach (var table in Tables)
            {
                var map = ids[table] = [];
                foreach (var row in tables.GetProperty(table).EnumerateArray())
                {
                    var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var column in row.EnumerateObject().Where(column => column.Name != "id"))
                    {
                        var type = types[table][column.Name];
                        var value = Value(type, column.Value);
                        if (value is not null && References.TryGetValue(column.Name, out var target))
                        {
                            // A form's versions are not there yet: the form points at them below.
                            value = table == "jfbForm" && target == "jfbFormVersion" ? null : Moved(ids[target], Convert.ToInt64(value, CultureInfo.InvariantCulture), type);
                        }

                        values[column.Name] = value;
                    }

                    if (table == "jfbSetting")
                    {
                        db.Execute($"DELETE FROM {T(table)} WHERE {Q("scope")} = @0 AND {Q("section")} = @1", values["scope"]!, values["section"]!);
                    }

                    var parameters = string.Join(", ", Enumerable.Range(0, values.Count).Select(i => "@" + i));
                    db.Execute(
                        $"INSERT INTO {T(table)} ({string.Join(", ", values.Keys.Select(Q))}) VALUES ({parameters})",
                        [.. values.Values.Select(value => value ?? DBNull.Value)]);
                    if (row.TryGetProperty("id", out var id))
                    {
                        map[id.GetInt64()] = NewId(db, table, values);
                    }
                }
            }

            // The forms point at their versions now that they are there.
            foreach (var row in tables.GetProperty("jfbForm").EnumerateArray())
            {
                db.Execute(
                    $"UPDATE {T("jfbForm")} SET {Q("draftVersionId")} = @0, {Q("publishedVersionId")} = @1 WHERE {Q("key")} = @2",
                    Moved(ids["jfbFormVersion"], row.GetProperty("draftVersionId")),
                    Moved(ids["jfbFormVersion"], row.GetProperty("publishedVersionId")),
                    Guid.Parse(row.GetProperty("key").GetString()!));
            }
        });
    }

    /// <summary>The id the database gave a row just put in, found by what is unique about it.</summary>
    private long NewId(IUmbracoDatabase db, string table, Dictionary<string, object?> values) => table switch
    {
        "jfbFormVersion" => db.ExecuteScalar<long>($"SELECT {Q("id")} FROM {T(table)} WHERE {Q("formId")} = @0 AND {Q("number")} = @1", values["formId"]!, values["number"]!),
        "jfbFormField" => db.ExecuteScalar<long>($"SELECT {Q("id")} FROM {T(table)} WHERE {Q("formId")} = @0 AND {Q("fieldKey")} = @1", values["formId"]!, values["fieldKey"]!),
        _ when values.ContainsKey("key") => db.ExecuteScalar<long>($"SELECT {Q("id")} FROM {T(table)} WHERE {Q("key")} = @0", values["key"]!),
        _ => db.ExecuteScalar<long>($"SELECT MAX({Q("id")}) FROM {T(table)}"),
    };

    private static object Moved(Dictionary<long, long> map, JsonElement old) =>
        old.ValueKind == JsonValueKind.Null ? DBNull.Value : (int)map[old.GetInt64()];

    /// <summary>A reference's new id, as its column's type (an int, or a long for the entries).</summary>
    private static object Moved(Dictionary<long, long> map, long old, Type type) =>
        Convert.ChangeType(map[old], Nullable.GetUnderlyingType(type) ?? type, CultureInfo.InvariantCulture);

    /// <summary>Each table's columns with the CLR types today's repositories read them as, from the repositories' rows (the migrations' frozen snapshots name the tables too).</summary>
    private static Dictionary<string, Dictionary<string, Type>> ColumnTypes() =>
        typeof(FormBuilderTables).Assembly.GetTypes()
            .Select(type => (Type: type, Table: type.GetCustomAttribute<TableNameAttribute>()?.Value))
            .Where(dto => dto.Table is not null && Tables.Contains(dto.Table) && dto.Type.Namespace == typeof(Core.Persistence.Dtos.EntryDto).Namespace)
            .ToDictionary(
                dto => dto.Table!,
                dto => dto.Type.GetProperties()
                    .Select(property => (Property: property, Column: property.GetCustomAttribute<ColumnAttribute>()?.Name))
                    .Where(column => column.Column is not null)
                    .ToDictionary(column => column.Column!, column => column.Property.PropertyType, StringComparer.Ordinal),
                StringComparer.Ordinal);

    /// <summary>A value as SQLite kept it (times and keys as text, flags as numbers), as its column's type.</summary>
    private static object? Value(Type type, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var inner = Nullable.GetUnderlyingType(type) ?? type;
        return inner switch
        {
            _ when inner == typeof(Guid) => Guid.Parse(value.GetString()!),
            _ when inner == typeof(DateTime) => DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            _ when inner == typeof(bool) => value.ValueKind == JsonValueKind.Number ? value.GetInt64() != 0 : value.GetBoolean(),
            _ when inner == typeof(int) => value.GetInt32(),
            _ when inner == typeof(long) => value.GetInt64(),
            _ when inner == typeof(double) => value.GetDouble(),
            _ when inner == typeof(decimal) => value.GetDecimal(),
            _ => value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText(),
        };
    }

    private static Guid KeyOf(JsonElement tables, string table, string column, string value) =>
        Guid.Parse(tables.GetProperty(table).EnumerateArray().Single(row => row.GetProperty(column).GetString() == value).GetProperty("key").GetString()!);

    private static string? Label(Core.Definitions.FormDefinition definition, string alias) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Single(field => field.Alias == alias).Label?.Default;

    private static IEnumerable<string?> Aliases(Core.Definitions.FormDefinition definition) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Select(field => field.Alias);

    /// <summary>The fixture, from the repository's tests folder above the test assembly.</summary>
    internal static string FixturePath()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var path = Path.Combine(folder.FullName, "tests", "fixtures", "upgrade", "schema-v1.json");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException("tests/fixtures/upgrade/schema-v1.json was not found above the test assembly.");
    }
}
