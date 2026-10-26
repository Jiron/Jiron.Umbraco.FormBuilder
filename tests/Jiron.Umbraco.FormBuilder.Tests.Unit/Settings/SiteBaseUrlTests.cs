using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The base URL (section 4.8, ADR-0008, ADR-0067): the general setting for the site or the global one, then the site's
/// first <c>https</c> domain, the root's before deeper ones, then Umbraco's application URL; the origins it takes, and
/// the setting's check.
/// </summary>
public sealed class SiteBaseUrlTests
{
    private static readonly Guid Site = Guid.Parse("50000000-0000-4000-8000-000000000001");
    private static readonly Guid English = Guid.Parse("50000000-0000-4000-8000-000000000002");
    private static readonly Guid German = Guid.Parse("50000000-0000-4000-8000-000000000003");
    private static readonly Guid Shop = Guid.Parse("50000000-0000-4000-8000-000000000004");
    private static readonly Guid Other = Guid.Parse("50000000-0000-4000-8000-000000000005");

    [Test]
    public async Task TheSetting_ComesFirst_AsItsOrigin()
    {
        var umbraco = new TestUmbraco();
        umbraco.Domain(Site, 1050, "https://lindenhall.example");
        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example";

        var baseUrl = await umbraco.BaseUrls(MergeTagTestSite.Settings("UTC", "https://www.lindenhall.example")).GetAsync(Site, CancellationToken.None);

        Assert.That(baseUrl, Is.EqualTo("https://www.lindenhall.example"));
    }

    [Test]
    public async Task TheSettingForASite_IsAskedForThatSite()
    {
        var umbraco = new TestUmbraco();
        var settings = MergeTagTestSite.Settings("UTC", "https://www.lindenhall.example");

        await umbraco.BaseUrls(settings).GetAsync(Site, CancellationToken.None);

        await settings.Received(1).GetAsync<GeneralSettings>(Site, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task WithoutTheSetting_TheRootsDomainsComeFirst_InTheirOrder_AndOnlyThoseReachedOverHttps()
    {
        var umbraco = new TestUmbraco();
        umbraco.Domain(Site, 1050, "*1050");
        umbraco.Domain(Site, 1050, "http://old.lindenhall.example", sortOrder: 0);
        umbraco.Domain(Site, 1050, "HTTPS://Lindenhall.Example/en", sortOrder: 2);
        umbraco.Domain(Site, 1050, "lindenhall.de/de", sortOrder: 1);
        umbraco.Node(English, Site);
        umbraco.Domain(English, 1051, "https://en.lindenhall.example", sortOrder: 0);

        var baseUrl = await umbraco.BaseUrls(MergeTagTestSite.Settings("UTC")).GetAsync(Site, CancellationToken.None);

        Assert.That(baseUrl, Is.EqualTo("https://lindenhall.de"), "a wildcard and an http domain skipped, then the sort order");
    }

    [Test]
    public async Task ARootWithoutDomains_TakesTheNearestDomainBelowIt_AndNoneOfAnotherSite()
    {
        var umbraco = new TestUmbraco();
        umbraco.Node(English, Site);
        umbraco.Node(Shop, English);
        umbraco.Node(German, Site);
        umbraco.Domain(Other, 2000, "https://other.example");
        umbraco.Domain(Shop, 1053, "https://shop.lindenhall.example");
        umbraco.Domain(German, 1052, "lindenhall.ch/de", sortOrder: 1);
        umbraco.Domain(English, 1051, "lindenhall.co.uk", sortOrder: 0);

        var baseUrl = await umbraco.BaseUrls(MergeTagTestSite.Settings("UTC")).GetAsync(Site, CancellationToken.None);

        Assert.That(baseUrl, Is.EqualTo("https://lindenhall.co.uk"), "the language roots before the shop below one, in their order");
    }

    [Test]
    public async Task WithoutTheSettingOrADomain_ItIsUmbracosApplicationUrl_AndOtherwiseNone()
    {
        var umbraco = new TestUmbraco();
        umbraco.Domain(Site, 1050, "http://lindenhall.example");
        var baseUrls = umbraco.BaseUrls(MergeTagTestSite.Settings("UTC"));

        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example:8443/umbraco/";
        var application = await baseUrls.GetAsync(Site, CancellationToken.None);
        umbraco.RoutingSettings.UmbracoApplicationUrl = "ftp://cms.lindenhall.example/";
        var notWeb = await baseUrls.GetAsync(Site, CancellationToken.None);
        umbraco.RoutingSettings.UmbracoApplicationUrl = string.Empty;
        var none = await baseUrls.GetAsync(Site, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(application, Is.EqualTo("https://cms.lindenhall.example:8443"), "its origin, with its port");
            Assert.That(notWeb, Is.Null);
            Assert.That(none, Is.Null);
        });
    }

    [Test]
    public async Task WithoutASite_ItIsTheGlobalSetting_OrUmbracosApplicationUrl_AndNoDomainIsRead()
    {
        var umbraco = new TestUmbraco();
        umbraco.Domain(Site, 1050, "https://lindenhall.example");
        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example";

        var global = await umbraco.BaseUrls(MergeTagTestSite.Settings("UTC", "https://forms.lindenhall.example")).GetAsync(null, CancellationToken.None);
        var application = await umbraco.BaseUrls(MergeTagTestSite.Settings("UTC")).GetAsync(null, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(global, Is.EqualTo("https://forms.lindenhall.example"));
            Assert.That(application, Is.EqualTo("https://cms.lindenhall.example"));
            await umbraco.Domains.DidNotReceive().GetAllAsync(Arg.Any<bool>());
        });
    }

    [TestCase("https://example.com", "https://example.com")]
    [TestCase("  HTTPS://Example.COM/  ", "https://example.com")]
    [TestCase("https://example.com:443/", "https://example.com")]
    [TestCase("https://example.com:8443", "https://example.com:8443")]
    [TestCase("http://localhost:5500", "http://localhost:5500")]
    [TestCase("https://example.com/de", null)]
    [TestCase("https://example.com/?ref=1", null)]
    [TestCase("https://example.com/#top", null)]
    [TestCase("https://user@example.com", null)]
    [TestCase("ftp://example.com", null)]
    [TestCase("example.com", null)]
    [TestCase("/relative", null)]
    [TestCase("", null)]
    public void TheSetting_TakesAnOrigin_AndNothingAfterIt(string address, string? origin)
    {
        Assert.That(BaseUrls.Normalize(address), Is.EqualTo(origin));
    }

    [TestCase("lindenhall.example", "https://lindenhall.example")]
    [TestCase("lindenhall.example/de", "https://lindenhall.example")]
    [TestCase("https://lindenhall.example:8443/en", "https://lindenhall.example:8443")]
    [TestCase("http://lindenhall.example", null)]
    [TestCase("*1050", null)]
    [TestCase(" ", null)]
    public void ADomain_GivesTheOriginItIsReachedAtOverHttps(string name, string? origin)
    {
        Assert.That(BaseUrls.DomainOrigin(name), Is.EqualTo(origin));
    }

    [Test]
    public void TheSettingsCheck_StoresAnOriginAsItsOrigin_TheDefaultForWhiteSpace_AndRefusesAnythingElse()
    {
        var validator = new GeneralSettingsValidator();

        var normalized = validator.Normalize(new GeneralSettings { BaseUrl = " HTTPS://www.Lindenhall.example/ " });
        var blank = validator.Normalize(new GeneralSettings { BaseUrl = "   " });
        var withPath = validator.Normalize(new GeneralSettings { BaseUrl = "https://www.lindenhall.example/de" });
        var refused = new SettingsErrors("general");
        validator.Validate(withPath, refused);
        var accepted = new SettingsErrors("general");
        validator.Validate(normalized, accepted);
        validator.Validate(blank, accepted);

        Assert.Multiple(() =>
        {
            Assert.That(normalized.BaseUrl, Is.EqualTo("https://www.lindenhall.example"));
            Assert.That(blank.BaseUrl, Is.Null);
            Assert.That(withPath.BaseUrl, Is.EqualTo("https://www.lindenhall.example/de"), "kept for the check to refuse");
            Assert.That(refused.Errors.Select(error => (error.Field, error.Code)), Is.EqualTo(new[] { ("general.baseUrl", "baseUrl") }));
            Assert.That(accepted.Errors, Is.Empty);
        });
    }
}
