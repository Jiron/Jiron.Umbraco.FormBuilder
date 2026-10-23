using System.Text;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;
using Umbraco.Cms.Persistence.Sqlite.Services;
using Umbraco.Cms.Persistence.SqlServer.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// Schema v1 on SQL Server without a server (ROADMAP 4.1, ADR-0033): step 1 runs against a statement recorder with
/// Umbraco's SQL Server syntax provider, and the statements are compared with a reviewed snapshot. Checks on the same
/// statements catch what SQL Server would refuse (a second cascade path, a key over its size limit, a key over
/// unbounded text, a foreign key to a table that does not exist yet) before the CI job does.
/// </summary>
/// <remarks>
/// After a deliberate schema change (a new step, never an edit of step 1 once it shipped), run the fixture with
/// <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so the diff gets reviewed and committed.
/// </remarks>
public sealed partial class SchemaV1DdlTests
{
    private static readonly string SnapshotPath =
        RepositoryRoot.Path(typeof(SchemaV1DdlTests).Assembly, "tests", "Jiron.Umbraco.FormBuilder.Tests.Integration", "Persistence", "schema-v1.sqlserver.sql");

    // SQL Server's limits on index keys, in bytes.
    private const int ClusteredKeyLimit = 900;
    private const int NonClusteredKeyLimit = 1700;

    [Test]
    public async Task Ddl_MatchesTheReviewedSnapshot()
    {
        var rendered = Snapshot(await RenderAsync(SqlServer()));

        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(SnapshotPath, rendered, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}");
        var reviewed = (await File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n");
        Assert.That(rendered, Is.EqualTo(reviewed), "the SQL Server DDL changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task Ddl_CreatesEveryTableOnce_AndEveryForeignKeyAfterTheTableItRefersTo()
    {
        var statements = await RenderAsync(SqlServer());
        var created = new List<string>();
        var problems = new List<string>();
        foreach (var statement in statements)
        {
            if (CreateTablePattern().Match(statement) is { Success: true } table)
            {
                created.Add(table.Groups["table"].Value);
            }

            foreach (Match reference in ReferencesPattern().Matches(statement))
            {
                if (!created.Contains(reference.Groups["table"].Value, StringComparer.Ordinal))
                {
                    problems.Add($"refers to {reference.Groups["table"].Value} before it exists: {statement}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(created, Is.EqualTo(FormBuilderTables.All));
            Assert.That(problems, Is.Empty);
            Assert.That(CreateSchemaV1.PutsForeignKeysInCreateTable(SqlServer()), Is.False, "SQL Server adds keys after the table");
            Assert.That(CreateSchemaV1.PutsForeignKeysInCreateTable(Sqlite()), Is.True, "SQLite writes them into CREATE TABLE");
            Assert.That(
                CreateSchemaV1.Plan(SqlServer(), foreignKeysInCreateTable: false).SelectMany(t => t.Deferred).Select(key => key.Name),
                Is.EquivalentTo(new[] { "FK_jfbForm_jfbFormVersion_draftVersionId", "FK_jfbForm_jfbFormVersion_publishedVersionId" }),
                "only the form's version keys wait for their table");
        });
    }

    [Test]
    public void CascadingDeletes_ReachEveryTableByAtMostOnePath_AndFormNoCycle()
    {
        // SQL Server refuses a foreign key that would give a table a second cascading path, or close a cycle of them.
        var cascading = CreateSchemaV1.Plan(SqlServer(), foreignKeysInCreateTable: true)
            .SelectMany(table => table.Definition.ForeignKeys)
            .Where(key => key.OnDelete != System.Data.Rule.None)
            .Select(key => (From: key.PrimaryTable!, To: key.ForeignTable!))
            .ToList();
        var problems = new List<string>();
        foreach (var start in FormBuilderTables.All)
        {
            var paths = new Dictionary<string, int>(StringComparer.Ordinal);
            Walk(start, [start]);

            void Walk(string table, List<string> path)
            {
                foreach (var (_, next) in cascading.Where(edge => edge.From == table))
                {
                    if (path.Contains(next, StringComparer.Ordinal))
                    {
                        problems.Add($"cycle: {string.Join(" -> ", path)} -> {next}");
                        continue;
                    }

                    paths[next] = paths.GetValueOrDefault(next) + 1;
                    Walk(next, [.. path, next]);
                }
            }

            problems.AddRange(paths.Where(p => p.Value > 1).Select(p => $"{start} reaches {p.Key} by {p.Value} cascading paths"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(cascading, Has.Count.EqualTo(ExpectedSchemaV1.ForeignKeys.Count(key => !key.EndsWith("no action", StringComparison.Ordinal))), "the check sees every cascading key");
        });
    }

    [Test]
    public async Task IndexKeys_FitSqlServersLimits_AndNoneIsOverUnboundedText()
    {
        var statements = await RenderAsync(SqlServer());
        var types = new Dictionary<(string Table, string Column), string>();
        foreach (var statement in statements)
        {
            if (CreateTablePattern().Match(statement) is { Success: true } table)
            {
                foreach (Match column in ColumnPattern().Matches(table.Groups["columns"].Value))
                {
                    types[(table.Groups["table"].Value, column.Groups["column"].Value)] = column.Groups["type"].Value;
                }
            }
        }

        var keys = statements.Select(statement => KeyPattern().Match(statement)).Where(match => match.Success).ToList();
        var problems = new List<string>();
        foreach (var key in keys)
        {
            var table = key.Groups["table"].Value;
            var columns = BracketedPattern().Matches(key.Groups["columns"].Value).Select(m => m.Groups[1].Value).ToList();
            var clustered = key.Groups["kind"].Value.Contains("CLUSTERED", StringComparison.Ordinal) && !key.Groups["kind"].Value.Contains("NONCLUSTERED", StringComparison.Ordinal);
            var limit = clustered ? ClusteredKeyLimit : NonClusteredKeyLimit;
            var bytes = 0;
            foreach (var column in columns)
            {
                var type = types[(table, column)];
                if (type == "NVARCHAR(MAX)")
                {
                    problems.Add($"{key.Groups["name"].Value}: {column} is NVARCHAR(MAX)");
                    continue;
                }

                bytes += Bytes(type);
            }

            if (bytes > limit)
            {
                problems.Add($"{key.Groups["name"].Value}: {bytes} bytes, over the limit of {limit}");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(problems, Is.Empty);
            Assert.That(keys, Has.Count.EqualTo(ExpectedSchemaV1.Indexes.Count + ExpectedSchemaV1.Columns.Count), "every index and primary key was checked");
        });
    }

    [Test]
    public async Task Strings_HaveAnExplicitLengthOrAreMax_AndTypesAreThoseOfAdr0003()
    {
        var columns = (await RenderAsync(SqlServer()))
            .Select(statement => CreateTablePattern().Match(statement))
            .Where(table => table.Success)
            .SelectMany(table => ColumnPattern().Matches(table.Groups["columns"].Value))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(columns.Where(c => BaseType(c.Groups["type"].Value) == "NVARCHAR" && !c.Groups["type"].Value.Contains('(', StringComparison.Ordinal)).Select(c => c.Groups["column"].Value), Is.Empty, "a bare NVARCHAR holds one character");
            Assert.That(columns.Select(c => BaseType(c.Groups["type"].Value)).Distinct().Order(StringComparer.Ordinal), Is.EqualTo(new[] { "BIGINT", "BIT", "DATETIME", "FLOAT", "INTEGER", "NVARCHAR", "UNIQUEIDENTIFIER" }));
            Assert.That(columns.Count(c => c.Groups["column"].Value == "number" && c.Groups["type"].Value == "FLOAT"), Is.EqualTo(1), "jfbEntryValue.number is a double (ADR-0003)");
            Assert.That(columns, Has.Count.EqualTo(ExpectedSchemaV1.Columns.Values.Sum(c => c.Split(", ").Length)), "every column was read");
        });

        // The source of every string column says how long it may be.
        var unbounded = CreateSchemaV1.Tables
            .SelectMany(table => table.Dto.GetProperties().Select(property => (table.Name, Property: property)))
            .Where(p => p.Property.PropertyType == typeof(string))
            .Where(p => p.Property.GetCustomAttributes(typeof(LengthAttribute), inherit: false).Length == 0
                && p.Property.GetCustomAttributes(typeof(SpecialDbTypeAttribute), inherit: false).Length == 0)
            .Select(p => $"{p.Name}.{p.Property.Name}");
        Assert.That(unbounded, Is.Empty);
    }

    /// <summary>The statements step 1 issues on an empty database, in order.</summary>
    internal static async Task<IReadOnlyList<string>> RenderAsync(ISqlSyntaxProvider syntax)
    {
        var (database, recorder) = StatementRecorder.Create();
        await CreateSchemaV1.CreateAsync(database, syntax, _ => false, _ => false, NullLogger.Instance);
        return recorder.Statements;
    }

    private static SqlServerSyntaxProvider SqlServer() => new(Options.Create(new GlobalSettings()), NullLogger<SqlServerSyntaxProvider>.Instance);

    private static SqliteSyntaxProvider Sqlite() => new(Options.Create(new GlobalSettings()), NullLogger<SqliteSyntaxProvider>.Instance);

    private static string Snapshot(IReadOnlyList<string> statements)
    {
        var text = new StringBuilder()
            .Append("-- Schema v1 on SQL Server: the statements of step 1 (CreateSchemaV1) on an empty database, as Umbraco's\n")
            .Append("-- SQL Server syntax provider renders them (ROADMAP 4.1, ADR-0033). Written by SchemaV1DdlTests with\n")
            .Append("-- JFB_UPDATE_SNAPSHOTS=1; review every change.\n");
        foreach (var statement in statements)
        {
            text.Append('\n').Append(statement.ReplaceLineEndings("\n").Trim()).Append(";\n");
        }

        return text.ToString();
    }

    // Umbraco writes UniqueIdentifier in mixed case.
    private static string BaseType(string type) => type.Split('(')[0].ToUpperInvariant();

    private static int Bytes(string type) => BaseType(type) switch
    {
        "NVARCHAR" => 2 * int.Parse(type[(type.IndexOf('(', StringComparison.Ordinal) + 1)..^1], System.Globalization.CultureInfo.InvariantCulture),
        "UNIQUEIDENTIFIER" => 16,
        "BIGINT" or "DATETIME" or "FLOAT" => 8,
        "INTEGER" => 4,
        "BIT" => 1,
        _ => throw new InvalidOperationException($"No size for {type}."),
    };

    [GeneratedRegex(@"^CREATE TABLE \[(?<table>\w+)\]\s*\((?<columns>.*)\)\s*$", RegexOptions.Singleline)]
    private static partial Regex CreateTablePattern();

    [GeneratedRegex(@"\[(?<column>\w+)\] (?<type>[A-Za-z]+(\((\d+|MAX)\))?)")]
    private static partial Regex ColumnPattern();

    [GeneratedRegex(@"REFERENCES \[(?<table>\w+)\]")]
    private static partial Regex ReferencesPattern();

    [GeneratedRegex(@"^(?:ALTER TABLE \[(?<table>\w+)\] ADD CONSTRAINT \[(?<name>\w+)\] PRIMARY KEY (?<kind>\w+) \((?<columns>[^)]*)\)|CREATE (?<kind>[A-Z ]*)INDEX \[(?<name>\w+)\] ON \[(?<table>\w+)\] \((?<columns>[^)]*)\).*)$", RegexOptions.Singleline)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex BracketedPattern();
}
