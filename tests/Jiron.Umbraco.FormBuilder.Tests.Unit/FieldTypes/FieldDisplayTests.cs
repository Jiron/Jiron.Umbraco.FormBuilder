using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// What a field type shows its values as (ADR-0066): the helpers a type writes its texts with, the words of yes and no,
/// country names from .NET's culture data, the display the built-in types fall back to, and the answers merge tags read.
/// </summary>
public sealed class FieldDisplayTests
{
    private static readonly MergeTagCultures Cultures = new();

    private static FieldDisplay Display(FormField? field = null, string culture = "en-GB", bool listing = false, params string[] values) =>
        new(field ?? new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "a" }, [.. values.Select(value => new FieldValue(value))], [], culture, Cultures.For(culture), listing);

    [TestCase("CH", "de-CH", "Schweiz")]
    [TestCase("CH", "fr-CH", "Suisse")]
    [TestCase("CH", "it", "Svizzera")]
    [TestCase("CH", "en-US", "Switzerland")]
    [TestCase("FR", "de-CH", "Frankreich")]
    [TestCase("DE", "fr-FR", "Allemagne")]
    [TestCase("ch", "de", "Schweiz")]
    [TestCase("CH", "", "Switzerland")]
    [TestCase("CH", "xx-not-a-culture", "Switzerland")]
    [TestCase("XX", "de", "XX")]
    [TestCase("C1", "de", "C1")]
    [TestCase("CHE", "de", "CHE")]
    [TestCase("", "de", "")]
    public void ACountry_IsNamedInTheCulturesLanguage_OrByItsCode(string code, string culture, string name)
    {
        Assert.That(Display(culture: culture).CountryName(code), Is.EqualTo(name));
    }

    [TestCase("en-GB", "Yes", "No")]
    [TestCase("de-CH", "Ja", "Nein")]
    [TestCase("DE", "Ja", "Nein")]
    [TestCase("fr-FR", "Yes", "No")]
    [TestCase("", "Yes", "No")]
    public void TheWordsOfYesAndNo_AreTheCulturesOrItsLanguagesOrEnglish(string culture, string yes, string no)
    {
        var display = Display(culture: culture);

        Assert.Multiple(() =>
        {
            Assert.That(display.Yes, Is.EqualTo(yes));
            Assert.That(display.No, Is.EqualTo(no));
        });
    }

    [Test]
    public void TheWords_AreInEnglishAndGerman_TheOtherLanguagesComeIn14_2()
    {
        Assert.That(FieldDisplayWords.Cultures, Is.EquivalentTo(new[] { "en", "de" }));
    }

    [Test]
    public void ANumber_IsWrittenAsTheCultureWritesIt_WithItsOwnPlacesOrRoundedAndPadded()
    {
        var display = Display(culture: "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(display.Number("1234.5"), Is.EqualTo("1.234,5"));
            Assert.That(display.Number("1234.5", 2), Is.EqualTo("1.234,50"));
            Assert.That(display.Number("2.5", 0), Is.EqualTo("3"), "half away from zero");
            Assert.That(display.Number("-2.5", -1), Is.EqualTo("-3"), "below 0 places counts as 0");
            Assert.That(display.Number("0.1", 99), Is.EqualTo("0,1" + new string((char)48, 14)), "above 15 places counts as 15");
            Assert.That(display.Number("twelve"), Is.EqualTo("twelve"), "no number in the wire form stays as it is");
        });
    }

    [Test]
    public void ADateOrTime_IsWrittenInTheCulturesFormatForIt_OrStaysAsItIs()
    {
        var display = Display(culture: "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(display.Date("2026-03-07"), Is.EqualTo("07.03.2026"));
            Assert.That(display.Date("09:05"), Is.EqualTo("09:05"));
            Assert.That(display.Date("2026-03-07T09:05"), Is.EqualTo("07.03.2026 09:05"));
            Assert.That(display.Date("2026-02-30"), Is.EqualTo("2026-02-30"));
            Assert.That(display.Date("soon"), Is.EqualTo("soon"));
        });
    }

    [Test]
    public void AnOption_IsShownByItsLabelInTheCulture_OrByItsValue()
    {
        var field = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "radio",
            Alias = "fruit",
            Options = new FieldOptions
            {
                Items =
                [
                    new FieldOption { Value = "a", Label = new LocalizedText([new(string.Empty, "Apple"), new("de", "Apfel")]) },
                    new FieldOption { Value = "b" },
                ],
            },
        };
        var display = Display(field, "de-AT");

        Assert.Multiple(() =>
        {
            Assert.That(display.OptionLabel("a"), Is.EqualTo("Apfel"));
            Assert.That(display.OptionLabel("b"), Is.EqualTo("b"), "an option without a label");
            Assert.That(display.OptionLabel("Quince"), Is.EqualTo("Quince"), "a text of the visitor's own");
            Assert.That(display.Text(null), Is.Empty);
        });
    }

    [Test]
    public void AnEmptyText_ShowsNothing_AndTheTextsKeepTheirOrderAndWhatIsBehindThem()
    {
        var display = Display();

        display.Add(string.Empty);
        display.Add("one");
        display.AddNumber(string.Empty, "1");
        display.AddNumber("2.0", "2");
        display.AddDate("today", "2026-03-07");

        Assert.Multiple(() =>
        {
            Assert.That(display.Texts, Is.EqualTo(new[]
            {
                new FieldDisplay.DisplayText("one", null, null),
                new FieldDisplay.DisplayText("2.0", "2", null),
                new FieldDisplay.DisplayText("today", null, "2026-03-07"),
            }));
            Assert.That(() => display.AddNumber("x", string.Empty), Throws.ArgumentException);
            Assert.That(() => display.AddDate("x", null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void TheFiles_AreOneForEachValue_OrNone()
    {
        var field = new FormField { Key = Guid.NewGuid(), Type = "file", Alias = "files" };

        Assert.That(
            () => new FieldDisplay(field, [new FieldValue("upload-1"), new FieldValue("upload-2")], [new FieldFile("a.pdf", 1)], "en", Cultures.For("en"), false),
            Throws.ArgumentException.With.Property(nameof(ArgumentException.ParamName)).EqualTo("files"));
    }

    [TestCase("CHF", "12", "net", "CHF 12 net")]
    [TestCase(" CHF ", "12", "", "CHF 12")]
    [TestCase("", "12", "%", "12 %")]
    [TestCase("", "12", "", "12")]
    public void APrefixAndASuffix_AreApartFromTheTextBySpaces(string prefix, string text, string suffix, string shown)
    {
        Assert.That(FieldDisplayDefaults.Around(prefix, text, suffix), Is.EqualTo(shown));
    }

    [Test]
    public void AHiddenValueOrAnUnseenCalculation_ShowsInItsTag_ButNotInTheListOfTheAnswers()
    {
        var hidden = TestFieldTypes.Registry.FindType("hidden")!;
        var calculation = TestFieldTypes.Registry.FindType("calculation")!;
        var unseen = new FormField { Key = Guid.NewGuid(), Type = "calculation", Alias = "total", Settings = SettingsObject.Parse("""{"formula":"1","visible":false}""") };

        var tag = Display(listing: false, values: "spring");
        var listed = Display(listing: true, values: "spring");
        var unseenTag = Display(unseen, listing: false, values: "3");
        var unseenListed = Display(unseen, listing: true, values: "3");
        hidden.Display(tag);
        hidden.Display(listed);
        calculation.Display(unseenTag);
        calculation.Display(unseenListed);

        Assert.Multiple(() =>
        {
            Assert.That(tag.Texts.Select(text => text.Text), Is.EqualTo(new[] { "spring" }));
            Assert.That(listed.Texts, Is.Empty);
            Assert.That(unseenTag.Texts.Select(text => text.Text), Is.EqualTo(new[] { "3.00" }));
            Assert.That(unseenListed.Texts, Is.Empty);
        });
    }

    [Test]
    public void ATypeThatHoldsNoValue_ShowsNothing_AndAValueWithPartsThatIsNoneShowsAsItIs()
    {
        var heading = TestFieldTypes.Registry.FindType("heading")!;
        var plain = new FieldTypeDeclaration { Alias = "acme.parts", Name = "Parts", Icon = "icon-box", PaletteGroup = FieldPaletteGroup.People, HoldsValue = true, Storage = FieldStorageKind.Json };
        var none = Display(values: "x");
        var parts = Display(values: """{"a":"One","b":"Two"}""");
        var broken = Display(values: "not json");

        heading.Display(none);
        FieldDisplayDefaults.Display(plain, parts);
        FieldDisplayDefaults.Display(plain, broken);

        Assert.Multiple(() =>
        {
            Assert.That(none.Texts, Is.Empty);
            Assert.That(parts.Texts.Single().Text, Is.EqualTo("One, Two"));
            Assert.That(broken.Texts.Single().Text, Is.EqualTo("not json"));
        });
    }

    [Test]
    public void ATypeWrittenAgainstTheInterfaceAlone_ShowsAsTheDefaultsShow()
    {
        IFormBuilderFieldType type = new PlainChoiceFieldType();
        var field = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "acme.choice",
            Alias = "pick",
            Options = new FieldOptions { Items = [new FieldOption { Value = "a", Label = new LocalizedText("Apple") }] },
        };
        var display = Display(field, values: ["a", "Quince"]);

        type.Display(display);

        Assert.That(display.Texts.Select(text => text.Text), Is.EqualTo(new[] { "Apple", "Quince" }));
    }

    [Test]
    public void TheAnswers_RefuseTwoForOneFieldAndRow_AndAnAnswerWithoutValuesCountsAsNone()
    {
        var field = Guid.NewGuid();
        var empty = new MergeTagAnswers([new MergeTagAnswer(field, [])]);

        Assert.Multiple(() =>
        {
            Assert.That(() => new MergeTagAnswers([new MergeTagAnswer(field, [new FieldValue("a")]), new MergeTagAnswer(field, [new FieldValue("b")])]), Throws.ArgumentException);
            Assert.That(() => new MergeTagAnswers([new MergeTagAnswer(field, [new FieldValue("a")]), new MergeTagAnswer(field, [new FieldValue("b")], row: 1)]), Throws.Nothing);
            Assert.That(() => new MergeTagAnswers([null!]), Throws.ArgumentException);
            Assert.That(empty.Find(field), Is.Null);
            Assert.That(empty.Rows([field]), Is.Empty);
            Assert.That(MergeTagAnswers.None.Hidden, Is.Empty);
        });
    }

    [Test]
    public void AnAnswer_RefusesNullsANegativeRowAndFilesThatAreNotOneForEachValue()
    {
        var field = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(() => new MergeTagAnswer(field, [null!]), Throws.ArgumentException);
            Assert.That(() => new MergeTagAnswer(field, [new FieldValue("a")], [null!]), Throws.ArgumentException);
            Assert.That(() => new MergeTagAnswer(field, [new FieldValue("a")], [new FieldFile("a.pdf", 1), new FieldFile("b.pdf", 1)]), Throws.ArgumentException);
            Assert.That(() => new MergeTagAnswer(field, [new FieldValue("a")], row: -1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(new MergeTagAnswer(field, [new FieldValue("a")], []).Files, Is.Empty);
        });
    }

    // A choice written against the interface alone: every step is the interface's default.
    private sealed class PlainChoiceFieldType : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = new()
        {
            Alias = "acme.choice",
            Name = "Choice",
            Icon = "icon-list",
            PaletteGroup = FieldPaletteGroup.Choices,
            HoldsValue = true,
            Storage = FieldStorageKind.Text,
            RuleKind = FieldKind.Choice,
        };

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));
    }
}
