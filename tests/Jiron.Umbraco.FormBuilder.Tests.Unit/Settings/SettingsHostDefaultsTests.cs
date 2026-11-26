using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The host configuration's default time zone (27.1): it fills the General settings' zone where no stored row names one,
/// and a stored zone wins, for every site or for one, a site's choice of the package's default (stored as a null) among
/// them; without the key, or for another section, nothing changes.
/// </summary>
[TestFixture]
public sealed class SettingsHostDefaultsTests
{
    private static readonly GeneralSettings Package = new();

    [Test]
    public void WithNothingStored_TheConfiguredZoneIsTheSites()
    {
        Assert.That(SettingsHostDefaults.Apply(Package, [], null, Host("Europe/Zurich")).TimeZone, Is.EqualTo("Europe/Zurich"));
        Assert.That(SettingsHostDefaults.Apply(Package, [], new JsonObject { ["theme"] = "bootstrap5" }, Host("Europe/Zurich")).TimeZone, Is.EqualTo("Europe/Zurich"), "a site row that overrides something else");
        Assert.That(SettingsHostDefaults.Apply(Package, new JsonObject { ["timeZone"] = null }, null, Host("Europe/Zurich")).TimeZone, Is.EqualTo("Europe/Zurich"), "a global null is no zone");
    }

    [Test]
    public void AStoredZone_Wins()
    {
        var global = Package with { TimeZone = "Asia/Tokyo" };

        Assert.Multiple(() =>
        {
            Assert.That(SettingsHostDefaults.Apply(global, new JsonObject { ["timeZone"] = "Asia/Tokyo" }, null, Host("Europe/Zurich")), Is.SameAs(global), "the global row's");
            Assert.That(SettingsHostDefaults.Apply(Package, [], new JsonObject { ["timeZone"] = null }, Host("Europe/Zurich")), Is.SameAs(Package), "a site that chose the package's default");
        });
    }

    [Test]
    public void WithoutTheKey_OrForAnotherSection_NothingChanges()
    {
        var privacy = new PrivacySettings();

        Assert.Multiple(() =>
        {
            Assert.That(SettingsHostDefaults.Apply(Package, [], null, new FormBuilderOptions()), Is.SameAs(Package));
            Assert.That(SettingsHostDefaults.Apply(privacy, [], null, Host("Europe/Zurich")), Is.SameAs(privacy));
        });
    }

    private static FormBuilderOptions Host(string zone)
    {
        var options = new FormBuilderOptions();
        options.General.DefaultTimeZone = zone;
        return options;
    }
}
