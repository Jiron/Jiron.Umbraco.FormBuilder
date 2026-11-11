using System.Collections.Frozen;
using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using NPoco;
using Dtos = Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The repositories read and write their own rows, apart from the frozen snapshots of the migration steps (ADR-0033):
/// each product row names exactly the columns of its table as schema v1 creates it, with the same types and nullability.
/// </summary>
[TestFixture]
public sealed class ProductDtoTests
{
    private static readonly object[] Pairs =
    [
        new object[] { typeof(Dtos.KeyValueDto), typeof(CreateSchemaV1.KeyValueDto) },
        new object[] { typeof(Dtos.FolderDto), typeof(CreateSchemaV1.FolderDto) },
        new object[] { typeof(Dtos.FormDto), typeof(CreateSchemaV1.FormDto) },
        new object[] { typeof(Dtos.FormVersionDto), typeof(CreateSchemaV1.FormVersionDto) },
        new object[] { typeof(Dtos.FormFieldDto), typeof(CreateSchemaV1.FormFieldDto) },
        new object[] { typeof(Dtos.SettingDto), typeof(CreateSchemaV1.SettingDto) },
        new object[] { typeof(Dtos.OptionListDto), typeof(CreateSchemaV1.OptionListDto) },
        new object[] { typeof(Dtos.ThemeDto), typeof(CreateSchemaV1.ThemeDto) },
        new object[] { typeof(Dtos.EmailLayoutDto), typeof(CreateSchemaV1.EmailLayoutDto) },
        new object[] { typeof(Dtos.FormTemplateDto), typeof(CreateSchemaV1.FormTemplateDto) },
        new object[] { typeof(Dtos.PermissionDto), typeof(CreateSchemaV1.PermissionDto) },
        new object[] { typeof(Dtos.EntryDto), typeof(CreateSchemaV1.EntryDto) },
        new object[] { typeof(Dtos.EntryFileDto), typeof(CreateSchemaV1.EntryFileDto) },
        new object[] { typeof(Dtos.EntryValueDto), typeof(CreateSchemaV1.EntryValueDto) },
        new object[] { typeof(Dtos.ConsentTextDto), typeof(CreateSchemaV1.ConsentTextDto) },
        new object[] { typeof(Dtos.OptionCapacityDto), typeof(CreateSchemaV1.OptionCapacityDto) },
        new object[] { typeof(Dtos.EntryEventDto), typeof(CreateSchemaV1.EntryEventDto) },
        new object[] { typeof(Dtos.EntryNoteDto), typeof(CreateSchemaV1.EntryNoteDto) },
        new object[] { typeof(Dtos.ActionRunDto), typeof(CreateSchemaV1.ActionRunDto) },
        new object[] { typeof(Dtos.EmailDto), typeof(CreateSchemaV1.EmailDto) },
        new object[] { typeof(Dtos.AuditEntryDto), typeof(CreateSchemaV1.AuditEntryDto) },
        new object[] { typeof(Dtos.DailyStatDto), typeof(CreateSchemaV1.DailyStatDto) },
    ];

    // The columns a later migration added, which the snapshot of schema v1 never holds; MigrationPlanTests checks they arrive.
    private static readonly FrozenSet<(Type Row, string Column)> Added = new[] { (typeof(Dtos.EntryDto), "siteKey Guid?"), (typeof(Dtos.EntryEventDto), "dueUtc DateTime?"), (typeof(Dtos.ActionRunDto), "lastAttemptUtc DateTime?") }.ToFrozenSet();

    [TestCaseSource(nameof(Pairs))]
    public void AProductRow_HasTheColumnsOfItsTable(Type product, Type snapshot)
    {
        Assert.Multiple(() =>
        {
            Assert.That(product.GetCustomAttribute<TableNameAttribute>()?.Value, Is.EqualTo(snapshot.GetCustomAttribute<TableNameAttribute>()?.Value), "table");
            Assert.That(Key(product), Is.EqualTo(Key(snapshot)), "primary key");
            Assert.That(product.GetCustomAttribute<ExplicitColumnsAttribute>(), Is.Not.Null, "only the columns it names");
            Assert.That(Columns(product).Where(column => !Added.Contains((product, column))), Is.EqualTo(Columns(snapshot)), "columns, types and nullability");
        });
    }

    [Test]
    public void EveryProductRow_IsCompared()
    {
        var products = typeof(FormBuilderCoreComposer).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(Dtos.FormDto).Namespace && type.GetCustomAttribute<TableNameAttribute>() is not null);

        Assert.That(products, Is.EquivalentTo(Pairs.Select(pair => ((object[])pair)[0])));
    }

    private static string Key(Type type) =>
        type.GetCustomAttribute<PrimaryKeyAttribute>() is { } key ? $"{key.Value} (auto increment: {key.AutoIncrement})" : "none";

    private static List<string> Columns(Type type)
    {
        var nullability = new NullabilityInfoContext();
        return type.GetProperties()
            .Where(property => property.GetCustomAttribute<ColumnAttribute>() is not null)
            .Select(property =>
            {
                var nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null
                    || nullability.Create(property).WriteState == NullabilityState.Nullable;
                return $"{property.GetCustomAttribute<ColumnAttribute>()!.Name} {(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).Name}{(nullable ? "?" : string.Empty)}";
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
