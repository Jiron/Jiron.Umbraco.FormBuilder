using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// What every request to the public API meets (ADR-0012, ADR-0084): Jiron Form Builder's own CORS for one allowed
/// origin besides the site's own, <c>no-store</c> and <c>Vary: Origin</c> on every answer, <c>404 not_found</c> for an
/// address that names no endpoint, <c>413 payload_too_large</c> for a body an endpoint does not take, and
/// <c>500 server_error</c>, logged without the request's values, for what fails unexpectedly.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicApiFrameTests : PublicApiTestBase
{
    private const string Allowed = "https://shop.example.com";
    private const string Denied = "https://evil.example";

    [Test]
    public async Task AnAllowedOrigin_GetsItsCorsHeaders_WithoutCredentials()
    {
        var key = await PublishAsync();

        using var allowed = await GetAsync($"{Root}/forms/{key}", request => request.Headers.Add("Origin", "HTTPS://Shop.Example.com:443"));
        using var preflight = await SendAsync(HttpMethod.Options, $"{Root}/forms/{key}/entries", request =>
        {
            request.Headers.Add("Origin", Allowed);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "content-type");
        });

        Assert.Multiple(() =>
        {
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Header(allowed, "Access-Control-Allow-Origin"), Is.EqualTo(Allowed), "the origin as browsers send it");
            Assert.That(Header(allowed, "Access-Control-Expose-Headers"), Is.EqualTo("ETag, Retry-After"));
            Assert.That(Header(allowed, "Access-Control-Allow-Credentials"), Is.Null);
            Assert.That(Header(allowed, "Vary"), Does.Contain("Origin"));
            Assert.That(preflight.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(Header(preflight, "Access-Control-Allow-Origin"), Is.EqualTo(Allowed));
            Assert.That(Header(preflight, "Access-Control-Allow-Methods"), Is.EqualTo("GET, POST"));
            Assert.That(Header(preflight, "Access-Control-Allow-Headers"), Is.EqualTo("Content-Type"));
            Assert.That(Header(preflight, "Access-Control-Max-Age"), Is.EqualTo("600"));
            Assert.That(Header(preflight, "Access-Control-Allow-Credentials"), Is.Null);
        });
    }

    [Test]
    public async Task AnotherOrigin_GetsNothingItCanRead_AndCannotPost()
    {
        var key = await PublishAsync();

        using var get = await GetAsync($"{Root}/forms/{key}", request => request.Headers.Add("Origin", Denied));
        using var opaque = await GetAsync($"{Root}/forms/{key}", request => request.Headers.Add("Origin", "null"));
        using var preflight = await SendAsync(HttpMethod.Options, $"{Root}/forms/{key}/entries", request =>
        {
            request.Headers.Add("Origin", Denied);
            request.Headers.Add("Access-Control-Request-Method", "POST");
        });
        using var post = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads", request =>
        {
            request.Headers.Add("Origin", Denied);
            request.Content = new StringContent("file", Encoding.UTF8, "text/plain");
        });
        using var opaquePost = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads", request => request.Headers.Add("Origin", "null"));

        Assert.Multiple(async () =>
        {
            Assert.That((get.StatusCode, Header(get, "Access-Control-Allow-Origin")), Is.EqualTo((HttpStatusCode.OK, (string?)null)), "a GET is answered, and the browser hands it nothing");
            Assert.That((opaque.StatusCode, Header(opaque, "Access-Control-Allow-Origin")), Is.EqualTo((HttpStatusCode.OK, (string?)null)), "an opaque origin is another origin");
            Assert.That(await RefusalAsync(preflight), Is.EqualTo("403 origin_denied"));
            Assert.That(Header(preflight, "Access-Control-Allow-Origin"), Is.Null);
            Assert.That(await RefusalAsync(post), Is.EqualTo("403 origin_denied"), "a post a browser sends without asking first is refused before anything runs");
            Assert.That(await RefusalAsync(opaquePost), Is.EqualTo("403 origin_denied"));
            Assert.That(Header(post, "Cache-Control"), Is.EqualTo("no-store"));
        });
    }

    [Test]
    public async Task TheSitesOwnPages_AndRequestsWithoutAnOrigin_Pass()
    {
        var key = await PublishAsync();
        var own = Client.BaseAddress!.GetLeftPart(UriPartial.Authority);

        using var sameGet = await GetAsync($"{Root}/forms/{key}", request => request.Headers.Add("Origin", own));
        using var samePost = await SendAsync(HttpMethod.Post, $"{Root}/nothing", request => request.Headers.Add("Origin", own));
        using var serverPost = await SendAsync(HttpMethod.Post, $"{Root}/nothing");

        Assert.Multiple(async () =>
        {
            Assert.That((sameGet.StatusCode, Header(sameGet, "Access-Control-Allow-Origin")), Is.EqualTo((HttpStatusCode.OK, (string?)null)), "the site's own page needs no CORS header");
            Assert.That(await RefusalAsync(samePost), Is.EqualTo("404 not_found"), "past the origin check");
            Assert.That(await RefusalAsync(serverPost), Is.EqualTo("404 not_found"));
        });
    }

    [Test]
    public async Task AnAddressThatNamesNoEndpoint_IsTheApisNotFound()
    {
        using var get = await GetAsync($"{Root}/nothing/here");
        using var root = await GetAsync($"{Root}");
        using var head = await SendAsync(HttpMethod.Head, $"{Root}/nothing");
        using var options = await SendAsync(HttpMethod.Options, $"{Root}/forms");
        using var post = await SendAsync(HttpMethod.Post, $"{Root}/forms/contactUs");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(get), Is.EqualTo("404 not_found"));
            Assert.That(get.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(Header(get, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(await RefusalAsync(root), Is.EqualTo("404 not_found"));
            Assert.That((head.StatusCode, (await head.Content.ReadAsByteArrayAsync()).Length), Is.EqualTo((HttpStatusCode.NotFound, 0)));
            Assert.That(await RefusalAsync(options), Is.EqualTo("404 not_found"), "an OPTIONS that is no preflight");
            Assert.That(await RefusalAsync(post), Is.EqualTo("404 not_found"), "a method the endpoint at the address does not take");
        });
    }

    [Test]
    public async Task ABody_ToAnEndpointThatTakesNone_IsRefusedBeforeItIsRead()
    {
        var key = await PublishAsync();

        using var sized = await GetAsync($"{Root}/forms/{key}", request => request.Content = new StringContent("{}", Encoding.UTF8, "application/json"));
        using var empty = await GetAsync($"{Root}/forms/{key}", request => request.Content = new ByteArrayContent([]));

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(sized), Is.EqualTo("413 payload_too_large"));
            Assert.That(sized.Headers.ConnectionClose, Is.True, "the connection closes, so the rest of the body is never read");
            Assert.That(empty.StatusCode, Is.EqualTo(HttpStatusCode.OK), "an empty body is none");
        });
    }

    [Test]
    public async Task WhatFailsUnexpectedly_IsServerError_LoggedWithoutTheRequestsValues()
    {
        var key = await PublishAsync();
        Watched.Fail = true;

        using var response = await GetAsync($"{Root}/forms/{key}?culture=de-CH&contentKey={Guid.NewGuid()}");
        var logged = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(Web.PublicApi.FormBuilderPublicApiMiddleware).FullName).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(response), Is.EqualTo("500 server_error"));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(response.Headers.ETag, Is.Null);
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Error }));
            Assert.That(logged[0].Exception, Is.InstanceOf<InvalidOperationException>());
            Assert.That(logged[0].Message, Does.Contain("FormController.Definition").And.Not.Contain("de-CH").And.Not.Contain(key.ToString()));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration[FormBuilderOptions.SectionName + ":PublicApi:AllowedOrigins:0"] = Allowed;
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }
}
