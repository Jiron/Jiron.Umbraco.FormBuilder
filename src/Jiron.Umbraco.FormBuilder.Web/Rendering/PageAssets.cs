using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The tags that load the website assets into a page (ADR-0004, ADR-0092), written at most once per request. A
/// <c>&lt;jfb-assets&gt;</c> writes them as it runs; a form's own tags decide when the page is written (ADR-0093). Razor
/// runs a view before its layout and writes the layout's head before the view's body, so a <c>&lt;jfb-assets&gt;</c>
/// anywhere on the page wins, and without one the tags appear once, before the first form.
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

    /// <summary>
    /// The request's tags as a form includes them: content that writes them, with the site's nonce, when the page is
    /// written, unless they were written before; written twice, it writes them once.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    public IHtmlContent Automatic(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return new AutomaticTags(this, httpContext);
    }

    private sealed class AutomaticTags(PageAssets assets, HttpContext httpContext) : IHtmlContent
    {
        public void WriteTo(TextWriter writer, HtmlEncoder encoder) => assets.TagsOnce(httpContext, null)?.WriteTo(writer, encoder);
    }
}
