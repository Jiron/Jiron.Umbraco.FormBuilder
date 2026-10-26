using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// <c>site.name</c>, <c>site.url</c> and <c>entry.url</c> (ADR-0014, ADR-0067): the site's root name in the culture,
/// its base URL with each default, and the entry's link in the backoffice after the base URL.
/// </summary>
public sealed class SiteMergeTagSourceTests
{
    private const string EntryPath = "/umbraco/section/jfb-forms/workspace/jfb-entry/edit/e0000000-0000-4000-8000-000000000001";
    private static readonly DateTimeOffset Submitted = new(2026, 3, 7, 10, 0, 0, TimeSpan.Zero);

    /// <summary>The site's home in English and German, with a German domain written without a scheme.</summary>
    private static TestUmbraco Library()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Language("de-CH");
        umbraco.Page(SiteKey, new Dictionary<string, string> { ["en-GB"] = "Lindenhall Library", ["de-CH"] = "Bibliothek Lindenhall" });
        return umbraco;
    }

    [Test]
    public async Task TheSitesName_IsItsRootsName_InTheCulture()
    {
        var site = Create(umbraco: Library());

        Assert.Multiple(async () =>
        {
            Assert.That((await site.RenderAsync("{{ site.name }}", Scope())).Text, Is.EqualTo("Lindenhall Library"));
            Assert.That((await site.RenderAsync("{{ site.name }}", Scope(culture: "de-CH"))).Text, Is.EqualTo("Bibliothek Lindenhall"));
            Assert.That((await site.RenderAsync("[{{ site.name }}]", Scope() with { Site = null })).Text, Is.EqualTo("[]"), "no site");
        });
    }

    [Test]
    public async Task TheSitesAddress_AndTheEntrysLink_ComeFromTheBaseUrlSetting()
    {
        var umbraco = Library();
        umbraco.Domain(SiteKey, 1050, "lindenhall.example");
        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example/";
        var site = Create(umbraco: umbraco, baseUrl: "https://www.lindenhall.example");

        var rendered = await site.RenderAsync("{{ site.url }} {{ entry.url }}", Scope(entry: Entry(Submitted)));

        Assert.That(rendered.Text, Is.EqualTo("https://www.lindenhall.example https://www.lindenhall.example" + EntryPath), "the setting before the domain and the application URL");
    }

    [Test]
    public async Task WithoutTheSetting_TheBaseUrlIsTheSitesFirstHttpsDomain()
    {
        var umbraco = Library();
        umbraco.Domain(SiteKey, 1050, "http://old.lindenhall.example", sortOrder: 0);
        umbraco.Domain(SiteKey, 1050, "lindenhall.example/de", sortOrder: 1);
        umbraco.Domain(SiteKey, 1050, "https://lindenhall.example:8443", sortOrder: 2);
        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example/";
        var site = Create(umbraco: umbraco);

        var rendered = await site.RenderAsync("{{ site.url }} {{ entry.url }}", Scope(entry: Entry(Submitted)));

        Assert.That(rendered.Text, Is.EqualTo("https://lindenhall.example https://lindenhall.example" + EntryPath), "a domain without a scheme is reached over https; one with http is skipped");
    }

    [Test]
    public async Task WithoutTheSettingOrADomain_TheBaseUrlIsUmbracosApplicationUrl()
    {
        var umbraco = Library();
        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example/umbraco/";
        var site = Create(umbraco: umbraco);

        var rendered = await site.RenderAsync("{{ site.url }} {{ entry.url }}", Scope(entry: Entry(Submitted)));

        Assert.That(rendered.Text, Is.EqualTo("https://cms.lindenhall.example https://cms.lindenhall.example" + EntryPath), "its origin");
    }

    [Test]
    public async Task WithoutABaseUrl_TheAddressAndTheLinkAreEmpty()
    {
        var umbraco = Library();
        umbraco.Domain(SiteKey, 1050, "http://lindenhall.example");
        var site = Create(umbraco: umbraco);

        var rendered = await site.RenderAsync("[{{ site.url }}|{{ entry.url }}]", Scope(entry: Entry(Submitted)));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[|]"));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task BeforeTheVisitorSubmits_TheEntrysLinkIsEmpty()
    {
        var site = Create(umbraco: Library(), baseUrl: "https://www.lindenhall.example");

        var rendered = await site.RenderAsync("[{{ entry.url }}]", Scope());

        Assert.That(rendered.Text, Is.EqualTo("[]"));
    }

    [Test]
    public async Task TheEntrysLink_InAnHtmlMail_IsTextOfTheMail()
    {
        var site = Create(umbraco: Library(), baseUrl: "https://www.lindenhall.example");

        var rendered = await site.RenderAsync("<p>Open {{ entry.url }}</p>", Scope(entry: Entry(Submitted)), MergeTagPlace.Mail, MergeTagContext.Html);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("<p>Open https://www.lindenhall.example" + EntryPath + "</p>"));
            Assert.That(BackofficeLinks.Entry(Guid.Parse("e0000000-0000-4000-8000-000000000001")), Is.EqualTo(EntryPath));
        });
    }
}
