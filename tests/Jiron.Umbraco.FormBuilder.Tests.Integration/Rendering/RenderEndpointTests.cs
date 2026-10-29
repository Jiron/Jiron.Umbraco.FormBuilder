using System.Net;
using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// <c>GET forms/{key}/render</c> behind Umbraco's website output cache (ROADMAP 6.2l, ADR-0004, ADR-0104): the published
/// form's markup as the page shows it at the place the request names, with the given instance id, in the page's
/// culture, for anyone or for the member signed in; never an outcome; <c>no-store</c>; and its refusals. The site reads no
/// culture from the query string, as a site may choose, so the culture render renders in is its own doing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RenderEndpointTests : OutputCacheTestBase
{
    [Test]
    public async Task Render_ReturnsThePublishedForm_WithTheGivenInstanceId_AsThePageShowsIt()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName with { Label = new LocalizedText("Name on {{ page.name }}") });
        var content = (await PagesAsync(CachePage))[CachePage];
        var culture = await GetRequiredService<ILanguageService>().GetDefaultIsoCodeAsync();
        var page = await ServedAsync(Client, "/cache/");

        var same = await RenderAsync(key, "jfb-1", "/cache/", content, culture);
        var seventh = await RenderAsync(key, "jfb-7", "/cache/", content, culture);
        await SaveAndPublishAsync(key, definition => WithFields(definition, RequiredName with { Label = new LocalizedText("Your full name") }));
        var changed = await RenderAsync(key, "jfb-1", "/cache/", content, culture);
        var stale = await ServedAsync(Visitor(), "/cache/");

        Assert.Multiple(() =>
        {
            Assert.That((same.Status, same.ContentType, same.Served.CacheControl, same.Served.SetsCookie), Is.EqualTo((HttpStatusCode.OK, "text/html; charset=utf-8", "no-store", false)));
            Assert.That(same.Served.Document.Body!.Children.Select(element => element.LocalName + "#" + element.Id), Is.EqualTo(new[] { "form#jfb-1" }), "the form alone");
            Assert.That(same.Served.Document.QuerySelectorAll("link, script[src]"), Is.Empty, "without the asset tags, which the page has");
            Assert.That(Markup(same.Served.Document.QuerySelector("form#jfb-1")!), Is.EqualTo(Markup(page.Document.QuerySelector("form#jfb-1")!)), "the page's markup, but for Umbraco's route token, which is new on every render");
            Assert.That(same.Served.Document.QuerySelector("label[for='jfb-1-name']")!.TextContent, Does.Contain("Name on cache"), "a visitor text that recalls the page, for the page the request names");
            Assert.That(seventh.Served.Document.QuerySelector("form#jfb-7")?.GetAttribute("action"), Is.EqualTo("/cache/#jfb-7"));
            Assert.That(seventh.Served.Document.QuerySelector("input#jfb-7-name"), Is.Not.Null, "its ids below the instance");
            Assert.That(changed.Served.Document.QuerySelector("form#jfb-1")!.GetAttribute("data-jfb-revision"), Is.EqualTo("2"), "the form as published now");
            Assert.That(changed.Served.Document.QuerySelector("label[for='jfb-1-name']")!.TextContent, Does.Contain("Your full name"));
            Assert.That((stale.FromCache, stale.Document.QuerySelector("form#jfb-1")!.GetAttribute("data-jfb-revision")), Is.EqualTo((true, "1")), "the page the runtime swaps it into came from the cache");
        });
    }

    [Test]
    public async Task Render_InThePagesCulture_IsTheGermanPagesMarkup()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName);
        var content = (await CulturePagesAsync("de-DE", "German", CachePage))[CachePage];
        var page = await ServedAsync(Client, "/cache/");

        var german = await RenderAsync(key, "jfb-1", "/cache/", content, "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(german.Served.Document.QuerySelector("form#jfb-1")!.GetAttribute("lang"), Is.EqualTo("de-DE"));
            Assert.That(german.Served.Document.QuerySelector("[data-test-culture]")!.GetAttribute("data-test-culture"), Is.EqualTo("de-DE de-DE"), "a site's own view renders in the page's culture, as on the page");
            Assert.That(Markup(german.Served.Document.QuerySelector("form#jfb-1")!), Is.EqualTo(Markup(page.Document.QuerySelector("form#jfb-1")!)));
        });
    }

    [Test]
    public async Task Render_OfAFormForMembers_ShowsAVisitorItsNotice_AndAMemberTheForm()
    {
        var key = await PublishFormAsync(CacheForm, definition => definition with { Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } } }, RequiredName);
        var content = (await PagesAsync(CachePage))[CachePage];

        var visitor = await RenderAsync(key, "jfb-1", "/cache/", content, null);
        await SignInMemberAsync("render-member@example.com");
        var member = await RenderAsync(key, "jfb-1", "/cache/", content, null);

        Assert.Multiple(() =>
        {
            Assert.That(visitor.Served.Document.QuerySelector("#jfb-1 .jfb-notice"), Is.Not.Null);
            Assert.That(visitor.Served.Document.QuerySelector("form"), Is.Null);
            Assert.That(member.Served.Document.QuerySelector("form#jfb-1"), Is.Not.Null);
            Assert.That((visitor.Served.CacheControl, member.Served.CacheControl), Is.EqualTo(("no-store", "no-store")));
        });
    }

    [Test]
    public async Task Render_ShowsNoOutcome_EvenWithTheTokenOfASentPost()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName);
        var content = (await PagesAsync(CachePage))[CachePage];
        var page = await ServedAsync(Client, "/cache/");
        var sent = await PostAsync(Client, "/cache/", Sent(page.Document.QuerySelector("form#jfb-1")!, ("name", "Ada")));
        var token = sent.Location!.Split('?')[1].Split('#')[0];

        var rendered = await RenderAsync(key, "jfb-1", "/cache/", content, null, "&" + token);

        Assert.Multiple(async () =>
        {
            Assert.That((await ServedAsync(Client, sent.Location)).Document.QuerySelector("#jfb-1 [data-jfb-outcome]"), Is.Not.Null, "the token shows the outcome on the page");
            Assert.That(rendered.Served.Document.QuerySelector("form#jfb-1"), Is.Not.Null);
            Assert.That(rendered.Served.Document.QuerySelector("[data-jfb-outcome]"), Is.Null);
        });
    }

    [Test]
    public async Task Render_RefusesWhatNamesNoPlaceOnAPage_AndAKeyThatNamesNoPublishedForm()
    {
        var key = await PublishFormAsync(CacheForm, null, RequiredName);
        var gone = await PublishFormAsync("cacheGone", null, RequiredName);
        _ = await Forms.UnpublishAsync(gone, (await Forms.GetDraftAsync(gone))!.Form.ConcurrencyVersion, Editor);
        var content = (await PagesAsync(CachePage))[CachePage];
        var place = $"page=%2Fcache%2F&contentKey={content}";

        var refusals = new Dictionary<string, string>
        {
            ["no instance"] = $"{Root}/forms/{key}/render?{place}",
            ["instance jfb-0"] = $"{Root}/forms/{key}/render?instance=jfb-0&{place}",
            ["instance one"] = $"{Root}/forms/{key}/render?instance=one&{place}",
            ["no page"] = $"{Root}/forms/{key}/render?instance=jfb-1&contentKey={content}",
            ["another site"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=https%3A%2F%2Fevil.example%2F",
            ["another host"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2F%2Fevil.example%2F",
            ["a query"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2Fcache%2F%3Fx%3D1",
            ["a content key that is no key"] = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2Fcache%2F&contentKey=nope",
            ["a culture that is none"] = $"{Root}/forms/{key}/render?instance=jfb-1&{place}&culture=not%20one",
            ["the alias"] = $"{Root}/forms/{CacheForm}/render?instance=jfb-1&{place}",
            ["a key without hyphens"] = $"{Root}/forms/{key:N}/render?instance=jfb-1&{place}",
            ["no form"] = $"{Root}/forms/{Guid.NewGuid()}/render?instance=jfb-1&{place}",
            ["an unpublished form"] = $"{Root}/forms/{gone}/render?instance=jfb-1&{place}",
        };
        var answers = new Dictionary<string, string>();
        foreach (var (name, path) in refusals)
        {
            using var response = await GetAsync(path);
            answers[name] = await RefusalAsync(response) + ", " + Header(response, "Cache-Control");
        }

        Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
        {
            ["no instance"] = "400 invalid_request, no-store",
            ["instance jfb-0"] = "400 invalid_request, no-store",
            ["instance one"] = "400 invalid_request, no-store",
            ["no page"] = "400 invalid_request, no-store",
            ["another site"] = "400 invalid_request, no-store",
            ["another host"] = "400 invalid_request, no-store",
            ["a query"] = "400 invalid_request, no-store",
            ["a content key that is no key"] = "400 invalid_request, no-store",
            ["a culture that is none"] = "400 invalid_request, no-store",
            ["the alias"] = "404 form_not_found, no-store",
            ["a key without hyphens"] = "404 form_not_found, no-store",
            ["no form"] = "404 form_not_found, no-store",
            ["an unpublished form"] = "404 form_not_found, no-store",
        }));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.PostConfigure<RequestLocalizationOptions>(options =>
            options.RequestCultureProviders = [.. options.RequestCultureProviders.Where(provider => provider is not QueryStringRequestCultureProvider)]);
    }

    // The render endpoint's answer for a place on a page.
    private async Task<Rendered> RenderAsync(Guid key, string instance, string page, Guid content, string? culture, string extra = "")
    {
        var path = $"{Root}/forms/{key}/render?instance={instance}&page={Uri.EscapeDataString(page)}&contentKey={content}" + (culture is null ? string.Empty : "&culture=" + culture) + extra;
        using var response = await Client.GetAsync(path);
        var served = await Served.ReadAsync(response);
        Assert.That(served.Status, Is.EqualTo(HttpStatusCode.OK), served.Html);
        return new Rendered(served.Status, response.Content.Headers.ContentType?.ToString(), served);
    }

    // A form's markup with Umbraco's route token blanked, which Data Protection makes new on every render.
    private static string Markup(IElement form)
    {
        var copy = (IElement)form.Clone();
        copy.QuerySelector("input[name='" + FormPostFields.RouteToken + "']")?.SetAttribute("value", string.Empty);
        return copy.OuterHtml;
    }

    private sealed record Rendered(HttpStatusCode Status, string? ContentType, Served Served);
}
