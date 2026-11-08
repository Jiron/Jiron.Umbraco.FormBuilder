using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
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
                    Is.EqualTo(FormLibraryUses.Of(definitions[i]).Select(use => KeyValuePair.Create(use.Key, use.Count))),
                    $"definition {i}");
            }
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
        });
    }

    private static FormField Field(Guid list) => new()
    {
        Key = Guid.NewGuid(),
        Type = "dropdown",
        Options = new FieldOptions { Source = OptionSource.List, OptionList = list },
    };
}
