using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// Texts that name a dictionary item (<c>#key</c>, ADR-0002) in <c>field.*</c> and <c>entry.answers</c> (ADR-0066,
/// ADR-0067): a field's label, an option's label, a grid row's statement and a text of a field's settings show the
/// item's text in the culture, and a text whose item has no text shows as it is written.
/// </summary>
public sealed class DictionaryAnswersTests
{
    /// <summary>English and German, with the form's texts in the dictionary.</summary>
    private static TestUmbraco Dictionary()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Language("de-DE");
        umbraco.Item("Forms.Email", new Dictionary<string, string> { ["en-GB"] = "Email", ["de-DE"] = "E-Mail" });
        umbraco.Item("Colours.Red", new Dictionary<string, string> { ["en-GB"] = "Red", ["de-DE"] = "Rot" });
        umbraco.Item("Grid.Speed", new Dictionary<string, string> { ["en-GB"] = "Speed", ["de-DE"] = "Tempo" });
        umbraco.Item("Words.Sure", new Dictionary<string, string> { ["en-GB"] = "Sure", ["de-DE"] = "Gerne" });
        umbraco.Item("Forms.Untranslated", new Dictionary<string, string> { ["it-IT"] = "Colore" });
        return umbraco;
    }

    /// <summary>A form whose texts name dictionary items, and its answers.</summary>
    private static (FormDefinition Form, MergeTagAnswers Answers) Contact()
    {
        var email = Field("email", "email", "#Forms.Email");
        var colour = Field("checkboxes", "colour", "#Forms.Untranslated") with { Options = Options(("red", "#Colours.Red"), ("blue", "Blue"), ("green", "#Colours.Green")) };
        var speed = NewKey();
        var grid = Field("grid", "rating", "Rating") with
        {
            Options = Options(("g", "#Colours.Red"), ("p", "Poor")),
            Rows = [new GridRow { Key = speed, Label = new LocalizedText("#Grid.Speed") }],
        };
        var sure = Field("yesno", "sure", "Coming?", """{"yesLabel":{"":"#Words.Sure"}}""");
        var form = Form(email, colour, grid, sure);
        var answers = Answers(
            Answer(email, "anna@example.com"),
            Answer(colour, "red", "green"),
            new MergeTagAnswer(grid.Key, [new FieldValue("g", speed)]),
            Answer(sure, "yes"));
        return (form, answers);
    }

    [TestCase("en-GB", "Email|Red, #Colours.Green|Speed: Red|Sure")]
    [TestCase("de-DE", "E-Mail|Rot, #Colours.Green|Tempo: Rot|Gerne")]
    public async Task TheFieldSource_ShowsTheDictionarysTexts_InTheCulture(string culture, string expected)
    {
        var (form, answers) = Contact();
        var site = MergeTagTestSite.Create(umbraco: Dictionary());

        var rendered = await site.RenderAsync("{{ field.email.label }}|{{ field.colour }}|{{ field.rating }}|{{ field.sure }}", Scope(form, answers, culture));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(expected), "an option without an item shows as it is written");
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task TheAnswers_ShowTheDictionarysTexts_AsLinesAndInTheTable()
    {
        var (form, answers) = Contact();
        var site = MergeTagTestSite.Create(umbraco: Dictionary());

        var text = await site.RenderAsync("{{ entry.answers }}", Scope(form, answers, "de-DE"));
        var html = await site.RenderAsync("{{ entry.answers }}", Scope(form, answers, "de-DE"), MergeTagPlace.Mail, MergeTagContext.Html);

        Assert.Multiple(() =>
        {
            Assert.That(text.Text, Is.EqualTo(string.Join('\n', "E-Mail: anna@example.com", "#Forms.Untranslated: Rot, #Colours.Green", "Rating: Tempo: Rot", "Coming?: Gerne")), "a label whose item has no text in German shows as written");
            Assert.That(html.Text, Does.Contain("<tr><th scope=\"row\">E-Mail</th><td>anna@example.com</td></tr>"));
            Assert.That(html.Text, Does.Contain("<tr><th scope=\"row\">Coming?</th><td>Gerne</td></tr>"));
        });
    }

    [Test]
    public async Task TheDictionary_IsReadOncePerRendering_AndNotAtAllForAFormThatNamesNoItem()
    {
        var (form, answers) = Contact();
        var umbraco = Dictionary();
        var site = MergeTagTestSite.Create(umbraco: umbraco);
        var plain = Form(Field("text", "name", "Name"));

        await site.RenderAsync("{{ field.email.label }} {{ field.colour }} {{ field.rating }} {{ entry.answers }}", Scope(form, answers));
        var reads = umbraco.DictionaryReads;
        await site.RenderAsync("{{ field.name }} {{ entry.answers }}", Scope(plain, Answers(Answer(plain.Pages[0].Groups[0].Fields[0], "Anna"))));

        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.EqualTo(1));
            Assert.That(umbraco.DictionaryReads, Is.EqualTo(1), "a form that names no item");
        });
    }

    [Test]
    public void AText_NamesAnItemOnlyWhenItIsAKeyAlone()
    {
        var texts = new DictionaryTexts(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Forms.Email"] = "Email" }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));

        Assert.Multiple(() =>
        {
            Assert.That(texts.Resolve(new LocalizedText("#forms.email"), "en-GB"), Is.EqualTo("Email"), "a key in any letter case");
            Assert.That(texts.Resolve(new LocalizedText("Your #Forms.Email"), "en-GB"), Is.EqualTo("Your #Forms.Email"), "a key inside a text");
            Assert.That(texts.Resolve(Text((string.Empty, "Mail"), ("de", "#Forms.Email")), "de-CH"), Is.EqualTo("Email"), "the culture's own text names it");
            Assert.That(texts.Resolve(null, "en-GB"), Is.Empty);
            Assert.That(DictionaryTexts.None.Resolve(new LocalizedText("#Forms.Email"), "en-GB"), Is.EqualTo("#Forms.Email"));
        });
    }
}
