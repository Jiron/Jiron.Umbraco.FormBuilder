using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The general section (section 4.8, ADR-0043): its defaults, its scope and its validator. A time zone the server cannot
/// resolve as an IANA zone cannot be saved, and a zone is stored as the server spells it (ADR-0005, ADR-0030). A site's
/// required-marker style is never inherit, and its button labels are texts a form's own labels take (ADR-0095). The
/// consent field a new form starts with has texts a definition takes (ADR-0044). The site's theme is an alias a form's
/// own theme would take, not held to the themes the code registers, or none (ADR-0105).
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

    [TestCase("europe/berlin", "Europe/Berlin")]
    [TestCase("EUROPE/BERLIN", "Europe/Berlin")]
    [TestCase("asia/calcutta", "Asia/Calcutta")]
    [TestCase("america/buenos_aires", "America/Buenos_Aires")]
    [TestCase("asia/kolkata", "Asia/Kolkata")]
    public void ATimeZoneInAnotherLetterCase_CannotBeSaved_EvenOnceTheServerHasMetItsSpelling(string timeZone, string spelled)
    {
        // Once the exact spelling has been looked up, the runtime's cache, which ignores case, finds any spelling.
        Assert.That(Errors(new GeneralSettings { TimeZone = spelled }), Is.Empty);

        Assert.That(Errors(new GeneralSettings { TimeZone = timeZone }).Select(e => e.Code), Is.EqualTo(new[] { "timeZone" }));
    }

    // Browsers list these under their older names (Chrome's Intl.supportedValuesOf('timeZone') does), which Windows finds
    // through ICU and Linux through the links of its time zone database, also without the package of the older names.
    [TestCase("America/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [TestCase("Asia/Calcutta", "Asia/Kolkata")]
    [TestCase("Asia/Katmandu", "Asia/Kathmandu")]
    [TestCase("Asia/Saigon", "Asia/Ho_Chi_Minh")]
    [TestCase("Europe/Kiev", "Europe/Kyiv")]
    [TestCase("Atlantic/Faeroe", "Atlantic/Faroe")]
    [TestCase("America/Indianapolis", "America/Indiana/Indianapolis")]
    public void AZoneTheBrowserListsUnderItsOlderName_IsSavedAsGiven_AndRunsAsItsZone(string older, string zone)
    {
        var section = new GeneralSettings { TimeZone = older };
        var found = CalendarValues.FindTimeZone(older, new SystemTimeZoneLinks());
        var instant = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(Errors(section), Is.Empty);
            Assert.That(((ISettingsValidator<GeneralSettings>)Validator).Normalize(section).TimeZone, Is.EqualTo(older), "kept as it was given");
            Assert.That(found, Is.Not.Null);
            Assert.That(found!.GetUtcOffset(instant), Is.EqualTo(TimeZoneInfo.FindSystemTimeZoneById(zone).GetUtcOffset(instant)), "the zone's own rules");
            Assert.That(CalendarValues.Today("2026-07-01T23:30:00Z", found.Id), Is.EqualTo(CalendarValues.Today("2026-07-01T23:30:00Z", zone)), "today, as the readers pass the resolved zone on");
        });
    }

    [Test]
    public void TheMarker_IsOptional_AndTheLabels_ThePackagesWords_EachSetPerSite()
    {
        var section = new GeneralSettings();

        Assert.Multiple(() =>
        {
            Assert.That(section.RequiredMarker, Is.EqualTo(RequiredMarker.Optional));
            Assert.That(new[] { section.SubmitLabel, section.NextLabel, section.BackLabel }, Is.All.Null);
            Assert.That(SettingsSectionJson.SiteFieldNames<GeneralSettings>(), Is.SupersetOf(new[] { "requiredMarker", "submitLabel", "nextLabel", "backLabel" }), "a site sets each on its own");
        });
    }

    [TestCase(RequiredMarker.Optional)]
    [TestCase(RequiredMarker.Required)]
    [TestCase(RequiredMarker.None)]
    public void ASitesStyle_Passes(RequiredMarker marker) =>
        Assert.That(Errors(new GeneralSettings { RequiredMarker = marker }), Is.Empty);

    [TestCase(RequiredMarker.Inherit, Description = "a form's way to take the site's")]
    [TestCase((RequiredMarker)9, Description = "no style")]
    public void InheritOrNoStyle_CannotBeSaved(RequiredMarker marker) =>
        Assert.That(Errors(new GeneralSettings { RequiredMarker = marker }), Is.EqualTo(new[] { new SettingsError("general.requiredMarker", "requiredMarker", "Must be optional, required or none: inherit is how a form takes the site's style.") }));

    [Test]
    public void TheTheme_IsNoneByDefault_SoFormsTakeTheCodesDefault() =>
        Assert.That(new GeneralSettings().Theme, Is.Null);

    [TestCase("bootstrap5")]
    [TestCase("a-theme-the-code-lacks")]
    [TestCase("Not An Alias")]
    public void ATheme_ThatAFormsOwnThemeWouldTake_Passes(string theme) =>
        Assert.That(Errors(new GeneralSettings { Theme = theme }), Is.Empty);

    [Test]
    public void AThemeOfSixtyFourCharacters_Passes() =>
        Assert.That(Errors(new GeneralSettings { Theme = new string('a', 64) }), Is.Empty);

    [TestCase(65, "")]
    [TestCase(3, "\n")]
    [TestCase(3, "\t")]
    [TestCase(3, "\u0000")]
    public void AThemeTooLongOrOverLines_CannotBeSaved(int length, string tail)
    {
        var theme = new string('a', length) + tail;

        Assert.That(Errors(new GeneralSettings { Theme = theme }), Is.EqualTo(new[] { new SettingsError("general.theme", "theme", "Must be a theme's alias of at most 64 characters on one line.") }));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void ABlankTheme_IsStoredAsNone(string theme) =>
        Assert.That(((ISettingsValidator<GeneralSettings>)Validator).Normalize(new GeneralSettings { Theme = theme }).Theme, Is.Null);

    [Test]
    public void ATheme_IsStoredAsGiven() =>
        Assert.That(((ISettingsValidator<GeneralSettings>)Validator).Normalize(new GeneralSettings { Theme = "bootstrap5" }).Theme, Is.EqualTo("bootstrap5"));

    [Test]
    public void EachLabelWithoutTexts_IsStoredAsNone()
    {
        var empty = new Dictionary<string, string>();

        var normalized = ((ISettingsValidator<GeneralSettings>)Validator).Normalize(new GeneralSettings { SubmitLabel = empty, NextLabel = empty, BackLabel = empty });

        Assert.That(new[] { normalized.SubmitLabel, normalized.NextLabel, normalized.BackLabel }, Is.All.Null);
    }

    [Test]
    public void TheLabels_AreStoredByCulture_AndReadBack_AndOneWithoutTexts_IsThePackagesWord()
    {
        var section = new GeneralSettings { SubmitLabel = Texts("Send", "de", "Senden"), NextLabel = new Dictionary<string, string>(), BackLabel = Texts("#Forms.Back") };

        var normalized = ((ISettingsValidator<GeneralSettings>)Validator).Normalize(section);
        var json = SettingsJson.Global(normalized);
        var read = SettingsSectionJson.Read<GeneralSettings>(JsonNode.Parse(json)!.AsObject());

        Assert.Multiple(() =>
        {
            Assert.That(normalized.NextLabel, Is.Null, "a label without texts is the default");
            Assert.That(Errors(normalized), Is.Empty, "a label may name a dictionary item");
            Assert.That(JsonNode.Parse(json)!["submitLabel"]!.ToJsonString(), Is.EqualTo("""{"":"Send","de":"Senden"}"""));
            Assert.That(read.SubmitLabel, Is.EqualTo(section.SubmitLabel));
            Assert.That(read.BackLabel, Is.EqualTo(section.BackLabel));
            Assert.That(read.NextLabel, Is.Null);
        });
    }

    public static IEnumerable<TestCaseData> BadLabels()
    {
        yield return BadLabel("submitLabel", new Dictionary<string, string> { ["de"] = "Senden" }, "required", "no default label");
        yield return BadLabel("nextLabel", Texts("  "), "required", "a blank default label");
        yield return BadLabel("backLabel", Texts("Back" + (char)10 + "now"), "invalid_value", "a label on two lines");
        yield return BadLabel("submitLabel", Texts(new string('a', 501)), "limit_exceeded", "a label of 501 characters");
        yield return BadLabel("nextLabel", Texts("Next", "not a culture", "x"), "text", "a culture name that is none");
    }

    [TestCaseSource(nameof(BadLabels))]
    public void ALabel_AFormsOwnLabelWouldRefuse_CannotBeSaved(string field, object texts, string code)
    {
        var labels = (IReadOnlyDictionary<string, string>)texts;
        var section = field switch
        {
            "submitLabel" => new GeneralSettings { SubmitLabel = labels },
            "nextLabel" => new GeneralSettings { NextLabel = labels },
            _ => new GeneralSettings { BackLabel = labels },
        };

        Assert.That(Errors(section).Select(error => (error.Field, error.Code)), Is.EqualTo(new[] { ("general." + field, code) }));
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
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse("""{"timeZone":"UTC","requiredMarker":"optional","newForms":{"consent":{"label":{"":"I agree","de":"Ich stimme zu"},"text":{"":"<p>Yes.</p>"}},"notifyCreator":false},"history":{"draftDays":90,"draftCount":50}}""")), Is.True, json);
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

    private static TestCaseData BadLabel(string field, IReadOnlyDictionary<string, string> texts, string code, string why) =>
        new TestCaseData(field, texts, code).SetArgDisplayNames(why);

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
