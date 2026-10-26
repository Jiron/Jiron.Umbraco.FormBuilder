using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Umbraco.Cms.Core.Strings;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagTestSite;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// <c>page.*</c> (ADR-0014, ADR-0067): the content page the form is on, its name in the culture, its address from
/// Umbraco and the site's base URL, its properties in the culture, and a property of the page or its nearest ancestor.
/// </summary>
public sealed class PageMergeTagSourceTests
{
    private static readonly Guid Events = Guid.Parse("60000000-0000-4000-8000-000000000001");
    private static readonly Guid Concert = Guid.Parse("60000000-0000-4000-8000-000000000002");

    /// <summary>
    /// The site's home, its events page in English and German, and a concert below it: the home holds the brand and the
    /// slogan, the events page its own slogan, the concert neither.
    /// </summary>
    private static TestUmbraco Library()
    {
        var umbraco = new TestUmbraco();
        umbraco.Language("en-GB", isDefault: true);
        umbraco.Language("de-CH");
        umbraco.Page(SiteKey, "Lindenhall", properties: new Dictionary<string, object?> { ["brand"] = "Lindenhall Library", ["slogan"] = "Read more" });
        umbraco.Page(
            Events,
            new Dictionary<string, string> { ["en-GB"] = "Events", ["de-CH"] = "Veranstaltungen" },
            SiteKey,
            new Dictionary<string, object?>
            {
                ["heroTitle"] = new Dictionary<string, object?> { ["en-GB"] = "Join us", ["de-CH"] = "Mach mit" },
                ["slogan"] = "Come along",
                ["seats"] = 1200,
                ["opens"] = new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Unspecified),
                ["intro"] = new HtmlEncodedString("<p>Readings &amp; <em>talks</em></p>"),
                ["tags"] = new[] { "music", "books" },
            });
        umbraco.Page(Concert, "Concert", Events);
        return umbraco;
    }

    [Test]
    public async Task ThePagesName_IsTheOneOfTheCulture()
    {
        var site = Create(umbraco: Library());

        Assert.Multiple(async () =>
        {
            Assert.That((await site.RenderAsync("{{ page.name }}", Scope(page: Events))).Text, Is.EqualTo("Events"));
            Assert.That((await site.RenderAsync("{{ page.name }}", Scope(page: Events, culture: "de-CH"))).Text, Is.EqualTo("Veranstaltungen"));
            Assert.That((await site.RenderAsync("{{ page.name }}", Scope(page: Concert, culture: "de-CH"))).Text, Is.EqualTo("Concert"), "an invariant page");
            Assert.That((await site.RenderAsync("{{ page.name }}", Scope(page: Events, culture: string.Empty))).Text, Is.EqualTo("Events"), "the invariant culture reads the default language");
        });
    }

    [Test]
    public async Task ThePagesAddress_IsUmbracosOne_AfterTheBaseUrl_AndUmbracoNeverSeesTheRequest()
    {
        var umbraco = Library();
        umbraco.Url(Events, "de-CH", "/de/veranstaltungen/");
        umbraco.Url(Events, "en-GB", "https://lindenhall.example/events/");
        var site = Create(umbraco: umbraco, baseUrl: "https://www.lindenhall.example");

        var underBase = await site.RenderAsync("{{ page.url }}", Scope(page: Events, culture: "de-CH"));
        var underDomain = await site.RenderAsync("{{ page.url }}", Scope(page: Events));
        var none = await site.RenderAsync("{{ page.url }}", Scope(page: Concert));

        Assert.Multiple(() =>
        {
            Assert.That(underBase.Text, Is.EqualTo("https://www.lindenhall.example/de/veranstaltungen/"), "a page without a domain, after the base URL");
            Assert.That(underDomain.Text, Is.EqualTo("https://lindenhall.example/events/"), "a page under a domain has the domain's address");
            Assert.That(none.Text, Is.Empty, "a page Umbraco gives no address");
            Assert.That(umbraco.Currents, Has.Count.EqualTo(3).And.All.EqualTo(new Uri("https://www.lindenhall.example/")), "Umbraco is given the base URL as the current address");
        });
    }

    [Test]
    public async Task WithoutABaseUrl_ThePagesAddressIsEmpty()
    {
        var umbraco = Library();
        umbraco.Url(Events, "en-GB", "/events/");
        var site = Create(umbraco: umbraco);

        var rendered = await site.RenderAsync("[{{ page.url }}]", Scope(page: Events));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("[]"));
            Assert.That(rendered.Problems, Is.Empty);
            Assert.That(umbraco.Currents, Is.Empty, "Umbraco is not asked without an address to give it");
        });
    }

    [Test]
    public async Task AProperty_ShowsItsValueInTheCulture_AsItsKindShows()
    {
        var site = Create(umbraco: Library());

        Assert.Multiple(async () =>
        {
            Assert.That((await site.RenderAsync("{{ page.heroTitle }}", Scope(page: Events))).Text, Is.EqualTo("Join us"));
            Assert.That((await site.RenderAsync("{{ page.heroTitle }}", Scope(page: Events, culture: "de-CH"))).Text, Is.EqualTo("Mach mit"));
            Assert.That((await site.RenderAsync("{{ page.seats }} / {{ page.seats | number: 2 }}", Scope(page: Events))).Text, Is.EqualTo("1,200 / 1,200.00"), "the number behind the text");
            Assert.That((await site.RenderAsync("{{ page.opens }} / {{ page.opens | date: \"iso\" }}", Scope(page: Events))).Text, Is.EqualTo("07/03/2026 / 2026-03-07"), "the date behind the text");
            Assert.That((await site.RenderAsync("{{ page.intro }}", Scope(page: Events))).Text, Is.EqualTo("Readings & talks"), "rich text as its text");
            Assert.That((await site.RenderAsync("{{ page.tags | join: \" + \" }}", Scope(page: Events))).Text, Is.EqualTo("music + books"), "a list");
            Assert.That((await site.RenderAsync("[{{ page.brand }}]", Scope(page: Events))).Text, Is.EqualTo("[]"), "a property the page's type lacks");
        });
    }

    [Test]
    public async Task UpAProperty_IsThePagesOwn_OrItsNearestAncestorsThatHasOne()
    {
        var site = Create(umbraco: Library());

        Assert.Multiple(async () =>
        {
            Assert.That((await site.RenderAsync("{{ page.up.slogan }}", Scope(page: Events))).Text, Is.EqualTo("Come along"), "the page's own");
            Assert.That((await site.RenderAsync("{{ page.up.slogan }}", Scope(page: Concert))).Text, Is.EqualTo("Come along"), "the parent's before the home's");
            Assert.That((await site.RenderAsync("{{ page.up.brand }}", Scope(page: Concert))).Text, Is.EqualTo("Lindenhall Library"), "two levels up");
            Assert.That((await site.RenderAsync("[{{ page.up.nowhere }}]", Scope(page: Concert))).Text, Is.EqualTo("[]"), "no page on the way has it");
        });
    }

    [Test]
    public async Task WithoutAPage_OrForAPageThatIsGone_EveryPathIsEmpty_WithoutAProblem()
    {
        var site = Create(umbraco: Library(), baseUrl: "https://www.lindenhall.example");
        const string Template = "[{{ page.name }}|{{ page.url }}|{{ page.heroTitle }}|{{ page.up.brand }}]";

        var without = await site.RenderAsync(Template, Scope());
        var gone = await site.RenderAsync(Template, Scope(page: Guid.Parse("60000000-0000-4000-8000-0000000000ff")));

        Assert.Multiple(() =>
        {
            Assert.That(without.Text, Is.EqualTo("[|||]"));
            Assert.That(without.Problems, Is.Empty);
            Assert.That(gone.Text, Is.EqualTo("[|||]"));
            Assert.That(gone.Problems, Is.Empty);
        });
    }

    [TestCase("page")]
    [TestCase("page.up")]
    [TestCase("page.heroTitle.text")]
    [TestCase("page.up.brand.text")]
    public async Task APathThatIsNotOneOfThePages_IsUnknown_WhenCheckedAndRendered(string path)
    {
        var site = Create(umbraco: Library());
        var template = "{{ " + path + " }}";

        var rendered = await site.RenderAsync(template, Scope(page: Events));
        var checkedOnly = site.Service.Check(template, MergeTagPlace.Mail, MergeTagContext.Text, Form());

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.Empty);
            Assert.That(rendered.Problems, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
            Assert.That(checkedOnly, Is.EqualTo(new[] { new MergeTagProblem(MergeTagProblems.UnknownPath, 3) }));
        });
    }

    [Test]
    public void TheCheck_KnowsEveryPropertyAlias_WithoutAPage()
    {
        var site = Create(umbraco: Library());

        Assert.That(site.Service.Check("{{ page.name }} {{ page.url }} {{ page.anything }} {{ page.up.anything }}", MergeTagPlace.VisitorText, MergeTagContext.Html, Form()), Is.Empty);
    }
}
