using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The check of a field's rows (ADR-0071) as the submission's step runs it (<see cref="FieldValues.CheckRows"/>): a
/// repeatable group holds its number of rows to its least and its most, with the error kind <c>rowCount</c>; a site's
/// type with child fields finds nothing unless it checks, and the check is refused for a type without child fields.
/// </summary>
public sealed class BuiltInRowsTests
{
    [TestCase(null, 0)]
    [TestCase(null, 1)]
    [TestCase(null, 10)]
    [TestCase(null, 11, "rowCount")]
    [TestCase("""{"minRows":2,"maxRows":5}""", 0, "rowCount")]
    [TestCase("""{"minRows":2,"maxRows":5}""", 1, "rowCount")]
    [TestCase("""{"minRows":2,"maxRows":5}""", 2)]
    [TestCase("""{"minRows":2,"maxRows":5}""", 5)]
    [TestCase("""{"minRows":2,"maxRows":5}""", 6, "rowCount")]
    [TestCase("""{"minRows":3,"maxRows":3}""", 2, "rowCount")]
    [TestCase("""{"minRows":3,"maxRows":3}""", 3)]
    [TestCase("""{"minRows":3,"maxRows":3}""", 4, "rowCount")]
    [TestCase("""{"minRows":1,"maxRows":1}""", 0, "rowCount")]
    [TestCase("""{"maxRows":100}""", 100)]
    [TestCase("""{"maxRows":100}""", 101, "rowCount")]
    [TestCase("""{"minRows":100,"maxRows":100}""", 99, "rowCount")]
    [TestCase("""{"minRows":100,"maxRows":100}""", 100)]
    [TestCase(null, int.MaxValue, "rowCount")]
    public void ARepeatableGroup_HoldsItsRowsToItsLimits(string? settings, int rows, params string[] errors) =>
        Assert.That(FieldValues.CheckRows(Type("repeater"), Field("repeater", settings), rows).Select(ValidationCase.Name), Is.EqualTo(errors));

    [Test]
    public void ARepeatableGroup_RaisesTheRowsKindAlone_AndHasItsMostRowsAsTheSettingsLimit()
    {
        var repeater = Type("repeater");

        Assert.Multiple(() =>
        {
            Assert.That(repeater.Declaration.Messages, Is.EqualTo(FieldMessageKinds.RowCount));
            Assert.That(repeater.SettingsSchema.Settings.Single(setting => setting.Name == "minRows").Rules.Maximum, Is.EqualTo(RepeaterFieldType.MostRows.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(repeater.SettingsSchema.Settings.Single(setting => setting.Name == "maxRows").Rules.Maximum, Is.EqualTo(RepeaterFieldType.MostRows.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        });
    }

    [TestCase("text")]
    [TestCase("grid")]
    [TestCase("file")]
    public void TheRowsCheck_OfATypeWithoutChildFields_IsRefused(string type) =>
        Assert.That(() => FieldValues.CheckRows(Type(type), Field(type), 1), Throws.ArgumentException.With.Message.Contains($"'{type}' has no child fields"));

    [Test]
    public void ANegativeNumberOfRows_IsRefused() =>
        Assert.That(() => FieldValues.CheckRows(Type("repeater"), Field("repeater"), -1), Throws.InstanceOf<ArgumentOutOfRangeException>());

    [Test]
    public void ASitesTypeWithChildFields_FindsNothingUnlessItChecks()
    {
        var field = Field("acme.group");

        Assert.Multiple(() =>
        {
            Assert.That(FieldValues.CheckRows(new GroupFieldType(), field, 1000), Is.Empty);
            Assert.That(FieldValues.CheckRows(new GroupFieldType(FieldMessageKinds.Invalid, FieldMessageKinds.RowCount, FieldMessageKinds.Invalid), field, 1), Is.EqualTo(new[] { FieldMessageKinds.Invalid, FieldMessageKinds.RowCount }), "each kind once, in order");
            Assert.That(() => FieldValues.CheckRows(new GroupFieldType(FieldMessageKinds.FileCount), field, 1), Throws.ArgumentException.With.Message.Contains("does not declare the error kind 'FileCount'"));
        });
    }

    private static IFormBuilderFieldType Type(string alias) => TestFieldTypes.Registry.FindType(alias)!;

    private static FormField Field(string type, string? settings = null) =>
        new() { Key = Guid.NewGuid(), Type = type, Alias = "guests", Settings = settings is null ? null : SettingsObject.Parse(settings) };

    // A site's repeatable type, written against the interface alone, that reports the given kinds for any rows.
    private sealed class GroupFieldType(params FieldMessageKinds[] kinds) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = new()
        {
            Alias = "acme.group",
            Name = "Group",
            Icon = "icon-repeat",
            PaletteGroup = FieldPaletteGroup.Structure,
            HoldsValue = true,
            Storage = FieldStorageKind.None,
            Features = FieldFeatures.Label | FieldFeatures.ChildFields,
            Messages = FieldMessageKinds.RowCount,
        };

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));

        public void ValidateRows(FieldRowsValidation validation)
        {
            foreach (var kind in kinds)
            {
                validation.Fail(kind);
            }
        }
    }
}
