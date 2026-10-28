using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Gives Jiron Form Builder a request's Content Security Policy nonce, for a site whose CSP middleware keeps it somewhere
/// a view cannot pass it on (ADR-0004, ADR-0092). <c>&lt;jfb-assets&gt;</c> without a <c>nonce</c> attribute, and the
/// assets <c>&lt;jfb-render&gt;</c> includes by itself, put the nonce on every tag they write, and the runtime's imports
/// inherit it. Register one with the site's services, for example <c>services.AddSingleton&lt;IFormBuilderCspNonceProvider,
/// MyNonceProvider&gt;()</c>; there is none by default.
/// </summary>
/// <remarks>
/// A nonce belongs only on a page that is never cached: a cached page keeps the nonce of the request that rendered it,
/// and the browser then refuses the script. Pages behind an output cache need only <c>script-src 'self'</c>.
/// </remarks>
public interface IFormBuilderCspNonceProvider
{
    /// <summary>The request's nonce, or null or empty when the request has none.</summary>
    /// <param name="httpContext">The request.</param>
    string? GetNonce(HttpContext httpContext);
}
