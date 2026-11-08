using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The middleware that serves the website assets (ADR-0092), without a host, over the test's own files
/// (<see cref="WebsiteAssetsTests"/>): it owns <c>/jfb/assets/</c> and nothing else, answers only the names the
/// catalogue holds, sends the copy the request accepts with its headers, caches a year only under the current
/// fingerprint or a name of content, and answers a matching <c>If-None-Match</c> with <c>304</c>. A library theme's
/// layer (ADR-0215) is served below <c>themes/library/</c> as CSS, cached a year under the theme's version only.
/// </summary>
[TestFixture]
public sealed class WebsiteAssetsMiddlewareTests
{
    private const string Runtime = "/jfb/assets/runtime/jfb-runtime.js";

    private WebsiteAssets _assets = null!;
    private WebsiteAssetsMiddleware _middleware = null!;

    [SetUp]
    public void CreateTheMiddleware()
    {
        _assets = WebsiteAssetsTests.Assets();
        var library = Substitute.For<IThemeRepository>();
        library.GetByAliasAsync("autumn", Arg.Any<CancellationToken>()).Returns(Task.FromResult<StoredTheme?>(
            new StoredTheme(1, Guid.NewGuid(), "autumn", "Autumn", "default", "{\"light\":{\"--jfb-radius\":\"6px\"}}", null, 3, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)));
        var registry = new FormBuilderThemes([new DefaultTheme(), new UnstyledTheme(), new Bootstrap5Theme()], NullLogger<FormBuilderThemes>.Instance);
        _middleware = new WebsiteAssetsMiddleware(_assets, new LibraryThemes(library, registry, AppCaches.NoCache));
    }

    private string Fingerprint => _assets.Find("runtime/jfb-runtime.js")!.Fingerprint;

    [TestCase("/")]
    [TestCase("/jfb")]
    [TestCase("/jfb/assetsx/runtime/jfb-runtime.js")]
    [TestCase("/jfb/asset/runtime/jfb-runtime.js")]
    [TestCase("/jfb/api/v1/forms/contact")]
    [TestCase("/assets/runtime/jfb-runtime.js")]
    [TestCase("/umbraco/jfb/assets/runtime/jfb-runtime.js")]
    public async Task ARequestOutsideTheAssets_PassesUntouched(string path)
    {
        var (http, passed) = await SendAsync("GET", path);

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.True);
            Assert.That(http.Response.Headers, Is.Empty);
        });
    }

    [TestCase("/jfb/assets")]
    [TestCase("/jfb/assets/")]
    [TestCase("/jfb/assets/runtime/nothing.js")]
    [TestCase("/jfb/assets/Runtime/jfb-runtime.js")]
    [TestCase("/jfb/assets/runtime/JFB-runtime.js")]
    [TestCase("/jfb/assets/runtime/jfb-runtime.js.br")]
    [TestCase("/jfb/assets/runtime/jfb-runtime.js.gz")]
    [TestCase("/jfb/assets/runtime%2Fjfb-runtime.js")]
    [TestCase("/jfb/assets/runtime/../runtime/jfb-runtime.js")]
    [TestCase("/jfb/assets/../../appsettings.json")]
    [TestCase("/jfb/assets//runtime/jfb-runtime.js")]
    public async Task AnUnknownName_Answers404_WithoutABody_AndIsNotPassedOn(string path)
    {
        var (http, passed) = await SendAsync("GET", path, "br");

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(http.Response.StatusCode, Is.EqualTo(404));
            Assert.That(Body(http), Is.Empty);
            Assert.That(http.Response.Headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
            Assert.That(http.Response.Headers.CacheControl.ToString(), Is.Empty);
            Assert.That(http.Response.Headers.AccessControlAllowOrigin.ToString(), Is.Empty);
        });
    }

    [TestCase("/JFB/Assets/runtime/jfb-runtime.js")]
    [TestCase("/jfb/ASSETS/runtime/jfb-runtime.js")]
    public async Task TheBasePath_IsOwnedInAnyCasing_AndTheNameStaysExact(string path)
    {
        var (http, passed) = await SendAsync("GET", path);

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(http.Response.StatusCode, Is.EqualTo(200));
        });
    }

    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    [TestCase("OPTIONS")]
    public async Task AnotherMethod_Answers405_WithTheMethodsAllowed(string method)
    {
        var (http, passed) = await SendAsync(method, Runtime, "br");
        var (unknown, _) = await SendAsync(method, "/jfb/assets/runtime/nothing.js");

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(http.Response.StatusCode, Is.EqualTo(405));
            Assert.That(http.Response.Headers.Allow.ToString(), Is.EqualTo("GET, HEAD"));
            Assert.That(http.Response.Headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
            Assert.That(Body(http), Is.Empty);
            Assert.That(unknown.Response.StatusCode, Is.EqualTo(404), "a name that is not there is not there for any method");
        });
    }

    [TestCase("br", "br", new byte[] { 1, 2 })]
    [TestCase("gzip", "gzip", new byte[] { 3, 4, 5 })]
    [TestCase(null, null, null)]
    public async Task AnAsset_GoesOutAsTheCopyTheRequestAccepts_WithItsHeaders(string? accepted, string? coding, byte[]? bytes)
    {
        var (http, passed) = await SendAsync("GET", Runtime, accepted);
        var expected = bytes ?? _assets.Find("runtime/jfb-runtime.js")!.Original;
        var headers = http.Response.Headers;

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(http.Response.StatusCode, Is.EqualTo(200));
            Assert.That(Body(http), Is.EqualTo(expected));
            Assert.That(http.Response.ContentLength, Is.EqualTo(expected.Length));
            Assert.That(http.Response.ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
            Assert.That(headers.ContentEncoding.ToString(), Is.EqualTo(coding ?? string.Empty));
            Assert.That(headers.Vary.ToString(), Is.EqualTo("Accept-Encoding"));
            Assert.That(headers.ETag.ToString(), Is.EqualTo(coding is null ? $"\"{Fingerprint}\"" : $"\"{Fingerprint}-{coding}\""));
            Assert.That(headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
            Assert.That(headers.AccessControlAllowOrigin.ToString(), Is.EqualTo("*"));
            Assert.That(headers["Cross-Origin-Resource-Policy"].ToString(), Is.EqualTo("cross-origin"));
            Assert.That(headers.AccessControlAllowCredentials.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task Head_SendsTheHeadersOfGet_WithoutTheBody()
    {
        var (http, _) = await SendAsync("HEAD", Runtime, "gzip");

        Assert.Multiple(() =>
        {
            Assert.That(http.Response.StatusCode, Is.EqualTo(200));
            Assert.That(Body(http), Is.Empty);
            Assert.That(http.Response.ContentLength, Is.EqualTo(3));
            Assert.That(http.Response.Headers.ContentEncoding.ToString(), Is.EqualTo("gzip"));
            Assert.That(http.Response.ContentType, Is.EqualTo("text/javascript; charset=utf-8"));
        });
    }

    [Test]
    public async Task TheCurrentFingerprint_IsCachedForAYear_AndAnythingElseIsRevalidated()
    {
        const string Year = "public, max-age=31536000, immutable";
        async Task<string> CachingOf(string path, string query = "") =>
            (await SendAsync("GET", path, query: query)).Http.Response.Headers.CacheControl.ToString();

        var current = await CachingOf(Runtime, $"?v={Fingerprint}");
        var otherCase = await CachingOf(Runtime, $"?v={Fingerprint.ToUpperInvariant()}");
        var old = await CachingOf(Runtime, "?v=000000000000");
        var twice = await CachingOf(Runtime, $"?v={Fingerprint}&v={Fingerprint}");
        var none = await CachingOf(Runtime);
        var empty = await CachingOf(Runtime, "?v=");
        var chunk = await CachingOf("/jfb/assets/chunks/rules-1a2b3c.js");
        var chunkWithOld = await CachingOf("/jfb/assets/chunks/rules-1a2b3c.js", "?v=000000000000");

        Assert.Multiple(() =>
        {
            Assert.That(current, Is.EqualTo(Year));
            Assert.That(otherCase, Is.EqualTo("no-cache"), "the fingerprint is compared exactly");
            Assert.That(old, Is.EqualTo("no-cache"), "a page rendered before an upgrade gets the new file and keeps asking");
            Assert.That(twice, Is.EqualTo("no-cache"));
            Assert.That(none, Is.EqualTo("no-cache"));
            Assert.That(empty, Is.EqualTo("no-cache"));
            Assert.That(chunk, Is.EqualTo(Year), "a name of content is its own fingerprint");
            Assert.That(chunkWithOld, Is.EqualTo(Year));
        });
    }

    [TestCase("br", "\"{0}-br\"", 304)]
    [TestCase("br", "W/\"{0}-br\"", 304)]
    [TestCase("br", "\"x\", \"{0}-br\"", 304)]
    [TestCase("br", "*", 304)]
    [TestCase("gzip", "\"{0}-br\"", 200)]
    [TestCase(null, "\"{0}-gzip\"", 200)]
    [TestCase(null, "\"{0}\"", 304)]
    [TestCase("br", "\"000000000000-br\"", 200)]
    public async Task AMatchingIfNoneMatch_Answers304_WithTheValidatorsAndWithoutTheBody(string? accepted, string tags, int status)
    {
        var (http, _) = await SendAsync("GET", Runtime, accepted, $"?v={Fingerprint}", string.Format(System.Globalization.CultureInfo.InvariantCulture, tags, Fingerprint));
        var headers = http.Response.Headers;

        Assert.Multiple(() =>
        {
            Assert.That(http.Response.StatusCode, Is.EqualTo(status));
            Assert.That(Body(http), status == 304 ? Is.Empty : Is.Not.Empty);
            Assert.That(headers.ETag.ToString(), Is.Not.Empty);
            Assert.That(headers.CacheControl.ToString(), Is.EqualTo("public, max-age=31536000, immutable"));
            Assert.That(headers.Vary.ToString(), Is.EqualTo("Accept-Encoding"));
        });

        if (status == 304)
        {
            Assert.Multiple(() =>
            {
                Assert.That(http.Response.ContentLength, Is.Null);
                Assert.That(headers.ContentEncoding.ToString(), Is.Empty);
                Assert.That(http.Response.ContentType, Is.Null);
            });
        }
    }

    [Test]
    public async Task AnAssetWithoutCopies_GoesOutAsItIs_WhateverTheRequestAccepts()
    {
        var (http, _) = await SendAsync("GET", "/jfb/assets/themes/calm.css", "br, gzip");

        Assert.Multiple(() =>
        {
            Assert.That(Body(http), Is.EqualTo("abc"u8.ToArray()));
            Assert.That(http.Response.Headers.ContentEncoding.ToString(), Is.Empty);
            Assert.That(http.Response.ContentType, Is.EqualTo("text/css; charset=utf-8"));
            Assert.That(http.Response.Headers.Vary.ToString(), Is.EqualTo("Accept-Encoding"));
        });
    }

    [Test]
    public async Task ALibraryThemesLayer_IsServedAsCss_CachedAYearUnderItsVersionOnly()
    {
        var (current, passed) = await SendAsync("GET", "/jfb/assets/themes/library/autumn.css", query: "?v=3");
        var (older, _) = await SendAsync("GET", "/jfb/assets/themes/library/autumn.css", query: "?v=2");
        var tag = current.Response.Headers.ETag.ToString();
        var (again, _) = await SendAsync("GET", "/jfb/assets/themes/library/autumn.css", query: "?v=3", ifNoneMatch: tag);
        var (unknown, _) = await SendAsync("GET", "/jfb/assets/themes/library/winter.css", query: "?v=1");
        var (posted, _) = await SendAsync("POST", "/jfb/assets/themes/library/autumn.css");

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(current.Response.StatusCode, Is.EqualTo(200));
            Assert.That(current.Response.ContentType, Is.EqualTo("text/css; charset=utf-8"));
            Assert.That(System.Text.Encoding.UTF8.GetString(Body(current)), Does.Contain(".jfb-form.jfb-theme-autumn {\n  --jfb-radius: 6px;\n}"));
            Assert.That(current.Response.Headers.CacheControl.ToString(), Is.EqualTo("public, max-age=31536000, immutable"));
            Assert.That(current.Response.Headers.AccessControlAllowOrigin.ToString(), Is.EqualTo("*"));
            Assert.That(current.Response.Headers.XContentTypeOptions.ToString(), Is.EqualTo("nosniff"));
            Assert.That(tag, Does.Match("^\"[0-9a-f]{32}\"$"));
            Assert.That(older.Response.StatusCode, Is.EqualTo(200), "an older version is answered with what the theme holds now");
            Assert.That(older.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-cache"));
            Assert.That(again.Response.StatusCode, Is.EqualTo(304));
            Assert.That(unknown.Response.StatusCode, Is.EqualTo(404));
            Assert.That(posted.Response.StatusCode, Is.EqualTo(405));
        });
    }

    private static byte[] Body(HttpContext http) => ((MemoryStream)http.Response.Body).ToArray();

    // Sends a request through the middleware, and says whether it was passed on.
    private async Task<(HttpContext Http, bool Passed)> SendAsync(string method, string path, string? acceptEncoding = null, string query = "", string? ifNoneMatch = null)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = new PathString(path);
        http.Request.QueryString = new QueryString(query);
        http.Response.Body = new MemoryStream();
        if (acceptEncoding is not null)
        {
            http.Request.Headers.AcceptEncoding = acceptEncoding;
        }

        if (ifNoneMatch is not null)
        {
            http.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

        var passed = false;
        await _middleware.InvokeAsync(http, _ =>
        {
            passed = true;
            return Task.CompletedTask;
        });
        return (http, passed);
    }
}
