using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// A published form as a visitor's page gets it in one culture (ADR-0084): its texts in the culture with the usual
/// fallback, the dictionary's items read once for all the texts that name one, rich text sanitised wherever it comes
/// from, plain text left for the page to escape, and a lone surrogate replaced, so the form always writes.
/// </summary>
[TestFixture]
public sealed class VisitorFormsTests
{
    private TestUmbraco _umbraco = null!;
    private VisitorForms _forms = null!;

    [SetUp]
    public void CreateTheSite()
    {
        _umbraco = new TestUmbraco();
        _umbraco.Language("en-US", isDefault: true);
        _umbraco.Language("de-DE", fallback: "en-US");
        _forms = new VisitorForms(new MergeTagDictionary(_umbraco.Dictionary, _umbraco.Languages), new EditorHtml(), TestFieldTypes.Registry);
    }

    [Test]
    public async Task TheTexts_AreTheCulturesOwn_WithTheDictionarysItems_ReadOnceForAll()
    {
        _umbraco.Item("Forms.Title", new Dictionary<string, string> { ["en-US"] = "Your details", ["de-DE"] = "Ihre Angaben" });
        _umbraco.Item("Forms.Intro", new Dictionary<string, string> { ["en-US"] = "<p>Hello<script>steal()</script></p>" });
        var projection = Projection(page => page with { Title = new LocalizedText("#Forms.Title"), Description = new LocalizedHtml("#Forms.Intro") });

        var german = JsonNode.Parse(await _forms.WriteAsync(projection, "de-DE"))!;
        var english = JsonNode.Parse(await _forms.WriteAsync(projection, "en-US"))!;

        Assert.Multiple(() =>
        {
            Assert.That(german["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("Ihre Angaben"));
            Assert.That(german["pages"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("<p>Hello</p>"), "an item's rich text falls back to English and is sanitised too");
            Assert.That(german["settings"]!["submitLabel"]!.GetValue<string>(), Is.EqualTo("Anmeldung senden"), "de-DE falls back to de");
            Assert.That(english["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("Your details"));
            Assert.That(english["settings"]!["submitLabel"]!.GetValue<string>(), Is.EqualTo("Send registration"));
            Assert.That(_umbraco.DictionaryReads, Is.EqualTo(2), "one read per answer for every item it names");
        });
    }

    [Test]
    public async Task AFormWhoseTextsNameNoItem_ReadsNoDictionary()
    {
        _ = await _forms.WriteAsync(Projection(page => page), "de-DE");

        Assert.That(_umbraco.DictionaryReads, Is.Zero);
    }

    [Test]
    public async Task AnItemWithoutAText_ShowsAsItIsWritten()
    {
        var shown = JsonNode.Parse(await _forms.WriteAsync(Projection(page => page with { Title = new LocalizedText("#Forms.Missing") }), "en-US"))!;

        Assert.That(shown["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("#Forms.Missing"));
    }

    [Test]
    public async Task PlainText_IsLeftForThePageToEscape_AndRichTextIsSanitised()
    {
        var projection = Projection(page => page with
        {
            Title = new LocalizedText("<b>Bold</b> & co"),
            Description = new LocalizedHtml("""<p onclick="steal()">Hi <img src="x.png"></p>"""),
        });

        var shown = JsonNode.Parse(await _forms.WriteAsync(projection, "en-US"))!;

        Assert.Multiple(() =>
        {
            Assert.That(shown["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("<b>Bold</b> & co"));
            Assert.That(shown["pages"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("<p>Hi </p>"));
        });
    }

    [Test]
    public async Task ALoneSurrogateOfAnItem_IsReplaced_SoTheFormWrites()
    {
        _umbraco.Item("Forms.Broken", new Dictionary<string, string> { ["en-US"] = "A" + (char)0xD800 + "B" });

        var shown = JsonNode.Parse(await _forms.WriteAsync(Projection(page => page with { Title = new LocalizedText("#Forms.Broken") }), "en-US"))!;

        Assert.That(shown["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("A" + (char)0xFFFD + "B"));
    }

    // The full form's projection with its first page changed.
    private static PublicForm Projection(Func<PublicPage, PublicPage> change)
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), TestFieldTypes.Registry);
        return projection with { Pages = [change(projection.Pages[0]), .. projection.Pages.Skip(1)] };
    }
}
