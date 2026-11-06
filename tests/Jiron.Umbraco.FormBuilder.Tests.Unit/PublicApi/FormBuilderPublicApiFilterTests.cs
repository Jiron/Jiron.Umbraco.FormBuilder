using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The filter that applies each public action's declarations (ADR-0012, ADR-0084), without a host: the body limit
/// before a byte is read and as the server's own, the largest upload from the host's limits for files (ADR-0087), the
/// time limit on the site's clock and the answer it writes past it (ADR-0193), the pending migrations, and the strictest
/// declarations for an action without any.
/// </summary>
[TestFixture]
public sealed class FormBuilderPublicApiFilterTests
{
    private ManualTimers _timers = null!;
    private Migrations _migrations = null!;
    private FormBuilderPublicApiFilter _filter = null!;

    [SetUp]
    public void CreateTheFilter()
    {
        _timers = new ManualTimers();
        _migrations = new Migrations();
        _filter = new FormBuilderPublicApiFilter(_migrations, _timers, new FieldHostLimits(1000, []));
    }

    [TestCase(0L, null, null, "ran")]
    [TestCase(0L, 0L, null, "ran")]
    [TestCase(0L, 1L, null, "413 payload_too_large, close")]
    [TestCase(0L, null, "chunked", "413 payload_too_large, close")]
    [TestCase(100L, 100L, null, "ran")]
    [TestCase(100L, 101L, null, "413 payload_too_large, close")]
    [TestCase(100L, 101L, "chunked", "413 payload_too_large, close")]
    [TestCase(100L, null, "chunked", "ran")]
    public async Task ABody_IsRefusedBeforeItIsRead_WhenItsDeclaredLengthIsAboveTheLimit_OrTheActionTakesNone(long limit, long? length, string? transferEncoding, string answer)
    {
        var context = Context(new FormBuilderBodyLimitAttribute(limit));
        context.HttpContext.Request.ContentLength = length;
        if (transferEncoding is not null)
        {
            context.HttpContext.Request.Headers.TransferEncoding = transferEncoding;
        }

        Assert.That(await RunAsync(context), Is.EqualTo(answer));
    }

    [TestCase(66_536L, "ran")]
    [TestCase(66_537L, "413 payload_too_large, close")]
    public async Task TheLargestUpload_IsTheHostsMostBytesOfAFile_WithWhatARequestCarriesBesides(long length, string answer)
    {
        var context = Context(new FormBuilderBodyLimitAttribute(FormBuilderBodyLimitAttribute.LargestUpload));
        context.HttpContext.Request.ContentLength = length;

        Assert.That(await RunAsync(context), Is.EqualTo(answer), "1,000 bytes and 65,536");
    }

    [TestCase(100L, false, 100L)]
    [TestCase(100L, true, 30_000_000L)]
    [TestCase(0L, false, 30_000_000L)]
    public async Task TheServersOwnLimit_BecomesTheEndpoints_WhileTheServerLetsItChange(long limit, bool readOnly, long seen)
    {
        var context = Context(new FormBuilderBodyLimitAttribute(limit));
        var server = new MaxBodySize(readOnly) { MaxRequestBodySize = 30_000_000 };
        context.HttpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(server);

        Assert.Multiple(async () =>
        {
            Assert.That(await RunAsync(context), Is.EqualTo("ran"));
            Assert.That(server.MaxRequestBodySize, Is.EqualTo(seen));
        });
    }

    [TestCase(true, "413 payload_too_large, close")]
    [TestCase(false, "ran")]
    public async Task ABodyTheServerSees_WithoutALength_IsRefusedByAnActionThatTakesNone(bool canHaveBody, string answer)
    {
        var context = Context(new FormBuilderBodyLimitAttribute(0));
        context.HttpContext.Features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(canHaveBody));

        Assert.That(await RunAsync(context), Is.EqualTo(answer));
    }

    [TestCase("HTTP/1.1", "close")]
    [TestCase("HTTP/1.0", "close")]
    [TestCase("HTTP/2", "")]
    [TestCase("HTTP/3", "")]
    public async Task ARefusedBody_ClosesTheConnection_OnlyWhereTheProtocolHasOne(string protocol, string connection)
    {
        var context = Context(new FormBuilderBodyLimitAttribute(0));
        context.HttpContext.Request.Protocol = protocol;
        context.HttpContext.Request.ContentLength = 10;

        await RunAsync(context);

        Assert.That(context.HttpContext.Response.Headers.Connection.ToString(), Is.EqualTo(connection));
    }

    [Test]
    public async Task TheTimeLimit_CancelsTheRequestOnTheSitesClock_AndEndsWithIt()
    {
        var context = Context(new FormBuilderTimeLimitAttribute(25));
        using var left = new CancellationTokenSource();
        context.HttpContext.RequestAborted = left.Token;
        var seen = CancellationToken.None;
        FormBuilderTimeLimitFeature? feature = null;

        await _filter.OnResourceExecutionAsync(context, () =>
        {
            seen = context.HttpContext.RequestAborted;
            feature = context.HttpContext.Features.Get<FormBuilderTimeLimitFeature>();
            var timedOutBefore = feature!.TimedOut;
            _timers.Timers.Single().Fire();
            Assert.Multiple(() =>
            {
                Assert.That(timedOutBefore, Is.False);
                Assert.That(seen.IsCancellationRequested, Is.True, "the endpoint's token");
                Assert.That(feature.TimedOut, Is.True);
                Assert.That(left.IsCancellationRequested, Is.False, "the visitor is still there");
            });
            return Task.FromResult(Executed(context));
        });

        Assert.Multiple(() =>
        {
            Assert.That(_timers.Timers.Single().DueTime, Is.EqualTo(TimeSpan.FromSeconds(25)));
            Assert.That(_timers.Timers.Single().Disposed, Is.True);
            Assert.That(context.HttpContext.RequestAborted, Is.EqualTo(left.Token), "the request's own token again");
        });
    }

    [Test]
    public async Task AnAnswerBuilt_IsWrittenOnTheVisitorsOwnToken_EvenPastTheTimeLimit()
    {
        var context = Context(new FormBuilderTimeLimitAttribute(10));
        using var left = new CancellationTokenSource();
        context.HttpContext.RequestAborted = left.Token;
        var written = CancellationToken.None;

        await _filter.OnResourceExecutionAsync(context, async () =>
        {
            _timers.Timers.Single().Fire();
            written = await WrittenWithAsync(context, new FileContentResult([123, 125], "application/json"));
            return Executed(context);
        });

        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo(left.Token), "the visitor's own token");
            Assert.That(written.IsCancellationRequested, Is.False);
            Assert.That(context.HttpContext.Features.Get<FormBuilderTimeLimitFeature>()!.TimedOut, Is.True, "the limit still says it ran out");
        });
    }

    [Test]
    public async Task AResultThatWorksAsItWrites_KeepsTheTimeLimit()
    {
        var context = Context(new FormBuilderTimeLimitAttribute(10));
        using var left = new CancellationTokenSource();
        context.HttpContext.RequestAborted = left.Token;
        var written = CancellationToken.None;

        await _filter.OnResourceExecutionAsync(context, async () =>
        {
            var limited = context.HttpContext.RequestAborted;
            written = await WrittenWithAsync(context, new ViewComponentResult { ViewComponentName = "Rendered" });
            Assert.That(written, Is.EqualTo(limited));
            return Executed(context);
        });

        Assert.That(written, Is.Not.EqualTo(left.Token), "the time limit's token");
    }

    [Test]
    public async Task AnAnswerWithoutATimeLimit_IsWrittenAsItIs()
    {
        var context = Context();
        using var left = new CancellationTokenSource();
        context.HttpContext.RequestAborted = left.Token;

        Assert.That(await WrittenWithAsync(context, new FileContentResult([1], "application/json")), Is.EqualTo(left.Token));
    }

    [Test]
    public async Task AVisitorWhoLeaves_CancelsTheEndpointsToken_WithoutATimeout()
    {
        var context = Context(new FormBuilderTimeLimitAttribute(10));
        using var left = new CancellationTokenSource();
        context.HttpContext.RequestAborted = left.Token;
        var answers = new List<bool>();

        await _filter.OnResourceExecutionAsync(context, async () =>
        {
            await left.CancelAsync();
            answers.Add(context.HttpContext.RequestAborted.IsCancellationRequested);
            answers.Add(context.HttpContext.Features.Get<FormBuilderTimeLimitFeature>()!.TimedOut);
            return Executed(context);
        });

        Assert.That(answers, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public async Task AnActionWithoutDeclarations_TakesNoBody_AndHasTheDefaultTimeLimit()
    {
        var withBody = Context();
        withBody.HttpContext.Request.ContentLength = 1;
        var without = Context();

        Assert.Multiple(async () =>
        {
            Assert.That(await RunAsync(withBody), Is.EqualTo("413 payload_too_large, close"));
            Assert.That(await RunAsync(without), Is.EqualTo("ran"));
            Assert.That(_timers.Timers.Single().DueTime, Is.EqualTo(FormBuilderPublicApiFilter.DefaultTimeLimit));
            Assert.That(FormBuilderPublicApiFilter.DefaultTimeLimit, Is.EqualTo(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public async Task WhileTheMigrationsArePending_EveryActionAnswersMigrationsPending_WithRetryAfter()
    {
        _migrations.Pending = true;
        var context = Context(new FormBuilderBodyLimitAttribute(0));

        Assert.Multiple(async () =>
        {
            Assert.That(await RunAsync(context), Is.EqualTo("503 migrations_pending"));
            Assert.That(context.HttpContext.Response.Headers.RetryAfter.ToString(), Is.EqualTo("10"));
            Assert.That(_migrations.Tokens.Single().CanBeCanceled, Is.True, "asked within the time limit");
        });
    }

    [Test]
    public async Task ARefusedBody_IsAnsweredBeforeTheMigrationsAreAskedFor()
    {
        _migrations.Pending = true;
        var context = Context(new FormBuilderBodyLimitAttribute(0));
        context.HttpContext.Request.ContentLength = 5;

        Assert.Multiple(async () =>
        {
            Assert.That(await RunAsync(context), Is.EqualTo("413 payload_too_large, close"));
            Assert.That(_migrations.Tokens, Is.Empty);
            Assert.That(_timers.Timers, Is.Empty, "and no time limit starts");
        });
    }

    [Test]
    public void TheDeclarations_RefuseValuesOutOfRange()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FormBuilderRateLimitAttribute("reads"), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new FormBuilderRateLimitAttribute(FormBuilderRateLimitPolicies.Submit).Policy, Is.EqualTo("submit"));
            Assert.That(() => new FormBuilderBodyLimitAttribute(-1), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new FormBuilderBodyLimitAttribute(0).Bytes, Is.Zero);
            Assert.That(() => new FormBuilderBodyLimitAttribute("largest"), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new FormBuilderBodyLimitAttribute(FormBuilderBodyLimitAttribute.LargestUpload).Bytes, Is.Null);
            Assert.That(new FormBuilderBodyLimitAttribute(FormBuilderBodyLimitAttribute.LargestUpload).BytesFor(FieldHostLimits.Default), Is.EqualTo(52_494_336), "50 MiB and 64 KiB");
            Assert.That(new FormBuilderBodyLimitAttribute(20).BytesFor(FieldHostLimits.Default), Is.EqualTo(20));
            Assert.That(() => new FormBuilderTimeLimitAttribute(0), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new FormBuilderTimeLimitAttribute(301), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new FormBuilderTimeLimitAttribute(1).Limit, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(new FormBuilderTimeLimitAttribute(300).Limit, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(FormBuilderRateLimitPolicies.All, Is.EqualTo(new[] { "read", "challenge", "submit", "upload", "events", "verify" }));
        });
    }

    private static ResourceExecutingContext Context(params object[] declarations)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Protocol = "HTTP/1.1";
        http.Response.Body = new MemoryStream();
        var action = new ActionDescriptor { EndpointMetadata = declarations };
        return new ResourceExecutingContext(new ActionContext(http, new RouteData(), action), [], []);
    }

    private static ResourceExecutedContext Executed(ResourceExecutingContext context) => new(context, []);

    // The token the result is written on, after the filter's result stage.
    private async Task<CancellationToken> WrittenWithAsync(ActionContext context, IActionResult answer)
    {
        var result = new ResultExecutingContext(context, [], answer, new object());
        var written = CancellationToken.None;
        await _filter.OnResultExecutionAsync(result, () =>
        {
            written = context.HttpContext.RequestAborted;
            return Task.FromResult(new ResultExecutedContext(context, [], answer, new object()));
        });
        return written;
    }

    // Whether the action ran, or the refusal the filter answered with and whether it closes the connection.
    private async Task<string> RunAsync(ResourceExecutingContext context)
    {
        var ran = false;
        await _filter.OnResourceExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(Executed(context));
        });

        if (ran)
        {
            return "ran";
        }

        var problem = ((FormBuilderProblemResult)context.Result!).Problem;
        var close = context.HttpContext.Response.Headers.Connection.ToString() == "close" ? ", close" : string.Empty;
        return $"{problem.Status} {problem.Extensions["code"]}{close}";
    }

    private sealed class Migrations : IFormBuilderMigrationState
    {
        public bool Pending { get; set; }

        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return ValueTask.FromResult(Pending);
        }

        public void Invalidate()
        {
        }
    }

    private sealed class BodyDetection(bool canHaveBody) : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => canHaveBody;
    }

    private sealed class MaxBodySize(bool readOnly) : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => readOnly;

        public long? MaxRequestBodySize { get; set; }
    }
}
