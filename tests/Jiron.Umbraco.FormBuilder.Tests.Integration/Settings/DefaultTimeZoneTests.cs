using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;

/// <summary>
/// The default time zone from the host configuration (<c>JironFormBuilder:General:DefaultTimeZone</c>, 27.1): a site
/// that has saved none runs in it, and the General settings show it; a zone saved for every site, or for one, wins,
/// a site's choice of UTC among them.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class DefaultTimeZoneTests : RepositoryTestBase
{
    private static readonly Guid Shop = Guid.Parse("8a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c11");
    private static readonly Guid Blog = Guid.Parse("8a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c12");

    /// <summary>The host configuration of every test's site, as a host passes it in its appsettings.json.</summary>
    public DefaultTimeZoneTests()
    {
        InMemoryConfiguration[FormBuilderGeneralOptions.DefaultTimeZoneKey] = "Europe/Zurich";
    }

    private IFormBuilderSettingsService Service => GetRequiredService<IFormBuilderSettingsService>();

    private SiteTimeZones TimeZones => GetRequiredService<SiteTimeZones>();

    [Test]
    public async Task WithNothingSaved_EverySiteRunsInTheConfiguredZone_AndTheSettingsShowIt()
    {
        var general = await Service.GetAsync<GeneralSettings>();

        Assert.Multiple(async () =>
        {
            Assert.That((await TimeZones.GetAsync(null)).Id, Is.EqualTo("Europe/Zurich"));
            Assert.That((await TimeZones.GetAsync(Shop)).Id, Is.EqualTo("Europe/Zurich"));
            Assert.That(general.Value.TimeZone, Is.EqualTo("Europe/Zurich"), "the screen shows the zone the forms run in");
            Assert.That(general.Locks, Is.Empty, "a default, not a lock");
        });
    }

    [Test]
    public async Task ASavedZone_Wins_ForEverySite_OrForOne_ASitesChoiceOfUtcAmongThem()
    {
        var current = await Service.GetAsync<GeneralSettings>();
        var shop = await Service.SaveSiteAsync(Shop, new GeneralSettings { TimeZone = "Pacific/Auckland" }, ["timeZone"], 0, Editor);
        await Service.SaveSiteAsync(Blog, new GeneralSettings { TimeZone = GeneralSettings.DefaultTimeZone }, ["timeZone"], 0, Editor);
        var beforeGlobal = (await TimeZones.GetAsync(null)).Id;
        await Service.SaveAsync(current.Value with { TimeZone = "Asia/Tokyo" }, current.Version, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(shop.Value.TimeZone, Is.EqualTo("Pacific/Auckland"));
            Assert.That((await TimeZones.GetAsync(Shop)).Id, Is.EqualTo("Pacific/Auckland"), "the site's own zone");
            Assert.That(await TimeZones.GetAsync(Blog), Is.EqualTo(TimeZoneInfo.Utc), "a site that chose UTC keeps it");
            Assert.That(beforeGlobal, Is.EqualTo("Europe/Zurich"), "until the global zone is saved, the configured one");
            Assert.That((await TimeZones.GetAsync(null)).Id, Is.EqualTo("Asia/Tokyo"), "the saved global zone");
            Assert.That((await TimeZones.GetAsync(Guid.NewGuid())).Id, Is.EqualTo("Asia/Tokyo"), "for a site without its own");
        });
    }
}
