using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The rate limiter's decision about a post that <see cref="FormPostFilesMiddleware"/> met before its body was read, so
/// the post's handler does not count it a second time (ADR-0137).
/// </summary>
internal sealed class FormPostRateLimit(RateLimitDecision decision, Guid form)
{
    private RateLimitDecision Decision { get; } = decision;

    private Guid Form { get; } = form;

    /// <summary>The decision this request carries for a form; null for one not met yet, also when it counted against another, as its address may name any.</summary>
    /// <param name="http">The request.</param>
    /// <param name="form">The form.</param>
    public static RateLimitDecision? Of(HttpContext http, Guid form) => http.Features.Get<FormPostRateLimit>() is { } limit && limit.Form == form ? limit.Decision : null;

    /// <summary>Keeps the decision with the request.</summary>
    /// <param name="http">The request.</param>
    /// <param name="decision">The decision.</param>
    /// <param name="form">The form it counted against.</param>
    public static void Set(HttpContext http, RateLimitDecision decision, Guid form) => http.Features.Set(new FormPostRateLimit(decision, form));
}
