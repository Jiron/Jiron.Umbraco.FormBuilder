using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// <c>dictionary.&lt;key&gt;</c> (ADR-0014, ADR-0067): an item of Umbraco's dictionary in the culture, with its fallback
/// through the culture's more general ones, the languages Umbraco falls back to, and the default language.
/// </summary>
public sealed class DictionaryMergeTagSourceTests
{
    /// <summary>
    /// English by default; Swiss German falling back to German; Austrian German falling back to Swiss German, which a
    /// test turns into a circle; French without a region; Italian without texts.
    /// </summary>
    private static TestUmbraco Dictionary(bool circle = false)
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Language("de-DE");
        umbraco.Language("de-CH", fallback: circle ? "de-AT" : "de-DE");
        umbraco.Language("de-AT", fallback: "de-CH");
        umbraco.Language("fr");
        umbraco.Language("it-IT");
        umbraco.Item("Forms.Thanks", new Dictionary<string, string> { ["en-GB"] = "Thank you", ["de-DE"] = "Danke", ["de-CH"] = "Merci vielmal", ["fr"] = "Merci" });
        umbraco.Item("Forms.Bye", new Dictionary<string, string> { ["en-GB"] = "Bye", ["de-DE"] = "Tschuess", ["de-CH"] = string.Empty });
        umbraco.Item("Forms.Italian", new Dictionary<string, string> { ["it-IT"] = "Ciao" });
        return umbraco;
    }

    [TestCase("de-CH", "Merci vielmal", "Tschuess")]
    [TestCase("de-AT", "Merci vielmal", "Tschuess")]
    [TestCase("de-DE", "Danke", "Tschuess")]
    [TestCase("fr-CA", "Merci", "Bye")]
    [TestCase("it-IT", "Thank you", "Bye")]
    [TestCase("", "Thank you", "Bye")]
    public async Task AnItem_IsItsTextInTheCulture_OrInTheLanguagesItFallsBackTo(string culture, string thanks, string bye)
    {
        var site = Create(umbraco: Dictionary());

        var rendered = await site.RenderAsync("{{ dictionary.Forms.Thanks }}|{{ dictionary.Forms.Bye }}", Scope(culture: culture));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(thanks + "|" + bye), "an empty translation counts as none");
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task FallbacksInACircle_EndAtTheDefaultLanguage()
    {
        var site = Create(umbraco: Dictionary(circle: true));

        var rendered = await site.RenderAsync("{{ dictionary.Forms.Bye }}", Scope(culture: "de-AT"));

        Assert.That(rendered.Text, Is.EqualTo("Bye"));
    }

    [Test]
    public async Task AnItemWithoutATextInAnyLanguageTried_IsEmpty_AndAMissingItem_IsAnUnknownPath()
    {
        var site = Create(umbraco: Dictionary());

        var untranslated = await site.RenderAsync("[{{ dictionary.Forms.Italian }}]", Scope(culture: "de-CH"));
        var missing = await site.RenderAsync("[{{ dictionary.Forms.Missing }}]", Scope());

        Assert.Multiple(() =>
        {
            Assert.That(untranslated.Text, Is.EqualTo("[]"));
            Assert.That(untranslated.Problems, Is.Empty);
            Assert.That(missing.Text, Is.EqualTo("[]"));
            Assert.That(missing.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 4) }));
        });
    }

    [Test]
    public async Task AnItemsText_IsEscapedByThePlacesContext_AndTheConsentTextTakesIt()
    {
        var umbraco = Dictionary();
        umbraco.Item("Forms.Consent", new Dictionary<string, string> { ["en-GB"] = "I agree to the <terms>" });
        var site = Create(umbraco: umbraco);

        var rendered = await site.RenderAsync("<p>{{ dictionary.Forms.Consent }}</p>", Scope(), MergeTagPlace.ConsentText, MergeTagContext.Html);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("<p>I agree to the &lt;terms&gt;</p>"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public void TheCheck_KnowsAnyKey_ButNotTheSourceAlone()
    {
        var site = Create(umbraco: Dictionary());

        Assert.Multiple(() =>
        {
            Assert.That(site.Service.Check("{{ dictionary.Any.Key.At.All }}", MergeTagPlace.VisitorText, MergeTagContext.Text, Form()), Is.Empty);
            Assert.That(site.Service.Check("{{ dictionary }}", MergeTagPlace.VisitorText, MergeTagContext.Text, Form()), Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
        });
    }
}
