using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What every request to the public API meets before Umbraco's routing (ADR-0006, ADR-0012, ADR-0084), and only those:
/// <list type="bullet">
/// <item>a form feature with no form content, so no part of the site reads the body before its endpoint does;</item>
/// <item><c>Cache-Control: no-store</c>, which only the form endpoint replaces, and <c>Vary: Origin</c>;</item>
/// <item>Jiron Form Builder's own CORS, for the base path only: the site's own pages always pass, its https pages too
/// when a proxy ends TLS before the site and the request comes over http; a preflight from an
/// allowed origin gets <c>GET, POST</c>, <c>Content-Type</c> and ten minutes without credentials, any other
/// <c>403 origin_denied</c>; an allowed origin's request gets <c>Access-Control-Allow-Origin</c> and sees <c>ETag</c>
/// and <c>Retry-After</c>; another origin's <c>GET</c> gets no CORS headers, so the browser hands it nothing, and its
/// other requests <c>403 origin_denied</c> before anything runs, since a browser sends some posts without asking first.
/// A request without an <c>Origin</c>, from a server or a page without JavaScript, passes;</item>
/// <item>the answers to what ends a request early: <c>504 timeout</c> when its time limit ran out, none when the visitor
/// left, and <c>server_error</c>, logged without the request's values, for anything unexpected.</item>
/// </list>
/// </summary>
/// <param name="paths">The public API's paths.</param>
/// <param name="logger">Logs what went wrong unexpectedly.</param>
/// <param name="serverKeys">The server keys a headless server signs with (ADR-0126); none in a test that leaves them out.</param>
internal sealed partial class FormBuilderPublicApiMiddleware(FormBuilderPublicApiPaths paths, ILogger<FormBuilderPublicApiMiddleware> logger, ServerKeys? serverKeys = null) : IMiddleware
{
    /// <summary>How long a browser keeps a preflight's answer.</summary>
    public const int PreflightSeconds = 600;

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!context.Request.Path.StartsWithSegments(paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Features.Set<IFormFeature>(NoFormFeature.Instance);
        var request = context.Request;
        var response = context.Response;
        response.Headers.CacheControl = "no-store";
        response.Headers.Append("Vary", "Origin");

        var origin = OriginOf(request);
        if (origin is not null && HttpMethods.IsOptions(request.Method) && request.Headers.ContainsKey("Access-Control-Request-Method"))
        {
            await PreflightAsync(context, origin);
            return;
        }

        if (origin is not null)
        {
            if (origin.Allowed)
            {
                if (!origin.Same)
                {
                    response.Headers.AccessControlAllowOrigin = origin.Value;
                    response.Headers.AccessControlExposeHeaders = "ETag, Retry-After";
                }
            }
            else if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
            {
                await FormBuilderPublicProblems.OriginDenied().WriteAsync(context);
                return;
            }
        }

        // A server key signs the request, or the request is refused: a revoked key is never taken for an anonymous call.
        if (serverKeys?.Check(request) is { } signed && signed.Status != ServerKeyStatus.None)
        {
            if (signed.Status == ServerKeyStatus.Invalid)
            {
                LogServerKeyInvalid(logger, request.Path.Value ?? string.Empty);
                response.Headers.WWWAuthenticate = ServerKeys.Scheme;
                await FormBuilderPublicProblems.ServerKeyInvalid().WriteAsync(context);
                return;
            }

            context.Features.Set(new ServerKeyRequest(signed.Name!));
        }

        var left = context.RequestAborted;
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (left.IsCancellationRequested)
        {
            // The visitor left: nobody waits for an answer.
        }
        catch (OperationCanceledException) when (context.Features.Get<FormBuilderTimeLimitFeature>() is { TimedOut: true } && !response.HasStarted)
        {
            Reset(response);
            await FormBuilderPublicProblems.Timeout().WriteAsync(context);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !response.HasStarted)
        {
            LogUnexpected(logger, context.GetEndpoint()?.DisplayName ?? "no endpoint", exception);
            Reset(response);
            await FormBuilderPublicProblems.ServerError().WriteAsync(context);
        }
    }

    // A preflight answers itself: the allowed origins get what they may send, the others are refused.
    private static async Task PreflightAsync(HttpContext context, RequestOrigin origin)
    {
        var response = context.Response;
        if (!origin.Allowed)
        {
            await FormBuilderPublicProblems.OriginDenied().WriteAsync(context);
            return;
        }

        response.StatusCode = StatusCodes.Status204NoContent;
        response.Headers.AccessControlAllowOrigin = origin.Value;
        response.Headers.AccessControlAllowMethods = "GET, POST";
        response.Headers.AccessControlAllowHeaders = "Content-Type";
        response.Headers.AccessControlMaxAge = PreflightSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // The request's origin, if it names one: whether it is the site's own, and whether it may call the API. An origin
    // that is no origin, such as "null", or several of them, may not.
    private RequestOrigin? OriginOf(HttpRequest request)
    {
        StringValues values = request.Headers.Origin;
        if (StringValues.IsNullOrEmpty(values))
        {
            return null;
        }

        var sent = values.Count == 1 ? values[0] : null;
        if (PublicApiRules.Origin(sent) is not { } origin)
        {
            return new RequestOrigin(sent ?? string.Empty, Same: false, Allowed: false);
        }

        // A proxy that ends TLS before the site hands it the site's own https pages' requests over http.
        var same = origin == PublicApiRules.Origin($"{request.Scheme}://{request.Host.Value}")
            || (!request.IsHttps && origin == PublicApiRules.Origin($"https://{request.Host.Value}"));
        return new RequestOrigin(origin, same, same || paths.AllowedOrigins.Contains(origin));
    }

    // What an endpoint set before it failed, except what every answer of the API carries.
    private static void Reset(HttpResponse response)
    {
        response.Headers.ETag = StringValues.Empty;
        response.Headers.CacheControl = "no-store";
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The public API's endpoint {Endpoint} failed unexpectedly; the visitor got server_error.")]
    private static partial void LogUnexpected(ILogger logger, string endpoint, Exception exception);
    [LoggerMessage(Level = LogLevel.Warning, Message = "A request to {Path} carried a server key that matches none; it got server_key_invalid.")] private static partial void LogServerKeyInvalid(ILogger logger, string path);

    private sealed record RequestOrigin(string Value, bool Same, bool Allowed);
}

/// <summary>
/// A form feature with no form content (ADR-0006): under the public API's base path it tells every part of the site
/// that the request has no form, so the body stays a stream that only its endpoint reads.
/// </summary>
internal sealed class NoFormFeature : IFormFeature
{
    /// <summary>The one instance; it holds nothing.</summary>
    public static readonly NoFormFeature Instance = new();

    private NoFormFeature()
    {
    }

    /// <inheritdoc />
    public bool HasFormContentType => false;

    /// <inheritdoc />
    public IFormCollection? Form
    {
        get => FormCollection.Empty;
        set { }
    }

    /// <inheritdoc />
    public IFormCollection ReadForm() => FormCollection.Empty;

    /// <inheritdoc />
    public Task<IFormCollection> ReadFormAsync(CancellationToken cancellationToken) => Task.FromResult<IFormCollection>(FormCollection.Empty);
}

/// <summary>An endpoint's time limit, which the public API's filter starts and its middleware reads when a request is cancelled.</summary>
/// <param name="limit">Cancelled when the time limit runs out.</param>
internal sealed class FormBuilderTimeLimitFeature(CancellationToken limit)
{
    /// <summary>Whether the time limit ran out.</summary>
    public bool TimedOut => limit.IsCancellationRequested;
}
