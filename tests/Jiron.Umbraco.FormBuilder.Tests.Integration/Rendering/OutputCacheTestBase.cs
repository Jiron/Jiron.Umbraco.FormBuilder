using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Website.Caching;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// Pages behind Umbraco's website output cache (ADR-0004, ADR-0104), turned on for five minutes, with a request filter
/// that caches a signed-in member's request too, as a site may have one, so what the tests see stay out is kept out by
/// Jiron Form Builder's own rules and not by Umbraco's default filter. The test page <c>cache</c> shows a stamp written
/// on every render, so a response from the cache shows the stamp of the render it stored, and the form
/// <c>cacheForm</c>.
/// </summary>
public abstract class OutputCacheTestBase : RenderingTestBase
{
    /// <summary>The test page.</summary>
    private protected const string CachePage = "cache";

    /// <summary>The alias of the form the test page shows.</summary>
    private protected const string CacheForm = "cacheForm";

    private readonly List<HttpClient> _visitors = [];

    /// <summary>A required name, which a post leaves empty to fail.</summary>
    private protected static FormField RequiredName { get; } = NameField with { Required = true };

    /// <summary>The site's own origin, as a browser names it.</summary>
    private protected string OwnOrigin => Client.BaseAddress!.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// Another visitor, with no cookie and a browser of their own, at the test client's address, or over https when
    /// <paramref name="secure"/> says so.
    /// </summary>
    private protected HttpClient Visitor(bool secure = false)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = secure ? new Uri("https://localhost/") : Client.BaseAddress!,
        });
        _visitors.Add(client);
        return client;
    }

    /// <summary>A response of the site as a cache test reads it.</summary>
    private protected static async Task<Served> ServedAsync(HttpClient client, string path, Action<HttpRequestMessage>? prepare = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        prepare?.Invoke(request);
        using var response = await client.SendAsync(request);
        return await Served.ReadAsync(response);
    }

    /// <summary>Posts names and values to a path as a browser without JavaScript does, from the site's own origin.</summary>
    private protected static async Task<Served> PostAsync(HttpClient client, string path, IEnumerable<KeyValuePair<string, string>> pairs)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new FormUrlEncodedContent(pairs) };
        request.Headers.TryAddWithoutValidation("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        using var response = await client.SendAsync(request);
        return await Served.ReadAsync(response);
    }

    /// <summary>
    /// What a browser posts for a form sent with Send: every named input and text area with its value, but boxes, radios,
    /// buttons and files, the answers given in place of theirs, and the Send button.
    /// </summary>
    private protected static List<KeyValuePair<string, string>> Sent(IElement form, params (string Name, string Value)[] answers)
    {
        var posted = new List<KeyValuePair<string, string>>();
        foreach (var control in form.QuerySelectorAll("input, textarea"))
        {
            if (control.GetAttribute("name") is not { Length: > 0 } name || control is IHtmlInputElement { Type: "checkbox" or "radio" or "submit" or "button" or "file" })
            {
                continue;
            }

            var given = answers.Where(answer => answer.Name == name).Select(answer => answer.Value).FirstOrDefault();
            posted.Add(new(name, given ?? (control as IHtmlInputElement)?.Value ?? ((IHtmlTextAreaElement)control).Value));
        }

        posted.Add(new(FormPostFields.Nav, FormPostFields.Submit));
        return posted;
    }

    [TearDown]
    public void DisposeTheVisitors()
    {
        foreach (var client in _visitors)
        {
            client.Dispose();
        }

        _visitors.Clear();
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        configuration["Umbraco:CMS:Website:OutputCache:Enabled"] = "true";
        configuration["Umbraco:CMS:Website:OutputCache:ContentDuration"] = "00:05:00";
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IWebsiteOutputCacheRequestFilter, CachesSignedInMembers>();
    }

    /// <summary>A response: its status, its caching headers, the page's stamp and its markup.</summary>
    /// <param name="Status">The status.</param>
    /// <param name="CacheControl">The <c>Cache-Control</c> header, or null.</param>
    /// <param name="FromCache">Whether it came from the output cache (it has an <c>Age</c> header).</param>
    /// <param name="SetsCookie">Whether it sets a cookie.</param>
    /// <param name="Location">Where it redirects to, or null.</param>
    /// <param name="Bytes">Its body.</param>
    /// <param name="Document">Its body, parsed.</param>
    private protected sealed record Served(HttpStatusCode Status, string? CacheControl, bool FromCache, bool SetsCookie, string? Location, byte[] Bytes, IHtmlDocument Document)
    {
        /// <summary>The test page's stamp, or null for another body.</summary>
        public string? Stamp => Document.QuerySelector("#rendered")?.TextContent;

        /// <summary>The body as text.</summary>
        public string Html => System.Text.Encoding.UTF8.GetString(Bytes);

        /// <summary>Reads a response.</summary>
        public static async Task<Served> ReadAsync(HttpResponseMessage response)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return new Served(
                response.StatusCode,
                response.Headers.CacheControl?.ToString(),
                response.Headers.Age is not null,
                response.Headers.Contains("Set-Cookie"),
                response.Headers.Location?.OriginalString,
                bytes,
                await new HtmlParser().ParseDocumentAsync(System.Text.Encoding.UTF8.GetString(bytes)));
        }
    }

    /// <summary>A site's request filter that caches a signed-in member's request too; preview stays out.</summary>
    /// <param name="umbraco">Umbraco's context.</param>
    private protected sealed class CachesSignedInMembers(IUmbracoContextAccessor umbraco) : DefaultWebsiteOutputCacheRequestFilter(umbraco)
    {
        /// <inheritdoc />
        protected override bool ShouldExcludeAuthenticated(HttpContext context) => false;
    }
}
