using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// How a response is kept out of every cache (ADR-0004, ADR-0104): <c>no-store</c> at once and again as the response
/// starts, the output cache's storage switched off, also once the response has started; and the vary rule for
/// <c>jfb-sent</c>, which acts on a request that carries it in any form and on no other.
/// </summary>
[TestFixture]
public sealed class PageCachingTests
{
    [Test]
    public async Task NoStore_MarksTheResponse_AgainAsItStarts_AndSwitchesTheOutputCachesStorageOff()
    {
        var (http, response, cache) = Request();

        PageCaching.NoStore(http);
        var before = http.Response.Headers.CacheControl.ToString();
        http.Response.Headers.CacheControl = "public, max-age=600";
        http.Response.Headers.Expires = "Thu, 01 Jan 2099 00:00:00 GMT";
        await response.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("no-store"));
            Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"), "after anything else set it");
            Assert.That(http.Response.Headers.ContainsKey("Expires"), Is.False);
            Assert.That(cache.Context.AllowCacheStorage, Is.False);
        });
    }

    [Test]
    public void NoStore_OnceTheResponseHasStarted_StillSwitchesTheOutputCachesStorageOff_AndLeavesTheHeaders()
    {
        var (http, response, cache) = Request();
        http.Response.Headers.CacheControl = "public";
        response.Started = true;

        Assert.DoesNotThrow(() => PageCaching.NoStore(http));
        Assert.Multiple(() =>
        {
            Assert.That(cache.Context.AllowCacheStorage, Is.False, "the cache stores the response once it is complete");
            Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("public"), "headers sent cannot change");
            Assert.That(response.Callbacks, Is.Zero);
        });
    }

    [Test]
    public void NoStore_WithoutAnOutputCache_MarksTheResponse()
    {
        var http = new DefaultHttpContext();

        PageCaching.NoStore(http);

        Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
    }

    [TestCase("?jfb-sent=jfb-1.token")]
    [TestCase("?jfb-sent=x123")]
    [TestCase("?jfb-sent=")]
    [TestCase("?jfb-sent")]
    [TestCase("?JFB-Sent=jfb-1.token")]
    [TestCase("?page=2&jfb-sent=a&jfb-sent=b")]
    public void TheVaryRule_OnARequestThatCarriesJfbSent_VariesByIt_KeepsTheSitesRules_AndStoresNothing(string query)
    {
        var (http, _, cache) = Request();
        http.Request.QueryString = new QueryString(query);
        var rules = new CacheVaryByRules { QueryKeys = "page" };

        new SentVaryBy().ConfigureVaryBy(http, rules);

        Assert.Multiple(() =>
        {
            Assert.That(rules.QueryKeys.ToArray(), Is.EqualTo(new[] { "page", "jfb-sent" }));
            Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(cache.Context.AllowCacheStorage, Is.False);
        });
    }

    [TestCase("")]
    [TestCase("?jfb-sentx=1")]
    [TestCase("?sent=jfb-1")]
    [TestCase("?q=jfb-sent")]
    public void TheVaryRule_OnAnyOtherRequest_ChangesNothing(string query)
    {
        var (http, _, cache) = Request();
        http.Request.QueryString = new QueryString(query);
        var rules = new CacheVaryByRules { QueryKeys = "page" };

        new SentVaryBy().ConfigureVaryBy(http, rules);

        Assert.Multiple(() =>
        {
            Assert.That(rules.QueryKeys.ToArray(), Is.EqualTo(new[] { "page" }));
            Assert.That(http.Response.Headers.CacheControl, Is.EqualTo(StringValues.Empty));
            Assert.That(cache.Context.AllowCacheStorage, Is.True);
        });
    }

    // A request under the output cache, with a response that starts when the test says so.
    private static (DefaultHttpContext Http, StartingResponse Response, CacheFeature Cache) Request()
    {
        var http = new DefaultHttpContext();
        var response = new StartingResponse { Headers = http.Response.Headers };
        http.Features.Set<IHttpResponseFeature>(response);
        var cache = new CacheFeature(new OutputCacheContext { HttpContext = http, AllowCacheStorage = true });
        http.Features.Set<IOutputCacheFeature>(cache);
        return (http, response, cache);
    }

    private sealed class CacheFeature(OutputCacheContext context) : IOutputCacheFeature
    {
        public OutputCacheContext Context { get; } = context;
    }

    // A response whose start runs the callbacks registered for it, as the server does.
    private sealed class StartingResponse : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public bool Started { get; set; }

        public int Callbacks => _starting.Count;

        public override bool HasStarted => Started;

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _starting)
            {
                await callback(state);
            }

            Started = true;
        }
    }
}
