using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// A member's page refuses another origin's frames (ADR-0004, ADR-0231): both headers once, as the response starts,
/// beside the site's own policy; a site that says itself how its page may be framed keeps its word.
/// </summary>
[TestFixture]
public sealed class PageFramingTests
{
    [Test]
    public async Task APage_GetsBothHeadersOnce_AsItStarts_BesideTheSitesOwnPolicy()
    {
        var (http, started) = Response();
        http.Response.Headers.ContentSecurityPolicy = "default-src 'self'";
        PageFraming.OwnOriginOnly(http);
        PageFraming.OwnOriginOnly(http);
        var before = http.Response.Headers.XFrameOptions.ToString();

        await started.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Empty, "nothing before the response starts, so the site's middleware can still speak");
            Assert.That(http.Response.Headers.ContentSecurityPolicy.ToArray(), Is.EqualTo(new[] { "default-src 'self'", PageFraming.FrameAncestors }), "a policy of its own, once");
            Assert.That(http.Response.Headers.XFrameOptions.ToString(), Is.EqualTo(PageFraming.SameOrigin));
        });
    }

    [TestCase("X-Frame-Options", "DENY")]
    [TestCase("Content-Security-Policy", "default-src 'self'; frame-ancestors https://partner.example")]
    public async Task ASiteThatSaysHowItsPageMayBeFramed_KeepsItsWord(string header, string value)
    {
        var (http, started) = Response();
        PageFraming.OwnOriginOnly(http);
        http.Response.Headers[header] = value;

        await started.StartAsync();

        Assert.Multiple(() =>
        {
            Assert.That(http.Response.Headers[header].ToArray(), Is.EqualTo(new[] { value }));
            Assert.That(http.Response.Headers.ContentSecurityPolicy.ToString(), Does.Not.Contain(PageFraming.FrameAncestors));
            Assert.That(http.Response.Headers.XFrameOptions.ToString(), Is.Not.EqualTo(PageFraming.SameOrigin));
        });
    }

    private static (DefaultHttpContext Http, StartingFeature Started) Response()
    {
        var http = new DefaultHttpContext();
        var started = new StartingFeature();
        http.Features.Set<IHttpResponseFeature>(started);
        return (http, started);
    }

    // A response whose start a test calls, running what was registered for it.
    private sealed class StartingFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _starting)
            {
                await callback(state);
            }
        }
    }
}
