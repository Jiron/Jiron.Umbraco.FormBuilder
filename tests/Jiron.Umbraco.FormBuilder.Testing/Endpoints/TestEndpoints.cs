using System.Net;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Testing.Endpoints;

/// <summary>
/// When the test-only endpoints answer (ADR-0011, ADR-0029): all three must hold, so a switch left on by mistake opens
/// nothing to the network.
/// <list type="number">
/// <item><description>The host turned them on (<c>JironFormBuilderTesting:Endpoints</c>).</description></item>
/// <item><description>The host runs in the Development environment; checked once, when the pipeline is built.</description></item>
/// <item><description>The request comes from this machine; checked per request.</description></item>
/// </list>
/// </summary>
internal static class TestEndpointsGate
{
    /// <summary>The paths the test-only endpoints answer.</summary>
    public const string Prefix = "/jfb-testing";

    /// <summary>True when the host may answer the test-only paths at all.</summary>
    public static bool IsOpen(FormBuilderTestingOptions options, IHostEnvironment environment) =>
        options.Endpoints && environment.IsDevelopment();

    /// <summary>True for a path under <see cref="Prefix"/>.</summary>
    public static bool IsTestPath(PathString path) =>
        path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the request comes from this machine: the browser tests run beside their host.</summary>
    public static bool IsLocal(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}

/// <summary>Adds <see cref="TestEndpointsMiddleware"/> in front of the host's pipeline when the gate is open.</summary>
internal sealed class TestEndpointsStartupFilter(IOptions<FormBuilderTestingOptions> options, IHostEnvironment environment) : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (TestEndpointsGate.IsOpen(options.Value, environment))
        {
            app.UseMiddleware<TestEndpointsMiddleware>();
        }

        next(app);
    };
}

/// <summary>
/// The test-only endpoints. Each later step adds what its browser tests need (seeding, running a job now, ageing
/// entries); these are the first:
/// <list type="bullet">
/// <item><description><c>GET /jfb-testing/status</c>: <c>{"testHost": true, "clock": ..., "utc": ..., "stub": ...}</c>,
/// so a global set-up can tell that it reached a test host.</description></item>
/// <item><description><c>/jfb-testing/clock</c>: reads (<c>GET</c>), moves (<c>POST {"utc": "..."}</c> or
/// <c>{"advanceSeconds": 360}</c>) and resets (<c>DELETE</c>) the host's <see cref="TestClock"/>; <c>404</c> when the
/// host has none.</description></item>
/// </list>
/// Requests from another machine, and paths outside <c>/jfb-testing/</c>, go on to the site as if this did not exist.
/// </summary>
internal sealed class TestEndpointsMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    /// <summary>Handles one request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!TestEndpointsGate.IsTestPath(context.Request.Path) || !TestEndpointsGate.IsLocal(context))
        {
            await next(context);
            return;
        }

        var rest = context.Request.Path.Value![TestEndpointsGate.Prefix.Length..].Trim('/').ToLowerInvariant();
        switch (rest)
        {
            case "status" when HttpMethods.IsGet(context.Request.Method):
                await StatusAsync(context);
                return;
            case "clock":
                await ClockAsync(context);
                return;
            default:
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
        }
    }

    private static async Task StatusAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var time = services.GetService<TimeProvider>() ?? TimeProvider.System;
        var stub = services.GetService<StubHostedService>()?.Server?.BaseAddress;
        await context.Response.WriteAsJsonAsync(
            new { testHost = true, clock = services.GetService<TestClock>() is not null, utc = time.GetUtcNow(), stub },
            Json,
            context.RequestAborted);
    }

    private static async Task ClockAsync(HttpContext context)
    {
        if (context.RequestServices.GetService<TestClock>() is not { } clock)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (HttpMethods.IsDelete(context.Request.Method))
        {
            clock.Reset();
        }
        else if (HttpMethods.IsPost(context.Request.Method))
        {
            var request = await JsonSerializer.DeserializeAsync<ClockRequest>(context.Request.Body, Json, context.RequestAborted) ?? new ClockRequest();
            if (request.Utc is { } utc)
            {
                clock.SetNow(utc);
            }

            if (request.AdvanceSeconds is > 0 and var seconds)
            {
                clock.Advance(TimeSpan.FromSeconds(seconds));
            }
        }
        else if (!HttpMethods.IsGet(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        await context.Response.WriteAsJsonAsync(new { utc = clock.GetUtcNow(), offsetSeconds = clock.Offset.TotalSeconds }, Json, context.RequestAborted);
    }

    private sealed record ClockRequest(DateTimeOffset? Utc = null, double? AdvanceSeconds = null);
}
