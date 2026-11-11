using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// A site's and a form's own wording of the package's visitor texts (ROADMAP 14.2, ADR-0230): the keys are the tables'
/// texts, each in one group with the values its English text names; a page takes the form's wording, then the site's,
/// each by the culture, its language and every language; a wording may name only its text's values and no other brace;
/// and both runtimes' tables take the wording in place of their own words.
/// </summary>
[TestFixture]
public sealed class VisitorWordingTests
{
    private static readonly string[] Buttons = ["back", "next", "send"];

    [Test]
    public void TheKeys_AreTheTablesTexts_ButTheButtonsAndTheBackofficeNote()
    {
        var english = RenderingWords.Of("en").ByName();
        var formKeys = VisitorTextKeys.All.Where(key => key.Key.StartsWith(VisitorTextKeys.FormPrefix, StringComparison.Ordinal)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(
                formKeys.Select(key => key.Key[VisitorTextKeys.FormPrefix.Length..]),
                Is.EquivalentTo(english.Keys.Except(Buttons).Except(["pendingHeading", "pendingText"])),
                "every word a rendered form brings but the buttons and the backoffice note");
            foreach (var key in formKeys)
            {
                Assert.That(key.Values, Is.EqualTo(VisitorTextKeys.ValuesOf(english[key.Key[VisitorTextKeys.FormPrefix.Length..]])), key.Key);
            }

            Assert.That(
                VisitorTextKeys.All.Where(key => key.Key.StartsWith(VisitorTextKeys.MessagePrefix, StringComparison.Ordinal)).Select(key => key.Key),
                Is.EqualTo(VisitorTextKeys.MessageTexts(VisitorMessageWords.Of("en")).Select(text => VisitorTextKeys.MessagePrefix + text.Name)),
                "every message, a counting one's forms and a category's name each");
            Assert.That(VisitorTextKeys.Find("answer.yes")?.Group, Is.EqualTo("answers"));
            Assert.That(VisitorTextKeys.Find("message.minLength.one")?.Values, Is.EqualTo(new[] { "{min}" }));
            Assert.That(VisitorTextKeys.Find("message.captchaCategories.marketing"), Is.Not.Null);
            Assert.That(VisitorTextKeys.All.Select(key => key.Group).Distinct(), Is.SubsetOf(VisitorTextKeys.Groups));
            Assert.That(VisitorTextKeys.All.Select(key => key.Key), Is.Unique);
            Assert.That(VisitorTextKeys.Find("form.send"), Is.Null, "the buttons keep their own fields");
            Assert.That(VisitorTextKeys.Find("message.nothing"), Is.Null);
        });
    }

    [TestCase("message.minLength.other", "Type {min} letters or more", null)]
    [TestCase("form.step", "{1} pages, this is page {0}", null)]
    [TestCase("form.chooseFile", "#Site.ChooseFile", null)]
    [TestCase("message.minLength.other", "Type {max} letters", "only {min}")]
    [TestCase("message.email", "Your {address}", "no value in braces")]
    [TestCase("form.step", "Step {0} of {1} {", "only {0}, {1}")]
    [TestCase("form.step", "Step } of", "only {0}, {1}")]
    [TestCase("message.email", "  ", "more than white space")]
    [TestCase("message.email", "Two\nlines", "one line")]
    public void AWording_NamesOnlyItsTextsValues_OnOneLine(string key, string wording, string? problem)
    {
        var found = VisitorTextKeys.Problem(VisitorTextKeys.Find(key)!, wording);

        Assert.That(found, problem is null ? Is.Null : Does.Contain(problem));
    }

    [Test]
    public void AWording_HasAtMostItsLength()
    {
        var key = VisitorTextKeys.Find("message.email")!;

        Assert.Multiple(() =>
        {
            Assert.That(VisitorTextKeys.Problem(key, new string('a', VisitorTextKeys.MaxLength)), Is.Null);
            Assert.That(VisitorTextKeys.Problem(key, new string('a', VisitorTextKeys.MaxLength + 1)), Does.Contain("at most"));
        });
    }

    [Test]
    public void AWording_IsTheCulturesOwn_ThenItsLanguages_ThenEveryLanguages()
    {
        var cultures = new Dictionary<string, string> { [""] = "Any", ["de"] = "German", ["de-CH"] = "Swiss", ["fr-FR"] = " " };

        Assert.Multiple(() =>
        {
            Assert.That(VisitorWording.Resolve(cultures, "de-CH"), Is.EqualTo("Swiss"));
            Assert.That(VisitorWording.Resolve(cultures, "DE-at"), Is.EqualTo("German"), "in any letter case");
            Assert.That(VisitorWording.Resolve(cultures, "fr-FR"), Is.EqualTo("Any"), "a wording of white space is none");
            Assert.That(VisitorWording.Resolve(new Dictionary<string, string> { ["de"] = "German" }, "fr-FR"), Is.Null, "no default needed");
        });
    }

    [Test]
    public void APage_TakesTheFormsWording_ThenTheSites_KeepingOnlyTheTextsThePackageHas()
    {
        var form = Texts(("message.email", "", "The form's"), ("form.chooseFile", "de", "Datei des Formulars"), ("message.nothing", "", "x"));
        var site = Texts(("message.email", "", "The site's"), ("form.chooseFile", "", "The site's file"), ("answer.yes", "fr", "Oui, bien"));

        Assert.Multiple(() =>
        {
            Assert.That(VisitorWording.ForCulture(form, site, "de-DE"), Is.EquivalentTo(new Dictionary<string, string>
            {
                ["message.email"] = "The form's",
                ["form.chooseFile"] = "Datei des Formulars",
            }));
            Assert.That(VisitorWording.ForCulture(form, site, "fr-FR"), Is.EquivalentTo(new Dictionary<string, string>
            {
                ["message.email"] = "The form's",
                ["form.chooseFile"] = "The site's file",
                ["answer.yes"] = "Oui, bien",
            }), "the site's where the form has none in the culture");
            Assert.That(VisitorWording.ForCulture(null, null, "en"), Is.SameAs(VisitorWording.None));
        });
    }

    [Test]
    public void AWordingThatBrokeSinceItWasSaved_LeavesThePackagesText()
    {
        var shown = VisitorWording.Shown(new Dictionary<string, string>
        {
            ["message.minLength.one"] = "Type {min} letter",
            ["form.step"] = "Page {0} of {2}",
            ["message.nothing"] = "x",
        });

        Assert.That(shown, Is.EquivalentTo(new Dictionary<string, string> { ["message.minLength.one"] = "Type {min} letter" }));
    }

    [Test]
    public void TheMessages_TakeTheWording_AWordACountingFormAndACategory()
    {
        var english = VisitorMessageWords.Of("en");
        var texts = new Dictionary<string, string>
        {
            ["message.email"] = "Your email, please",
            ["message.minLength.other"] = "At least {min} letters",
            ["message.captchaCategories.marketing"] = "advertising",
            ["form.chooseFile"] = "Pick a file",
        };

        var reworded = VisitorWording.Apply(english, texts);

        Assert.Multiple(() =>
        {
            Assert.That(reworded.Email, Is.EqualTo("Your email, please"));
            Assert.That(reworded.MinLength, Is.EqualTo(new PluralWords("Enter at least {min} character", "At least {min} letters")));
            Assert.That(reworded.CaptchaCategories["marketing"], Is.EqualTo("advertising"));
            Assert.That(reworded.Phone, Is.EqualTo(english.Phone));
            Assert.That(english.Email, Is.EqualTo("Enter an email address"), "the package's table stays as it is");
            Assert.That(VisitorWording.Apply(english, new Dictionary<string, string> { ["form.chooseFile"] = "x" }), Is.SameAs(english));
            Assert.That(VisitorWording.Messages(texts).Keys, Is.EquivalentTo(new[] { "email", "minLength.other", "captchaCategories.marketing" }));
            Assert.That(VisitorMessages.SummaryHeading(1, "en", new Dictionary<string, string> { ["message.summaryHeading.one"] = "Fix this" }), Is.EqualTo("Fix this"));
        });
    }

    [Test]
    public void TheRenderedFormsWords_TakeTheWording()
    {
        var german = RenderingWords.Of("de");

        var reworded = german.With(new Dictionary<string, string> { ["form.chooseFile"] = "Datei hochladen", ["message.email"] = "x" });

        Assert.Multiple(() =>
        {
            Assert.That(reworded.ChooseFile, Is.EqualTo("Datei hochladen"));
            Assert.That(reworded.ChooseFiles, Is.EqualTo(german.ChooseFiles));
            Assert.That(german.ChooseFile, Is.Not.EqualTo("Datei hochladen"), "the package's words stay as they are");
            Assert.That(german.With(new Dictionary<string, string>()), Is.SameAs(german));
            Assert.That(german.With(new Dictionary<string, string> { ["message.email"] = "x" }), Is.SameAs(german));
        });
    }

    [Test]
    public void YesAndNo_AreThePagesOwn_ElseThePackagesInTheCulture()
    {
        var texts = new Dictionary<string, string> { ["answer.yes"] = "Gern" };

        Assert.Multiple(() =>
        {
            Assert.That((VisitorWording.Yes("de-DE", texts), VisitorWording.No("de-DE", texts)), Is.EqualTo(("Gern", "Nein")));
            Assert.That((VisitorWording.Yes("fr", null), VisitorWording.No("fr", null)), Is.EqualTo(("Oui", "Non")));
        });
    }

    [Test]
    public void TheEditorsList_GivesEveryTextItsPackageWords_InTheLanguage()
    {
        var german = VisitorTextsController.PackageWords("de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(german.Keys, Is.SupersetOf(VisitorTextKeys.All.Select(key => key.Key)));
            Assert.That(VisitorTextKeys.All.Where(key => string.IsNullOrWhiteSpace(german.GetValueOrDefault(key.Key))).Select(key => key.Key), Is.Empty);
            Assert.That(german["message.email"], Is.EqualTo("Geben Sie eine E-Mail-Adresse ein"));
            Assert.That(german["answer.no"], Is.EqualTo("Nein"));
            Assert.That(VisitorTextsController.PackageWords("it")["form.next"], Is.EqualTo("Avanti"));
        });
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Texts(params (string Key, string Culture, string Wording)[] texts) =>
        texts.GroupBy(text => text.Key).ToDictionary(
            group => group.Key,
            group => (IReadOnlyDictionary<string, string>)group.ToDictionary(text => text.Culture, text => text.Wording),
            StringComparer.Ordinal);
}
