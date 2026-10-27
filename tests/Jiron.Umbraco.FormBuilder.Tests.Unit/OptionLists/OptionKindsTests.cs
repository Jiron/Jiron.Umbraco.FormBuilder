using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// The kinds of this step (ADR-0073): manual lists, the countries and sources from code, each with its configuration
/// held to its schema and its own check at its path, and the countries resolved in the culture.
/// </summary>
[TestFixture]
public sealed class OptionKindsTests
{
    private static readonly string OUmlaut = ((char)0xD6).ToString();

    [Test]
    public void TheKinds_OfThisVersion_AreManualCountriesAndCode_WithWhereEachMayBeConfigured()
    {
        var kinds = new OptionListTestSite().Kinds;

        Assert.Multiple(() =>
        {
            Assert.That(kinds.All.Select(kind => (kind.Kind, kind.Name, kind.OnField, kind.HasItems)), Is.EqualTo(new[]
            {
                (OptionListKind.Manual, "manual", false, true),
                (OptionListKind.Countries, "countries", true, false),
                (OptionListKind.Code, "code", true, false),
            }));
            Assert.That(new[] { OptionListKind.Content, OptionListKind.DataType, OptionListKind.Url, OptionListKind.File }.Select(kinds.Find), Has.All.Null, "5.5b and 5.5c add them");
            Assert.That(kinds.Find(OptionListKind.Countries)!.Schema.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "countries" }));
            Assert.That(kinds.Find(OptionListKind.Code)!.Schema.Settings.Select(setting => (setting.Name, setting.Rules.Required)), Is.EqualTo(new[] { ("source", true) }));
            Assert.That(kinds.Find(OptionListKind.Manual)!.Schema.Settings, Is.Empty);
        });
    }

    [Test]
    public void AManualList_TakesNoConfiguration()
    {
        var manual = new OptionListTestSite().Kinds.Find(OptionListKind.Manual)!;

        Assert.Multiple(() =>
        {
            Assert.That(Check(manual, null), Is.Empty);
            Assert.That(Check(manual, """{"items":["a"]}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingUnknown, "$.config.items", "A manual list has no setting 'items'.") }));
        });
    }

    [Test]
    public void TheCountries_AreEveryCountryWithoutAny_AndOtherwiseKnownCodes_EachOnce()
    {
        var countries = new OptionListTestSite().Kinds.Find(OptionListKind.Countries)!;

        Assert.Multiple(() =>
        {
            Assert.That(Check(countries, null), Is.Empty, "every country");
            Assert.That(Check(countries, """{"countries":["CH","LI"]}"""), Is.Empty);
            Assert.That(Check(countries, """{"countries":["CH","ch","XX","CH"]}"""), Is.EqualTo(new[]
            {
                (DefinitionErrors.SettingInvalid, "$.config.countries[3]", "The setting 'countries' holds each value once; \"CH\" is there before."),
            }), "a country named twice is refused before the codes are looked at");
            Assert.That(Check(countries, """{"countries":["CH","ch","XX"]}"""), Is.EqualTo(new[]
            {
                (DefinitionErrors.SettingInvalid, "$.config.countries[1]", "A country is its two-letter code in capitals, such as CH, not 'ch'."),
                (DefinitionErrors.SettingInvalid, "$.config.countries[2]", "A country is its two-letter code in capitals, such as CH, not 'XX'."),
            }));
            Assert.That(Check(countries, """{"countries":"CH"}""").Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.countries") }));
            Assert.That(Check(countries, """{"regions":["CH"]}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingUnknown, "$.config.regions", "A countries list has no setting 'regions'.") }));
        });
    }

    [Test]
    public async Task TheCountries_AreNamedAndOrderedInTheCulture_AllOfThemWithoutAny()
    {
        var countries = new OptionListTestSite().Kinds.Find(OptionListKind.Countries)!;
        var three = SettingsObject.Parse("""{"countries":["DE","CH","AT"]}""");

        var english = await ResolveAsync(countries, three, "en-GB");
        var german = await ResolveAsync(countries, three, "de-CH");
        var all = await ResolveAsync(countries, null, "de");

        Assert.Multiple(() =>
        {
            Assert.That(english.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("AT", "Austria"), ("DE", "Germany"), ("CH", "Switzerland") }));
            Assert.That(german.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("DE", "Deutschland"), ("AT", OUmlaut + "sterreich"), ("CH", "Schweiz") }), "an umlaut sorts with its letter in German");
            Assert.That(all.Select(option => option.Value), Is.EquivalentTo(BuiltInChecks.CountryCodes));
            Assert.That(all.Select(option => option.Label), Is.Ordered.Using((IComparer<string?>)StringComparer.Create(MergeTagCultures.Culture("de"), ignoreCase: false)));
            Assert.That(all.Select(option => option.Label), Has.None.Null.And.None.Empty);
        });
    }

    [Test]
    public void ACodeList_NamesASourceTheSiteHas()
    {
        var code = new OptionListTestSite().Kinds.Find(OptionListKind.Code)!;

        Assert.Multiple(() =>
        {
            Assert.That(Check(code, """{"source":"months"}"""), Is.Empty);
            Assert.That(Check(code, null), Is.EqualTo(new[] { (DefinitionErrors.SettingMissing, "$.config.source", "The setting 'source' needs a value.") }));
            Assert.That(Check(code, """{"source":"Months"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.source", "The site has no option source 'Months'.") }), "a name is matched exactly");
            Assert.That(Check(code, """{"source":"products"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.source", "The site has no option source 'products'.") }));
            Assert.That(Check(code, """{"source":5}""").Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.source") }));
        });
    }

    [Test]
    public async Task ACodeList_GivesItsSourcesOptions_ForTheRequest_AndASourceThatIsGoneIsAFailure()
    {
        var products = new TestOptionSource("products", new FormBuilderOption("p1", "Kettle"));
        var code = new OptionListTestSite(products).Kinds.Find(OptionListKind.Code)!;
        var request = new FormBuilderOptionRequest("de", ["kitchen"], Guid.Parse("73000000-0000-4000-8000-000000000009"));

        var options = await code.ResolveAsync(new OptionKindRequest(SettingsObject.Parse("""{"source":"products"}"""), [], DictionaryTexts.None, request), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("p1", "Kettle") }));
            Assert.That(products.Requests, Is.EqualTo(new[] { request }));
            Assert.That(
                async () => await code.ResolveAsync(new OptionKindRequest(SettingsObject.Parse("""{"source":"gone"}"""), [], DictionaryTexts.None, request), CancellationToken.None),
                Throws.InvalidOperationException,
                "the service turns it into an unavailable source");
        });
    }

    private static List<(string Code, string Path, string Message)> Check(OptionKind kind, string? config)
    {
        var problems = new DefinitionProblems();
        var passed = kind.Check(config is null ? null : SettingsObject.Parse(config), "$.config", $"A {kind.Name} list", problems);
        var found = problems.ToReadOnlyList().Select(problem => (problem.Code, problem.Path, problem.Message)).ToList();
        Assert.That(passed, Is.EqualTo(found.Count == 0), "whether it passed");
        return found;
    }

    private static async Task<IReadOnlyList<FormBuilderOption>> ResolveAsync(OptionKind kind, SettingsObject? config, string culture) =>
        await kind.ResolveAsync(new OptionKindRequest(config, [], DictionaryTexts.None, new FormBuilderOptionRequest(culture, [], null)), CancellationToken.None);
}
