using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The rate limiter's decision about a post that <see cref="FormPostFilesMiddleware"/> met before its body was read, so
/// the post's handler does not count it a second time (ADR-0137).
/// </summary>
internal sealed class FormPostRateLimit(RateLimitDecision decision)
{
    private RateLimitDecision Decision { get; } = decision;

    /// <summary>The decision this request carries; null for one not met yet.</summary>
    /// <param name="http">The request.</param>
    public static RateLimitDecision? Of(HttpContext http) => http.Features.Get<FormPostRateLimit>()?.Decision;

    /// <summary>Keeps the decision with the request.</summary>
    /// <param name="http">The request.</param>
    /// <param name="decision">The decision.</param>
    public static void Set(HttpContext http, RateLimitDecision decision) => http.Features.Set(new FormPostRateLimit(decision));
}
