using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The catalogue of the website assets (ADR-0092), over files of the test's own: only the originals are names, each
/// with its copies, its media type and its fingerprint; the copy each <c>Accept-Encoding</c> gets; and the address with
/// the fingerprint.
/// </summary>
[TestFixture]
public sealed class WebsiteAssetsTests
{
    [Test]
    public void TheOriginals_AreTheNames_WithTheirCopies_AndTheCopiesAreNone()
    {
        var assets = Assets();

        Assert.Multiple(() =>
        {
            Assert.That(assets.All.Select(asset => asset.Name), Is.EquivalentTo(new[] { "runtime/jfb-runtime.js", "chunks/rules-1a2b3c.js", "themes/calm.css", "notes.br", "fonts/a/assets/b.woff2" }));
            Assert.That(assets.Find("runtime/jfb-runtime.js")!.Brotli, Is.EqualTo(new byte[] { 1, 2 }));
            Assert.That(assets.Find("runtime/jfb-runtime.js")!.Gzip, Is.EqualTo(new byte[] { 3, 4, 5 }));
            Assert.That(assets.Find("chunks/rules-1a2b3c.js")!.Brotli, Is.Null, "the build made no Brotli copy");
            Assert.That(assets.Find("themes/calm.css")!.Gzip, Is.Null);
            Assert.That(assets.Find("notes.br")!.Original, Is.EqualTo(new byte[] { 9 }), "a name ending in .br without its original is an original");
            Assert.That(assets.Find("runtime/jfb-runtime.js.br"), Is.Null, "a copy");
            Assert.That(assets.Find("runtime/jfb-runtime.js.gz"), Is.Null, "a copy");
        });
    }

    [TestCase("Runtime/jfb-runtime.js")]
    [TestCase("runtime/JFB-runtime.js")]
    [TestCase("runtime/jfb-runtime.JS")]
    [TestCase("runtime%2Fjfb-runtime.js")]
    [TestCase("runtime/../runtime/jfb-runtime.js")]
    [TestCase("./runtime/jfb-runtime.js")]
    [TestCase("runtime//jfb-runtime.js")]
    [TestCase("runtime\\jfb-runtime.js")]
    [TestCase("/runtime/jfb-runtime.js")]
    [TestCase("runtime/jfb-runtime.js/")]
    [TestCase("runtime/jfb-runtime.js ")]
    [TestCase("runtime/jfb-runtime")]
    [TestCase("runtime")]
    [TestCase("")]
    public void ANameOfAnotherSpelling_FindsNothing(string name) =>
        Assert.That(Assets().Find(name), Is.Null);

    [Test]
    public void EachAsset_HasItsMediaType_AndTheFingerprintOfItsOriginal()
    {
        var assets = Assets();

        Assert.Multiple(() =>
        {
            Assert.That(assets.Find("themes/calm.css")!.Fingerprint, Is.EqualTo("ba7816bf8f01"), "the SHA-256 of abc begins so");
            Assert.That(assets.Find("runtime/jfb-runtime.js")!.Fingerprint, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("export const runtime = 1;")))[..12]), "of the original, not of a copy");
            Assert.That(assets.Find("runtime/jfb-runtime.js")!.ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
            Assert.That(assets.Find("themes/calm.css")!.ContentType, Is.EqualTo("text/css; charset=utf-8"));
            Assert.That(assets.Find("notes.br")!.ContentType, Is.EqualTo("application/octet-stream"));
            Assert.That(assets.Find("fonts/a/assets/b.woff2")!.ContentType, Is.EqualTo("font/woff2"));
        });
    }

    [TestCase("a.js", "text/javascript; charset=utf-8")]
    [TestCase("a.mjs", "text/javascript; charset=utf-8")]
    [TestCase("a.css", "text/css; charset=utf-8")]
    [TestCase("a.json", "application/json; charset=utf-8")]
    [TestCase("a.svg", "image/svg+xml")]
    [TestCase("a.png", "image/png")]
    [TestCase("a.webp", "image/webp")]
    [TestCase("a.woff2", "font/woff2")]
    [TestCase("a.JS", "application/octet-stream")]
    [TestCase("a.map", "application/octet-stream")]
    [TestCase("a", "application/octet-stream")]
    public void TheMediaType_FollowsTheExtension_ExactlyAsWritten(string name, string type) =>
        Assert.That(WebsiteAssets.ContentTypeOf(name), Is.EqualTo(type));

    [TestCase("chunks/rules-1a2b3c.js", true)]
    [TestCase("fonts/a/assets/b.woff2", true)]
    [TestCase("runtime/jfb-runtime.js", false)]
    [TestCase("themes/calm.css", false)]
    [TestCase("notes.br", false)]
    public void ANameInAChunksOrAssetsFolder_IsNamedByItsContent(string name, bool named) =>
        Assert.That(Assets().Find(name)!.NamedByContent, Is.EqualTo(named));

    [Test]
    public void AFileNamedLikeTheFolders_IsNotNamedByItsContent()
    {
        var assets = new WebsiteAssets(new Dictionary<string, byte[]> { ["runtime/chunks"] = [1], ["chunksx/a.js"] = [2], ["assets"] = [3] });

        Assert.That(assets.All.Where(asset => asset.NamedByContent), Is.Empty);
    }

    [TestCase("gzip, deflate, br", "br")]
    [TestCase("BR", "br")]
    [TestCase("br;q=0.001", "br")]
    [TestCase("br;q=0, gzip", "gzip")]
    [TestCase("br;q=0, *", "gzip")]
    [TestCase("gzip;q=0, *", "br")]
    [TestCase("*", "br")]
    [TestCase("*;q=0", null)]
    [TestCase("gzip;q=0, br;q=0", null)]
    [TestCase("deflate", null)]
    [TestCase("identity", null)]
    public void TheCopy_IsBrotli_ThenGzip_ThenTheOriginal_AsTheRequestAccepts(string accepted, string? coding)
    {
        var asset = Assets().Find("runtime/jfb-runtime.js")!;

        var copy = WebsiteAssets.CopyFor(asset, StringWithQualityHeaderValue.ParseList([accepted]));

        Assert.Multiple(() =>
        {
            Assert.That(copy.Coding, Is.EqualTo(coding));
            Assert.That(copy.Bytes, Is.EqualTo(coding switch { "br" => asset.Brotli, "gzip" => asset.Gzip, _ => asset.Original }));
            Assert.That(copy.ETag, Is.EqualTo(coding is null ? $"\"{asset.Fingerprint}\"" : $"\"{asset.Fingerprint}-{coding}\""));
        });
    }

    [Test]
    public void ARequestWithoutAcceptEncoding_GetsTheOriginal()
    {
        var asset = Assets().Find("runtime/jfb-runtime.js")!;

        Assert.That(WebsiteAssets.CopyFor(asset, []).Coding, Is.Null);
    }

    [TestCase("br, gzip", "gzip")]
    [TestCase("br", null)]
    public void ACopyTheBuildDidNotMake_IsNeverChosen(string accepted, string? coding)
    {
        var asset = Assets().Find("chunks/rules-1a2b3c.js")!;

        Assert.That(WebsiteAssets.CopyFor(asset, StringWithQualityHeaderValue.ParseList([accepted])).Coding, Is.EqualTo(coding));
    }

    [Test]
    public void TheAddress_IsBelowThePathBase_WithTheFingerprint()
    {
        var assets = Assets();
        var fingerprint = assets.Find("runtime/jfb-runtime.js")!.Fingerprint;

        Assert.Multiple(() =>
        {
            Assert.That(assets.Url(PathString.Empty, "runtime/jfb-runtime.js"), Is.EqualTo($"/jfb/assets/runtime/jfb-runtime.js?v={fingerprint}"));
            Assert.That(assets.Url(new PathString("/site"), "runtime/jfb-runtime.js"), Is.EqualTo($"/site/jfb/assets/runtime/jfb-runtime.js?v={fingerprint}"));
        });
    }

    [Test]
    public void TheAddressOfAMissingAsset_SaysToBuildTheClient()
    {
        var assets = new WebsiteAssets(new Dictionary<string, byte[]>());

        Assert.That(() => assets.Url(PathString.Empty, WebsiteAssets.Runtime), Throws.InvalidOperationException.With.Message.Contains("runtime/jfb-runtime.js").And.Message.Contains("build the client"));
    }

    [Test]
    public void OnlyTheResourcesBelowTheAssetsPrefix_AreRead()
    {
        var other = typeof(global::Umbraco.Cms.Core.Constants).Assembly;

        Assert.Multiple(() =>
        {
            Assert.That(other.GetManifestResourceNames(), Is.Not.Empty, "an assembly with resources of its own");
            Assert.That(WebsiteAssets.Embedded(other), Is.Empty);
        });
    }

    // The test's files: an original with both copies, one with only gzip, one with none, a name that ends like a copy and
    // a font in an assets folder.
    internal static WebsiteAssets Assets() => new(new Dictionary<string, byte[]>
    {
        ["runtime/jfb-runtime.js"] = Encoding.UTF8.GetBytes("export const runtime = 1;"),
        ["runtime/jfb-runtime.js.br"] = [1, 2],
        ["runtime/jfb-runtime.js.gz"] = [3, 4, 5],
        ["chunks/rules-1a2b3c.js"] = Encoding.UTF8.GetBytes("export const rules = 2;"),
        ["chunks/rules-1a2b3c.js.gz"] = [6, 7],
        ["themes/calm.css"] = Encoding.UTF8.GetBytes("abc"),
        ["notes.br"] = [9],
        ["fonts/a/assets/b.woff2"] = [8],
    });
}
