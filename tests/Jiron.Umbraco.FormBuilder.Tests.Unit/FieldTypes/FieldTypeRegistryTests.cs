using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The field type registry (ADR-0055): the collection builder holds the built-in types from its start, a site adds,
/// replaces and removes types there, and the registry refuses a collection with an alias taken twice or a declaration
/// that contradicts itself, naming the type, as start-up does.
/// </summary>
public sealed class FieldTypeRegistryTests
{
    private const string Field = "$.pages[0].groups[0].fields[0]";

    // A valid declaration of a type that holds a value, which each contradiction changes in one place.
    private static readonly FieldTypeDeclaration Valid = new()
    {
        Alias = "acme.thing",
        Name = "Thing",
        Icon = "icon-thing",
        PaletteGroup = FieldPaletteGroup.Text,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Text,
        Features = FieldFeatures.Label | FieldFeatures.Required | FieldFeatures.VisibilityRules,
        Messages = FieldMessageKinds.Required,
    };

    public static IEnumerable<TestCaseData> Contradictions()
    {
        static TestCaseData Case(string name, FieldTypeDeclaration declaration, string says) => new TestCaseData(declaration, says).SetName($"Refuses({name})");
        const FieldFeatures Input = FieldFeatures.Label | FieldFeatures.Required | FieldFeatures.VisibilityRules;

        yield return Case("a type without a value that has storage", Valid with { HoldsValue = false, RuleKind = null, Messages = FieldMessageKinds.None, Features = FieldFeatures.VisibilityRules }, "holds no value");
        yield return Case("a type without a value that has a label", new FieldTypeDeclaration { Alias = "acme.thing", Name = "Thing", Icon = "icon-thing", PaletteGroup = FieldPaletteGroup.Content, Features = FieldFeatures.Label }, "holds no value");
        yield return Case("a type without a value that keeps its default on the server", new FieldTypeDeclaration { Alias = "acme.thing", Name = "Thing", Icon = "icon-thing", PaletteGroup = FieldPaletteGroup.Content, ServerOnlyDefault = true }, "holds no value");
        yield return Case("a type without a value that is personal data", new FieldTypeDeclaration { Alias = "acme.thing", Name = "Thing", Icon = "icon-thing", PaletteGroup = FieldPaletteGroup.Content, PersonalData = true }, "or personal data");
        yield return Case("a value without a label", Valid with { Features = FieldFeatures.Required }, "supports a label");
        yield return Case("storage None without parts", Valid with { Storage = FieldStorageKind.None }, "storage None exactly when");
        yield return Case("rows with storage of its own", Valid with { Features = Input | FieldFeatures.Rows }, "storage None exactly when");
        yield return Case("rows and child fields", Valid with { Storage = FieldStorageKind.None, Features = Input | FieldFeatures.Rows | FieldFeatures.ChildFields }, "one or the other");
        yield return Case("needing options without options", Valid with { Features = Input | FieldFeatures.NeedsOptions }, "supports options");
        yield return Case("places without options", Valid with { Features = Input | FieldFeatures.Places }, "supports options");
        yield return Case("required without its error", Valid with { Messages = FieldMessageKinds.Invalid }, "'required' error");
        yield return Case("the required error without being required", Valid with { Features = FieldFeatures.Label | FieldFeatures.VisibilityRules }, "'required' error");
        yield return Case("the rows' error without child fields", Valid with { Messages = FieldMessageKinds.Required | FieldMessageKinds.RowCount }, "'rowCount' error");
        yield return Case("a comparing check without a rule kind", Valid with { RuleKind = null, Features = Input | FieldFeatures.MatchesFieldCheck }, "has a rule kind");
        yield return Case("a unique check on yes or no", Valid with { Storage = FieldStorageKind.Bool, RuleKind = FieldKind.Checkbox, Features = Input | FieldFeatures.UniquePerFormCheck }, "stores text, long text, a number or a date");
        yield return Case("a unique check on parts", Valid with { Storage = FieldStorageKind.Json, Features = Input | FieldFeatures.UniquePerFormCheck }, "stores text, long text, a number or a date");
        yield return Case("a unique check on files", Valid with { Storage = FieldStorageKind.File, RuleKind = FieldKind.Files, Features = Input | FieldFeatures.UniquePerFormCheck }, "stores text, long text, a number or a date");
        yield return Case("a server-only default without a default", Valid with { ServerOnlyDefault = true }, "supports a default value");
        yield return Case("a server-only default that stores files", Valid with { ServerOnlyDefault = true, Storage = FieldStorageKind.File, RuleKind = FieldKind.Files, Features = Input | FieldFeatures.DefaultValue }, "stores no files");
        yield return Case("a multiple setting on a text", Valid with { MultipleSetting = true }, "compares as a choice or has rows");
        yield return Case("a multiple setting without the setting", Valid with { RuleKind = FieldKind.Choice, MultipleSetting = true }, "the boolean 'multiple'");
        yield return Case("a feature that does not exist", Valid with { Features = Input | (FieldFeatures)(1 << 20) }, "feature or an error kind that does not exist");
        yield return Case("an error kind that does not exist", Valid with { Messages = FieldMessageKinds.Required | (FieldMessageKinds)(1 << 20) }, "feature or an error kind that does not exist");
        yield return Case("a storage kind that does not exist", Valid with { Storage = (FieldStorageKind)99 }, "does not exist");
        yield return Case("a rule kind that does not exist", Valid with { RuleKind = (FieldKind)99 }, "does not exist");
        yield return Case("a palette group that does not exist", Valid with { PaletteGroup = (FieldPaletteGroup)99 }, "does not exist");
        yield return Case("an icon that is not Umbraco's kind of name", Valid with { Icon = "palette" }, "has the icon 'palette'");
        yield return Case("no name", Valid with { Name = " " }, "has no name");
        yield return Case("an alias with a space", Valid with { Alias = "acme thing" }, "alias outside the pattern");
        yield return Case("an alias that starts with a digit", Valid with { Alias = "1thing" }, "alias outside the pattern");
        yield return Case("an alias of 65 characters", Valid with { Alias = new string('a', 65) }, "alias outside the pattern");
    }

    [Test]
    public void TheBuiltInTypes_AreTheRegistry_InThePalettesOrder_WithoutAProblem()
    {
        var registry = TestFieldTypes.Registry;

        Assert.Multiple(() =>
        {
            Assert.That(new FormBuilderFieldTypeCollectionBuilder().GetTypes(), Is.EqualTo(BuiltInFieldTypes.All), "the builder holds them from its start");
            Assert.That(registry.Types.Select(type => type.GetType()), Is.EqualTo(BuiltInFieldTypes.All));
            Assert.That(registry.All.Select(traits => traits.Alias), Is.EqualTo(registry.Types.Select(type => type.Declaration.Alias)));
            Assert.That(FieldTypeRegistry.Problems([.. registry.Types]), Is.Empty);
            Assert.That(registry.Types.Select(type => type.Declaration.PaletteGroup), Is.Ordered, "the palette's groups, each in one run");
            Assert.That(registry.Find("email")!.Alias, Is.EqualTo("email"));
            Assert.That(registry.FindType("email"), Is.InstanceOf<EmailFieldType>());
            Assert.That(registry.Find("Email"), Is.Null, "an alias is matched exactly");
            Assert.That(registry.FindType("acme.colour"), Is.Null);
        });
    }

    [Test]
    public void ATypeAppended_IsInTheRegistry_WithTheTraitsItDeclares_AndThePublicSettingsOfItsSchema()
    {
        var registry = TestFieldTypes.Build(builder => builder.Append<ColourFieldType>());
        var traits = registry.Find("acme.colour")!;

        Assert.Multiple(() =>
        {
            Assert.That(registry.Types[^1], Is.InstanceOf<ColourFieldType>(), "after the built-in types");
            Assert.That(traits.HoldsValue, Is.True);
            Assert.That(traits.Storage, Is.EqualTo(FieldStorageKind.Text));
            Assert.That(traits.Kind, Is.EqualTo(FieldKind.Choice));
            Assert.That(traits.Features, Is.EqualTo(ColourFieldType.Features));
            Assert.That(traits.Messages, Is.EqualTo(FieldMessageKinds.Required));
            Assert.That(traits.PublicSettings, Is.EquivalentTo(new[] { "swatches" }), "the setting visitors see, not the one that stays on the server");
        });
    }

    [Test]
    public void ATypeReplaced_AnswersForItsAlias()
    {
        var registry = TestFieldTypes.Build(builder => builder.Replace<TextFieldType, OwnTextFieldType>());

        Assert.Multiple(() =>
        {
            Assert.That(registry.FindType("text"), Is.InstanceOf<OwnTextFieldType>());
            Assert.That(registry.Types.Select(type => type.Declaration.Alias).First(), Is.EqualTo("text"), "in the place of the one it replaced");
            Assert.That(registry.Find("text")!.PublicSettings, Is.Empty, "with its own settings");
        });
    }

    [Test]
    public void ATypeRemoved_IsGone_AndAFieldOfItIsUnknownAtPublish()
    {
        var registry = TestFieldTypes.Build(builder => builder.Remove<SignatureFieldType>());
        var definition = DefinitionFixtures.Read(Form(new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000001", ["type"] = "signature", ["alias"] = "signed" }).ToJsonString());

        Assert.Multiple(() =>
        {
            Assert.That(registry.Find("signature"), Is.Null);
            Assert.That(registry.Types, Has.Length.EqualTo(BuiltInFieldTypes.All.Length - 1));
            Assert.That(new StructuralPublishCheck(DefinitionFixtures.Serializer(), registry).Check(definition).Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.UnknownType, $"{Field}.type") }));
            Assert.That(new StructuralPublishCheck(DefinitionFixtures.Serializer(), TestFieldTypes.Registry).Check(definition), Is.Empty, "a site that has the type publishes it");
            Assert.That(FieldCatalogue.Of(definition, registry), Is.Empty, "a field of a type the site lacks has no catalogue row");
        });
    }

    [Test]
    public void AFieldOfACustomType_PublishesAndShowsVisitorsItsPublicSettingsOnly()
    {
        var registry = TestFieldTypes.Build(builder => builder.Append<ColourFieldType>());
        var definition = DefinitionFixtures.Read(Form(new JsonObject
        {
            ["key"] = "30000000-0000-4000-8000-000000000001",
            ["type"] = "acme.colour",
            ["alias"] = "colour",
            ["label"] = new JsonObject { [string.Empty] = "Colour" },
            ["settings"] = new JsonObject { ["swatches"] = new JsonArray("red", "blue"), ["source"] = "internal" },
        }).ToJsonString());
        var settings = PublicProjection.Create(definition, registry).Pages[0].Groups[0].Fields[0].Settings;

        Assert.Multiple(() =>
        {
            Assert.That(new StructuralPublishCheck(DefinitionFixtures.Serializer(), registry).Check(definition), Is.Empty);
            Assert.That(settings?.ToString(), Is.EqualTo("""{"swatches":["red","blue"]}"""));
            Assert.That(FieldCatalogue.Of(definition, registry).Single().Storage, Is.EqualTo(FieldStorageKind.Text));
        });
    }

    [Test]
    public void ASecondTypeWithAnAliasTaken_IsRefused_NamingBoth_AndHowToReplaceTheFirst()
    {
        var problems = FieldTypeRegistry.Problems([new TextFieldType(), new Declared(Valid with { Alias = "text" })]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.StartWith($"The field type 'text' ({typeof(Declared).FullName}) has the alias of the field type {typeof(TextFieldType).FullName}."));
            Assert.That(problems[0], Does.Contain("Replace<TextFieldType, Declared>()"));
            Assert.That(() => new FieldTypeRegistry([new TextFieldType(), new Declared(Valid with { Alias = "text" })]), Throws.InvalidOperationException.With.Message.EqualTo(problems[0]));
        });
    }

    [Test]
    public void ASecondTypeWithAnAliasTakenInAnotherLetterCase_IsRefused_NamingTheFirstAlias()
    {
        var problems = FieldTypeRegistry.Problems([new TextFieldType(), new Declared(Valid with { Alias = "Text" })]);

        Assert.That(problems, Is.EqualTo(new[]
        {
            $"The field type 'Text' ({typeof(Declared).FullName}) has the alias, in another letter case ('text'), of the field type {typeof(TextFieldType).FullName}. "
                + "Keep one, or replace the first with builder.JironFormBuilder().FieldTypes().Replace<TextFieldType, Declared>().",
        }));
    }

    [TestCaseSource(nameof(Contradictions))]
    public void ADeclarationThatContradictsItself_IsRefused_NamingTheType(FieldTypeDeclaration declaration, string says)
    {
        var problems = FieldTypeRegistry.Problems([new Declared(declaration)]);

        Assert.Multiple(() =>
        {
            Assert.That(problems, Has.Some.Contains(says));
            Assert.That(problems, Has.All.StartWith($"The field type '{declaration.Alias}' ({typeof(Declared).FullName})"));
            Assert.That(FieldTypeRegistry.Problems([new Declared(Valid)]), Is.Empty, "the declaration it changes is valid");
        });
    }

    [Test]
    public void AMultipleSetting_IsAcceptedWithItsBooleanSetting()
    {
        Assert.That(FieldTypeRegistry.Problems([new Declared<MultipleSettings>(Valid with { RuleKind = FieldKind.Choice, MultipleSetting = true })]), Is.Empty);
    }

    [Test]
    public void ATypeWithoutADeclarationOrASchema_IsRefused()
    {
        var empty = Substitute.For<IFormBuilderFieldType>();
        var schemaless = Substitute.For<IFormBuilderFieldType>();
        schemaless.Declaration.Returns(Valid);

        Assert.Multiple(() =>
        {
            Assert.That(FieldTypeRegistry.Problems([empty]), Has.One.Contains("declares nothing"));
            Assert.That(FieldTypeRegistry.Problems([schemaless]), Has.One.Contains("has no settings schema"));
        });
    }

    [Test]
    public void TheStartupCheck_PassesTheBuiltInTypes_AndRefusesASiteWithAProblem_NamingIt()
    {
        using var valid = Site(_ => { });
        using var taken = Site(builder => builder.Append<OwnTextFieldType>());
        using var broken = Site(builder => builder.Append<BrokenFieldType>());

        Assert.Multiple(() =>
        {
            Assert.That(() => HostOptions.Start(valid), Throws.Nothing);
            Assert.That(valid.GetRequiredService<FieldTypeRegistry>().Types, Has.Length.EqualTo(BuiltInFieldTypes.All.Length));
            Assert.That(() => HostOptions.Start(taken), Throws.TypeOf<OptionsValidationException>().With.Message.Contains($"The field type 'text' ({typeof(OwnTextFieldType).FullName}) has the alias of the field type {typeof(TextFieldType).FullName}."));
            Assert.That(() => HostOptions.Start(broken), Throws.TypeOf<OptionsValidationException>().With.Message.Contains(
                $"Jiron Form Builder cannot create its field types: The settings of the field type 'acme.broken' ({typeof(BrokenFieldType).FullName}) cannot be read: {typeof(BrokenSettings).FullName}.Ratio is of the type System.Double, which no setting holds"));
        });
    }

    // The services of a site whose composers changed the field types, as the core composer registers them, with the
    // clock and the logging every Umbraco site has.
    private static ServiceProvider Site(Action<FormBuilderFieldTypeCollectionBuilder> change)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(HostOptions.Folders());
        services.AddFormBuilderOptions(HostOptions.Configuration());
        var builder = new FormBuilderFieldTypeCollectionBuilder();
        change(builder);
        builder.RegisterWith(services);
        TestFieldTypes.AddCaptchaProviders(services);
        services.AddFieldTypeRegistry();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static JsonObject Form(JsonObject field) => new()
    {
        ["schemaVersion"] = 1,
        ["key"] = "f0000000-0000-4000-8000-000000000001",
        ["pages"] = new JsonArray(new JsonObject
        {
            ["key"] = "10000000-0000-4000-8000-000000000001",
            ["groups"] = new JsonArray(new JsonObject { ["key"] = "20000000-0000-4000-8000-000000000001", ["fields"] = new JsonArray(field) }),
        }),
    };

    /// <summary>A choice of colours, as a site might add one.</summary>
    public sealed class ColourFieldType() : FormBuilderFieldTypeBase<ColourSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.colour",
        Name = "Colour",
        Icon = "icon-colorpicker",
        PaletteGroup = FieldPaletteGroup.Choices,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Choice,
        Features = Features,
        Messages = FieldMessageKinds.Required,
    })
    {
        /// <summary>What a colour field supports.</summary>
        public const FieldFeatures Features = FieldFeatures.Label | FieldFeatures.Help | FieldFeatures.Required | FieldFeatures.VisibilityRules;
    }

    /// <summary>The settings of a colour field: one visitors see, and one that stays on the server.</summary>
    public sealed class ColourSettings
    {
        /// <summary>The colours.</summary>
        [FormBuilderSetting("Swatches", Public = true)]
        public IReadOnlyList<string> Swatches { get; init; } = [];

        /// <summary>Where the colours come from.</summary>
        [FormBuilderSetting("Source")]
        public string? Source { get; init; }
    }

    /// <summary>A site's own one-line text, without settings.</summary>
    public sealed class OwnTextFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(Valid with { Alias = "text", Name = "Text", Icon = "icon-autofill" });

    /// <summary>A type whose settings class holds a value no setting holds.</summary>
    public sealed class BrokenFieldType() : FormBuilderFieldTypeBase<BrokenSettings>(Valid with { Alias = "acme.broken" });

    /// <summary>A settings class with a double.</summary>
    public sealed class BrokenSettings
    {
        /// <summary>Not a setting's type.</summary>
        [FormBuilderSetting("Ratio")]
        public double Ratio { get; init; }
    }

    /// <summary>The settings of a type with a multiple setting.</summary>
    public sealed class MultipleSettings
    {
        /// <summary>Several choices.</summary>
        [FormBuilderSetting("Multiple")]
        public bool Multiple { get; init; }
    }

    private sealed class Declared(FieldTypeDeclaration declaration) : FormBuilderFieldTypeBase<NoFieldSettings>(declaration);

    private sealed class Declared<TSettings>(FieldTypeDeclaration declaration) : FormBuilderFieldTypeBase<TSettings>(declaration)
        where TSettings : class, new();
}
