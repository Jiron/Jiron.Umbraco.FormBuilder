using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The host's rules on backoffice values (ADR-0008, ADR-0043): locks win where a value is used, without touching what is
/// stored, and a save that changes a locked or refused value is refused, naming the field and the key.
/// </summary>
[TestFixture]
public sealed class SettingsHostRulesTests
{
    private static readonly IReadOnlySet<string> Everything = SettingsSectionJson.FieldNames<SampleSettings>();

    [Test]
    public void WithoutRules_TheValuesAreTheStoredOnes_AndNothingIsLocked()
    {
        var stored = new SampleSettings { Name = "Mine" };

        var (value, locks) = SettingsHostRules.Apply(stored, new FormBuilderOptions(), []);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.SameAs(stored));
            Assert.That(locks, Is.Empty);
        });
    }

    [Test]
    public void ALockedSection_IsLockedWhole_NamingItsIndex_InAnyLetterCase()
    {
        var host = new FormBuilderOptions();
        host.LockedSections.Add("privacy");
        host.LockedSections.Add("SAMPLE");
        var stored = new SampleSettings { Name = "Mine" };

        var (value, locks) = SettingsHostRules.Apply(stored, host, []);

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.SameAs(stored), "a locked section shows its stored values");
            Assert.That(locks, Is.EqualTo(new Dictionary<string, SettingsLock> { ["*"] = new(SettingsLockKind.Locked, "JironFormBuilder:LockedSections:1") }));
            Assert.That(SettingsHostRules.Apply(new GeneralSettings(), host, []).Locks, Is.Empty, "another section");
        });
    }

    [Test]
    public void ALock_OverridesTheBackofficeValue_WhereItIsUsed()
    {
        var stored = new SampleSettings { Name = "Mine" };

        var (value, locks) = SettingsHostRules.Apply(stored, new FormBuilderOptions(), [new SampleHostRule { LockedName = "Host" }]);

        Assert.Multiple(() =>
        {
            Assert.That(value.Name, Is.EqualTo("Host"));
            Assert.That(stored.Name, Is.EqualTo("Mine"), "what is stored stays");
            Assert.That(locks, Is.EqualTo(new Dictionary<string, SettingsLock> { ["name"] = new(SettingsLockKind.Locked, SampleHostRule.NameKey) }));
        });
    }

    [TestCase(true, Description = "a refused value")]
    [TestCase(false, Description = "an allowed value")]
    public void ARestriction_ReplacesARefusedValue_WithTheNearestAllowed_AndKeepsAnAllowedOne(bool loud)
    {
        var stored = loud ? SampleMode.LoudAndClear : SampleMode.Quiet;

        var (value, locks) = SettingsHostRules.Apply(new SampleSettings { Mode = stored }, new FormBuilderOptions(), [new SampleHostRule { QuietOnly = true }]);

        Assert.Multiple(() =>
        {
            Assert.That(value.Mode, Is.EqualTo(SampleMode.Quiet));
            Assert.That(locks["mode"], Is.EqualTo(new SettingsLock(SettingsLockKind.Restricted, SampleHostRule.ModeKey)), "named whether or not it changed the value");
        });
    }

    [TestCase(12, 8)]
    [TestCase(8, 8)]
    [TestCase(3, 3)]
    public void AClamp_LowersAValueAboveTheHostsLimit(int stored, int effective)
    {
        var (value, locks) = SettingsHostRules.Apply(new SampleSettings { Limits = new SampleLimits { Max = stored } }, new FormBuilderOptions(), [new SampleHostRule { MaxLimit = 8 }]);

        Assert.Multiple(() =>
        {
            Assert.That(value.Limits.Max, Is.EqualTo(effective));
            Assert.That(locks["limits.max"], Is.EqualTo(new SettingsLock(SettingsLockKind.Clamped, SampleHostRule.MaxKey)));
        });
    }

    [Test]
    public void Rules_ApplyInTheOrderTheyAreRegistered_TheLaterLockOnAPathWinning()
    {
        var (value, locks) = SettingsHostRules.Apply(new SampleSettings(), new FormBuilderOptions(), [new SampleHostRule { LockedName = "First" }, new SampleHostRule { LockedName = "Second" }, new RestrictedNameRule()]);

        Assert.Multiple(() =>
        {
            Assert.That(value.Name, Is.EqualTo("Second"));
            Assert.That(locks, Is.EqualTo(new Dictionary<string, SettingsLock> { ["name"] = new(SettingsLockKind.Restricted, RestrictedNameRule.Key) }));
        });
    }

    [Test]
    public void ASaveOfALockedSection_IsRefused_NamingTheSectionAndTheKey()
    {
        var host = new FormBuilderOptions();
        host.LockedSections.Add("sample");

        Assert.That(
            () => SettingsHostRules.CheckSave(new SampleSettings(), Everything, host, [], new SettingsErrors(SampleSettings.Alias)),
            Throws.InstanceOf<FormBuilderSettingLockedException>()
                .With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("sample")
                .And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo("JironFormBuilder:LockedSections:0"));
    }

    [Test]
    public void ASaveOfAnotherValue_ForALockedField_IsRefused_NamingTheFieldAndTheKey()
    {
        Assert.That(
            () => Check(new SampleSettings { Name = "Other" }, new SampleHostRule { LockedName = "Host" }),
            Throws.InstanceOf<FormBuilderSettingLockedException>()
                .With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("sample.name")
                .And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo(SampleHostRule.NameKey)
                .And.Message.EqualTo("sample.name is locked by the host configuration (JironFormBuilder:Sample:Name)."));
    }

    [Test]
    public void ASaveOfTheValueTheHostSets_ForALockedField_Passes()
    {
        Assert.That(Check(new SampleSettings { Name = "Host" }, new SampleHostRule { LockedName = "Host" }), Is.Empty);
    }

    [Test]
    public void ASaveOfARefusedValue_IsRefused_AndOfAnAllowedOnePasses()
    {
        var rule = new SampleHostRule { QuietOnly = true };

        Assert.Multiple(() =>
        {
            Assert.That(
                () => Check(new SampleSettings { Mode = SampleMode.LoudAndClear }, rule),
                Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("sample.mode"));
            Assert.That(Check(new SampleSettings { Mode = SampleMode.Quiet }, rule), Is.Empty);
        });
    }

    [Test]
    public void ASaveAboveAClamp_IsARangeError_NamingTheKey_AndAtTheLimitPasses()
    {
        var rule = new SampleHostRule { MaxLimit = 8 };

        Assert.Multiple(() =>
        {
            Assert.That(
                Check(new SampleSettings { Limits = new SampleLimits { Max = 9 } }, rule),
                Is.EqualTo(new[] { new SettingsError("sample.limits.max", "range", "Is above the limit that JironFormBuilder:Sample:MaxLimit sets.") }));
            Assert.That(Check(new SampleSettings { Limits = new SampleLimits { Max = 8 } }, rule), Is.Empty);
        });
    }

    [Test]
    public void ASave_IsNotRefused_ForAFieldItDoesNotWrite()
    {
        var rule = new SampleHostRule { LockedName = "Host", QuietOnly = true, MaxLimit = 8 };
        var errors = new SettingsErrors(SampleSettings.Alias);

        SettingsHostRules.CheckSave(
            new SampleSettings { Name = "Other", Mode = SampleMode.LoudAndClear, Limits = new SampleLimits { Max = 9 }, Count = 1 },
            new HashSet<string> { "count" },
            new FormBuilderOptions(),
            [rule],
            errors);

        Assert.That(errors.Errors, Is.Empty, "a site that sets only the count is not refused for the global name, mode or maximum");
    }

    private static IReadOnlyList<SettingsError> Check(SampleSettings proposed, SampleHostRule rule)
    {
        var errors = new SettingsErrors(SampleSettings.Alias);
        SettingsHostRules.CheckSave(proposed, Everything, new FormBuilderOptions(), [rule], errors);
        return errors.Errors;
    }
}

/// <summary>A rule that restricts the name the sample rule locked, under a key of its own, and changes nothing.</summary>
file sealed class RestrictedNameRule : ISettingsHostRule<SampleSettings>
{
    public const string Key = "JironFormBuilder:Test:Name";

    public SampleSettings Apply(SampleSettings section, SettingsLocks locks)
    {
        locks.Add("name", SettingsLockKind.Restricted, Key);
        return section;
    }
}
