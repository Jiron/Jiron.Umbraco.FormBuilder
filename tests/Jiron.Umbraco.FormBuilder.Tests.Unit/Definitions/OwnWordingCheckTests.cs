using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// A form's and a site's own wording of the package's visitor texts (ROADMAP 14.2, ADR-0230) is held to the same rules
/// where it is saved: each a text the package has, each culture a culture's name or <c>""</c> for every language, each
/// wording one line of at most 500 characters that names only its text's values. A form's wording is written and read
/// back as it was given, and a site's is stored by text and culture.
/// </summary>
public sealed class OwnWordingCheckTests
{
    private static readonly FormDefinitionSerializer Serializer = DefinitionFixtures.Serializer();

    [Test]
    public void AFormsOwnWording_IsTaken_AndWrittenAndReadBack()
    {
        var definition = With(Texts(("message.email", "", "Your email, please"), ("message.email", "de", "Ihre E-Mail, bitte"), ("form.chooseFile", "fr-CH", "#Site.ChooseFile")));

        var read = Serializer.Read(FormDefinitionSerializer.Write(definition));

        Assert.Multiple(() =>
        {
            Assert.That(Serializer.CheckSave(definition), Is.Empty);
            Assert.That(read.Problems, Is.Empty);
            Assert.That(read.Definition!.Settings.Submission.Texts!["message.email"]["de"], Is.EqualTo("Ihre E-Mail, bitte"));
            Assert.That(read.Definition.Settings.Submission.Texts["form.chooseFile"].Keys, Is.EqualTo(new[] { "fr-CH" }), "no default needed");
        });
    }

    [TestCase("message.nothing", "", "x", DefinitionErrors.InvalidValue, "no visitor text")]
    [TestCase("message.email", "not a culture", "x", DefinitionErrors.InvalidValue, "not a culture name")]
    [TestCase("message.email", "", " ", DefinitionErrors.InvalidValue, "white space")]
    [TestCase("message.minLength.one", "de", "Mindestens {max}", DefinitionErrors.InvalidValue, "only {min}")]
    [TestCase("form.step", "", "Step {0} of {1}}", DefinitionErrors.InvalidValue, "no other brace")]
    [TestCase("message.email", "", "Two\nlines", DefinitionErrors.InvalidValue, "one line")]
    public void AFormsWording_ItsTextCannotTake_IsRefusedAtItsPlace(string key, string culture, string wording, string code, string why)
    {
        var problems = Serializer.CheckSave(With(Texts((key, culture, wording))));

        Assert.Multiple(() =>
        {
            Assert.That(problems.Select(problem => problem.Code), Is.EqualTo(new[] { code }));
            Assert.That(problems[0].Path, Does.StartWith("$.settings.submission.texts").And.Contain(key));
            Assert.That(problems[0].Message, Does.Contain(why));
        });
    }

    [Test]
    public void AFormsWording_TooLong_OrAReworkedTextWithoutWording_IsRefused()
    {
        var problems = Serializer.CheckSave(With(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["message.email"] = new Dictionary<string, string> { [""] = new('a', 501) },
            ["message.phone"] = new Dictionary<string, string>(),
        }));

        Assert.That(problems.Select(problem => problem.Code), Is.EquivalentTo(new[] { DefinitionErrors.LimitExceeded, DefinitionErrors.InvalidValue }));
    }

    [Test]
    public void ASitesOwnWording_IsHeldToTheSameRules_AndStoredByTextAndCulture()
    {
        var validator = new GeneralSettingsValidator();
        var good = new GeneralSettings { Texts = Texts(("form.chooseFile", "de", "Datei hochladen"), ("answer.yes", "", "Sure")) };
        var bad = new GeneralSettings { Texts = Texts(("message.nothing", "", "x"), ("message.email", "xx yy", "x"), ("form.step", "", "Step {2}")) };

        var goodErrors = new SettingsErrors(GeneralSettings.Alias);
        validator.Validate(good, goodErrors);
        var badErrors = new SettingsErrors(GeneralSettings.Alias);
        validator.Validate(bad, badErrors);
        var stored = System.Text.Json.Nodes.JsonNode.Parse(SettingsJson.Global(good))!;
        var read = SettingsSectionJson.Read<GeneralSettings>(stored.AsObject());

        Assert.Multiple(() =>
        {
            Assert.That(goodErrors.Errors, Is.Empty);
            Assert.That(
                badErrors.Errors.Select(error => (error.Field, error.Code)),
                Is.EquivalentTo(new[] { ("general.texts.message.nothing", "visitorText"), ("general.texts.message.email", "visitorText"), ("general.texts.form.step", "visitorText") }));
            Assert.That(stored["texts"]!["form.chooseFile"]!["de"]!.GetValue<string>(), Is.EqualTo("Datei hochladen"));
            Assert.That(read.Texts!["answer.yes"][""], Is.EqualTo("Sure"));
            Assert.That(SettingsSectionJson.SiteFieldNames<GeneralSettings>(), Does.Contain("texts"), "set per site");
        });
    }

    private static FormDefinition With(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> texts)
    {
        var form = MergeTagTestSite.Form();
        return form with { Settings = form.Settings with { Submission = form.Settings.Submission with { Texts = texts } } };
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> Texts(params (string Key, string Culture, string Wording)[] texts) =>
        texts.GroupBy(text => text.Key).ToDictionary(
            group => group.Key,
            group => (IReadOnlyDictionary<string, string>)group.ToDictionary(text => text.Culture, text => text.Wording),
            StringComparer.Ordinal);
}
