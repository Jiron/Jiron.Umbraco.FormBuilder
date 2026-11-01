using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;

/// <summary>
/// Applies the rate limits after routing, only to endpoints that carry <see cref="FormBuilderRateLimitAttribute"/>
/// (ADR-0012): a refusal answers <c>429 rate_limited</c> with <c>Retry-After</c> before the body is read. Jiron Form
/// Builder's policies never enter the site's <c>RateLimiterOptions</c>, so a site's own <c>UseRateLimiter()</c> neither
/// counts these endpoints nor fails on names it does not know. A request that names a form counts its address here, which
/// takes no table; its form's limits and the site's take a read, so they wait for the public API's filter, after the check of the
/// migrations and within the request's time limit (ADR-0149).
/// </summary>
/// <param name="limiter">The rate limits.</param>
internal sealed class FormBuilderRateLimitMiddleware(FormBuilderRateLimiter limiter) : IMiddleware
{
    /// <summary>The route value that names an endpoint's form.</summary>
    public const string FormRouteValue = "key";

    /// <summary>The route value that names an endpoint's form by its key or its alias.</summary>
    public const string KeyOrAliasRouteValue = "keyOrAlias";

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.GetEndpoint()?.Metadata.GetMetadata<FormBuilderRateLimitAttribute>() is not { } declared)
        {
            await next(context);
            return;
        }

        RateLimitDecision decision;
        if (NamedBy(context) is { } named)
        {
            var count = limiter.Start(context, declared.Policy);
            context.Features.Set(new FormLimitsToTake(count, named));
            decision = count.Refusal ?? RateLimitDecision.Go;
        }
        else
        {
            decision = limiter.Take(context, declared.Policy, null);
        }

        if (decision.Allowed)
        {
            await next(context);
            return;
        }

        await FormBuilderPublicProblems.RateLimited(context, decision).WriteAsync(context);
    }

    // What the route names as its form; a route value that is no key names none. Null for a route that names no form.
    private static string? NamedBy(HttpContext context)
    {
        var values = context.Request.RouteValues;
        if (values.TryGetValue(KeyOrAliasRouteValue, out var named))
        {
            return Convert.ToString(named, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return values.TryGetValue(FormRouteValue, out var value)
            ? (Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var form) ? form.ToString("D") : string.Empty)
            : null;
    }
}
