using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The preview frame's requests to the public API (ROADMAP 10.3e, ADR-0203): the frame's origin is the opaque
/// <c>null</c>, which a preflight lets send its ticket and which passes only with a ticket that holds; one without a
/// ticket is refused as before, and a ticket that is malformed, expired, another form's or sent to an endpoint the preview
/// does not use answers <c>401 preview_ticket_invalid</c>. A ticket that holds reads the version it names, a draft never
/// published among them and not a later save, admits a form for members as a test member, and asks no puzzle.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormPreviewTests : PublicApiTestBase
{
    private static readonly Guid Page = Guid.Parse("30000000-0000-4000-8000-000000001031");

    private TestClock Clock => GetRequiredService<TestClock>();

    private PreviewTickets Tickets => GetRequiredService<PreviewTickets>();

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    [Test]
    public async Task TheNullOrigin_IsRefusedWithoutATicket_AsBefore_WhileAPreflightFromItMaySendOne()
    {
        var (key, _) = await DraftAsync();

        using var refused = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket: null, body: "{}");
        using var asked = await PreflightAsync(key, "null");
        using var elsewhere = await PreflightAsync(key, "https://elsewhere.test");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(refused), Is.EqualTo("403 origin_denied"));
            Assert.That(Header(refused, "Access-Control-Allow-Origin"), Is.Null);
            Assert.That(asked.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(Header(asked, "Access-Control-Allow-Origin"), Is.EqualTo("null"));
            Assert.That(Header(asked, "Access-Control-Allow-Headers"), Is.EqualTo("Content-Type, Authorization"));
            Assert.That(Header(asked, "Access-Control-Allow-Methods"), Is.EqualTo("GET, POST"));
            Assert.That(await RefusalAsync(elsewhere), Is.EqualTo("403 origin_denied"), "another origin is refused as before");
        });
    }

    [Test]
    public async Task ATicketThatDoesNotHold_IsRefused_Malformed_Expired_AnotherFormsOr_SentWhereThePreviewDoesNotAsk()
    {
        var (key, version) = await DraftAsync();
        var (other, otherVersion) = await DraftAsync("Another form");
        var ticket = Tickets.Issue(key, version).Value;

        using var malformed = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", "not-a-ticket", "{}");
        using var schemeAlone = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request =>
        {
            request.Headers.TryAddWithoutValidation("Origin", "null");
            request.Headers.TryAddWithoutValidation("Authorization", PreviewTickets.Scheme);
            request.Content = Json("{}");
        });
        using var anotherForms = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", Tickets.Issue(other, otherVersion).Value, "{}");
        using var entries = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", ticket, """{"revision":1,"values":{}}""");
        using var drafts = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/drafts", ticket, """{"revision":1,"values":{}}""");
        using var render = await FromFrameAsync(HttpMethod.Get, $"{Root}/forms/{key}/render?instance=jfb-1&page=/", ticket);
        using var byAlias = await FromFrameAsync(HttpMethod.Get, $"{Root}/forms/{(await Forms.GetAsync(key))!.Alias}", ticket);
        using var held = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");
        Clock.Advance(PreviewTickets.Lifetime - TimeSpan.FromSeconds(1));
        using var stillHeld = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");
        Clock.Advance(TimeSpan.FromSeconds(1));
        using var expired = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");

        Assert.Multiple(async () =>
        {
            foreach (var answer in new[] { malformed, schemeAlone, anotherForms, entries, drafts, render, byAlias, expired })
            {
                Assert.That(await RefusalAsync(answer), Is.EqualTo("401 preview_ticket_invalid"), answer.RequestMessage?.RequestUri?.ToString());
                Assert.That(Header(answer, "WWW-Authenticate"), Is.EqualTo(PreviewTickets.Scheme));
                Assert.That(Header(answer, "Access-Control-Allow-Origin"), Is.EqualTo("null"), "the frame may read why");
            }

            Assert.That(held.StatusCode, Is.EqualTo(HttpStatusCode.OK), await held.Content.ReadAsStringAsync());
            Assert.That(stillHeld.StatusCode, Is.EqualTo(HttpStatusCode.OK), "ten minutes, to the second");
        });
    }

    [Test]
    public async Task ATicketThatHolds_ReadsTheVersionItNames_ADraftNeverPublished_AndNotALaterSave()
    {
        var (key, version) = await DraftAsync();
        var ticket = Tickets.Issue(key, version).Value;

        using var visitors = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json("{}"));
        using var challenge = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");
        var token = (await JsonAsync(challenge))["challenge"]!.GetValue<string>();
        using var missing = await ValidateAsync(key, ticket, token, """{}""");
        using var answered = await ValidateAsync(key, ticket, token, """{"name":"Ada"}""");

        // A save after the preview opened leaves what its ticket reads as it was.
        var draft = (await Forms.GetDraftAsync(key))!;
        var phone = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "phone", Label = new LocalizedText("Phone"), Required = true };
        await Forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, WithFields(draft.Definition!, NameField with { Required = true }, phone), null, Editor);
        using var afterTheSave = await ValidateAsync(key, ticket, token, """{"name":"Ada"}""");
        using var aNewTicket = await ValidateAsync(key, Tickets.Issue(key, (await Forms.GetDraftAsync(key))!.DraftVersion).Value, token, """{"name":"Ada"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(visitors), Is.EqualTo("404 form_not_found"), "visitors get no draft");
            Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.OK), await challenge.Content.ReadAsStringAsync());
            Assert.That(Header(challenge, "Access-Control-Allow-Origin"), Is.EqualTo("null"));
            Assert.That(await RefusalAsync(missing), Is.EqualTo("422 validation_failed"));
            Assert.That((await JsonAsync(missing))["errors"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("name"));
            Assert.That(answered.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await answered.Content.ReadAsStringAsync());
            Assert.That(afterTheSave.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "the version the ticket names, without the phone");
            Assert.That(await RefusalAsync(aNewTicket), Is.EqualTo("422 validation_failed"), "the new draft asks for the phone");
        });
    }

    [Test]
    public async Task APublishMeanwhile_LeavesThePagesCheck_ToTheVersionTheTicketNames()
    {
        var (key, version) = await DraftAsync();
        var ticket = Tickets.Issue(key, version).Value;
        using var challenge = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");
        var token = (await JsonAsync(challenge))["challenge"]!.GetValue<string>();

        // Another tab renames the field and publishes: revision 1 is now a version without "name".
        var draft = (await Forms.GetDraftAsync(key))!;
        var renamed = NameField with { Alias = "fullName", Required = true };
        var saved = (await Forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, WithFields(draft.Definition!, renamed), null, Editor))!;
        _ = (await Forms.PublishAsync(key, saved.Form.ConcurrencyVersion, null, Editor))!;

        using var page = await ValidateAsync(key, ticket, token, """{"name":"Ada"}""");

        Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await page.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task APublishedFormsTicket_ReadsItsDraft_NotTheVersionVisitorsGet()
    {
        var key = await PublishAsync();
        var draft = (await Forms.GetDraftAsync(key))!;
        var topic = new FormField
        {
            Key = Guid.NewGuid(),
            Type = "dropdown",
            Alias = "topic",
            Label = new LocalizedText("Topic"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "press", Label = new LocalizedText("Press") }] },
        };
        await Forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, WithFields(draft.Definition!, NameField, topic), null, Editor);
        var ticket = Tickets.Issue(key, (await Forms.GetDraftAsync(key))!.DraftVersion).Value;

        using var visitors = await GetAsync($"{Root}/forms/{key}/options/{topic.Key}");
        using var preview = await FromFrameAsync(HttpMethod.Get, $"{Root}/forms/{key}/options/{topic.Key}", ticket);
        using var challenge = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");
        var token = (await JsonAsync(challenge))["challenge"]!.GetValue<string>();

        // The draft of a form published once renders revision 2, the one it would get, and its page checks with its topic.
        using var page = await ValidateAsync(key, ticket, token, """{"name":"Ada","topic":"press"}""", revision: 2, page: draft.Definition!.Pages[0].Key);

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(visitors), Is.EqualTo("404 not_found"), "the version visitors get has no such field");
            Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK), await preview.Content.ReadAsStringAsync());
            Assert.That((await JsonAsync(preview))["options"]![0]!["value"]!.GetValue<string>(), Is.EqualTo("press"));
            Assert.That(Header(preview, "Access-Control-Allow-Origin"), Is.EqualTo("null"));
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await page.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task ATicket_StandsInForATestMember_OnAFormForMembers_AndAsksNoPuzzle()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>();
        await settings.SaveAsync(current.Value with { Level = ProtectionLevel.ProofOfWork, ProofBits = 8 }, current.Version, null);
        var (key, version) = await DraftAsync(change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        });
        var ticket = Tickets.Issue(key, version).Value;

        using var challenge = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, "{}");
        var answer = await JsonAsync(challenge);
        using var page = await ValidateAsync(key, ticket, answer["challenge"]!.GetValue<string>(), """{"name":"Ada"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(challenge.StatusCode, Is.EqualTo(HttpStatusCode.OK), answer.ToJsonString());
            Assert.That(answer["member"], Is.Null, "a test member has no values");
            Assert.That(answer["proofOfWork"], Is.Null, "the ticket shows who asks");
            Assert.That(GetRequiredService<ChallengeTokens>().Read(answer["challenge"]!.GetValue<string>(), key, TimeSpan.Zero)!.Member, Is.Null);
            Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await page.Content.ReadAsStringAsync());
        });
    }

    // A form never published whose draft holds the required name on one page; its draft's version.
    private async Task<(Guid Key, int Version)> DraftAsync(string name = "Previewed", Func<FormDefinition, FormDefinition>? change = null)
    {
        var created = await Forms.CreateAsync(new NewFormRequest(name, UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(created.Key))!;
        var definition = WithFields(draft.Definition!, NameField with { Required = true });
        definition = (change ?? (d => d))(definition with { Pages = [definition.Pages[0] with { Key = Page }] });
        await Forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, definition, null, Editor);
        return (created.Key, (await Forms.GetDraftAsync(created.Key))!.DraftVersion);
    }

    // A request as the preview frame sends it: from the origin null, with the ticket when there is one.
    private Task<HttpResponseMessage> FromFrameAsync(HttpMethod method, string path, string? ticket, string? body = null) =>
        SendAsync(method, path, request =>
        {
            request.Headers.TryAddWithoutValidation("Origin", "null");
            if (ticket is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"{PreviewTickets.Scheme} {ticket}");
            }

            if (body is not null)
            {
                request.Content = Json(body);
            }
        });

    // The page's check as the frame sends it, with the revision its markup carries: a draft never published renders 1,
    // on the page its draft holds.
    private Task<HttpResponseMessage> ValidateAsync(Guid key, string ticket, string challenge, string values, int revision = 1, Guid? page = null) =>
        FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/validate", ticket, $$$"""{"revision":{{{revision}}},"pageKey":"{{{page ?? Page}}}","values":{{{values}}},"challenge":"{{{challenge}}}"}""");

    private Task<HttpResponseMessage> PreflightAsync(Guid key, string origin) =>
        SendAsync(HttpMethod.Options, $"{Root}/forms/{key}/challenge", request =>
        {
            request.Headers.TryAddWithoutValidation("Origin", origin);
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "POST");
            request.Headers.TryAddWithoutValidation("Access-Control-Request-Headers", "authorization, content-type");
        });

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
