using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Actions;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.Extensions.DependencyInjection;
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
/// ticket names a version of the form. Neither answer is kept. With 10.3f (ADR-0204): the sample answers start the pages,
/// a document or a ticket whose test entries run their actions needs <c>manageActions</c>, and a test entry the frame sent
/// is found by its submission id, its actions run once by who may manage them.
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

    [Test]
    public async Task SampleAnswers_StartThePages_AndTellThePreviewScriptToAddFiles()
    {
        var (form, definition) = await DraftAsync();
        var party = Field("repeater", "guests", "Guests");
        party["fields"] = new JsonArray(Field("text", "guest", "Guest"));
        Fields(definition, Field("text", "name", "Name"), Field("email", "email", "Email"), Field("signature", "signed", "Signature"), party);
        await SaveAsync(form, definition);

        var (_, sampled) = await PreviewAsync(form, "?sample=true");
        var (_, empty) = await PreviewAsync(form, string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(sampled.QuerySelector("[name='name']")?.GetAttribute("value"), Is.EqualTo("Name"), "a text's sample is its label");
            Assert.That(sampled.QuerySelector("[name='email']")?.GetAttribute("value"), Is.EqualTo("alex.berger@example.com"));
            Assert.That(sampled.QuerySelector("[name='signed.typed']")?.GetAttribute("value"), Is.EqualTo("Alex Berger"), "a typed name, which the page draws");
            Assert.That(sampled.QuerySelector("[name='guests[0].guest']")?.GetAttribute("value"), Is.EqualTo("Guest"), "a repeatable group's first row");
            Assert.That(sampled.QuerySelector("meta[name='jfb-preview']")?.GetAttribute("data-sample"), Is.EqualTo("true"));
            Assert.That(empty.QuerySelector("[name='name']")?.GetAttribute("value") ?? string.Empty, Is.Empty);
            Assert.That(empty.QuerySelector("meta[name='jfb-preview']")?.HasAttribute("data-sample"), Is.False);
        });
    }

    [Test]
    public async Task DocumentsAndTickets_WhoseTestEntriesRunActions_NeedManageActions()
    {
        var (form, definition) = await DraftAsync();
        Fields(definition, Field("text", "name", "Name"));
        var version = await SaveAsync(form, definition);
        var drafter = await DrafterAsync();
        var tickets = GetRequiredService<PreviewTickets>();

        var (_, runs) = await PreviewAsync(form, "?actions=true");
        var (_, plain) = await PreviewAsync(form, string.Empty);
        var ticket = await CallAsync("designer", HttpMethod.Post, $"form/{form}/preview/ticket", new { version, runActions = true });
        var drafterDocument = await drafter.CallAsync("drafter", HttpMethod.Get, $"form/{form}/preview?actions=true");
        var drafterTicket = await drafter.CallAsync("drafter", HttpMethod.Post, $"form/{form}/preview/ticket", new { version, runActions = true });
        var drafterPlain = await drafter.CallAsync("drafter", HttpMethod.Post, $"form/{form}/preview/ticket", new { version });

        Assert.Multiple(() =>
        {
            Assert.That(tickets.Read(TicketOf(runs))?.RunsActions, Is.True);
            Assert.That(tickets.Read(TicketOf(plain))?.RunsActions, Is.False);
            Assert.That(tickets.Read(ticket.Body!["ticket"]!.GetValue<string>())?.RunsActions, Is.True);
            Assert.That((drafterDocument.Outcome, drafterDocument.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")));
            Assert.That((drafterTicket.Outcome, drafterTicket.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")));
            Assert.That(drafterPlain.Outcome, Is.EqualTo("200"), "design alone gives a ticket whose entries run nothing");
        });
    }

    [Test]
    public async Task ATestEntry_IsFoundByItsSubmission_AndItsActionsRunOnce_ForWhoMayManageThem()
    {
        var action = Guid.NewGuid();
        var (form, definition) = await DraftAsync();
        Fields(definition, Field("text", "name", "Name"));
        definition["actions"] = new JsonArray(new JsonObject { ["key"] = action.ToString(), ["type"] = StandInEmailActionType.Alias, ["name"] = "Always" });
        var version = await SaveAsync(form, definition);
        var submissionId = await TestEntryAsync(form, version);
        var drafter = await DrafterAsync();

        var found = await CallAsync("designer", HttpMethod.Get, $"form/{form}/preview/entries/{submissionId}");
        var drafterFinds = await drafter.CallAsync("drafter", HttpMethod.Get, $"form/{form}/preview/entries/{submissionId}");
        var drafterRuns = await drafter.CallAsync("drafter", HttpMethod.Post, $"form/{form}/preview/entries/{submissionId}/actions");
        var run = await CallAsync("designer", HttpMethod.Post, $"form/{form}/preview/entries/{submissionId}/actions");
        var again = await CallAsync("designer", HttpMethod.Post, $"form/{form}/preview/entries/{submissionId}/actions");
        var after = await CallAsync("designer", HttpMethod.Get, $"form/{form}/preview/entries/{submissionId}");
        var other = await CallAsync("designer", HttpMethod.Get, $"form/{form}/preview/entries/{Guid.NewGuid()}");

        Assert.Multiple(() =>
        {
            Assert.That(found.Outcome, Is.EqualTo("200"), found.ToString());
            Assert.That(found.Body!["number"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(found.Body!["actionsRan"]!.GetValue<bool>(), Is.False);
            Assert.That(found.Body!["awaitsConfirmation"]!.GetValue<bool>(), Is.False, "its form asks for no confirmation");
            Assert.That(found.Body!["actions"]!.AsArray().Select(key => key!.GetValue<Guid>()), Is.EqualTo(new[] { action }));
            Assert.That(drafterFinds.Outcome, Is.EqualTo("200"), "design reads it");
            Assert.That((drafterRuns.Outcome, drafterRuns.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageActions")));
            Assert.That(run.Outcome, Is.EqualTo("204"), run.ToString());
            Assert.That(again.Outcome, Is.EqualTo("409 actions_ran"), again.ToString());
            Assert.That((after.Body!["actionsRan"]!.GetValue<bool>(), after.Body!["actions"]!.AsArray().Count), Is.EqualTo((true, 0)));
            Assert.That(other.Outcome, Is.EqualTo("404 test_entry_not_found"), other.ToString());
        });
    }

    // A persona with design alone (the designer manages actions too).
    private async Task<ManagementApiCaller> DrafterAsync()
    {
        await using (var scope = Services.CreateAsyncScope())
        {
            var drafters = await ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider)
                .EnsureUserGroupAsync(new TestUserGroup { Alias = "jfbDrafters", Name = "Drafters", Sections = { AccessPersonas.Section } });
            await GrantAsync(drafters.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.Design);
        }

        return new ManagementApiCaller(Client, Services, role => role == "drafter" ? ["jfbDrafters"] : [AccessPersonas.GroupOf(role)]);
    }

    // A test entry the preview frame sends with the version's ticket; its submission id.
    private async Task<Guid> TestEntryAsync(Guid form, int version)
    {
        var ticket = GetRequiredService<PreviewTickets>().Issue(form, version).Value;
        HttpRequestMessage FromFrame(string path, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/jfb/api/v1/forms/{form}/{path}") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation("Origin", "null");
            request.Headers.TryAddWithoutValidation("Authorization", $"{PreviewTickets.Scheme} {ticket}");
            return request;
        }

        using var challenged = await Client.SendAsync(FromFrame("challenge", "{}"));
        var challenge = JsonNode.Parse(await challenged.Content.ReadAsStringAsync())!["challenge"]!.GetValue<string>();
        var submissionId = Guid.NewGuid();
        using var sent = await Client.SendAsync(FromFrame("entries", $$"""{"submissionId":"{{submissionId}}","revision":1,"values":{"name":"Ada"},"challenge":"{{challenge}}"}"""));
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
        return submissionId;
    }

    // The ticket the document holds.
    private static string TicketOf(IDocument shown) => shown.QuerySelector("meta[name='jfb-preview']")!.GetAttribute("content")!;

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
