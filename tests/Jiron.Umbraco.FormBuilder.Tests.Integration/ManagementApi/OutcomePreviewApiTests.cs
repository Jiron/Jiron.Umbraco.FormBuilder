using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// An outcome's live preview through the management API (ROADMAP 10.1c, ADR-0187): the draft the outcome's sidebar holds,
/// rendered for the sample entry as a real entry's outcome is, and written by the site's own view: a message's tags hold
/// the sample's answers, a sensitive one masked; the reference has the sample's number; the copy of the answers lists
/// them; a redirect after the message waits with "Go now" and its countdown; a redirect alone shows no page, and one that
/// cannot be followed shows the thank-you message visitors get then. It changes nothing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OutcomePreviewApiTests : ManagementApiTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    [Test]
    public async Task AMessage_ShowsTheSampleEntrysAnswers_InPlaceOfItsTags_ASensitiveOneMasked()
    {
        var (form, definition) = await DraftAsync();
        var email = Field("email", "email", "Email");
        email["sensitive"] = true;
        var topic = Field("dropdown", "topic", "Topic");
        topic["options"] = JsonNode.Parse("""{"items":[{"value":"delivery","label":{"":"Delivery"}},{"value":"press","label":{"":"Press"}}]}""");
        Fields(definition, Field("name", "name", "Your name"), email, topic);
        var outcome = Outcome(definition, """{"kind":"message","message":{"":"<p>Thanks, {{ field.name }} ({{ field.email }}), about {{ field.topic }}. Number {{ entry.number }}.</p>"}}""");

        var answer = await PreviewAsync(form, definition, outcome);

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var shown = Html(answer);
        Assert.Multiple(() =>
        {
            Assert.That(shown.QuerySelector("[data-jfb-outcome] > p")!.TextContent, Is.EqualTo($"Thanks, {SampleEntries.GivenName} {SampleEntries.FamilyName} ({MergeTagAnswerTexts.Masked}), about Delivery. Number {SampleEntries.Number}."));
            Assert.That(shown.QuerySelector("[data-jfb-reference]")!.TextContent, Is.EqualTo("Your reference is 2026-00042"), "the sample's number in the form's format, the year of now");
            Assert.That(shown.QuerySelector("[data-jfb-continue]"), Is.Null, "no redirect, no link on");
            Assert.That(shown.QuerySelector(".jfb-summary"), Is.Null, "no copy unless the outcome offers it");
            Assert.That(answer.Body!["address"], Is.Null);
            Assert.That(answer.Body["delaySeconds"]!.GetValue<int>(), Is.Zero);
            Assert.That(answer.Body["direction"]!.GetValue<string>(), Is.EqualTo("ltr"));
        });
    }

    [Test]
    public async Task ACopyOfTheAnswers_ListsTheSample_AsARealEntrysCopyDoes_ASensitiveAnswerMasked()
    {
        var (form, definition) = await DraftAsync();
        var email = Field("email", "email", "Email");
        email["sensitive"] = true;
        Fields(definition, Field("name", "name", "Your name"), email, Field("file", "upload", "Upload"));
        var outcome = Outcome(definition, """{"kind":"message","message":{"":"<p>Thanks.</p>"},"showAnswers":true}""");

        var answer = await PreviewAsync(form, definition, outcome);

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var shown = Html(answer);
        var rows = shown.QuerySelectorAll(".jfb-summary dl > div").Select(row => (row.QuerySelector("dt")!.TextContent, row.QuerySelector("dd")!)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.Item1), Is.EqualTo(new[] { "Your name", "Email", "Upload" }), "every field the copy lists, as a real entry's copy");
            Assert.That((rows[2].Item2.HasAttribute("data-jfb-empty"), rows[2].Item2.TextContent.Trim()), Is.EqualTo((true, "Not answered")), "a file the sample cannot answer");
            Assert.That(rows[0].Item2.TextContent.Trim(), Is.EqualTo($"{SampleEntries.GivenName} {SampleEntries.FamilyName}"));
            Assert.That(rows[1].Item2.HasAttribute("data-jfb-masked"), Is.True);
            Assert.That(rows[1].Item2.TextContent, Does.Not.Contain(SampleEntries.Email), "a sensitive answer never shows");
            Assert.That(shown.QuerySelector("[data-jfb-print]")!.HasAttribute("hidden"), Is.True, "the runtime reveals it, as after a send");
            Assert.That(shown.QuerySelector("[data-jfb-copy] a")!.TextContent, Is.EqualTo("Fill in the form again"));
        });
    }

    [Test]
    public async Task ARedirectAfterTheMessage_WaitsWithGoNowAndItsCountdown_AtTheSamplesAddress()
    {
        var (form, definition) = await DraftAsync();
        Fields(definition, Field("name", "name", "Your name"));
        var outcome = Outcome(definition, """{"kind":"message","message":{"":"<p>Thanks.</p>"},"redirect":{"url":"/thanks?who={{ field.name }}","delaySeconds":5}}""");
        var waiting = await PreviewAsync(form, definition, outcome);
        outcome["redirect"]!["delaySeconds"] = 0;
        var waitingForThem = await PreviewAsync(form, definition, outcome);

        Assert.That(waiting.Outcome, Is.EqualTo("200"), waiting.ToString());
        var shown = Html(waiting);
        var address = waiting.Body!["address"]!.GetValue<string>();
        Assert.Multiple(() =>
        {
            Assert.That(address, Is.EqualTo("/thanks?who=Alex%20Berger"), "the address's tags in the URL context");
            Assert.That(waiting.Body["delaySeconds"]!.GetValue<int>(), Is.EqualTo(5));
            Assert.That(shown.QuerySelector("a[data-jfb-continue]")!.GetAttribute("href"), Is.EqualTo(address));
            Assert.That(shown.QuerySelector("a[data-jfb-continue]")!.TextContent, Is.EqualTo("Go now"));
            Assert.That(shown.QuerySelector("[data-jfb-countdown]")!.GetAttribute("data-jfb-other"), Is.EqualTo("Taking you to the next page in {0} seconds."));
            Assert.That(shown.QuerySelector("[data-jfb-stay]")!.TextContent, Is.EqualTo("Stay here"));
            Assert.That(Html(waitingForThem).QuerySelector("[data-jfb-countdown]"), Is.Null, "without a wait, Go now waits for the visitor");
            Assert.That(Html(waitingForThem).QuerySelector("a[data-jfb-continue]"), Is.Not.Null);
        });
    }

    [Test]
    public async Task ARedirect_ShowsNoPage_AndOneThatCannotBeFollowed_TheThankYouVisitorsGetThen()
    {
        var (form, definition) = await DraftAsync();
        var outcome = Outcome(definition, """{"kind":"redirect","redirect":{"url":"/press/thanks"}}""");
        var redirect = await PreviewAsync(form, definition, outcome);
        outcome["redirect"]!["url"] = "javascript:alert(1)";
        var refused = await PreviewAsync(form, definition, outcome);

        Assert.Multiple(() =>
        {
            Assert.That(redirect.Outcome, Is.EqualTo("200"), redirect.ToString());
            Assert.That(redirect.Body!["html"], Is.Null);
            Assert.That(redirect.Body["address"]!.GetValue<string>(), Is.EqualTo("/press/thanks"));
            Assert.That(refused.Outcome, Is.EqualTo("200"), refused.ToString());
            Assert.That(refused.Body!["address"], Is.Null);
            Assert.That(Html(refused).QuerySelector("[data-jfb-outcome] > p")!.TextContent, Is.EqualTo("Thank you. We have received your message."));
        });
    }

    [Test]
    public async Task ALanguageOfTheSite_GetsItsWordsItsMessageAndItsDirection_AndOneTheSiteLacks_TheDefaultLanguage()
    {
        await GetRequiredService<ILanguageService>().CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        await GetRequiredService<ILanguageService>().CreateAsync(new Language("he-IL", "Hebrew"), Constants.Security.SuperUserKey);
        var (form, definition) = await DraftAsync();
        var outcome = Outcome(definition, """{"kind":"message","message":{"":"<p>Thanks.</p>","de-DE":"<p>Danke.</p>"}}""");

        var german = Html(await PreviewAsync(form, definition, outcome, "de-DE"));
        var unknown = Html(await PreviewAsync(form, definition, outcome, "xx-XX"));
        var hebrew = await PreviewAsync(form, definition, outcome, "he-IL");

        Assert.Multiple(() =>
        {
            Assert.That(german.QuerySelector("[data-jfb-outcome] > p")!.TextContent, Is.EqualTo("Danke."));
            Assert.That(german.QuerySelector("[data-jfb-reference]")!.TextContent, Is.EqualTo("Ihre Referenz lautet 2026-00042"));
            Assert.That(unknown.QuerySelector("[data-jfb-outcome] > p")!.TextContent, Is.EqualTo("Thanks."));
            Assert.That(unknown.QuerySelector("[data-jfb-reference]")!.TextContent, Is.EqualTo("Your reference is 2026-00042"));
            Assert.That(hebrew.Body!["direction"]!.GetValue<string>(), Is.EqualTo("rtl"), "a language written from the right, as its page is");
        });
    }

    [Test]
    public async Task ThePreview_ChangesNothing_AndRefusesADefinitionItCannotRead_AnOutcomeItLacks_AndAFormThatIsNone()
    {
        var (form, definition) = await DraftAsync();
        var before = (await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!.ToJsonString();
        var version = (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion;
        var outcome = Outcome(definition, """{"kind":"message","message":{"":"<p>Changed.</p>"}}""");

        var shown = await PreviewAsync(form, definition, outcome);
        var unreadable = await CallAsync("designer", HttpMethod.Post, $"form/{form}/outcome-preview", new { definition = new JsonArray(), outcome = outcome["key"]!.GetValue<string>() });
        var lacking = await CallAsync("designer", HttpMethod.Post, $"form/{form}/outcome-preview", new { definition, outcome = Guid.NewGuid() });
        var none = await CallAsync("designer", HttpMethod.Post, $"form/{Guid.NewGuid()}/outcome-preview", new { definition, outcome = outcome["key"]!.GetValue<string>() });

        Assert.Multiple(async () =>
        {
            Assert.That(shown.Outcome, Is.EqualTo("200"), shown.ToString());
            Assert.That(unreadable.Status, Is.EqualTo(HttpStatusCode.BadRequest), unreadable.ToString());
            Assert.That(unreadable.Body?["errors"]?.AsArray(), Is.Not.Empty, unreadable.ToString());
            Assert.That(lacking.Status, Is.EqualTo(HttpStatusCode.NotFound), lacking.ToString());
            Assert.That(lacking.Body?["code"]?.GetValue<string>(), Is.EqualTo(FormBuilderProblems.OutcomeNotFoundCode));
            Assert.That(none.Status, Is.EqualTo(HttpStatusCode.NotFound), none.ToString());
            Assert.That((await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion, Is.EqualTo(version), "nothing saved");
            Assert.That((await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!.ToJsonString(), Is.EqualTo(before));
        });
    }

    private async Task<(Guid Form, JsonNode Definition)> DraftAsync()
    {
        GetRequiredService<TestClock>().SetNow(Now);
        var form = (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Previewed"))).Key;
        var draft = await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        Assert.That(draft.Outcome, Is.EqualTo("200"), draft.ToString());
        return (form, draft.Body!["definition"]!.DeepClone());
    }

    private Task<ApiAnswer> PreviewAsync(Guid form, JsonNode definition, JsonNode outcome, string? culture = null) =>
        CallAsync("designer", HttpMethod.Post, $"form/{form}/outcome-preview", new { definition, outcome = outcome["key"]!.GetValue<string>(), culture });

    private static IDocument Html(ApiAnswer answer) => new HtmlParser().ParseDocument(answer.Body!["html"]!.GetValue<string>());

    // The definition's one outcome, replaced by this one with a key of its own.
    private static JsonNode Outcome(JsonNode definition, string json)
    {
        var outcome = JsonNode.Parse(json)!;
        outcome["key"] = Guid.NewGuid().ToString();
        definition["outcomes"] = new JsonArray(outcome);
        return outcome;
    }

    private static void Fields(JsonNode definition, params JsonObject[] fields) => definition["pages"]![0]!["groups"]![0]!["fields"] = new JsonArray(fields);

    private static JsonObject Field(string type, string alias, string label) =>
        new() { ["key"] = Guid.NewGuid().ToString(), ["type"] = type, ["alias"] = alias, ["label"] = new JsonObject { [string.Empty] = label } };
}
