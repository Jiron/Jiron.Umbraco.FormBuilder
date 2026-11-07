using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// What every request to the public API meets (ADR-0012, ADR-0084), without a host: only below the base path, the
/// headers every answer carries, the CORS answer for each kind of origin and method, the preview frame's ticket (ADR-0203),
/// and the answers to what ends a request early.
/// </summary>
[TestFixture]
public sealed class FormBuilderPublicApiMiddlewareTests
{
    private const string Allowed = "https://shop.example.com";
    private const string Own = "https://www.example.com";

    private RecordingLogger<FormBuilderPublicApiMiddleware> _logger = null!;
    private FormBuilderPublicApiMiddleware _middleware = null!;

    [SetUp]
    public void CreateTheMiddleware()
    {
        var options = new FormBuilderOptions();
        options.PublicApi.AllowedOrigins.Add("HTTPS://Shop.Example.com:443");
        _logger = new RecordingLogger<FormBuilderPublicApiMiddleware>();
        _middleware = new FormBuilderPublicApiMiddleware(new FormBuilderPublicApiPaths(Options.Create(options)), _logger);
    }

    [TestCase("/")]
    [TestCase("/jfb/api")]
    [TestCase("/jfb/apiv1/forms")]
    [TestCase("/jfb/api/v2/forms")]
    [TestCase("/jfb/api/v10/forms")]
    [TestCase("/umbraco/jfb/management/api/v1/tree/root")]
    public async Task ARequestOutsideTheBasePath_PassesUntouched(string path)
    {
        var (http, passed) = await SendAsync(Request("POST", path, origin: "https://elsewhere.example"));

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.True);
            Assert.That(http.Response.Headers.CacheControl.ToString(), Is.Empty);
            Assert.That(http.Response.Headers.Vary.ToString(), Is.Empty);
            Assert.That(http.Features.Get<IFormFeature>(), Is.Null);
        });
    }

    [TestCase("/jfb/api/v1")]
    [TestCase("/jfb/api/v1/forms/contact")]
    [TestCase("/JFB/Api/V1/forms/contact")]
    public async Task EveryAnswerBelowTheBasePath_IsNoStore_VariesByOrigin_AndHasNoForm(string path)
    {
        var (http, passed) = await SendAsync(Request("GET", path));
        var form = http.Features.Get<IFormFeature>();

        Assert.Multiple(async () =>
        {
            Assert.That(passed, Is.True);
            Assert.That(http.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(http.Response.Headers.Vary.ToString(), Is.EqualTo("Origin"));
            Assert.That(form, Is.SameAs(NoFormFeature.Instance));
            Assert.That(form!.HasFormContentType, Is.False);
            Assert.That(form.Form, Is.SameAs(FormCollection.Empty));
            Assert.That(form.ReadForm(), Is.SameAs(FormCollection.Empty));
            Assert.That(await form.ReadFormAsync(CancellationToken.None), Is.SameAs(FormCollection.Empty));
        });
    }

    [TestCase("GET", null, "passed")]
    [TestCase("POST", null, "passed")]
    [TestCase("GET", Allowed, "passed, allowed https://shop.example.com")]
    [TestCase("POST", Allowed, "passed, allowed https://shop.example.com")]
    [TestCase("POST", "HTTPS://SHOP.example.com:443", "passed, allowed https://shop.example.com")]
    [TestCase("POST", Own, "passed")]
    [TestCase("POST", "https://WWW.example.com", "passed")]
    [TestCase("GET", "https://evil.example", "passed")]
    [TestCase("HEAD", "https://evil.example", "passed")]
    [TestCase("POST", "https://evil.example", "403 origin_denied")]
    [TestCase("PUT", "https://evil.example", "403 origin_denied")]
    [TestCase("DELETE", "https://evil.example", "403 origin_denied")]
    [TestCase("OPTIONS", "https://evil.example", "403 origin_denied")]
    [TestCase("POST", "https://shop.example.com:8443", "403 origin_denied")]
    [TestCase("POST", "http://shop.example.com", "403 origin_denied")]
    [TestCase("POST", "http://www.example.com", "403 origin_denied")]
    [TestCase("POST", "null", "403 origin_denied")]
    [TestCase("POST", "*", "403 origin_denied")]
    [TestCase("POST", "https://www.example.com/", "403 origin_denied")]
    [TestCase("GET", "null", "passed")]
    public async Task EachOrigin_GetsItsAnswer(string method, string? origin, string answer)
    {
        var (http, passed) = await SendAsync(Request(method, "/jfb/api/v1/forms/contact", origin));

        Assert.That(await AnswerAsync(http, passed), Is.EqualTo(answer));
    }

    [Test]
    public async Task TheSitesOwnHttpsPages_PassBehindAProxyThatEndsTls_ButNotTheOtherWayRound()
    {
        var (overHttp, passedOverHttp) = await SendAsync(Request("POST", "/jfb/api/v1/forms/contact", Own, scheme: "http"));
        var (downgraded, passedDowngraded) = await SendAsync(Request("POST", "/jfb/api/v1/forms/contact", "http://www.example.com", scheme: "https"));
        var (ported, passedPorted) = await SendAsync(Request("POST", "/jfb/api/v1/forms/contact", "http://localhost:5500", scheme: "http", host: "localhost:5500"));

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(overHttp, passedOverHttp), Is.EqualTo("passed"), "the request came over http from the site's https page");
            Assert.That(await AnswerAsync(downgraded, passedDowngraded), Is.EqualTo("403 origin_denied"), "an http page is not the https site's own");
            Assert.That(await AnswerAsync(ported, passedPorted), Is.EqualTo("passed"), "the port is part of the origin");
        });
    }

    [Test]
    public async Task SeveralOrigins_AreNone()
    {
        var http = Request("POST", "/jfb/api/v1/forms/contact");
        http.Request.Headers.Origin = new[] { Allowed, Own };

        var (answered, passed) = await SendAsync(http);

        Assert.That(await AnswerAsync(answered, passed), Is.EqualTo("403 origin_denied"));
    }

    [Test]
    public async Task APreflight_FromAnAllowedOrigin_AnswersItself_WithWhatItMaySend()
    {
        var http = Request("OPTIONS", "/jfb/api/v1/forms/contact/entries", Allowed);
        http.Request.Headers.AccessControlRequestMethod = "POST";
        http.Request.Headers.AccessControlRequestHeaders = "content-type";

        var (answered, passed) = await SendAsync(http);
        var headers = answered.Response.Headers;

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(answered.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
            Assert.That(headers.AccessControlAllowOrigin.ToString(), Is.EqualTo(Allowed));
            Assert.That(headers.AccessControlAllowMethods.ToString(), Is.EqualTo("GET, POST"));
            Assert.That(headers.AccessControlAllowHeaders.ToString(), Is.EqualTo("Content-Type"));
            Assert.That(headers.AccessControlMaxAge.ToString(), Is.EqualTo("600"));
            Assert.That(headers.AccessControlAllowCredentials.ToString(), Is.Empty);
            Assert.That(headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(answered.Response.Body.Length, Is.Zero);
        });
    }

    [TestCase("https://evil.example", "403 origin_denied")]
    [TestCase(Own, "passed")]
    [TestCase(null, "passed")]
    public async Task APreflight_FromAnyOtherOrigin_IsRefused_AndAnOptionsWithoutOne_IsNoPreflight(string? origin, string answer)
    {
        var http = Request("OPTIONS", "/jfb/api/v1/forms/contact/entries", origin);
        http.Request.Headers.AccessControlRequestMethod = "POST";

        var (answered, passed) = await SendAsync(http);

        Assert.That(await AnswerAsync(answered, passed), Is.EqualTo(origin == Own ? "204" : answer));
    }

    [Test]
    public async Task APreflight_FromThePreviewsOpaqueOrigin_MaySendItsTicket()
    {
        var http = Request("OPTIONS", "/jfb/api/v1/forms/contact/challenge", "null");
        http.Request.Headers.AccessControlRequestMethod = "POST";

        var (answered, passed) = await SendAsync(http);

        var headers = answered.Response.Headers;
        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.False);
            Assert.That(answered.Response.StatusCode, Is.EqualTo(StatusCodes.Status204NoContent));
            Assert.That(headers.AccessControlAllowOrigin.ToString(), Is.EqualTo("null"));
            Assert.That(headers.AccessControlAllowHeaders.ToString(), Is.EqualTo("Content-Type, Authorization"));
            Assert.That(headers.AccessControlAllowMethods.ToString(), Is.EqualTo("GET, POST"));
            Assert.That(headers.AccessControlAllowCredentials.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task ThePreviewsOpaqueOrigin_PassesWithATicketThatHolds_AndATicketThatDoesNot_IsRefusedWhateverTheOrigin()
    {
        var options = new FormBuilderOptions();
        var clock = new TestTimeProvider();
        var tickets = new PreviewTickets(new EphemeralDataProtectionProvider(), clock);
        var middleware = new FormBuilderPublicApiMiddleware(new FormBuilderPublicApiPaths(Options.Create(options)), _logger, tickets: tickets, time: clock);
        var form = Guid.NewGuid();
        var ticket = tickets.Issue(form, 3).Value;

        async Task<(DefaultHttpContext Http, string Answer, PreviewRequest? Preview)> SendAsync(string? origin, string? authorization, string method = "POST")
        {
            var http = Request(method, $"/jfb/api/v1/forms/{form}/challenge", origin);
            if (authorization is not null)
            {
                http.Request.Headers.Authorization = authorization;
            }

            PreviewRequest? seen = null;
            var passed = false;
            await middleware.InvokeAsync(http, context =>
            {
                passed = true;
                seen = PreviewRequest.Of(context);
                return Task.CompletedTask;
            });
            return (http, await AnswerAsync(http, passed && http.Response.Body.Length == 0), seen);
        }

        var held = await SendAsync("null", "Jfb-Preview " + ticket);
        var none = await SendAsync("null", null);
        var noneRead = await SendAsync("null", null, "GET");
        var forged = await SendAsync("null", "Jfb-Preview forged");
        var ownForged = await SendAsync(Own, "Jfb-Preview forged");
        var ownHeld = await SendAsync(Own, "Jfb-Preview " + ticket);
        var elsewhere = await SendAsync("https://evil.example", "Jfb-Preview " + ticket);
        clock.Advance(PreviewTickets.Lifetime);
        var expired = await SendAsync("null", "Jfb-Preview " + ticket);

        Assert.Multiple(() =>
        {
            Assert.That(held.Answer, Is.EqualTo("passed, allowed null"));
            Assert.That(held.Preview, Is.EqualTo(new PreviewRequest(form, 3)), "the endpoints read the version it names");
            Assert.That(held.Http.Response.Headers.AccessControlExposeHeaders.ToString(), Is.EqualTo("ETag, Retry-After"));
            Assert.That(none.Answer, Is.EqualTo("403 origin_denied"), "the null origin without a ticket, as before");
            Assert.That((noneRead.Answer, noneRead.Preview), Is.EqualTo(("passed", (PreviewRequest?)null)), "a read without a ticket runs without CORS headers, as before");
            Assert.That(forged.Answer, Is.EqualTo("401 preview_ticket_invalid"));
            Assert.That(forged.Http.Response.Headers.WWWAuthenticate.ToString(), Is.EqualTo(PreviewTickets.Scheme));
            Assert.That(forged.Http.Response.Headers.AccessControlAllowOrigin.ToString(), Is.EqualTo("null"), "the frame may read why");
            Assert.That(ownForged.Answer, Is.EqualTo("401 preview_ticket_invalid"), "whatever the origin");
            Assert.That(ownForged.Http.Response.Headers.AccessControlAllowOrigin.ToString(), Is.Empty);
            Assert.That((ownHeld.Answer, ownHeld.Preview), Is.EqualTo(("passed", (PreviewRequest?)new PreviewRequest(form, 3))));
            Assert.That(expired.Answer, Is.EqualTo("401 preview_ticket_invalid"));
            Assert.That(elsewhere.Answer, Is.EqualTo("403 origin_denied"), "a ticket that holds admits no other origin");
        });
    }

    [Test]
    public async Task AnAllowedOriginsAnswer_ShowsTheETagAndRetryAfter_WithoutCredentials()
    {
        var (http, _) = await SendAsync(Request("GET", "/jfb/api/v1/forms/contact", Allowed));

        Assert.Multiple(() =>
        {
            Assert.That(http.Response.Headers.AccessControlExposeHeaders.ToString(), Is.EqualTo("ETag, Retry-After"));
            Assert.That(http.Response.Headers.AccessControlAllowCredentials.ToString(), Is.Empty);
        });
    }

    [Test]
    public async Task AVisitorWhoLeft_GetsNoAnswer_AndIsNoError()
    {
        using var left = new CancellationTokenSource();
        var http = Request("GET", "/jfb/api/v1/forms/contact");
        http.RequestAborted = left.Token;

        var (answered, _) = await SendAsync(http, async _ =>
        {
            await left.CancelAsync();
            left.Token.ThrowIfCancellationRequested();
        });

        Assert.Multiple(() =>
        {
            Assert.That(answered.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK), "nothing was written");
            Assert.That(answered.Response.Body.Length, Is.Zero);
            Assert.That(_logger.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task ARequestPastItsTimeLimit_IsTimeout_WithoutWhatTheEndpointSetBefore()
    {
        using var limit = new CancellationTokenSource();
        var http = Request("GET", "/jfb/api/v1/forms/contact");
        http.Features.Set(new FormBuilderTimeLimitFeature(limit.Token, CancellationToken.None));

        var (answered, passed) = await SendAsync(http, async context =>
        {
            context.Response.Headers.ETag = "\"0123\"";
            context.Response.Headers.CacheControl = "no-cache";
            await limit.CancelAsync();
            limit.Token.ThrowIfCancellationRequested();
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(answered, passed), Is.EqualTo("504 timeout"));
            Assert.That(answered.Response.Headers.ETag.ToString(), Is.Empty);
            Assert.That(answered.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
            Assert.That(_logger.Entries, Is.Empty, "a time limit is no error of the site");
        });
    }

    [Test]
    public async Task WhatFailsUnexpectedly_IsServerError_LoggedWithTheEndpointOnly()
    {
        var http = Request("GET", "/jfb/api/v1/forms/contact");
        http.Request.QueryString = new QueryString("?culture=de-CH");
        http.SetEndpoint(new Endpoint(null, null, "FormController.Definition"));
        var failure = new InvalidOperationException("JFB_TEST: the read fails.");

        var (answered, passed) = await SendAsync(http, context =>
        {
            context.Response.Headers.ETag = "\"0123\"";
            throw failure;
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(answered, passed), Is.EqualTo("500 server_error"));
            Assert.That(answered.Response.Headers.ETag.ToString(), Is.Empty);
            Assert.That(_logger.Entries.Select(entry => (entry.Level, entry.Exception)), Is.EqualTo(new[] { (LogLevel.Error, (Exception?)failure) }));
            Assert.That(_logger.Entries[0].Message, Does.Contain("FormController.Definition").And.Not.Contain("de-CH"));
        });
    }

    [Test]
    public async Task ACancellationNobodyAskedFor_IsUnexpected()
    {
        var (answered, passed) = await SendAsync(Request("GET", "/jfb/api/v1/forms/contact"), _ => throw new OperationCanceledException("JFB_TEST"));

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(answered, passed), Is.EqualTo("500 server_error"));
            Assert.That(_logger.Entries.Single().Message, Does.Contain("no endpoint"));
        });
    }

    [Test]
    public void AFailureAfterTheAnswerStarted_GoesOnToTheSite()
    {
        var started = Request("GET", "/jfb/api/v1/forms/contact");
        started.Features.Set<IHttpResponseFeature>(new StartedResponse());

        Assert.Multiple(() =>
        {
            Assert.That(async () => await SendAsync(started, _ => throw new InvalidOperationException("JFB_TEST")), Throws.InvalidOperationException);

            Assert.That(_logger.Entries, Is.Empty);
        });
    }

    private static DefaultHttpContext Request(string method, string path, string? origin = null, string scheme = "https", string host = "www.example.com")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = path;
        http.Request.Scheme = scheme;
        http.Request.Host = new HostString(host);
        if (origin is not null)
        {
            http.Request.Headers.Origin = origin;
        }

        http.Response.Body = new MemoryStream();
        return http;
    }

    // What the middleware answered: passed on to the site (with the CORS origin it allowed), or its own status and code.
    private static async Task<string> AnswerAsync(DefaultHttpContext http, bool passed)
    {
        if (passed)
        {
            var allowed = http.Response.Headers.AccessControlAllowOrigin.ToString();
            return allowed.Length == 0 ? "passed" : $"passed, allowed {allowed}";
        }

        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        return body.Length == 0
            ? $"{http.Response.StatusCode}"
            : $"{http.Response.StatusCode} {JsonNode.Parse(body)!["code"]!.GetValue<string>()}";
    }

    [Test]
    public async Task AKeyThatMatchesNone_IsLoggedOnceAMinute_WithoutThePath()
    {
        var options = new FormBuilderOptions();
        var clock = new TestTimeProvider(DateTimeOffset.UnixEpoch.AddDays(1));
        var middleware = new FormBuilderPublicApiMiddleware(new FormBuilderPublicApiPaths(Options.Create(options)), _logger, new ServerKeys(Options.Create(options)), clock);

        async Task SendAsync(string path)
        {
            var http = Request("POST", path);
            http.Request.Headers.Authorization = ServerKeys.Scheme + " JFB_FAKE_no_such_key";
            await middleware.InvokeAsync(http, _ => Task.CompletedTask);
            Assert.That(http.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
        }

        await SendAsync("/jfb/api/v1/forms/jfb-chosen-path");
        await SendAsync("/jfb/api/v1/forms/another");
        clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromSeconds(1));
        await SendAsync("/jfb/api/v1/forms/another");
        var withinAMinute = _logger.Entries.Count;
        clock.Advance(TimeSpan.FromSeconds(1));
        await SendAsync("/jfb/api/v1/forms/another");

        Assert.Multiple(() =>
        {
            Assert.That(withinAMinute, Is.EqualTo(1));
            Assert.That(_logger.Entries, Has.Count.EqualTo(2), "and again a minute later");
            Assert.That(_logger.Entries[0].Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(_logger.Entries[0].Message, Does.Not.Contain("jfb-chosen-path").And.Not.Contain("/jfb"));
        });
    }

    private async Task<(DefaultHttpContext Http, bool Passed)> SendAsync(DefaultHttpContext http, RequestDelegate? next = null)
    {
        var passed = false;
        await _middleware.InvokeAsync(http, context =>
        {
            passed = true;
            return next is null ? Task.CompletedTask : next(context);
        });
        return (http, passed && http.Response.Body.Length == 0 && http.Response.StatusCode == StatusCodes.Status200OK);
    }

    // A response whose headers went out already.
    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
