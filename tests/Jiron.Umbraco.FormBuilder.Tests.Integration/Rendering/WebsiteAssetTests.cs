using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Web.Common.ApplicationBuilder;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The website assets on a real site (ADR-0092): every file the client build wrote is served from the Web assembly
/// before Umbraco's routing, as the copy the request accepts with its headers, also while the migration plan has not
/// run; an unknown, differently cased or traversing name answers <c>404</c> without a page; and <c>&lt;jfb-assets&gt;</c>
/// with the site's services writes the address that is served, once per request, with the site's nonce.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class WebsiteAssetTests : ConfiguredTestServerTestBase
{
    private const string Year = "public, max-age=31536000, immutable";

    // The key of the request item the test's nonce provider reads.
    private const string NonceItem = "jfb-test-nonce";

    [Test]
    public async Task EveryBuiltAsset_IsServedWithBothItsCopies_WhileThePlanHasNotRun()
    {
        var built = BuiltFiles();
        var originals = built.Keys.Where(name => !name.EndsWith(".br", StringComparison.Ordinal) && !name.EndsWith(".gz", StringComparison.Ordinal)).ToList();
        var assets = GetRequiredService<WebsiteAssets>();
        var pending = await GetRequiredService<IFormBuilderMigrationState>().IsPendingAsync();
        var withoutCopies = originals.Where(name => !built.ContainsKey(name + ".br") || !built.ContainsKey(name + ".gz")).ToList();
        Assert.That(withoutCopies, Is.Empty, "every built file has its Brotli and gzip copies");
        var answers = new List<string>();

        foreach (var name in originals)
        {
            var asset = assets.Find(name);
            Assert.That(asset, Is.Not.Null, $"{name} is in the assembly");
            foreach (var (coding, bytes) in new[] { ("br", built[name + ".br"]), ("gzip", built[name + ".gz"]), ((string?)null, built[name]) })
            {
                using var answer = await GetAsync($"/jfb/assets/{name}?v={asset!.Fingerprint}", coding);
                var body = await answer.Content.ReadAsByteArrayAsync();
                answers.Add($"{name} as {coding ?? "none"}: {Describe(answer, asset, coding, body.SequenceEqual(bytes) ? body.Length : -1)}");
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(pending, Is.True, "the test host has not run the plan");
            Assert.That(originals, Does.Contain("runtime/jfb-runtime.js").And.Contain("element/jfb-element.js").And.Contain("themes/base.css").And.Contain("themes/default.css"), "the client was built, the default theme's stylesheet with it");
            Assert.That(assets.All.Select(asset => asset.Name), Is.EquivalentTo(originals), "the assembly holds exactly the built originals");
            Assert.That(originals.Where(name => assets.Find(name)?.Fingerprint != Convert.ToHexStringLower(SHA256.HashData(built[name]))[..12]), Is.Empty, "each fingerprint is of its original");
            Assert.That(originals.Where(name => !Decompressed(built, name)), Is.Empty, "each copy holds its original");
            Assert.That(answers, Has.Count.EqualTo(originals.Count * 3));
            Assert.That(answers, Is.All.EndWith(": 200, coded as asked, same bytes, typed, nosniff, CORS, cross-origin, Vary, a year"), string.Join("\n", answers));
        });
    }

    [Test]
    public async Task TheRuntimeWithoutTheFingerprint_IsRevalidated()
    {
        using var answer = await GetAsync("/jfb/assets/runtime/jfb-runtime.js", "gzip, br");

        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(answer.Headers.CacheControl?.ToString(), Is.EqualTo("no-cache"));
            Assert.That(answer.Content.Headers.ContentEncoding, Is.EqualTo(new[] { "br" }));
        });
    }

    [TestCase("/jfb/assets/runtime/nothing.js")]
    [TestCase("/jfb/assets/Runtime/jfb-runtime.js")]
    [TestCase("/jfb/assets/runtime/JFB-runtime.js")]
    [TestCase("/jfb/assets/runtime/jfb-runtime.js.br")]
    [TestCase("/jfb/assets/runtime%2Fjfb-runtime.js")]
    [TestCase("/jfb/assets/runtime%2fjfb-runtime.js")]
    [TestCase("/jfb/assets/")]
    public async Task AnUnknownOrDifferentlyCasedName_Answers404_WithoutAPage(string path)
    {
        using var answer = await GetAsync(path, "br");

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await answer.Content.ReadAsByteArrayAsync(), Is.Empty, "Umbraco renders no page for it");
            Assert.That(answer.Headers.GetValues("X-Content-Type-Options"), Is.EqualTo(new[] { "nosniff" }));
        });
    }

    [TestCase("/jfb/assets/runtime/../runtime/jfb-runtime.js")]
    [TestCase("/jfb/assets/../../appsettings.json")]
    [TestCase("/jfb/assets/../../Jiron.Umbraco.FormBuilder.Web.dll")]
    [TestCase("/jfb/assets/./runtime/jfb-runtime.js")]
    public async Task ATraversingName_Answers404_AsTheServerHandsItOn(string path)
    {
        // A client normalizes such a path before sending it, so the test hands it to the server as it is.
        var context = await Factory.Server.SendAsync(http =>
        {
            http.Request.Method = HttpMethods.Get;
            http.Request.Path = new PathString(path);
        });

        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(context.Response.StatusCode, Is.EqualTo(404));
            Assert.That(context.Response.Headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
            Assert.That(body, Is.Empty, "Umbraco renders no page for it");
        });
    }

    [Test]
    public async Task TheBasePathInAnotherCasing_ServesTheSameFile()
    {
        using var answer = await GetAsync("/JFB/Assets/runtime/jfb-runtime.js", null);

        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task TheAssetsTag_WritesTheServedAddresses_OncePerRequest_WithTheSitesNonce()
    {
        var page = Request("site-nonce-1");
        var first = Run(page, null);
        var second = Run(page, "page-nonce");
        var other = Run(Request(string.Empty), null);
        using var script = await GetAsync(Attribute(first, "src"), "br");
        using var stylesheet = await GetAsync(Attribute(first, "href"), "br");

        Assert.Multiple(() =>
        {
            Assert.That(first, Does.StartWith("<link href=\"/jfb/assets/themes/base.css?v=").And.Contain("\" nonce=\"site-nonce-1\" rel=\"stylesheet\"><script nonce=\"site-nonce-1\" src=\"/jfb/assets/runtime/jfb-runtime.js?v=").And.EndWith("\" type=\"module\"></script>"));
            Assert.That(second, Is.Empty, "named twice, written once");
            Assert.That(other, Does.Not.Contain("nonce"), "the site gave this request none");
            Assert.That((script.StatusCode, script.Headers.CacheControl?.ToString()), Is.EqualTo((HttpStatusCode.OK, Year)));
            Assert.That((stylesheet.StatusCode, stylesheet.Headers.CacheControl?.ToString(), stylesheet.Content.Headers.ContentType?.ToString()), Is.EqualTo((HttpStatusCode.OK, Year, "text/css; charset=utf-8")));
        });
    }

    [Test]
    public void TheComposer_AddsTheMiddlewareOnce()
    {
        var filters = GetRequiredService<IOptions<UmbracoPipelineOptions>>().Value.PipelineFilters;

        Assert.That(filters.Count(filter => filter.Name == FormBuilderRenderingServices.AssetsFilterName), Is.EqualTo(1));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<IFormBuilderCspNonceProvider, ItemNonce>();
    }

    // The files the client build wrote, by their names below /jfb/assets/.
    private static Dictionary<string, byte[]> BuiltFiles()
    {
        var root = RepositoryRoot.Path(typeof(WebsiteAssetTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Web", "WebsiteAssets");
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(root, file).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
    }

    // Whether the Brotli and gzip copies of a file decompress to it.
    private static bool Decompressed(Dictionary<string, byte[]> built, string name)
    {
        static byte[] Read(Stream compressed)
        {
            using var copy = new MemoryStream();
            compressed.CopyTo(copy);
            return copy.ToArray();
        }

        using var brotli = new BrotliStream(new MemoryStream(built[name + ".br"]), CompressionMode.Decompress);
        using var gzip = new GZipStream(new MemoryStream(built[name + ".gz"]), CompressionMode.Decompress);
        return Read(brotli).SequenceEqual(built[name]) && Read(gzip).SequenceEqual(built[name]);
    }

    // One answer as a line: the status, whether the coding is the one asked for, whether the bytes are the file's (their
    // length, or -1 when they are not), its type and length, and the headers every answer carries.
    private static string Describe(HttpResponseMessage answer, WebsiteAsset asset, string? coding, int length)
    {
        var content = answer.Content.Headers;
        var sent = content.ContentEncoding.Count == 0 ? null : string.Join(",", content.ContentEncoding);
        var parts = new List<string>
        {
            ((int)answer.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture),
            sent == coding ? "coded as asked" : $"coded {sent ?? "not at all"}",
            length >= 0 ? "same bytes" : "other bytes",
            content.ContentType?.ToString() == asset.ContentType && content.ContentLength == length ? "typed" : $"typed {content.ContentType} of {content.ContentLength}",
        };

        if (answer.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Single() == "nosniff")
        {
            parts.Add("nosniff");
        }

        if (answer.Headers.TryGetValues("Access-Control-Allow-Origin", out var origin) && origin.Single() == "*")
        {
            parts.Add("CORS");
        }

        if (answer.Headers.TryGetValues("Cross-Origin-Resource-Policy", out var policy) && policy.Single() == "cross-origin")
        {
            parts.Add("cross-origin");
        }

        if (answer.Headers.Vary.SequenceEqual(["Accept-Encoding"]))
        {
            parts.Add("Vary");
        }

        parts.Add(answer.Headers.CacheControl?.ToString() == Year ? "a year" : "revalidated");
        return string.Join(", ", parts);
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string? acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptEncoding is not null)
        {
            request.Headers.AcceptEncoding.ParseAdd(acceptEncoding);
        }

        return await Client.SendAsync(request);
    }

    // A request with the site's services, whose nonce the test's provider gives.
    private DefaultHttpContext Request(string nonce)
    {
        var http = new DefaultHttpContext { RequestServices = Services };
        http.Items[NonceItem] = nonce;
        return http;
    }

    // The value of the first attribute of this name that the tags hold.
    private static string Attribute(string tags, string name)
    {
        var start = tags.IndexOf(name + "=\"", StringComparison.Ordinal) + name.Length + 2;
        return tags[start..tags.IndexOf('"', start)];
    }

    // Runs <jfb-assets> as Razor does, and writes what it leaves.
    private static string Run(HttpContext http, string? nonce)
    {
        var helper = new FormBuilderAssetsTagHelper { ViewContext = new ViewContext { HttpContext = http }, Nonce = nonce };
        var output = new TagHelperOutput("jfb-assets", [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        helper.Process(new TagHelperContext("jfb-assets", [], new Dictionary<object, object>(), "jfb-assets-test"), output);
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }

    private sealed class ItemNonce : IFormBuilderCspNonceProvider
    {
        public string? GetNonce(HttpContext httpContext) => httpContext.Items[NonceItem] as string;
    }
}
