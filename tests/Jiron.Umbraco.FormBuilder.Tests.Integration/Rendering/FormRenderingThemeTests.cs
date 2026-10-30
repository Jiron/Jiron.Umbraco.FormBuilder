using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// A form's theme on a page (ROADMAP 6.3a, ADR-0015, ADR-0105): the placement's, else the form's own, else the site's
/// General setting, else <c>default</c>, and a theme the site lacks as <c>default</c>; each view found in the order
/// form, theme, default; and the stylesheets, the base rules first, written once per page, by a <c>&lt;jfb-assets&gt;</c>
/// for every form the page renders, one the layout renders after it included, or else by each form's own tags for what
/// the page still lacks. The test themes <c>acme</c>, <c>calm</c> and <c>quiet</c> are the site's own, with stylesheets
/// the site serves.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormRenderingThemeTests : RenderingTestBase
{
    private const string PackageSheets = "/jfb/assets/themes/";

    [Test]
    public async Task AFormsTheme_IsThePlacements_ThenTheFormsOwn_ThenTheSites_ThenTheDefault()
    {
        _ = await PublishFormAsync("renderingBoot", Themed("bootstrap5"));
        _ = await PublishFormAsync("renderingPlain");
        _ = await PublishFormAsync("renderingGone", Themed("a-theme-the-site-lacks"));
        var pages = await PagesAsync("themed");
        var before = (Own: await ThemeOfAsync("renderingBoot"), Placed: await ThemeOfAsync("renderingBoot", "unstyled"), Plain: await ThemeOfAsync("renderingPlain"), Gone: await ThemeOfAsync("renderingGone"));

        var site = Contents.GetById(Contents.GetById(pages["themed"])!.ParentId)!.Key;
        var saved = await GetRequiredService<IFormBuilderSettingsService>().SaveSiteAsync(site, new GeneralSettings { Theme = "unstyled" }, ["theme"], 0, Editor);
        var after = (Own: await ThemeOfAsync("renderingBoot"), Placed: await ThemeOfAsync("renderingBoot", "bootstrap5"), Plain: await ThemeOfAsync("renderingPlain"), Gone: await ThemeOfAsync("renderingGone"));

        _ = await GetRequiredService<IFormBuilderSettingsService>().SaveSiteAsync(site, new GeneralSettings { Theme = "gone-from-the-code" }, ["theme"], saved.Version, Editor);
        var removed = await ThemeOfAsync("renderingPlain");

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(("bootstrap5", "unstyled", "default", "default")), "the form's own, the placement's, the code's default, and a theme the site lacks as default");
            Assert.That(after, Is.EqualTo(("bootstrap5", "bootstrap5", "unstyled", "default")), "the site's theme for a form without one, the form's own and the placement's before it");
            Assert.That(removed, Is.EqualTo("default"), "a site's theme that is gone from the code renders as default");
        });
    }

    [Test]
    public async Task ATheme_AddsItsClassesBesideOurs_AndUnstyledNone()
    {
        _ = await PublishFormAsync("renderingPlain");
        _ = await PagesAsync("themed");

        var boot = (await DocumentAsync("/themed/?form=renderingPlain&theme=bootstrap5")).QuerySelector("form.jfb-form")!;
        var plain = (await DocumentAsync("/themed/?form=renderingPlain&theme=unstyled")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(boot.QuerySelector("input#jfb-1-name")!.GetAttribute("class"), Is.EqualTo("jfb-control form-control"));
            Assert.That(boot.QuerySelector("label[for='jfb-1-name']")!.GetAttribute("class"), Is.EqualTo("jfb-label form-label"));
            Assert.That(boot.QuerySelector(".jfb-navigation")!.GetAttribute("class"), Is.EqualTo("jfb-navigation d-flex flex-wrap gap-2"));
            Assert.That(boot.QuerySelector(".jfb-navigation button")!.GetAttribute("class"), Is.EqualTo("jfb-button jfb-button-primary btn btn-primary"));
            Assert.That(boot.QuerySelector("[data-jfb-field]")!.GetAttribute("class"), Is.EqualTo("jfb-field jfb-field-text mb-3"));
            Assert.That(plain.QuerySelector("input#jfb-1-name")!.GetAttribute("class"), Is.EqualTo("jfb-control"));
            Assert.That(plain.GetAttribute("class"), Is.EqualTo("jfb-form jfb-theme-unstyled"));
        });
    }

    [Test]
    public async Task EachView_IsTheFormsOwnInItsTheme_ThenTheThemes_ThenTheDefault()
    {
        _ = await PublishFormAsync("renderingAcme", Themed("acme"));
        _ = await PublishFormAsync("renderingAcmeOwn", Themed("acme"));
        _ = await PagesAsync("themed");

        var themed = (await DocumentAsync("/themed/?form=renderingAcme")).QuerySelector("form.jfb-form")!;
        var own = (await DocumentAsync("/themed/?form=renderingAcmeOwn")).QuerySelector("form.jfb-form")!;
        var placed = (await DocumentAsync("/themed/?form=renderingAcmeOwn&theme=default")).QuerySelector("form.jfb-form")!;

        Assert.Multiple(() =>
        {
            Assert.That(themed.QuerySelector("[data-jfb-field]")!.GetAttribute("data-test-view"), Is.EqualTo("acme"), "the theme's field wrapper");
            Assert.That(themed.QuerySelector("input#jfb-1-name")!.GetAttribute("data-test-view"), Is.EqualTo("acme-text"), "the theme's control");
            Assert.That(themed.QuerySelector("input#jfb-1-name")!.GetAttribute("class"), Is.EqualTo("jfb-control acme-input"), "with the theme's classes");
            Assert.That(themed.GetAttribute("class"), Is.EqualTo("jfb-form jfb-theme-acme acme-form"), "the form's, after ours and the theme's name");
            Assert.That(themed.QuerySelector("[data-jfb-group]")!.GetAttribute("class"), Does.StartWith("jfb-group ").And.EndWith(" acme-group"), "a group's");
            Assert.That(themed.QuerySelector("[data-jfb-field]")!.GetAttribute("class"), Is.EqualTo("jfb-field jfb-field-text acme-field"), "a field's, in the theme's own view");
            Assert.That(placed.GetAttribute("class"), Is.EqualTo("jfb-form jfb-theme-default"), "none of them in another theme");
            Assert.That(themed.QuerySelectorAll("section.jfb-page"), Has.Length.EqualTo(1), "a view the theme lacks is the default one");
            Assert.That(themed.QuerySelector("[data-jfb-group]")?.GetAttribute("data-test-view"), Is.Null);
            Assert.That(own.QuerySelector("[data-jfb-field]")!.GetAttribute("data-test-view"), Is.EqualTo("acme-form"), "the form's own override in the theme's folder first");
            Assert.That(own.QuerySelector("input#jfb-1-name")!.GetAttribute("data-test-view"), Is.EqualTo("acme-text"));
            Assert.That(placed.QuerySelectorAll("[data-test-view]"), Is.Empty, "in another theme, that theme's views");
        });
    }

    [Test]
    public async Task WithoutJfbAssets_EachFormsOwnTags_WriteWhatThePageLacks_BeforeTheForm()
    {
        await PublishThemedFormsAsync();
        _ = await PagesAsync("sheets");
        var (html, document) = await PageAsync("sheets");

        Assert.Multiple(() =>
        {
            Assert.That(Sheets(document), Is.EqualTo(new[] { "base", "/css/acme.css", "/css/calm.css", "default" }), "the base rules and each theme's once, the default theme's the package's; quiet's form brings no tags, and no tag covers it");
            Assert.That(document.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1));
            Assert.That(document.QuerySelectorAll("form.jfb-form").Select(form => form.ClassList.First(name => name.StartsWith("jfb-theme-", StringComparison.Ordinal))), Is.EqualTo(new[] { "jfb-theme-acme", "jfb-theme-acme", "jfb-theme-calm", "jfb-theme-default", "jfb-theme-quiet" }));
            Assert.That(html.IndexOf("/css/acme.css", StringComparison.Ordinal), Is.LessThan(html.IndexOf("<form", StringComparison.Ordinal)), "before the first form");
            Assert.That(html.IndexOf("/css/calm.css", StringComparison.Ordinal), Is.GreaterThan(html.IndexOf("id=\"jfb-2\"", StringComparison.Ordinal)).And.LessThan(html.IndexOf("id=\"jfb-3\"", StringComparison.Ordinal)), "calm's before its form");
            Assert.That(html.IndexOf("themes/base.css", StringComparison.Ordinal), Is.LessThan(html.IndexOf("/css/acme.css", StringComparison.Ordinal)), "the base rules first");
            Assert.That(html.IndexOf("/jfb/assets/themes/default.css?v=", StringComparison.Ordinal), Is.GreaterThan(html.IndexOf("id=\"jfb-3\"", StringComparison.Ordinal)).And.LessThan(html.IndexOf("id=\"jfb-4\"", StringComparison.Ordinal)), "default's before its form, under its fingerprint");
        });
    }

    [Test]
    public async Task AJfbAssetsInTheHead_WritesEveryFormsStylesheets_AFormTheLayoutRendersAfterItAndOneWithoutItsOwnTagsIncluded()
    {
        await PublishThemedFormsAsync();
        _ = await PagesAsync("sheets-head");
        var (_, document) = await PageAsync("sheets-head");

        Assert.Multiple(() =>
        {
            Assert.That(Sheets(document.Head!), Is.EqualTo(new[] { "base", "/css/acme.css", "/css/quiet.css", "/css/calm.css" }), "in the order the forms rendered: the view's, then the layout's footer");
            Assert.That(document.Head!.QuerySelectorAll("script[type=module]"), Has.Length.EqualTo(1));
            Assert.That(document.Body!.QuerySelectorAll("link, script[src]"), Is.Empty, "the forms add none");
            Assert.That(document.QuerySelector("footer form.jfb-form")!.ClassList, Does.Contain("jfb-theme-calm"));
        });
    }

    [Test]
    public async Task AJfbAssetsBetweenTwoForms_KeepsTheScript_AndTheFormBeforeItWritesItsStylesheets()
    {
        await PublishThemedFormsAsync();
        _ = await PagesAsync("sheets-late");
        var (_, document) = await PageAsync("sheets-late");
        var main = document.QuerySelector("main")!;

        Assert.Multiple(() =>
        {
            Assert.That(main.Children.Select(Tag), Is.EqualTo(new[] { "link base", "link /css/acme.css", "form jfb-1", "link /css/calm.css", "script", "form jfb-2" }), "the page's order: each tag where it is first needed");
            Assert.That(document.Head!.QuerySelectorAll("link, script[src]"), Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().Themes().Append<AcmeTheme>().Append<CalmTheme>().Append<QuietTheme>();
    }

    private static Func<FormDefinition, FormDefinition> Themed(string theme) =>
        definition => definition with { Settings = definition.Settings with { Appearance = definition.Settings.Appearance with { Theme = theme } } };

    // A stylesheet tag's address, a package stylesheet's by its name without its fingerprint, such as base; other tags
    // by name.
    private static string Tag(IElement element) => element.LocalName switch
    {
        "link" => "link " + Address(element),
        "form" => "form " + element.Id,
        _ => element.LocalName,
    };

    private static string Address(IElement link) => link.GetAttribute("href")! is var href && href.StartsWith(PackageSheets, StringComparison.Ordinal)
        ? href[PackageSheets.Length..href.IndexOf(".css?v=", StringComparison.Ordinal)]
        : href;

    private static IEnumerable<string> Sheets(IParentNode node) => node.QuerySelectorAll("link[rel=stylesheet]").Select(Address);

    private async Task PublishThemedFormsAsync()
    {
        _ = await PublishFormAsync("renderingAcme", Themed("acme"));
        _ = await PublishFormAsync("renderingCalm", Themed("calm"));
        _ = await PublishFormAsync("renderingQuiet", Themed("quiet"));
        _ = await PublishFormAsync("renderingPlain");
    }

    // The theme a form renders with on the page themed, in the placement's theme if one is given.
    private async Task<string> ThemeOfAsync(string form, string? theme = null)
    {
        var document = await DocumentAsync($"/themed/?form={form}" + (theme is null ? string.Empty : "&theme=" + theme));
        var classes = document.QuerySelector("form.jfb-form")?.ClassList ?? throw new AssertionException($"{form}: no form");
        return classes.Single(name => name.StartsWith("jfb-theme-", StringComparison.Ordinal))["jfb-theme-".Length..];
    }

    private async Task<IHtmlDocument> DocumentAsync(string path)
    {
        using var response = await GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{path} answers {(int)response.StatusCode}: {html}");
        return await new HtmlParser().ParseDocumentAsync(html);
    }

    /// <summary>A site's own theme with its own views, and classes for the form, its groups, its fields and their controls.</summary>
    public sealed class AcmeTheme : TestTheme
    {
        /// <summary>The theme acme.</summary>
        public AcmeTheme()
            : base("acme", new Dictionary<string, string>
            {
                [FormBuilderThemeSlots.Form] = "acme-form",
                [FormBuilderThemeSlots.Group] = "acme-group",
                [FormBuilderThemeSlots.Field] = "acme-field",
                [FormBuilderThemeSlots.Control] = "acme-input",
            })
        {
        }
    }

    /// <summary>A site's own theme with a stylesheet only.</summary>
    public sealed class CalmTheme : TestTheme
    {
        /// <summary>The theme calm.</summary>
        public CalmTheme()
            : base("calm", new Dictionary<string, string>())
        {
        }
    }

    /// <summary>Another site's own theme with a stylesheet only.</summary>
    public sealed class QuietTheme : TestTheme
    {
        /// <summary>The theme quiet.</summary>
        public QuietTheme()
            : base("quiet", new Dictionary<string, string>())
        {
        }
    }

    /// <summary>A test theme whose view folder is its alias and whose stylesheet the site serves at <c>/css/&lt;alias&gt;.css</c>.</summary>
    public abstract class TestTheme(string name, IReadOnlyDictionary<string, string> classes) : IFormBuilderTheme
    {
        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public string Label => name;

        /// <inheritdoc />
        public string? Stylesheet => $"/css/{name}.css";

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Classes => classes;

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string>();

        /// <inheritdoc />
        public bool DarkScheme => false;

        /// <inheritdoc />
        public string ViewFolder => name;
    }
}
