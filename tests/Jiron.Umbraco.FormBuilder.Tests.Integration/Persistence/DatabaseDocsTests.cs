using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseModelDefinitions;
using Umbraco.Cms.Persistence.SqlServer.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// docs/database.md follows the schema (section 4.5): its ER diagram is the one the snapshot DTOs describe, column by
/// column, and every table has a section that names exactly its indexes.
/// </summary>
/// <remarks>
/// After a schema change, run the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it rewrites the diagram and fails, so
/// the change gets reviewed; the sections are written by hand.
/// </remarks>
public sealed partial class DatabaseDocsTests
{
    private static readonly string DocPath = RepositoryRoot.Path(typeof(DatabaseDocsTests).Assembly, "docs", "database.md");

    [Test]
    public async Task Diagram_IsTheOneTheSnapshotsDescribe()
    {
        var doc = (await File.ReadAllTextAsync(DocPath)).ReplaceLineEndings("\n");
        var block = MermaidBlock().Match(doc);
        Assert.That(block.Success, Is.True, "docs/database.md has one mermaid block");
        var expected = Diagram();

        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1" && block.Groups["diagram"].Value != expected)
        {
            var updated = doc[..block.Groups["diagram"].Index] + expected + doc[(block.Groups["diagram"].Index + block.Groups["diagram"].Length)..];
            await File.WriteAllTextAsync(DocPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: rewrote the diagram in {DocPath}. Review the diff and run again without the variable.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(block.Groups["diagram"].Value, Is.EqualTo(expected), "run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
            Assert.That(MermaidBlock().Matches(doc), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task EveryTable_HasASection_InCreationOrder_ThatNamesExactlyItsIndexes()
    {
        var doc = (await File.ReadAllTextAsync(DocPath)).ReplaceLineEndings("\n");
        var headings = TableHeading().Matches(doc).ToList();
        var indexes = TableIndexes();

        Assert.Multiple(() =>
        {
            Assert.That(headings.Select(h => h.Groups["table"].Value), Is.EqualTo(FormBuilderTables.All));
            for (var i = 0; i < headings.Count; i++)
            {
                var start = headings[i].Index + headings[i].Length;
                var end = i + 1 < headings.Count ? headings[i + 1].Index : NextSection(doc, start);
                var table = headings[i].Groups["table"].Value;
                var named = IndexName().Matches(doc[start..end]).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal);
                Assert.That(named, Is.EqualTo(indexes.GetValueOrDefault(table, []).Order(StringComparer.Ordinal)), $"the indexes named under {table}");
            }
        });
    }

    /// <summary>The ER diagram of the snapshots: every table with every column, and every foreign key.</summary>
    internal static string Diagram()
    {
        var tables = CreateSchemaV1.Plan(SqlServer(), foreignKeysInCreateTable: true);
        var text = new StringBuilder("erDiagram\n");
        foreach (var table in tables.Select(t => t.Definition))
        {
            var primaryKey = PrimaryKey(table);
            var foreign = table.ForeignKeys.SelectMany(key => key.ForeignColumns).ToHashSet(StringComparer.Ordinal);
            var unique = table.Indexes
                .Where(index => index.IndexType == IndexTypes.UniqueNonClustered)
                .Select(ColumnsOf)
                .Where(columns => columns.Count == 1)
                .Select(columns => columns[0])
                .ToHashSet(StringComparer.Ordinal);
            text.Append("    ").Append(table.Name).Append(" {\n");
            foreach (var column in table.Columns)
            {
                var keys = new[] { primaryKey.Contains(column.Name) ? "PK" : null, foreign.Contains(column.Name) ? "FK" : null, unique.Contains(column.Name) ? "UK" : null }
                    .OfType<string>()
                    .ToList();
                text.Append("        ").Append(TypeOf(column)).Append(' ').Append(column.Name);
                if (keys.Count > 0)
                {
                    text.Append(' ').Append(string.Join(", ", keys));
                }

                if (column.IsNullable)
                {
                    text.Append(" \"null\"");
                }

                text.Append('\n');
            }

            text.Append("    }\n");
        }

        foreach (var table in tables.Select(t => t.Definition))
        {
            foreach (var key in table.ForeignKeys)
            {
                var column = key.ForeignColumns.Single();
                var optional = table.Columns.Single(c => c.Name == column).IsNullable;
                text.Append("    ").Append(key.PrimaryTable).Append(optional ? " |o--o{ " : " ||--o{ ").Append(table.Name)
                    .Append(" : \"").Append(column).Append(", ").Append(OnDelete(key.OnDelete)).Append("\"\n");
            }
        }

        return text.ToString();
    }

    private static Dictionary<string, List<string>> TableIndexes()
    {
        var indexes = CreateSchemaV1.Plan(SqlServer(), foreignKeysInCreateTable: true)
            .ToDictionary(t => t.Name, t => t.Definition.Indexes.Select(index => index.Name!).ToList(), StringComparer.Ordinal);
        foreach (var filtered in CreateSchemaV1.FilteredIndexes)
        {
            indexes[filtered.Table].Add(filtered.Name);
        }

        indexes[FormBuilderTables.EntryFile].Add(ChangeFileScanIndexV1.IndexName);

        return indexes;
    }

    // An index on one property keeps its column in ColumnName; one over several lists them in Columns.
    private static List<string> ColumnsOf(IndexDefinition index) =>
        index.Columns.Count > 0 ? index.Columns.Select(column => column.Name!).ToList() : [index.ColumnName!];

    private static HashSet<string> PrimaryKey(TableDefinition table)
    {
        var column = table.Columns.Single(c => c.IsPrimaryKey);
        var columns = string.IsNullOrEmpty(column.PrimaryKeyColumns) ? [column.Name] : column.PrimaryKeyColumns.Split(',').Select(c => c.Trim());
        return columns.ToHashSet(StringComparer.Ordinal);
    }

    private static string TypeOf(ColumnDefinition column)
    {
        // Umbraco sets no property type on a column with a special database type.
        if (column.CustomDbType is { } special)
        {
            return special == SpecialDbType.NVARCHARMAX
                ? "text"
                : throw new InvalidOperationException($"No diagram type for {special} ({column.TableName}.{column.Name}).");
        }

        var type = Nullable.GetUnderlyingType(column.PropertyType) ?? column.PropertyType;
        return type switch
        {
            _ when type == typeof(string) => $"string({column.Size})",
            _ when type == typeof(int) => "int",
            _ when type == typeof(long) => "bigint",
            _ when type == typeof(Guid) => "guid",
            _ when type == typeof(DateTime) => "datetime",
            _ when type == typeof(bool) => "bool",
            _ when type == typeof(double) => "double",
            _ => throw new InvalidOperationException($"No diagram type for {type} ({column.TableName}.{column.Name})."),
        };
    }

    private static string OnDelete(Rule rule) => rule switch
    {
        Rule.Cascade => "cascade",
        Rule.SetNull => "set null",
        Rule.None => "no action",
        _ => throw new InvalidOperationException($"No text for {rule}."),
    };

    private static int NextSection(string doc, int from)
    {
        var next = doc.IndexOf("\n## ", from, StringComparison.Ordinal);
        return next < 0 ? doc.Length : next;
    }

    private static SqlServerSyntaxProvider SqlServer() => new(Options.Create(new GlobalSettings()), NullLogger<SqlServerSyntaxProvider>.Instance);

    [GeneratedRegex("^```mermaid\n(?<diagram>.*?)```$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex MermaidBlock();

    [GeneratedRegex(@"^### `(?<table>jfb\w+)`$", RegexOptions.Multiline)]
    private static partial Regex TableHeading();

    [GeneratedRegex(@"\bIX_jfb\w+")]
    private static partial Regex IndexName();
}
