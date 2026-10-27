using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists.OptionListTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// The publish check of a field's source (ADR-0073): each choice field's source configuration is held to its kind at
/// <c>...options.config</c>, in every page, group and repeater; a kind the site lacks and an upload are refused at
/// <c>...options.source</c>; a field's own options and a library list are the structural validation's.
/// </summary>
[TestFixture]
public sealed class OptionSourcePublishCheckTests
{
    [Test]
    public async Task AFieldsSourceConfiguration_IsHeldToItsKind_AtItsPath()
    {
        var problems = await CheckAsync(
            Field(OptionSource.Countries, """{"countries":["CH","XX"]}"""),
            Field(OptionSource.Code, """{"source":"products"}""") with { Key = Guid.NewGuid() },
            Field(OptionSource.Code) with { Key = Guid.NewGuid() },
            Field(OptionSource.Countries, """{"regions":["CH"]}""") with { Key = Guid.NewGuid() });

        Assert.That(problems, Is.EqualTo(new[]
        {
            new DefinitionProblem(DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[0].options.config.countries[1]", "A country is its two-letter code in capitals, such as CH, not 'XX'."),
            new DefinitionProblem(DefinitionErrors.SettingInvalid, "$.pages[0].groups[0].fields[1].options.config.source", "The site has no option source 'products'."),
            new DefinitionProblem(DefinitionErrors.SettingMissing, "$.pages[0].groups[0].fields[2].options.config.source", "The setting 'source' needs a value."),
            new DefinitionProblem(DefinitionErrors.SettingUnknown, "$.pages[0].groups[0].fields[3].options.config.regions", "A countries source has no setting 'regions'."),
        }));
    }

    [Test]
    public async Task AKindTheSiteLacks_AndAnUpload_AreRefusedAtTheSource()
    {
        var problems = await CheckAsync(
            Field(OptionSource.Content, """{"root":"x"}"""),
            Field(OptionSource.File) with { Key = Guid.NewGuid() },
            Field(OptionSource.Url) with { Key = Guid.NewGuid() },
            Field(OptionSource.DataType) with { Key = Guid.NewGuid() });

        Assert.That(problems, Is.EqualTo(new[]
        {
            new DefinitionProblem(DefinitionErrors.OptionsInvalid, "$.pages[0].groups[0].fields[0].options.source", "The site has no option source of the kind 'content'."),
            new DefinitionProblem(DefinitionErrors.OptionsInvalid, "$.pages[0].groups[0].fields[1].options.source", "A field takes no upload as its source: an upload becomes the field's own options, or a library list's."),
            new DefinitionProblem(DefinitionErrors.OptionsInvalid, "$.pages[0].groups[0].fields[2].options.source", "The site has no option source of the kind 'url'."),
            new DefinitionProblem(DefinitionErrors.OptionsInvalid, "$.pages[0].groups[0].fields[3].options.source", "The site has no option source of the kind 'dataType'."),
        }));
    }

    [Test]
    public async Task EveryPageGroupAndRepeater_IsChecked_AndOwnOptionsListsAndValidSourcesPass()
    {
        var nested = Field(OptionSource.Code, """{"source":"gone"}""") with { Key = Guid.NewGuid() };
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
                                Field(new FieldOptions { Items = [new FieldOption { Value = "a" }] }),
                                Field(new FieldOptions { Source = OptionSource.List, OptionList = Guid.NewGuid(), Config = SettingsObject.Parse("""{"x":1}""") }) with { Key = Guid.NewGuid() },
                                Field(OptionSource.Code, """{"source":"months"}""") with { Key = Guid.NewGuid() },
                                Field(OptionSource.Countries) with { Key = Guid.NewGuid() },
                            ],
                        },
                    ],
                },
                new FormPage
                {
                    Key = Guid.NewGuid(),
                    Groups =
                    [
                        new FormGroup { Key = Guid.NewGuid() },
                        new FormGroup
                        {
                            Key = Guid.NewGuid(),
                            Fields = [new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "guests", Fields = [new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name" }, nested] }],
                        },
                    ],
                },
            ],
        };

        var problems = await new OptionSourcePublishCheck(new OptionListTestSite().Kinds).CheckAsync(definition, CancellationToken.None);

        Assert.That(problems.Select(problem => problem.Path), Is.EqualTo(new[] { "$.pages[1].groups[1].fields[0].fields[1].options.config.source" }), "the structural validation refuses a list's configuration");
    }

    private static async Task<IReadOnlyList<DefinitionProblem>> CheckAsync(params FormField[] fields)
    {
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages = [new FormPage { Key = Guid.NewGuid(), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = fields }] }],
        };
        return await new OptionSourcePublishCheck(new OptionListTestSite().Kinds).CheckAsync(definition, CancellationToken.None);
    }
}
