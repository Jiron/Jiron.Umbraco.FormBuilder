using System.Text;
using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// <c>&lt;jfb-assets&gt;</c> and a form's own tags (ADR-0004, ADR-0092, ADR-0105), run as Razor runs them, over the
/// test's own files: the base rules, each theme's stylesheet and the module script, under their fingerprints and the
/// path base, each written once per request however often the page names the tag or renders a form, without the
/// element itself, and with the nonce of the attribute, else of the site's provider, else none. The script goes to the
/// first tag that runs; each stylesheet is decided when the page is written, in the page's order.
/// </summary>
[TestFixture]
public sealed class FormBuilderAssetsTagHelperTests
{
    private static readonly IFormBuilderTheme Calm = new Theme("calm", "/jfb/assets/themes/calm.css");
    private static readonly IFormBuilderTheme Bare = new Theme("bare", null);
    private static readonly IFormBuilderTheme Acme = new Theme("acme", "/css/acme%20forms.css");
    private static readonly IFormBuilderTheme CalmAgain = new Theme("calm-again", "/jfb/assets/themes/calm.css");

    private WebsiteAssets _assets = null!;

    [SetUp]
    public void CreateTheAssets() => _assets = new WebsiteAssets(new Dictionary<string, byte[]>
    {
        ["runtime/jfb-runtime.js"] = Encoding.UTF8.GetBytes("export const runtime = 1;"),
        ["themes/base.css"] = Encoding.UTF8.GetBytes(".jfb-form [hidden] { display: none; }"),
        ["themes/calm.css"] = Encoding.UTF8.GetBytes(".jfb-form { color: teal; }"),
    });

    private string Src(string pathBase = "") => $"{pathBase}/jfb/assets/runtime/jfb-runtime.js?v={_assets.Find("runtime/jfb-runtime.js")!.Fingerprint}";

    private string Script(string? nonce = null, string pathBase = "") =>
        $"<script {(nonce is null ? string.Empty : $"nonce=\"{nonce}\" ")}src=\"{Src(pathBase)}\" type=\"module\"></script>";

    private string Link(string name, string? nonce = null, string pathBase = "") =>
        $"<link href=\"{pathBase}/jfb/assets/{name}?v={_assets.Find(name)!.Fingerprint}\" {(nonce is null ? string.Empty : $"nonce=\"{nonce}\" ")}rel=\"stylesheet\">";

    private string BaseRules(string? nonce = null, string pathBase = "") => Link("themes/base.css", nonce, pathBase);

    [Test]
    public void TheTag_WritesTheBaseRulesAndTheModuleScript_WithoutTheElementItself()
    {
        var http = Request();

        Assert.That(Run(http), Is.EqualTo(BaseRules() + Script()));
    }

    [Test]
    public void ThePathBase_PrecedesEveryAddress()
    {
        var http = Request();
        http.Request.PathBase = "/site";
        PageAssets.Use(http, Calm);
        PageAssets.Use(http, Acme);

        Assert.That(Run(http), Is.EqualTo(BaseRules(pathBase: "/site") + Link("themes/calm.css", pathBase: "/site") + "<link href=\"/site/css/acme%20forms.css\" rel=\"stylesheet\">" + Script(pathBase: "/site")));
    }

    [Test]
    public void ATagNamedTwice_WritesOnce_AndTheSecondLeavesNothing()
    {
        var http = Request(new Provider("from-the-site"));
        PageAssets.Use(http, Calm);

        var first = Run(http, "first");
        var second = Run(http, "second");
        var third = Run(http);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(BaseRules("first") + Link("themes/calm.css", "first") + Script("first")));
            Assert.That(second, Is.Empty);
            Assert.That(third, Is.Empty);
        });
    }

    [Test]
    public void TheTagsContent_WrittenTwice_WritesItsTagsOnce()
    {
        var http = Request();
        var tags = http.RequestServices.GetRequiredService<PageAssets>().TagsOnce(http, null)!;

        Assert.Multiple(() =>
        {
            Assert.That(Run(http), Is.Empty, "the tag claims the script as it runs");
            Assert.That(Write(tags), Is.EqualTo(BaseRules() + Script()));
            Assert.That(Write(tags), Is.Empty);
        });
    }

    [Test]
    public void EachRequest_GetsItsOwnTags()
    {
        var one = Request();
        var other = Request();

        Assert.Multiple(() =>
        {
            Assert.That(Run(one), Is.Not.Empty);
            Assert.That(Run(other), Is.Not.Empty);
        });
    }

    [Test]
    public void TheAttributesNonce_WinsOverTheProviders()
    {
        var provider = new Provider("from-the-site");
        var http = Request(provider);

        Assert.Multiple(() =>
        {
            Assert.That(Run(http, "from-the-page"), Is.EqualTo(BaseRules("from-the-page") + Script("from-the-page")));
            Assert.That(provider.Asked, Is.Zero, "the provider is not asked");
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void WithoutTheAttributesNonce_TheProviderGivesIt_ForThisRequest(string? attribute)
    {
        var provider = new Provider("from-the-site");
        var http = Request(provider);

        Assert.Multiple(() =>
        {
            Assert.That(Run(http, attribute), Is.EqualTo(BaseRules("from-the-site") + Script("from-the-site")));
            Assert.That(provider.Requests, Is.EqualTo(new[] { http }));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void WithNeitherNonce_TheTagsHaveNone(string? given)
    {
        var withoutProvider = Request();
        var withEmptyProvider = Request(new Provider(given));

        Assert.Multiple(() =>
        {
            Assert.That(Run(withoutProvider, given), Does.Not.Contain("nonce"));
            Assert.That(Run(withEmptyProvider, given), Does.Not.Contain("nonce"), "an empty nonce is none");
        });
    }

    [Test]
    public void TheTagsChildren_AreNeverWritten()
    {
        var http = Request();

        var first = Run(http, child: "<p>child</p>");
        var second = Run(http, child: "<p>child</p>");

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(BaseRules() + Script()));
            Assert.That(second, Is.Empty);
        });
    }

    [Test]
    public void TheNonce_IsWrittenAsAnAttributeValue()
    {
        var http = Request();

        Assert.That(Run(http, "a\"b<c"), Does.Contain("<link href").And.Contain("nonce=\"a&quot;b&lt;c\" rel").And.Contain("<script nonce=\"a&quot;b&lt;c\""));
    }

    [Test]
    public void TheTag_WritesEachUsedThemesStylesheetOnce_InTheOrderTheFormsRendered()
    {
        var http = Request();
        PageAssets.Use(http, Acme);
        PageAssets.Use(http, Bare);
        PageAssets.Use(http, Calm);
        PageAssets.Use(http, Acme);
        PageAssets.Use(http, CalmAgain);

        Assert.That(
            Run(http),
            Is.EqualTo(BaseRules() + "<link href=\"/css/acme%20forms.css\" rel=\"stylesheet\">" + Link("themes/calm.css") + Script()),
            "a theme without a stylesheet writes none, and two themes of one stylesheet write it once");
    }

    [Test]
    public void TheTag_DecidesItsStylesheetsWhenThePageIsWritten_SoAFormTheLayoutRendersAfterItCounts()
    {
        var http = Request();
        var tags = http.RequestServices.GetRequiredService<PageAssets>().TagsOnce(http, null)!;
        PageAssets.Use(http, Calm);

        Assert.That(Write(tags), Is.EqualTo(BaseRules() + Link("themes/calm.css") + Script()));
    }

    [Test]
    public void AFormWithoutItsOwnTags_IsCoveredByTheTag_WhereverTheTagIs()
    {
        var http = Request();
        PageAssets.Use(http, Calm);

        Assert.That(Run(http), Does.Contain(Link("themes/calm.css")));
    }

    [Test]
    public void TheAutomaticTags_DecideWhenTheyAreWritten_SoATagWrittenBeforeThemWins()
    {
        var http = Request(new Provider("from-the-site"));
        var pageAssets = http.RequestServices.GetRequiredService<PageAssets>();
        PageAssets.Use(http, Calm);
        var automatic = pageAssets.Automatic(http, Calm);

        var tag = Run(http, "from-the-page");

        Assert.Multiple(() =>
        {
            Assert.That(tag, Is.EqualTo(BaseRules("from-the-page") + Link("themes/calm.css", "from-the-page") + Script("from-the-page")), "taking the automatic tags writes nothing yet");
            Assert.That(Write(automatic), Is.Empty);
        });
    }

    [Test]
    public void TheAutomaticTags_WrittenFirst_CarryTheProvidersNonce_AndLeaveTheTagNothing()
    {
        var http = Request(new Provider("from-the-site"));
        var automatic = http.RequestServices.GetRequiredService<PageAssets>().Automatic(http, Calm);

        Assert.Multiple(() =>
        {
            Assert.That(Write(automatic), Is.EqualTo(BaseRules("from-the-site") + Link("themes/calm.css", "from-the-site") + Script("from-the-site")));
            Assert.That(Write(automatic), Is.Empty, "once per request");
            Assert.That(Run(http), Is.Empty);
        });
    }

    [Test]
    public void EachFormsAutomaticTags_WriteOnlyWhatThePageLacks()
    {
        var provider = new Provider("from-the-site");
        var http = Request(provider);
        var pageAssets = http.RequestServices.GetRequiredService<PageAssets>();
        var first = pageAssets.Automatic(http, Bare);
        var second = pageAssets.Automatic(http, Calm);
        var third = pageAssets.Automatic(http, CalmAgain);
        var fourth = pageAssets.Automatic(http, Bare);

        Assert.Multiple(() =>
        {
            Assert.That(Write(first), Is.EqualTo(BaseRules("from-the-site") + Script("from-the-site")), "a theme without a stylesheet adds none");
            Assert.That(Write(second), Is.EqualTo(Link("themes/calm.css", "from-the-site")), "the next form adds its theme's stylesheet only");
            Assert.That(Write(third), Is.Empty, "a stylesheet written before");
            Assert.That(Write(fourth), Is.Empty);
            Assert.That(provider.Asked, Is.EqualTo(2), "the provider is asked only for tags written");
        });
    }

    [Test]
    public void ATagLaterInThePage_KeepsTheScript_AndAFormBeforeItWritesItsStylesheets()
    {
        var http = Request();
        var pageAssets = http.RequestServices.GetRequiredService<PageAssets>();
        PageAssets.Use(http, Calm);
        var automatic = pageAssets.Automatic(http, Calm);
        var tags = pageAssets.TagsOnce(http, null)!;
        PageAssets.Use(http, Acme);

        Assert.Multiple(() =>
        {
            Assert.That(Write(automatic), Is.EqualTo(BaseRules() + Link("themes/calm.css")), "the form comes first in the page, the tag claimed the script");
            Assert.That(Write(tags), Is.EqualTo("<link href=\"/css/acme%20forms.css\" rel=\"stylesheet\">" + Script()));
        });
    }

    [TestCase("/jfb/assets/themes/calm.css", "themes/calm.css")]
    [TestCase("/JFB/Assets/themes/calm.css", "themes/calm.css")]
    [TestCase("/jfb/assets/x.css", "x.css")]
    [TestCase("/jfb/assetsx/calm.css", null)]
    [TestCase("/jfb/calm.css", null)]
    [TestCase("/css/jfb/assets/calm.css", null)]
    public void AStylesheetBelowTheAssetsPath_IsOneOfThePackages(string stylesheet, string? name) =>
        Assert.That(PageAssets.AssetName(stylesheet), Is.EqualTo(name));

    [Test]
    public void APackageStylesheetTheAssemblyLacks_CannotBeWritten() =>
        Assert.That(() => Run(Used(Request(), new Theme("gone", "/jfb/assets/themes/gone.css"))), Throws.InvalidOperationException.With.Message.Contains("themes/gone.css"));

    private static HttpContext Used(HttpContext http, IFormBuilderTheme theme)
    {
        PageAssets.Use(http, theme);
        return http;
    }

    private static string Write(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    // A request whose services hold the page's assets over the test's files, and the provider when one is given.
    private DefaultHttpContext Request(IFormBuilderCspNonceProvider? provider = null)
    {
        var services = new ServiceCollection().AddSingleton(_assets).AddSingleton<PageAssets>();
        if (provider is not null)
        {
            services.AddSingleton(provider);
        }

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    // Runs the tag helper on <jfb-assets nonce="...">child</jfb-assets> as Razor does, and writes it as the page is
    // written: a tag helper that leaves its content alone gets its children written.
    private static string Run(HttpContext http, string? nonce = null, string child = "")
    {
        var helper = new FormBuilderAssetsTagHelper { ViewContext = new ViewContext { HttpContext = http }, Nonce = nonce };
        var context = new TagHelperContext("jfb-assets", [], new Dictionary<object, object>(), "jfb-assets-test");
        var output = new TagHelperOutput("jfb-assets", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        helper.Process(context, output);
        if (!output.IsContentModified)
        {
            output.Content.SetHtmlContent(child);
        }

        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private sealed class Provider(string? nonce) : IFormBuilderCspNonceProvider
    {
        public List<HttpContext> Requests { get; } = [];

        public int Asked => Requests.Count;

        public string? GetNonce(HttpContext httpContext)
        {
            Requests.Add(httpContext);
            return nonce;
        }
    }

    // A theme of the given name and stylesheet, with nothing else.
    private sealed class Theme(string name, string? stylesheet) : IFormBuilderTheme
    {
        public string Name => name;

        public string Label => name;

        public string? Stylesheet => stylesheet;

        public IReadOnlyDictionary<string, string> Classes { get; } = new Dictionary<string, string>();

        public IReadOnlyDictionary<string, string> Tokens { get; } = new Dictionary<string, string>();

        public bool DarkScheme => false;

        public string ViewFolder => name;
    }
}
