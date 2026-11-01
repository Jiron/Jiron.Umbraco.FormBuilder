using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;

/// <summary>
/// Applies the rate limits after routing, only to endpoints that carry <see cref="FormBuilderRateLimitAttribute"/>
/// (ADR-0012): a refusal answers <c>429 rate_limited</c> with <c>Retry-After</c> before the body is read. Jiron Form
/// Builder's policies never enter the site's <c>RateLimiterOptions</c>, so a site's own <c>UseRateLimiter()</c> neither
/// counts these endpoints nor fails on names it does not know.
/// </summary>
/// <param name="limiter">The rate limits.</param>
internal sealed class FormBuilderRateLimitMiddleware(FormBuilderRateLimiter limiter) : IMiddleware
{
    /// <summary>The route value that names an endpoint's form.</summary>
    public const string FormRouteValue = "key";

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

        var decision = limiter.Take(context, declared.Policy, FormOf(context));
        if (decision.Allowed)
        {
            await next(context);
            return;
        }

        await FormBuilderPublicProblems.RateLimited(context, decision).WriteAsync(context);
    }

    // The form the route names; a route value that is no key names none, and its request costs a lookup of nothing.
    private static Guid? FormOf(HttpContext context) =>
        context.Request.RouteValues.TryGetValue(FormRouteValue, out var value)
        && Guid.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var form)
            ? form
            : null;
}
