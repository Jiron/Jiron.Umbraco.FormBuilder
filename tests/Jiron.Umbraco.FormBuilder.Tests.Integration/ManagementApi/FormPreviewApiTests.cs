using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The preview through the management API (ROADMAP 10.3e, ADR-0203): the saved draft, never published, as a page of the
/// site shows it, in a document of its own with the base rules and its theme's stylesheet, the preview script before the
/// runtime, a ticket that holds for the draft's version, and a form that posts nowhere; in a language of the site or the
/// default one, dark only where the theme offers the dark scheme. A draft whose rules have errors is not shown. A new
/// ticket names a version of the form. Neither answer is kept.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormPreviewApiTests : ManagementApiTestBase
{
    [Test]
    public async Task ThePreview_IsTheSavedDraftAsAPageShowsIt_WithItsTicketItsAssets_AndAFormThatPostsNowhere()
    {
        var (form, definition) = await DraftAsync("Contact </title><b>us</b>");
        var order = Field("ranking", "order", "Order");
        order["options"] = JsonNode.Parse("""{"items":[{"value":"a","label":{"":"A"}},{"value":"b","label":{"":"B"}}]}""");
        Fields(definition, Field("text", "name", "Name", "Ihr Name"), order);
        var version = await SaveAsync(form, definition);

        using var response = await Api.SendAsync("designer", $"form/{form}/preview");
        var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var shown = Html(answer);
        var meta = shown.QuerySelector("meta[name='jfb-preview']")!;
        var ticket = GetRequiredService<PreviewTickets>().Read(meta.GetAttribute("content"));
        var styles = shown.QuerySelectorAll("link[rel='stylesheet']").Select(link => Path(link.GetAttribute("href"))).ToList();
        var scripts = shown.QuerySelectorAll("head script").Select(script => (script.GetAttribute("type"), Path(script.GetAttribute("src")))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), answer.ToJsonString());
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True, "a ticket is never kept");
            Assert.That(answer["version"]!.GetValue<int>(), Is.EqualTo(version));
            Assert.That(answer["culture"]!.GetValue<string>(), Is.EqualTo("en-US"));
            Assert.That(answer["darkScheme"]!.GetValue<bool>(), Is.True, "the default theme offers it");
            Assert.That(answer["lifetimeSeconds"]!.GetValue<int>(), Is.EqualTo(600));
            Assert.That((ticket?.FormKey, ticket?.Version), Is.EqualTo(((Guid?)form, (int?)version)), "a ticket that holds for the draft's version");
            Assert.That(meta.GetAttribute("data-form"), Is.EqualTo(form.ToString("D")));
            Assert.That(meta.GetAttribute("data-api"), Is.EqualTo("/jfb/api/v1"));
            Assert.That(meta.GetAttribute("data-lifetime"), Is.EqualTo("600"));
            Assert.That(styles, Is.EqualTo(new[] { "/jfb/assets/themes/base.css", "/jfb/assets/themes/default.css" }));
            Assert.That(scripts, Is.EqualTo(new[] { ("module", "/jfb/assets/preview/jfb-preview.js"), ("module", "/jfb/assets/runtime/jfb-runtime.js") }), "the preview script first, so the runtime's first request carries the ticket");
            Assert.That(shown.DocumentElement.GetAttribute("lang"), Is.EqualTo("en-US"));
            Assert.That(shown.Title, Is.EqualTo("Contact </title><b>us</b>"), "titled with the form's name, as text");
            Assert.That(shown.QuerySelector("head style")!.TextContent, Does.StartWith("html{color-scheme:light;background:Canvas;color:CanvasText}html.jfb-scheme-dark{color-scheme:dark}"), "the ground's colours said outright, which follow the scheme");
            Assert.That(shown.DocumentElement.ClassList, Does.Not.Contain("jfb-scheme-dark"));
            Assert.That(shown.QuerySelector("form.jfb-form")!.HasAttribute("action"), Is.False, "no address to post to, as the headless element's");
            Assert.That(shown.QuerySelectorAll(".jfb-field-ranking button"), Is.Not.Empty);
            Assert.That(shown.QuerySelectorAll("[formaction]"), Is.Empty, "nor have its buttons");
            Assert.That(shown.QuerySelector("form.jfb-form")!.GetAttribute("data-jfb-revision"), Is.EqualTo("1"), "the revision the draft would get once published");
            Assert.That(shown.QuerySelector(".jfb-label")!.TextContent.Trim(), Does.StartWith("Name"));
        });
    }

    [Test]
    public async Task ALanguageOfTheSite_AndTheDarkScheme_ShowAsAsked_ALanguageTheSiteLacksTheDefault_AndAThemeWithoutItLight()
    {
        await GetRequiredService<ILanguageService>().CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        var (form, definition) = await DraftAsync();
        Fields(definition, Field("text", "name", "Name", "Ihr Name"));
        await SaveAsync(form, definition);
        var (plain, plainDefinition) = await DraftAsync("Plain");
        Fields(plainDefinition, Field("text", "name", "Name", "Ihr Name"));
        plainDefinition["settings"]!["appearance"]!["theme"] = "bootstrap5";
        await SaveAsync(plain, plainDefinition);

        var german = await PreviewAsync(form, "?culture=de-DE&dark=true");
        var unknown = await PreviewAsync(form, "?culture=xx-XX");
        var themed = await PreviewAsync(plain, "?dark=true");

        Assert.Multiple(() =>
        {
            Assert.That(german.Answer["culture"]!.GetValue<string>(), Is.EqualTo("de-DE"));
            Assert.That(german.Shown.DocumentElement.GetAttribute("lang"), Is.EqualTo("de-DE"));
            Assert.That(german.Shown.DocumentElement.ClassList, Does.Contain("jfb-scheme-dark"));
            Assert.That(german.Shown.QuerySelector(".jfb-label")!.TextContent.Trim(), Does.StartWith("Ihr Name"));
            Assert.That(unknown.Answer["culture"]!.GetValue<string>(), Is.EqualTo("en-US"));
            Assert.That(unknown.Shown.QuerySelector(".jfb-label")!.TextContent.Trim(), Does.StartWith("Name"));
            Assert.That(themed.Answer["darkScheme"]!.GetValue<bool>(), Is.False);
            Assert.That(themed.Shown.DocumentElement.ClassList, Does.Not.Contain("jfb-scheme-dark"), "a theme without the dark scheme shows light");
            Assert.That(themed.Shown.QuerySelector("form.jfb-form")!.ClassList, Does.Contain("jfb-theme-bootstrap5"));
        });
    }

    [Test]
    public async Task AFormForMembers_ShowsTheFormToTheTestMember_NotTheSignInNotice()
    {
        var (form, definition) = await DraftAsync();
        Fields(definition, Field("text", "name", "Name"));
        definition["settings"]!["availability"]!["members"] = "members";
        await SaveAsync(form, definition);

        var (_, shown) = await PreviewAsync(form, string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(shown.QuerySelector("form.jfb-form .jfb-label"), Is.Not.Null, "the form, as for a member it admits");
            Assert.That(shown.QuerySelector(".jfb-notice"), Is.Null);
        });
    }

    [Test]
    public async Task ADraftWhoseRulesHaveErrors_IsNotShown_AndAFormThatIsNone_IsNotFound()
    {
        var (form, definition) = await DraftAsync();
        var shown = Field("text", "name", "Name");
        shown["rules"] = JsonNode.Parse("""[{"key":"40000000-0000-4000-8000-000000000001","effect":"show","conditions":[{"field":"30000000-0000-4000-8000-0000000000ff","op":"isNotEmpty"}]}]""");
        Fields(definition, shown);
        await SaveAsync(form, definition);

        var broken = await CallAsync("designer", HttpMethod.Get, $"form/{form}/preview");
        var none = await CallAsync("designer", HttpMethod.Get, $"form/{Guid.NewGuid()}/preview");

        Assert.Multiple(() =>
        {
            Assert.That(broken.Outcome, Is.EqualTo("422 rules_invalid"), broken.ToString());
            Assert.That(none.Outcome, Is.EqualTo("404 form_not_found"), none.ToString());
        });
    }

    [Test]
    public async Task ANewTicket_NamesAVersionOfTheForm_AndAVersionItLacks_IsNotFound()
    {
        var (form, definition) = await DraftAsync();
        Fields(definition, Field("text", "name", "Name"));
        var version = await SaveAsync(form, definition);

        var response = await CallAsync("designer", HttpMethod.Post, $"form/{form}/preview/ticket", new { version });
        var answer = response.Body!;
        var ticket = GetRequiredService<PreviewTickets>().Read(answer["ticket"]!.GetValue<string>());
        var missing = await CallAsync("designer", HttpMethod.Post, $"form/{form}/preview/ticket", new { version = version + 50 });
        var nothing = await CallAsync("designer", HttpMethod.Post, $"form/{form}/preview/ticket", new { version = 0 });
        var none = await CallAsync("designer", HttpMethod.Post, $"form/{Guid.NewGuid()}/preview/ticket", new { version });

        Assert.Multiple(() =>
        {
            Assert.That(response.Outcome, Is.EqualTo("200"), response.ToString());
            Assert.That(response.NoStore, Is.True, "a ticket is never kept");
            Assert.That((ticket?.FormKey, ticket?.Version), Is.EqualTo(((Guid?)form, (int?)version)));
            Assert.That(answer["lifetimeSeconds"]!.GetValue<int>(), Is.EqualTo(600));
            Assert.That(missing.Outcome, Is.EqualTo("404 version_not_found"), missing.ToString());
            Assert.That(nothing.Outcome, Is.EqualTo("404 version_not_found"), nothing.ToString());
            Assert.That(none.Outcome, Is.EqualTo("404 form_not_found"), none.ToString());
        });
    }

    private async Task<(Guid Form, JsonNode Definition)> DraftAsync(string name = "Previewed")
    {
        var form = (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest(name))).Key;
        var draft = await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        Assert.That(draft.Outcome, Is.EqualTo("200"), draft.ToString());
        return (form, draft.Body!["definition"]!.DeepClone());
    }

    // Saves the draft through the API and gives its version's number.
    private async Task<int> SaveAsync(Guid form, JsonNode definition)
    {
        var concurrency = (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion;
        var saved = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = concurrency, definition });
        Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
        return (await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.DraftVersion;
    }

    private async Task<(JsonNode Answer, IDocument Shown)> PreviewAsync(Guid form, string query)
    {
        var answer = await CallAsync("designer", HttpMethod.Get, $"form/{form}/preview{query}");
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        return (answer.Body!, Html(answer.Body!));
    }

    private static IDocument Html(JsonNode answer) => new HtmlParser().ParseDocument(answer["document"]!.GetValue<string>());

    // An address without its fingerprint.
    private static string? Path(string? address) => address?.Split('?')[0];

    private static void Fields(JsonNode definition, params JsonObject[] fields) => definition["pages"]![0]!["groups"]![0]!["fields"] = new JsonArray(fields);

    private static JsonObject Field(string type, string alias, string label, string? german = null)
    {
        var labels = new JsonObject { [string.Empty] = label };
        if (german is not null)
        {
            labels["de-DE"] = german;
        }

        return new() { ["key"] = Guid.NewGuid().ToString(), ["type"] = type, ["alias"] = alias, ["label"] = labels };
    }
}
