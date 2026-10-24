using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;

/// <summary>
/// A site whose host locks the general section (<c>JironFormBuilder:LockedSections</c>, ADR-0008): the stored values
/// show read-only, and no save of the section goes through, globally or for a site.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class LockedSectionTests : RepositoryTestBase
{
    private static readonly Guid Site = Guid.Parse("8a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c01");

    /// <summary>The host configuration of every test's site, as a host passes it in its appsettings.json; the second entry locks general.</summary>
    public LockedSectionTests()
    {
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:0"] = "privacy";
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:1"] = "General";
    }

    private IFormBuilderSettingsService Service => GetRequiredService<IFormBuilderSettingsService>();

    [Test]
    public async Task ALockedSection_ShowsItsStoredValues_LockedWhole_NamingTheKey()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "general", 1, 1, """{"timeZone":"Europe/Berlin"}""", null));

        var general = await Service.GetAsync<GeneralSettings>();

        Assert.Multiple(() =>
        {
            Assert.That(general.Value.TimeZone, Is.EqualTo("Europe/Berlin"));
            Assert.That(general.Locks, Is.EqualTo(new Dictionary<string, SettingsLock> { ["*"] = new(SettingsLockKind.Locked, "JironFormBuilder:LockedSections:1") }));
        });
    }

    [Test]
    public async Task ASaveOfALockedSection_IsRefused_InEveryScope_AndWritesNothing()
    {
        var current = await Service.GetAsync<GeneralSettings>();

        Assert.Multiple(async () =>
        {
            Assert.That(
                () => Service.SaveAsync(current.Value with { TimeZone = "Europe/Berlin" }, current.Version, Editor),
                Throws.InstanceOf<FormBuilderSettingLockedException>()
                    .With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("general")
                    .And.Property(nameof(FormBuilderSettingLockedException.ConfigurationKey)).EqualTo("JironFormBuilder:LockedSections:1"));
            Assert.That(() => Service.SaveAsync(current.Value, current.Version, Editor), Throws.InstanceOf<FormBuilderSettingLockedException>(), "even unchanged");
            Assert.That(() => Service.SaveSiteAsync(Site, new GeneralSettings { TimeZone = "Asia/Tokyo" }, ["timeZone"], 0, Editor), Throws.InstanceOf<FormBuilderSettingLockedException>());
            Assert.That((await Settings.GetAsync(SettingsScopes.Global, "general"))!.Version, Is.EqualTo(1));
            Assert.That(await Settings.GetAsync(SettingsScopes.ForSite(Site), "general"), Is.Null);
            Assert.That(Count("jfbAuditEntry"), Is.Zero);
        });
    }

    [Test]
    public async Task AnotherSection_IsNotLocked()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.ForSite(Site), "general", 1, 0, """{"timeZone":"Asia/Tokyo"}""", null));

        Assert.That((await Service.GetAsync<GeneralSettings>(Site)).Value.TimeZone, Is.EqualTo("Asia/Tokyo"), "a site's stored override shows too");
    }

}
