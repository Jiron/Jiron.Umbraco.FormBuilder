using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;
using Umbraco.Cms.Web.Website.Caching;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Keeps a response out of every cache (ADR-0004, ADR-0104): <c>Cache-Control: no-store</c> for browsers, proxies and
/// CDNs, and the output cache's own switch for Umbraco's website cache, which holds even once the response has started.
/// </summary>
internal static class PageCaching
{
    /// <summary>The value of <c>Cache-Control</c> that no cache stores.</summary>
    public const string NoStoreValue = "no-store";

    /// <summary>
    /// Marks the response <c>no-store</c>: the header while it can still be set and again as the response starts, after
    /// anything else set it, and no storage in the output cache the request runs under, if any.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    public static void NoStore(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (httpContext.Features.Get<IOutputCacheFeature>() is { } cache)
        {
            cache.Context.AllowCacheStorage = false;
        }

        var response = httpContext.Response;
        if (response.HasStarted)
        {
            return;
        }

        response.Headers.CacheControl = NoStoreValue;
        response.OnStarting(() =>
        {
            response.Headers.CacheControl = NoStoreValue;
            response.Headers.Remove("Expires");
            return Task.CompletedTask;
        });
    }
}

/// <summary>
/// The vary rule of Umbraco's website output cache for the page after a post without JavaScript (ADR-0004, ADR-0104):
/// the cache's key ignores the query string, so a request that carries <c>jfb-sent</c>, in any letter case and with any
/// value or none, varies by it and is never stored. Any other request is left to the site's rules.
/// </summary>
internal sealed class SentVaryBy : IWebsiteOutputCacheVaryByProvider
{
    /// <inheritdoc />
    public void ConfigureVaryBy(HttpContext context, CacheVaryByRules rules)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);
        if (!context.Request.Query.ContainsKey(SentTokens.QueryName))
        {
            return;
        }

        rules.QueryKeys = StringValues.Concat(rules.QueryKeys, SentTokens.QueryName);
        PageCaching.NoStore(context);
    }
}
