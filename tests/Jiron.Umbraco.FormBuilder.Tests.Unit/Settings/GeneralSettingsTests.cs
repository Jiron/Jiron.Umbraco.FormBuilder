using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The general section (section 4.8, ADR-0043): its defaults, its scope and its validator. A time zone the server cannot
/// resolve as an IANA zone cannot be saved, and a zone is stored as the server spells it (ADR-0005, ADR-0030). The
/// consent field a new form starts with has texts a definition takes (ADR-0044).
/// </summary>
[TestFixture]
public sealed class GeneralSettingsTests
{
    private static readonly GeneralSettingsValidator Validator = new();

    [Test]
    public void TheSection_IsGeneral_AtSchemaVersion1_AndSetPerSite()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Alias<GeneralSettings>(), Is.EqualTo(FormBuilderSettingsSections.General));
            Assert.That(SchemaVersion<GeneralSettings>(), Is.EqualTo(1));
            Assert.That(PerSite<GeneralSettings>(), Is.True);
        });
    }

    [Test]
    public void TheDefaultTimeZone_IsUtc_WhichTheServerResolves_AndWhichPasses()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new GeneralSettings().TimeZone, Is.EqualTo("UTC"));
            Assert.That(CalendarValues.FindTimeZone(GeneralSettings.DefaultTimeZone)?.Id, Is.EqualTo("UTC"));
            Assert.That(Errors(new GeneralSettings()), Is.Empty);
        });
    }

    [TestCase("Europe/Berlin")]
    [TestCase("America/New_York")]
    [TestCase("Pacific/Auckland")]
    [TestCase("Etc/GMT-14")]
    public void AnIanaTimeZone_Passes(string timeZone)
    {
        Assert.That(Errors(new GeneralSettings { TimeZone = timeZone }), Is.Empty);
    }

    [TestCase("Mars/Olympus_Mons", Description = "unknown")]
    [TestCase("W. Europe Standard Time", Description = "a Windows name, which no browser takes")]
    [TestCase("+01:00", Description = "an offset, which the server does not take")]
    [TestCase("", Description = "empty")]
    [TestCase(" Europe/Berlin", Description = "not trimmed")]
    [TestCase("utc", Description = "another letter case, which Windows finds through its own names")]
    public void ATimeZoneTheServerCannotResolve_CannotBeSaved(string timeZone)
    {
        var section = new GeneralSettings { TimeZone = timeZone };

        Assert.Multiple(() =>
        {
            Assert.That(((ISettingsValidator<GeneralSettings>)Validator).Normalize(section), Is.SameAs(section), "a zone stays as it was given");
            Assert.That(Errors(section), Is.EqualTo(new[] { new SettingsError("general.timeZone", "timeZone", "Must be an IANA time zone this server knows, in its exact spelling, such as Europe/Berlin.") }));
        });
    }

    [Test]
    public void ANullTimeZone_CannotBeSaved()
    {
        Assert.That(Errors(new GeneralSettings { TimeZone = null! }).Select(e => e.Code), Is.EqualTo(new[] { "timeZone" }));
    }

    [TestCase("europe/berlin")]
    [TestCase("EUROPE/BERLIN")]
    public void ATimeZoneInAnotherLetterCase_CannotBeSaved_EvenOnceTheServerHasMetItsSpelling(string timeZone)
    {
        // Once the exact spelling has been looked up, the runtime's cache, which ignores case, finds any spelling.
        Assume.That(CalendarValues.FindTimeZone("Europe/Berlin"), Is.Not.Null);

        Assert.That(Errors(new GeneralSettings { TimeZone = timeZone }).Select(e => e.Code), Is.EqualTo(new[] { "timeZone" }));
    }

    [Test]
    public void ANewForm_StartsWithoutAConsentField_UnlessTheSettingsGiveOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new GeneralSettings().NewForms.Consent, Is.Null);
            Assert.That(Errors(new GeneralSettings { NewForms = new NewFormDefaults { Consent = Consent(Texts("I agree", "de", "Ich stimme zu"), Texts("<p>I agree to the <a href=\"/terms\">terms</a>.</p>" + (char)10 + "<p>Thank you.</p>")) } }), Is.Empty, "rich text may span lines");
        });
    }

    [Test]
    public void TheNewFormDefaults_AreSetForEverySiteAtOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SettingsSectionJson.FieldNames<GeneralSettings>(), Does.Contain("newForms"));
            Assert.That(SettingsSectionJson.SiteFieldNames<GeneralSettings>(), Does.Not.Contain("newForms"), "a new form belongs to no site");
        });
    }

    [Test]
    public void TheConsentTexts_AreStoredByCulture_AndReadBack()
    {
        var section = new GeneralSettings { NewForms = new NewFormDefaults { Consent = Consent(Texts("I agree", "de", "Ich stimme zu"), Texts("<p>Yes.</p>")) } };

        var json = SettingsJson.Global(section);
        var read = SettingsSectionJson.Read<GeneralSettings>(JsonNode.Parse(json)!.AsObject());

        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse("""{"timeZone":"UTC","newForms":{"consent":{"label":{"":"I agree","de":"Ich stimme zu"},"text":{"":"<p>Yes.</p>"}}},"history":{"draftDays":90,"draftCount":50}}""")), Is.True, json);
            Assert.That(read.NewForms.Consent!.Label, Is.EqualTo(section.NewForms.Consent!.Label));
            Assert.That(read.NewForms.Consent.Text, Is.EqualTo(section.NewForms.Consent.Text));
        });
    }

    public static IEnumerable<TestCaseData> BadConsents()
    {
        var line = Texts("I agree");
        var html = Texts("<p>Yes.</p>");
        yield return Bad(Consent(Texts("I agree").Where(t => t.Key != string.Empty).ToDictionary(), html), "label", "required", "no default label");
        yield return Bad(Consent(Texts("  "), html), "label", "required", "a blank default label");
        yield return Bad(Consent(null!, html), "label", "required", "no labels");
        yield return Bad(Consent(line, Texts(string.Empty)), "text", "required", "an empty default text");
        yield return Bad(Consent(Texts("I" + (char)10 + "agree"), html), "label", "invalid_value", "a label on two lines");
        yield return Bad(Consent(Texts(new string('a', 501)), html), "label", "limit_exceeded", "a label of 501 characters");
        yield return Bad(Consent(line, Texts(new string('a', 50001))), "text", "limit_exceeded", "a text of 50,001 characters");
        yield return Bad(Consent(Texts("I agree", "not a culture", "x"), html), "label", "text", "a culture name that is none");
        yield return Bad(Consent(line, Texts("<p>Yes.</p>", "de", "<p>Ja.</p>", "DE", "<p>Ja!</p>")), "text", "text", "a culture twice");
        yield return Bad(Consent(Texts("I agree" + (char)0xD800), html), "label", "invalid_value", "a lone surrogate");
    }

    [TestCaseSource(nameof(BadConsents))]
    public void AConsentDefault_ATextOfADefinitionWouldRefuse_CannotBeSaved(object consent, string field, string code)
    {
        var errors = Errors(new GeneralSettings { NewForms = new NewFormDefaults { Consent = (NewFormConsent)consent } });

        Assert.That(errors.Select(error => (error.Field, error.Code)), Is.EqualTo(new[] { ("general.newForms.consent." + field, code) }));
    }

    [Test]
    public void ARichConsentText_MaySpanLines()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DefinitionSaveChecks.CheckText(new LocalizedHtml("<p>Yes.</p>" + (char)10 + "<p>Really.</p>"), html: true), Is.Empty);
            Assert.That(DefinitionSaveChecks.CheckText(new LocalizedText("Yes." + (char)10 + "Really."), html: false).Select(problem => problem.Code), Is.EqualTo(new[] { "invalid_value" }));
            Assert.That(() => DefinitionSaveChecks.CheckText(null!, html: false), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void TheHistory_KeepsNinetyDaysAndFiftyDraftSaves_ForEverySiteAtOnce()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new GeneralSettings().History, Is.EqualTo(new HistorySettings { DraftDays = 90, DraftCount = 50 }));
            Assert.That(SettingsSectionJson.FieldNames<GeneralSettings>(), Does.Contain("history"));
            Assert.That(SettingsSectionJson.SiteFieldNames<GeneralSettings>(), Does.Not.Contain("history"), "the cleanup runs for every form at once");
            Assert.That(Errors(new GeneralSettings { History = null! }), Is.Empty, "a section without it keeps the defaults");
        });
    }

    [TestCase(1, 0)]
    [TestCase(90, 50)]
    [TestCase(3650, 1000)]
    public void AHistoryWithinItsRanges_Passes(int days, int count) =>
        Assert.That(Errors(new GeneralSettings { History = new HistorySettings { DraftDays = days, DraftCount = count } }), Is.Empty);

    [TestCase(0, 50, "draftDays", "Must be a whole number from 1 to 3650.")]
    [TestCase(3651, 50, "draftDays", "Must be a whole number from 1 to 3650.")]
    [TestCase(90, -1, "draftCount", "Must be a whole number from 0 to 1000.")]
    [TestCase(90, 1001, "draftCount", "Must be a whole number from 0 to 1000.")]
    public void AHistoryOutsideItsRanges_CannotBeSaved(int days, int count, string field, string message)
    {
        var errors = Errors(new GeneralSettings { History = new HistorySettings { DraftDays = days, DraftCount = count } });

        Assert.That(errors.Select(error => (error.Field, error.Code, error.Message)), Is.EqualTo(new[] { ("general.history." + field, "range", message) }));
    }

    private static NewFormConsent Consent(IReadOnlyDictionary<string, string> label, IReadOnlyDictionary<string, string> text) => new() { Label = label, Text = text };

    // A default text and pairs of a culture and its text.
    private static Dictionary<string, string> Texts(string fallback, params string[] cultures)
    {
        var texts = new Dictionary<string, string> { [string.Empty] = fallback };
        for (var i = 0; i < cultures.Length; i += 2)
        {
            texts[cultures[i]] = cultures[i + 1];
        }

        return texts;
    }

    private static TestCaseData Bad(NewFormConsent consent, string field, string code, string why) =>
        new TestCaseData(consent, field, code).SetArgDisplayNames(why);

    private static IReadOnlyList<SettingsError> Errors(GeneralSettings section)
    {
        var errors = new SettingsErrors(GeneralSettings.Alias);
        Validator.Validate(section, errors);
        return errors.Errors;
    }

    private static string Alias<T>()
        where T : ISettingsSection => T.Alias;

    private static int SchemaVersion<T>()
        where T : ISettingsSection => T.SchemaVersion;

    private static bool PerSite<T>()
        where T : ISettingsSection => T.PerSite;
}
