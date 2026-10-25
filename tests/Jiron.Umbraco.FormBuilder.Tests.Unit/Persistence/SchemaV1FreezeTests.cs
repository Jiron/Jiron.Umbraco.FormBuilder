using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// Schema v1 is frozen (ROADMAP 4.8i, ADR-0054): step 1's snapshot DTOs in their order, with every declaration that
/// shapes a table, and its filtered indexes are described from the declarations alone, and the SHA-256 of that
/// description is pinned here. A schema change is a new migration step with snapshots of its own, never an edit of
/// step 1, so this test has no switch that rewrites the pin.
/// </summary>
public sealed class SchemaV1FreezeTests
{
    // The schema of the tag baseline/schema-v1, which the upgrade test (16.5) installs and upgrades from.
    private const string FrozenHash = "8ea9e906d498a99cc51b8130d1ddd4db4546e503a2980bda584daa03d207dfff";

    [Test]
    public void TheSnapshotsOfStep1_AreTheFrozenSchemaV1()
    {
        var description = DescribeSchemaV1();
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(description)));

        TestContext.Out.WriteLine(description);
        Assert.That(
            hash,
            Is.EqualTo(FrozenHash),
            "Schema v1 is frozen (ADR-0054): change the schema in a new migration step with snapshots of its own, never in CreateSchemaV1.");
    }

    [Test]
    public void TheDescription_HoldsEveryTableInItsOrder_EveryColumn_AndEveryFilteredIndex()
    {
        var lines = DescribeSchemaV1().Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Multiple(() =>
        {
            Assert.That(
                lines.Where(line => line.StartsWith("table ", StringComparison.Ordinal)).Select(line => line.Split(' ')[1]),
                Is.EqualTo(CreateSchemaV1.Tables.Select(table => table.Name)));
            Assert.That(
                lines.Count(line => line.StartsWith("  column ", StringComparison.Ordinal)),
                Is.EqualTo(CreateSchemaV1.Tables.Sum(table => Properties(table.Dto).Count())));
            Assert.That(lines.Count(line => line.StartsWith("filtered index ", StringComparison.Ordinal)), Is.EqualTo(CreateSchemaV1.FilteredIndexes.Count));
        });
    }

    [TestCase(typeof(LongerNameDto), TestName = "A column's length")]
    [TestCase(typeof(RequiredParentDto), TestName = "A column's nullability")]
    [TestCase(typeof(OtherIndexDto), TestName = "An index")]
    [TestCase(typeof(OtherForeignKeyDto), TestName = "A foreign key's target")]
    [TestCase(typeof(ReorderedDto), TestName = "The order of the columns")]
    [TestCase(typeof(WiderIdDto), TestName = "A column's type")]
    [TestCase(typeof(RenamedColumnDto), TestName = "A column's name")]
    [TestCase(typeof(OtherPrimaryKeyDto), TestName = "The primary key")]
    public void TheDescription_ChangesWithEveryDeclarationThatShapesATable(Type variant)
    {
        Assert.That(Describe("jfbSample", variant), Is.Not.EqualTo(Describe("jfbSample", typeof(SampleDto))));
    }

    [Test]
    public void TheDescription_IgnoresTheClassNames_TheOrderOfTheAttributes_AndTheLayoutOfTheSource()
    {
        Assert.That(Describe("jfbSample", typeof(SameSampleDto)), Is.EqualTo(Describe("jfbSample", typeof(SampleDto))));
    }

    /// <summary>The description that the pin hashes: every table of step 1 in its order, then the filtered indexes.</summary>
    private static string DescribeSchemaV1()
    {
        var tables = CreateSchemaV1.Tables.Select(table => Describe(table.Name, table.Dto));
        var filtered = CreateSchemaV1.FilteredIndexes.Select(index =>
            $"filtered index {index.Name} on {index.Table} ({string.Join(", ", index.Columns)}) where {index.NotNullColumn} is not null" +
            (index.Unique ? ", unique" : string.Empty));
        return string.Join("\n", tables.Concat(filtered)) + "\n";
    }

    private static string Describe(string table, Type dto)
    {
        var lines = new List<string> { $"table {table} {Attributes(dto)}" };
        lines.AddRange(Properties(dto).Select(property => $"  column {property.Name} {TypeName(property.PropertyType)} {Attributes(property)}"));
        return string.Join("\n", lines);
    }

    // In declaration order, the order in which Umbraco creates the columns.
    private static IEnumerable<PropertyInfo> Properties(Type dto) =>
        dto.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .OrderBy(property => property.MetadataToken);

    private static string TypeName(Type type) => Nullable.GetUnderlyingType(type) is { } inner ? inner.Name + "?" : type.Name;

    // The compiler's own attributes (nullable annotations) do not shape a table.
    private static string Attributes(MemberInfo member) =>
        string.Join(
            " ",
            member.GetCustomAttributesData()
                .Where(attribute => attribute.AttributeType.Namespace != "System.Runtime.CompilerServices")
                .Select(Format)
                .Order(StringComparer.Ordinal));

    private static string Format(CustomAttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments.Select(Format)
            .Concat(attribute.NamedArguments.OrderBy(argument => argument.MemberName, StringComparer.Ordinal)
                .Select(argument => $"{argument.MemberName} = {Format(argument.TypedValue)}"));
        return $"[{attribute.AttributeType.Name}({string.Join(", ", arguments)})]";
    }

    private static string Format(CustomAttributeTypedArgument argument) => argument.Value switch
    {
        null => "null",
        string text => JsonSerializer.Serialize(text),
        Type type => $"table({type.GetCustomAttribute<TableNameAttribute>()?.Value ?? type.Name})",
        bool flag => flag ? "true" : "false",
        IReadOnlyCollection<CustomAttributeTypedArgument> items => $"[{string.Join(", ", items.Select(Format))}]",
        var value when argument.ArgumentType.IsEnum => $"{argument.ArgumentType.Name}.{Enum.ToObject(argument.ArgumentType, value)}",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        var value => value.ToString() ?? string.Empty,
    };

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class SampleDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(SampleDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    // SampleDto again, with its attributes in another order and laid out differently.
    [ExplicitColumns, PrimaryKey("id", AutoIncrement = true), TableName("jfbSample")]
    private sealed class SameSampleDto
    {
        [PrimaryKeyColumn(AutoIncrement = true), Column("id")] public int Id { get; set; }

        [Length(255), Column("name")] public string Name { get; set; } = string.Empty;

        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId"), ForeignKey(typeof(SameSampleDto), Name = "FK_jfbSample_jfbSample")]
        [NullSetting(NullSetting = NullSettings.Null), Column("parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class LongerNameDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("name")]
        [Length(256)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(LongerNameDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class RequiredParentDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [ForeignKey(typeof(RequiredParentDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class OtherIndexDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(OtherIndexDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class OtherForeignKeyDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(OtherTableDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    // The table OtherForeignKeyDto refers to.
    [TableName("jfbOther")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class OtherTableDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class ReorderedDto
    {
        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(ReorderedDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class WiderIdDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(WiderIdDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    private sealed class RenamedColumnDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("title")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(RenamedColumnDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }

    [TableName("jfbSample")]
    [PrimaryKey("id", AutoIncrement = false)]
    [ExplicitColumns]
    private sealed class OtherPrimaryKeyDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = false)]
        public int Id { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(OtherPrimaryKeyDto), Name = "FK_jfbSample_jfbSample")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbSample_parentId")]
        public int? ParentId { get; set; }
    }
}
