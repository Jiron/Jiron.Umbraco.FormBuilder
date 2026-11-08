using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Serves the website assets from the Web assembly before Umbraco's routing (ADR-0092), which owns the path
/// <c>/jfb/assets/</c> in any casing: only the names the assembly holds, compared exactly; the Brotli or gzip copy the
/// request accepts; a year's caching under the file's fingerprint, and <c>no-cache</c> otherwise; <c>nosniff</c> on every
/// answer; and CORS for any origin, as the files are public and fetched without credentials.
/// </summary>
internal sealed class WebsiteAssetsMiddleware(WebsiteAssets assets, LibraryThemes libraryThemes) : IMiddleware
{
    private const string Immutable = "public, max-age=31536000, immutable";
    private const string Revalidate = "no-cache";
    private const string Methods = "GET, HEAD";

    /// <inheritdoc />
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var request = context.Request;
        if (!request.Path.StartsWithSegments(WebsiteAssets.BasePath, StringComparison.OrdinalIgnoreCase, out var rest))
        {
            await next(context);
            return;
        }

        var response = context.Response;
        response.Headers.XContentTypeOptions = "nosniff";
        if (LibraryThemeLayers.AliasOf(rest.Value) is { } alias)
        {
            await LayerAsync(context, alias);
            return;
        }

        if (rest.Value is not { Length: > 1 } path || assets.Find(path[1..]) is not { } asset)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = Methods;
            return;
        }

        var headers = request.GetTypedHeaders();
        var copy = WebsiteAssets.CopyFor(asset, headers.AcceptEncoding);
        var current = asset.NamedByContent
            || (request.Query.TryGetValue("v", out var version) && version.Count == 1 && string.Equals(version[0], asset.Fingerprint, StringComparison.Ordinal));
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        response.Headers.Vary = HeaderNames.AcceptEncoding;
        response.Headers.CacheControl = current ? Immutable : Revalidate;
        response.Headers.ETag = copy.ETag;
        if (headers.IfNoneMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || string.Equals(tag.Tag.Value, copy.ETag, StringComparison.Ordinal)))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        response.ContentType = asset.ContentType;
        if (copy.Coding is { } coding)
        {
            response.Headers.ContentEncoding = coding;
        }

        response.ContentLength = copy.Bytes.Length;
        if (HttpMethods.IsGet(request.Method))
        {
            await response.Body.WriteAsync(copy.Bytes, context.RequestAborted);
        }
    }

    // A library theme's layer (ADR-0215), as the package's stylesheets are served: for good under the version the theme
    // has now, else revalidated, as an address with an older version is answered with what the theme holds now.
    private async Task LayerAsync(HttpContext context, string alias)
    {
        var request = context.Request;
        var response = context.Response;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = Methods;
            return;
        }

        if (await libraryThemes.FindAsync(alias, context.RequestAborted) is not { } theme)
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var current = request.Query.TryGetValue("v", out var version) && version.Count == 1
            && string.Equals(version[0], theme.Version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var tag = $"\"{theme.LayerTag}\"";
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        response.Headers.CacheControl = current ? Immutable : Revalidate;
        response.Headers.ETag = tag;
        if (request.GetTypedHeaders().IfNoneMatch.Any(match => match.Equals(EntityTagHeaderValue.Any) || string.Equals(match.Tag.Value, tag, StringComparison.Ordinal)))
        {
            response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(theme.Layer);
        response.ContentType = "text/css; charset=utf-8";
        response.ContentLength = bytes.Length;
        if (HttpMethods.IsGet(request.Method))
        {
            await response.Body.WriteAsync(bytes, context.RequestAborted);
        }
    }
}
