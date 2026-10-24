using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// Validation errors and refusals of settings saves (ADR-0008): errors are keyed <c>section.field</c> with a stable code,
/// and the exceptions carry what the API answers with.
/// </summary>
[TestFixture]
public sealed class SettingsValidationTests
{
    [Test]
    public void AnError_IsKeyedBySectionAndField()
    {
        var errors = new SettingsErrors("sample");
        errors.Add("limits.max", "range", "Too high.");

        Assert.That(errors.Errors, Is.EqualTo(new[] { new SettingsError("sample.limits.max", "range", "Too high.") }));
    }

    [Test]
    public void AValidator_KeepsTheValuesAsTheyAre_UnlessItNormalizes()
    {
        var section = new SampleSharedSettings { Enabled = false };

        Assert.Multiple(() =>
        {
            Assert.That(((ISettingsValidator<SampleSharedSettings>)new NoChecks()).Normalize(section), Is.SameAs(section));
            Assert.That(new SampleSettingsValidator().Normalize(new SampleSettings { Name = "  Spaced " }).Name, Is.EqualTo("Spaced"));
        });
    }


    [Test]
    public void TheValidationRefusal_ListsItsErrors()
    {
        var error = new FormBuilderSettingsValidationException([new SettingsError("general.timeZone", "timeZone", "Bad."), new SettingsError("general.x", "range", "Bad.")]);

        Assert.Multiple(() =>
        {
            Assert.That(error.Errors, Has.Count.EqualTo(2));
            Assert.That(error.Message, Is.EqualTo("The settings have 2 invalid values: general.timeZone, general.x."));
        });
    }

    [Test]
    public void TheLockRefusal_NamesTheFieldAndTheKey()
    {
        var error = new FormBuilderSettingLockedException("general", "JironFormBuilder:LockedSections:0");

        Assert.Multiple(() =>
        {
            Assert.That(error.Field, Is.EqualTo("general"));
            Assert.That(error.ConfigurationKey, Is.EqualTo("JironFormBuilder:LockedSections:0"));
            Assert.That(error.Message, Is.EqualTo("general is locked by the host configuration (JironFormBuilder:LockedSections:0)."));
        });
    }

    private sealed class NoChecks : ISettingsValidator<SampleSharedSettings>
    {
        public void Validate(SampleSharedSettings section, SettingsErrors errors)
        {
        }
    }
}
