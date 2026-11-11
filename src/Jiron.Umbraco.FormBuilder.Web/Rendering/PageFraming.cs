using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Keeps a member's page out of another origin's frames (ADR-0004, ADR-0012, ADR-0231): a page that renders a form for
/// members or a member's values in a form, and the render endpoint's answer for such a form, sends a separate
/// <c>Content-Security-Policy: frame-ancestors 'self'</c> (never replacing the site's own policy) and
/// <c>X-Frame-Options: SAMEORIGIN</c>, as the response starts, so that headers the site's middleware adds later count too.
/// A response that already says how it may be framed, with an <c>X-Frame-Options</c> header or a policy with
/// <c>frame-ancestors</c>, keeps the site's word.
/// </summary>
internal static class PageFraming
{
    /// <summary>The policy that allows frames of the page's own origin alone.</summary>
    public const string FrameAncestors = "frame-ancestors 'self'";

    /// <summary>The older header's value to the same effect.</summary>
    public const string SameOrigin = "SAMEORIGIN";

    /// <summary>Marks the response, as it starts, as framed by its own origin alone, unless the site says otherwise.</summary>
    /// <param name="httpContext">The request.</param>
    public static void OwnOriginOnly(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        if (response.HasStarted || httpContext.Items.ContainsKey(typeof(PageFraming)))
        {
            return;
        }

        // A page may render several such forms; the headers go once.
        httpContext.Items[typeof(PageFraming)] = true;
        response.OnStarting(() =>
        {
            Apply(response.Headers);
            return Task.CompletedTask;
        });
    }

    // Both headers, unless the response says already how it may be framed.
    private static void Apply(IHeaderDictionary headers)
    {
        var said = !StringValues.IsNullOrEmpty(headers.XFrameOptions)
            || headers.ContentSecurityPolicy.Any(policy => policy?.Contains("frame-ancestors", StringComparison.OrdinalIgnoreCase) == true);
        if (said)
        {
            return;
        }

        headers.ContentSecurityPolicy = StringValues.Concat(headers.ContentSecurityPolicy, FrameAncestors);
        headers.XFrameOptions = SameOrigin;
    }
}
