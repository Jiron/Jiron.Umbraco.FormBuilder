using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The tags that load the website assets into a page (ADR-0004, ADR-0092), written at most once per request: by the
/// first <c>&lt;jfb-assets&gt;</c>, or else by the first <c>&lt;jfb-render&gt;</c> (6.2b), whichever comes first.
/// </summary>
internal sealed class PageAssets(WebsiteAssets assets)
{
    // The key under which a request remembers that its tags were written.
    private const string Written = "Jiron.FormBuilder.PageAssets.Written";

    /// <summary>Whether the request's tags were written.</summary>
    /// <param name="httpContext">The request.</param>
    public static bool AreWritten(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.ContainsKey(Written);
    }

    /// <summary>
    /// The request's tags, each with the nonce: the one given, else the site's <see cref="IFormBuilderCspNonceProvider"/>'s,
    /// else none (an empty nonce is none). Null when they were written before in this request.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="nonce">The nonce the page gives, or null.</param>
    public IHtmlContent? TagsOnce(HttpContext httpContext, string? nonce)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (!httpContext.Items.TryAdd(Written, true))
        {
            return null;
        }

        if (string.IsNullOrEmpty(nonce))
        {
            nonce = httpContext.RequestServices.GetService<IFormBuilderCspNonceProvider>()?.GetNonce(httpContext);
        }

        var script = new TagBuilder("script");
        script.Attributes["type"] = "module";
        script.Attributes["src"] = assets.Url(httpContext.Request.PathBase, WebsiteAssets.Runtime);
        if (!string.IsNullOrEmpty(nonce))
        {
            script.Attributes["nonce"] = nonce;
        }

        return script;
    }
}
