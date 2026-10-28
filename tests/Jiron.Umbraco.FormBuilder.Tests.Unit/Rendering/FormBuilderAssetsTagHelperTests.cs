using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// <c>&lt;jfb-assets&gt;</c> (ADR-0004, ADR-0092), run as Razor runs it, over the test's own files
/// (<see cref="WebsiteAssetsTests"/>): the module script under the fingerprint and the path base, written once per
/// request however often the page names it, without the element itself, and with the nonce of the attribute, else of
/// the site's provider, else none; and the tags a form adds by itself, which are written when the page writes them.
/// </summary>
[TestFixture]
public sealed class FormBuilderAssetsTagHelperTests
{
    private WebsiteAssets _assets = null!;

    [SetUp]
    public void CreateTheAssets() => _assets = WebsiteAssetsTests.Assets();

    private string Src(string pathBase = "") =>
        $"{pathBase}/jfb/assets/runtime/jfb-runtime.js?v={_assets.Find("runtime/jfb-runtime.js")!.Fingerprint}";

    [Test]
    public void TheTag_WritesTheModuleScript_WithoutTheElementItself()
    {
        var http = Request();

        Assert.That(Run(http), Is.EqualTo($"<script src=\"{Src()}\" type=\"module\"></script>"));
    }

    [Test]
    public void ThePathBase_PrecedesTheAddress()
    {
        var http = Request();
        http.Request.PathBase = "/site";

        Assert.That(Run(http), Is.EqualTo($"<script src=\"{Src("/site")}\" type=\"module\"></script>"));
    }

    [Test]
    public void ATagNamedTwice_WritesOnce_AndTheSecondLeavesNothing()
    {
        var http = Request(new Provider("from-the-site"));

        var first = Run(http, "first");
        var second = Run(http, "second");
        var third = Run(http);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo($"<script nonce=\"first\" src=\"{Src()}\" type=\"module\"></script>"));
            Assert.That(second, Is.Empty);
            Assert.That(third, Is.Empty);
            Assert.That(PageAssets.AreWritten(http), Is.True);
        });
    }

    [Test]
    public void EachRequest_GetsItsOwnTags()
    {
        var one = Request();
        var other = Request();

        Assert.Multiple(() =>
        {
            Assert.That(PageAssets.AreWritten(one), Is.False);
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
            Assert.That(Run(http, "from-the-page"), Does.Contain("nonce=\"from-the-page\""));
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
            Assert.That(Run(http, attribute), Is.EqualTo($"<script nonce=\"from-the-site\" src=\"{Src()}\" type=\"module\"></script>"));
            Assert.That(provider.Requests, Is.EqualTo(new[] { http }));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    public void WithNeitherNonce_TheTagHasNone(string? given)
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
            Assert.That(first, Is.EqualTo($"<script src=\"{Src()}\" type=\"module\"></script>"));
            Assert.That(second, Is.Empty);
        });
    }

    [Test]
    public void TheNonce_IsWrittenAsAnAttributeValue()
    {
        var http = Request();

        Assert.That(Run(http, "a\"b<c"), Does.Contain("nonce=\"a&quot;b&lt;c\""));
    }

    [Test]
    public void TheAutomaticTags_DecideWhenTheyAreWritten_SoATagWrittenBeforeThemWins()
    {
        var http = Request(new Provider("from-the-site"));
        var automatic = http.RequestServices.GetRequiredService<PageAssets>().Automatic(http);

        var tag = Run(http, "from-the-page");

        Assert.Multiple(() =>
        {
            Assert.That(tag, Does.Contain("nonce=\"from-the-page\""), "taking the automatic tags writes nothing yet");
            Assert.That(Write(automatic), Is.Empty);
        });
    }

    [Test]
    public void TheAutomaticTags_WrittenFirst_CarryTheProvidersNonce_AndLeaveTheTagNothing()
    {
        var http = Request(new Provider("from-the-site"));
        var automatic = http.RequestServices.GetRequiredService<PageAssets>().Automatic(http);

        Assert.Multiple(() =>
        {
            Assert.That(Write(automatic), Is.EqualTo($"<script nonce=\"from-the-site\" src=\"{Src()}\" type=\"module\"></script>"));
            Assert.That(Write(automatic), Is.Empty, "once per request");
            Assert.That(Run(http), Is.Empty);
        });
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

    // Runs the tag helper on <jfb-assets nonce="...">child</jfb-assets> as Razor does: a tag helper that leaves its
    // content alone gets its children written.
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
}
