using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// What of the library a definition uses (ADR-0214): the option lists its fields take options from, each with how many
/// fields; and the migration's frozen reading of a stored definition, which finds the same in every definition.
/// </summary>
[TestFixture]
public sealed class FormLibraryUsesTests
{
    private const int Seeds = 200;

    private static readonly Guid Topics = Guid.Parse("72000000-0000-4000-8000-000000000021");
    private static readonly Guid Countries = Guid.Parse("72000000-0000-4000-8000-000000000022");

    [Test]
    public void AListsFields_AreCountedOnce_ChildrenOfARepeatableGroupIncluded_InKeyOrder()
    {
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages =
            [
                new FormPage
                {
                    Key = Guid.NewGuid(),
                    Groups =
                    [
                        new FormGroup
                        {
                            Key = Guid.NewGuid(),
                            Fields =
                            [
                                Field(Countries),
                                new FormField { Key = Guid.NewGuid(), Type = "repeater", Fields = [Field(Topics), Field(Countries)] },
                                new FormField { Key = Guid.NewGuid(), Type = "dropdown", Options = new FieldOptions { Items = [new FieldOption { Value = "a" }] } },
                            ],
                        },
                    ],
                },
            ],
        };

        Assert.That(FormLibraryUses.Of(definition), Is.EqualTo(new[] { new LibraryUse(LibraryUseKind.OptionList, Topics, 1), new LibraryUse(LibraryUseKind.OptionList, Countries, 2) }));
    }

    [Test]
    public void TheMigrationsReading_FindsWhatTheModelSays_InTheFullDefinition_AndInGeneratedOnes()
    {
        var generator = new DefinitionGenerator(4511);
        var definitions = new List<FormDefinition> { DefinitionFixtures.Read(DefinitionFixtures.Full) };
        for (var i = 0; i < Seeds; i++)
        {
            definitions.Add(generator.Next());
        }

        Assert.Multiple(() =>
        {
            Assert.That(FormLibraryUses.Of(definitions[0]), Is.Not.Empty, "the full definition takes options from a list");
            for (var i = 0; i < definitions.Count; i++)
            {
                var stored = FormDefinitionSerializer.WriteString(definitions[i]);
                Assert.That(
                    AddFormLibraryUsesV1.ListsOf(stored).OrderBy(pair => pair.Key),
                    Is.EqualTo(FormLibraryUses.Of(definitions[i]).Where(use => use.Kind == LibraryUseKind.OptionList).Select(use => KeyValuePair.Create(use.Key, use.Count))),
                    $"definition {i}");
                Assert.That(
                    AddThemeAndLayoutUsesV1.UsesOf(stored),
                    Is.EqualTo(FormLibraryUses.Of(definitions[i]).Where(use => use.Kind != LibraryUseKind.OptionList).Select(use => (StoredNames.Of(use.Kind), use.Key, use.Count))),
                    $"definition {i}'s theme and layouts");
            }
        });
    }

    [Test]
    public void TheTheme_AndTheLayoutsOfEmailActions_AreFound_EachLayoutWithItsActions()
    {
        var brand = Guid.Parse("72000000-0000-4000-8000-000000000031");
        var plain = Guid.Parse("72000000-0000-4000-8000-000000000032");
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Settings = new FormSettings { Appearance = new FormAppearanceSettings { Theme = "Autumn" } },
            Actions =
            [
                Action("email", $$"""{"layout":"{{plain}}"}"""),
                Action("email", $$"""{"layout":"{{brand}}","to":["a@example.com"]}"""),
                Action("email", $$"""{"layout":"{{brand}}"}"""),
                Action("email", """{"layout":"not a key"}"""),
                Action("email", null),
                Action("webhook", $$"""{"layout":"{{brand}}"}"""),
            ],
        };

        Assert.That(FormLibraryUses.Of(definition), Is.EqualTo(new[]
        {
            new LibraryUse(LibraryUseKind.Theme, FormLibraryUses.ThemeKey("autumn"), 1),
            new LibraryUse(LibraryUseKind.EmailLayout, brand, 2),
            new LibraryUse(LibraryUseKind.EmailLayout, plain, 1),
        }));
    }

    [Test]
    public void AThemesKey_IsItsAliasWithoutRegardToCase_ANameBasedUuid()
    {
        var key = FormLibraryUses.ThemeKey("Autumn");

        Assert.Multiple(() =>
        {
            Assert.That(FormLibraryUses.ThemeKey(" autumn "), Is.EqualTo(key));
            Assert.That(FormLibraryUses.ThemeKey("winter"), Is.Not.EqualTo(key));
            Assert.That(key.Version, Is.EqualTo(8));
            Assert.That(key.Variant, Is.EqualTo(0b1000).Or.EqualTo(0b1001).Or.EqualTo(0b1010).Or.EqualTo(0b1011), "RFC 9562's variant");
            Assert.That(AddThemeAndLayoutUsesV1.UsesOf("""{"settings":{"appearance":{"theme":"AUTUMN"}}}"""), Is.EqualTo(new[] { ("theme", key, 1) }), "the migration's frozen key is the model's");
        });
    }

    [Test]
    public void TheMigrationsReading_OfADefinitionThatDoesNotRead_FindsNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AddFormLibraryUsesV1.ListsOf("{not json"), Is.Empty);
            Assert.That(AddFormLibraryUsesV1.ListsOf("""{"pages":{"groups":[]}}"""), Is.Empty);
            Assert.That(AddFormLibraryUsesV1.ListsOf("""{"pages":[{"groups":[{"fields":[{"options":{"source":"list","list":"not a key"}}]}]}]}"""), Is.Empty);
            Assert.That(AddThemeAndLayoutUsesV1.UsesOf("{not json"), Is.Empty);
            Assert.That(AddThemeAndLayoutUsesV1.UsesOf("""{"settings":{"appearance":{"theme":"  "}},"actions":{"type":"email"}}"""), Is.Empty);
        });
    }

    private static FormAction Action(string type, string? settings) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static FormField Field(Guid list) => new()
    {
        Key = Guid.NewGuid(),
        Type = "dropdown",
        Options = new FieldOptions { Source = OptionSource.List, OptionList = list },
    };
}
