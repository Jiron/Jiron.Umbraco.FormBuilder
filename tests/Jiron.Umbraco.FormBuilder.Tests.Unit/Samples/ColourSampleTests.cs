using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Samples.Colour;
using Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Samples;

/// <summary>
/// The colour sample's field type (docs/extending.md, ADR-0234): its colours are a list of names and values, which
/// publishing holds to their shape; an answer is one of the colours the field offers; and mail, merge tags and exports
/// show a colour by its name and value.
/// </summary>
public sealed class ColourSampleTests
{
    private static readonly IFormBuilderFieldType Type = new ColourFieldType();

    private static readonly MergeTagCultures Cultures = new();

    private const string Offered = """[{"name":"Blue","value":"#1f6feb"},{"name":"Forest","value":"#2da44e"}]""";

    public static IEnumerable<TestCaseData> Colours()
    {
        static TestCaseData Case(string name, string json, string? problem) => new TestCaseData(json, problem).SetName($"ChecksTheColours({name})");

        yield return Case("two colours", Offered, null);
        yield return Case("a name with spaces round it", """[{"name":" Blue ","value":"#1f6feb"}]""", null);
        yield return Case("no list", """{"name":"Blue","value":"#1f6feb"}""", "The colours are a list.");
        yield return Case("an empty list", "[]", "Offer at least one colour.");
        yield return Case("a colour without a name", """[{"name":"  ","value":"#1f6feb"}]""", "Colour 1 needs a name of at most 40 characters and a value such as #1f6feb.");
        yield return Case("a name too long", $$"""[{"name":"Blue","value":"#1f6feb"},{"name":"{{new string('x', 41)}}","value":"#2da44e"}]""", "Colour 2 needs a name of at most 40 characters and a value such as #1f6feb.");
        yield return Case("a value in capitals", """[{"name":"Blue","value":"#1F6FEB"}]""", "Colour 1 needs a name of at most 40 characters and a value such as #1f6feb.");
        yield return Case("a short value", """[{"name":"Blue","value":"#16e"}]""", "Colour 1 needs a name of at most 40 characters and a value such as #1f6feb.");
        yield return Case("a value that is a style", """[{"name":"Blue","value":"red;background:url(x)"}]""", "Colour 1 needs a name of at most 40 characters and a value such as #1f6feb.");
        yield return Case("a number as a name", """[{"name":7,"value":"#1f6feb"}]""", "Colour 1 needs a name of at most 40 characters and a value such as #1f6feb.");
        yield return Case("a value twice", """[{"name":"Blue","value":"#1f6feb"},{"name":"Sky","value":"#1f6feb"}]""", "#1f6feb is offered twice.");
        yield return Case("too many colours", $"[{string.Join(',', Enumerable.Range(0, 25).Select(at => $$"""{"name":"C{{at}}","value":"#0000{{at:x2}}"}"""))}]", "Offer at most 24 colours.");
    }

    [TestCaseSource(nameof(Colours))]
    public void ChecksTheColours(string json, string? problem)
    {
        Assert.That(ColourSwatch.Problem(Parse(json)), Is.EqualTo(problem));

        // An empty list is stored as no setting at all, so a field holds the default colours instead.
        if (json != "[]")
        {
            var problems = new DefinitionProblems();
            Type.CheckSettings(new FieldSettingsCheck(Field($$"""{"colours":{{json}}}"""), "$.f.settings", problems));
            Assert.That(problems.ToReadOnlyList().Select(each => (each.Path, each.Message)), Is.EqualTo(problem is null ? [] : new[] { ("$.f.settings.colours", problem) }));
        }
    }

    [Test]
    public void TheColours_AreReadInOrder_LeavingOutWhatIsNoColour()
    {
        var read = ColourSwatch.Read(Parse("""[{"name":" Blue ","value":"#1f6feb"},{"name":"","value":"#2da44e"},"red",{"name":"Red","value":"#cf222e"}]"""));

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(new[] { new ColourSwatch("Blue", "#1f6feb"), new ColourSwatch("Red", "#cf222e") }));
            Assert.That(ColourSwatch.Read(Parse("""{"colours":[]}""")), Is.Empty, "no list");
            Assert.That(ColourSwatch.Read(ColourSwatch.Write(read)), Is.EqualTo(read), "written as it is read");
        });
    }

    [Test]
    public void ANewField_OffersThreeColours_WithTheirNames()
    {
        var defaults = new ColourSettings();

        Assert.Multiple(() =>
        {
            Assert.That(ColourSwatch.Read(defaults.Colours).Select(colour => colour.Name), Is.EqualTo(new[] { "Blue", "Green", "Red" }));
            Assert.That(ColourSwatch.Problem(defaults.Colours), Is.Null);
            Assert.That(defaults.ShowNames, Is.True);
        });
    }

    [TestCase("#2da44e", new string[0])]
    [TestCase("#000000", new[] { "invalid" }, Description = "a colour the field does not offer")]
    [TestCase("#2DA44E", new[] { "invalid" }, Description = "an offered colour in capitals")]
    [TestCase("Forest", new[] { "invalid" }, Description = "a colour by its name")]
    public void AnAnswer_IsOneOfTheOfferedColours(string answer, string[] errors)
    {
        var validation = new FieldValidation(Field($$"""{"colours":{{Offered}}}"""), new FieldAnswers([answer]), [new FieldValue(answer)], Type.Declaration.Messages, new FieldCheckInput(false, ValidationCase.Today));
        Type.Validate(validation);

        Assert.That(validation.Errors.Select(ValidationCase.Name), Is.EqualTo(errors));
    }

    [Test]
    public void AnAnswer_IsOneValue_AsLongAsAColoursValue()
    {
        var limits = Type.AnswerLimits(Field($$"""{"colours":{{Offered}}}"""));

        Assert.That((limits.LongestText, limits.MostTexts), Is.EqualTo((7, 1)));
    }

    [TestCase("#2da44e", "Forest (#2da44e)")]
    [TestCase("#cf222e", "#cf222e", Description = "a colour the field no longer offers")]
    public void AColour_ShowsByItsNameAndValue(string answer, string shown)
    {
        var display = new FieldDisplay(Field($$"""{"colours":{{Offered}}}"""), [new FieldValue(answer)], [], "en-GB", Cultures.For("en-GB"), false);
        Type.Display(display);

        Assert.That(display.Texts.Select(text => text.Text), Is.EqualTo(new[] { shown }));
    }

    [Test]
    public void TheType_IsAChoiceThatHoldsText_WithItsOwnEditorForItsColours()
    {
        var schema = Type.Declaration;
        var colours = Type.SettingsSchema.Settings.Single(setting => setting.Name == ColourSettings.ColoursName);

        Assert.Multiple(() =>
        {
            Assert.That(schema.Alias, Is.EqualTo("sample.colour"));
            Assert.That((schema.HoldsValue, schema.Storage, schema.RuleKind), Is.EqualTo((true, FieldStorageKind.Text, FieldKind.Choice)));
            Assert.That((colours.Kind, colours.Editor, colours.IsPublic), Is.EqualTo((FieldSettingKind.Json, "sample.swatches", true)));
            Assert.That(Type.SettingsSchema.Settings.Select(setting => setting.Name), Is.EqualTo(new[] { "colours", "showNames" }));
        });
    }

    private static FormField Field(string settings) =>
        new() { Key = Guid.NewGuid(), Type = ColourFieldType.TypeAlias, Alias = "colour", Settings = SettingsObject.Parse(settings) };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
