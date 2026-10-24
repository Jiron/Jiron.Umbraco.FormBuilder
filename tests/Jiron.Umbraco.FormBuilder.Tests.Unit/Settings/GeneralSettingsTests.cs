using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The general section (section 4.8, ADR-0043): its defaults, its scope and its validator. A time zone the server cannot
/// resolve as an IANA zone cannot be saved, and a zone is stored as the server spells it (ADR-0005, ADR-0030).
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
