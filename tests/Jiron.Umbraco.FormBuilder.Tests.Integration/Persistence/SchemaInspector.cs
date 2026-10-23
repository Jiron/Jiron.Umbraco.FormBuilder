using System.Text.RegularExpressions;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>Jiron Form Builder's tables as the database reports them, in a provider-neutral, comparable form.</summary>
/// <param name="Columns">Per table, its columns in order; a nullable column ends with <c>?</c>.</param>
/// <param name="PrimaryKeys">Per table, its primary key columns in key order.</param>
/// <param name="Indexes">
/// <c>table.name(columns)</c>, plus <c> unique</c>, plus <c> where column is not null</c> for a filtered index;
/// primary keys excluded.
/// </param>
/// <param name="ForeignKeys"><c>table.column -> referencedTable on delete action</c>.</param>
internal sealed record SchemaReport(
    IReadOnlyDictionary<string, string> Columns,
    IReadOnlyDictionary<string, string> PrimaryKeys,
    IReadOnlyList<string> Indexes,
    IReadOnlyList<string> ForeignKeys)
{
    /// <summary>The nullable columns of a table.</summary>
    public IReadOnlySet<string> NullableColumns(string table) =>
        Columns.TryGetValue(table, out var columns)
            ? columns.Split(", ").Where(column => column.EndsWith('?')).Select(column => column.TrimEnd('?')).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
}

/// <summary>
/// Reads Jiron Form Builder's schema back from SQLite or SQL Server (ADR-0033), with queries of its own: Umbraco 17.7.1
/// reports SQLite nullability inverted (<c>ColumnInfo.IsNullable</c>), SQL Server's <c>GetDefinedIndexes</c> does not
/// order an index's columns, and neither says whether an index is filtered. The SQL Server branch runs in the CI job
/// only (CLAUDE.md rule 6).
/// </summary>
internal static partial class SchemaInspector
{
    private const string Prefix = "jfb";

    public static bool IsSqlite(IUmbracoDatabase database) =>
        database.SqlContext.SqlSyntax.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    public static SchemaReport Inspect(IUmbracoDatabase database)
    {
        var sqlite = IsSqlite(database);

        var columns = database.Fetch<ColumnRow>(sqlite
                ? $"SELECT m.name AS TableName, c.name AS ColumnName, c.cid AS Ordinal, CASE WHEN c.\"notnull\" = 0 THEN 1 ELSE 0 END AS IsNullable, c.pk AS KeyOrdinal FROM sqlite_master m JOIN pragma_table_info(m.name) c WHERE m.type = 'table' AND m.name LIKE '{Prefix}%'"
                : $"SELECT c.TABLE_NAME AS TableName, c.COLUMN_NAME AS ColumnName, c.ORDINAL_POSITION AS Ordinal, CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable, 0 AS KeyOrdinal FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME LIKE '{Prefix}%'")
            .Where(c => c.TableName.StartsWith(Prefix, StringComparison.Ordinal))
            .ToList();

        var keys = sqlite
            ? columns.Where(c => c.KeyOrdinal > 0).Select(c => (c.TableName, c.ColumnName, Ordinal: c.KeyOrdinal)).ToList()
            : database.Fetch<KeyRow>(
                    $"SELECT T.name AS TableName, AC.name AS ColumnName, IC.key_ordinal AS Ordinal FROM sys.tables T JOIN sys.indexes I ON T.object_id = I.object_id JOIN sys.index_columns IC ON IC.object_id = I.object_id AND IC.index_id = I.index_id JOIN sys.all_columns AC ON AC.object_id = IC.object_id AND AC.column_id = IC.column_id WHERE T.name LIKE '{Prefix}%' AND I.is_primary_key = 1")
                .Select(k => (k.TableName, k.ColumnName, k.Ordinal))
                .ToList();

        // A filtered index's condition: on SQLite from the index's own SQL, on SQL Server from sys.indexes.
        var filters = (sqlite
                ? database.Fetch<FilterRow>(
                    $"SELECT name AS IndexName, sql AS Filter FROM sqlite_master WHERE type = 'index' AND tbl_name LIKE '{Prefix}%' AND sql IS NOT NULL")
                : database.Fetch<FilterRow>(
                    $"SELECT I.name AS IndexName, I.filter_definition AS Filter FROM sys.tables T JOIN sys.indexes I ON T.object_id = I.object_id WHERE T.name LIKE '{Prefix}%' AND I.has_filter = 1"))
            .Select(f => (f.IndexName, Condition: NormaliseFilter(f.Filter, sqlite)))
            .Where(f => f.Condition is not null)
            .ToDictionary(f => f.IndexName, f => f.Condition!, StringComparer.Ordinal);

        var indexes = (sqlite
                ? database.Fetch<IndexRow>(
                    $"SELECT m.tbl_name AS TableName, il.name AS IndexName, ii.name AS ColumnName, il.\"unique\" AS IsUnique, ii.seqno AS Ordinal FROM sqlite_master m, pragma_index_list(m.name) il, pragma_index_info(il.name) ii WHERE m.type = 'table' AND m.name LIKE '{Prefix}%' AND il.origin = 'c'")
                : database.Fetch<IndexRow>(
                    $"SELECT T.name AS TableName, I.name AS IndexName, AC.name AS ColumnName, CAST(I.is_unique AS int) AS IsUnique, IC.key_ordinal AS Ordinal FROM sys.tables T JOIN sys.indexes I ON T.object_id = I.object_id JOIN sys.index_columns IC ON IC.object_id = I.object_id AND IC.index_id = I.index_id JOIN sys.all_columns AC ON AC.object_id = IC.object_id AND AC.column_id = IC.column_id WHERE T.name LIKE '{Prefix}%' AND I.is_primary_key = 0 AND I.is_unique_constraint = 0 AND IC.is_included_column = 0"))
            .Where(i => i.TableName.StartsWith(Prefix, StringComparison.Ordinal))
            .GroupBy(i => (i.TableName, i.IndexName, i.IsUnique))
            .Select(g => $"{g.Key.TableName}.{g.Key.IndexName}({string.Join(",", g.OrderBy(i => i.Ordinal).Select(i => i.ColumnName))})"
                + (g.Key.IsUnique == 1 ? " unique" : string.Empty)
                + (filters.TryGetValue(g.Key.IndexName, out var condition) ? " " + condition : string.Empty))
            .Order(StringComparer.Ordinal)
            .ToList();

        var foreignKeys = (sqlite
                ? database.Fetch<ForeignKeyRow>(
                    $"SELECT m.name AS TableName, p.\"from\" AS ColumnName, p.\"table\" AS ReferencedTable, p.on_delete AS OnDelete FROM sqlite_master m JOIN pragma_foreign_key_list(m.name) p WHERE m.type = 'table' AND m.name LIKE '{Prefix}%'")
                : database.Fetch<ForeignKeyRow>(
                    $"SELECT OBJECT_NAME(fk.parent_object_id) AS TableName, COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS ColumnName, OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable, fk.delete_referential_action_desc AS OnDelete FROM sys.foreign_keys fk JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id WHERE OBJECT_NAME(fk.parent_object_id) LIKE '{Prefix}%'"))
            .Where(f => f.TableName.StartsWith(Prefix, StringComparison.Ordinal))
            .Select(f => $"{f.TableName}.{f.ColumnName} -> {f.ReferencedTable} on delete {f.OnDelete.Replace('_', ' ').ToLowerInvariant()}")
            .Order(StringComparer.Ordinal)
            .ToList();

        return new SchemaReport(
            columns.GroupBy(c => c.TableName).ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.OrderBy(c => c.Ordinal).Select(c => c.IsNullable == 1 ? c.ColumnName + "?" : c.ColumnName)),
                StringComparer.Ordinal),
            keys.GroupBy(k => k.TableName).ToDictionary(
                g => g.Key,
                g => string.Join(",", g.OrderBy(k => k.Ordinal).Select(k => k.ColumnName)),
                StringComparer.Ordinal),
            indexes,
            foreignKeys);
    }

    /// <summary>
    /// <c>where column is not null</c> for an index filtered on one column's value, the filter as written (in lower
    /// case) for any other filter, so a test shows it, and null for an index without a filter.
    /// </summary>
    internal static string? NormaliseFilter(string? text, bool sqlite)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string condition;
        if (sqlite)
        {
            // CREATE [UNIQUE] INDEX "name" ON "table" (...) WHERE "column" IS NOT NULL
            var where = WhereClause().Match(text);
            if (!where.Success)
            {
                return null;
            }

            condition = where.Groups[1].Value;
        }
        else
        {
            // ([column] IS NOT NULL)
            condition = text;
        }

        condition = Spaces().Replace(condition.Replace("[", string.Empty).Replace("]", string.Empty).Replace("\"", string.Empty), " ").Trim();
        while (condition.StartsWith('(') && condition.EndsWith(')'))
        {
            condition = condition[1..^1].Trim();
        }

        var notNull = NotNull().Match(condition);
        return notNull.Success ? $"where {notNull.Groups[1].Value} is not null" : "where " + condition.ToLowerInvariant();
    }

    [GeneratedRegex(@"\sWHERE\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereClause();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(\w+) IS NOT NULL$", RegexOptions.IgnoreCase)]
    private static partial Regex NotNull();

    private sealed class ColumnRow
    {
        public string TableName { get; set; } = string.Empty;

        public string ColumnName { get; set; } = string.Empty;

        public int Ordinal { get; set; }

        public int IsNullable { get; set; }

        public int KeyOrdinal { get; set; }
    }

    private sealed class KeyRow
    {
        public string TableName { get; set; } = string.Empty;

        public string ColumnName { get; set; } = string.Empty;

        public int Ordinal { get; set; }
    }

    private sealed class IndexRow
    {
        public string TableName { get; set; } = string.Empty;

        public string IndexName { get; set; } = string.Empty;

        public string ColumnName { get; set; } = string.Empty;

        public int IsUnique { get; set; }

        public int Ordinal { get; set; }
    }

    private sealed class FilterRow
    {
        public string IndexName { get; set; } = string.Empty;

        public string? Filter { get; set; }
    }

    private sealed class ForeignKeyRow
    {
        public string TableName { get; set; } = string.Empty;

        public string ColumnName { get; set; } = string.Empty;

        public string ReferencedTable { get; set; } = string.Empty;

        public string OnDelete { get; set; } = string.Empty;
    }
}
