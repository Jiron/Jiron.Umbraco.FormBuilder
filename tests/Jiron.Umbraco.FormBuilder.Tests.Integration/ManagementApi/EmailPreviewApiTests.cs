using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// An email action's live preview through the management API (ROADMAP 10.2b, ADR-0189): the mail of the draft the action's
/// sidebar holds, as the team gets it for the sample entry, rendered as a run renders it: its tags hold the sample's answers,
/// a sensitive one masked unless the action includes it; it goes to the team's addresses and those the sample's answer
/// chooses, with its copies and reply-to, and the address a mail of its own goes to; it has the site's sender, the
/// address the host locks, and the action's layout. A mail only a field's answer gets is the visitor's, which carries no
/// copy and no reply-to, its copies the team's mail of their own, as a run sends them. It queues and logs nothing, changes
/// nothing, and refuses settings the type cannot read.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EmailPreviewApiTests : ManagementApiTestBase
{
    private const string Locks = FormBuilderOptions.SectionName + ":Designer:SettingLocks:";
    private const string Locked = "locked@example.com";

    [Test]
    public async Task TheTeamsMail_HasTheSamplesAnswers_InTheSitesSenderAndLayout_WithEveryoneItGoesTo()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var email = await settings.GetAsync<EmailSettings>();
        await settings.SaveAsync(email.Value with { SenderName = "Example", SenderAddress = "forms@example.com" }, email.Version, null);
        var (form, definition) = await DraftAsync();
        var (_, address, topic) = ContactFields(definition);
        var action = Action(definition, $$$"""
            {"to":["sales@example.com"],"toField":"{{{address}}}","recipientMap":{"field":"{{{topic}}}","routes":[{"value":"delivery","to":["delivery@example.com"]},{"value":"press","to":["press@example.com"]}]},
             "cc":["boss@example.com"],"bcc":["audit@example.com"],"replyToField":"{{{address}}}",
             "subject":{"":"New from {{ field.name }}"},"body":{"":"<p>{{ field.name }} wrote about {{ field.topic }}.</p>"}}
            """);

        var answer = await PreviewAsync(form, definition, action);

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var body = answer.Body!;
        var sample = $"{SampleEntries.GivenName} {SampleEntries.FamilyName}";
        Assert.Multiple(() =>
        {
            Assert.That(body["from"]!.GetValue<string>(), Does.Contain("Example").And.Contain(Locked).And.Not.Contain("forms@example.com"), "the site's sender's name, at the address the host locks, as a run sends it");
            Assert.That(Texts(body["to"]), Is.EqualTo(new[] { "sales@example.com", "delivery@example.com" }), "the team's address, then the one the sample's first option chooses");
            Assert.That(body["visitor"]!.GetValue<string>(), Is.EqualTo(SampleEntries.Email), "the sample's answer gets a mail of its own");
            Assert.That(Texts(body["cc"]), Is.EqualTo(new[] { "boss@example.com" }));
            Assert.That(Texts(body["bcc"]), Is.EqualTo(new[] { "audit@example.com" }));
            Assert.That(body["replyTo"]!.GetValue<string>(), Is.EqualTo(SampleEntries.Email));
            Assert.That(body["subject"]!.GetValue<string>(), Is.EqualTo($"New from {sample}"));
            Assert.That(body["html"]!.GetValue<string>(), Does.Contain($"{sample} wrote about Delivery.").And.Contain("prefers-color-scheme"), "the body in the layout, with its dark colours");
            Assert.That(body["text"]!.GetValue<string>(), Does.Contain($"{sample} wrote about Delivery."));
            Assert.That(body["direction"]!.GetValue<string>(), Is.EqualTo("ltr"));
        });
    }

    [Test]
    public async Task ASensitiveAnswer_IsMaskedAndNoReplyTo_UnlessTheActionIncludesIt()
    {
        var (form, definition) = await DraftAsync();
        var (_, address, _) = ContactFields(definition, sensitiveEmail: true);
        var json = $$$"""{"to":["sales@example.com"],"replyToField":"{{{address}}}","subject":{"":"New"},"body":{"":"<p>From {{ field.email }}.</p>"}}""";
        var masked = await PreviewAsync(form, definition, Action(definition, json));
        var included = await PreviewAsync(form, definition, Action(definition, json.Replace("\"subject\"", $"\"includeSensitive\":[\"{address}\"],\"subject\"", StringComparison.Ordinal)));

        Assert.Multiple(() =>
        {
            Assert.That(masked.Outcome, Is.EqualTo("200"), masked.ToString());
            Assert.That(masked.Body!["text"]!.GetValue<string>(), Does.Contain($"From {MergeTagAnswerTexts.Masked}.").And.Not.Contain(SampleEntries.Email));
            Assert.That(masked.Body["replyTo"], Is.Null, "a sensitive field is no reply-to unless the action includes it");
            Assert.That(included.Body!["text"]!.GetValue<string>(), Does.Contain($"From {SampleEntries.Email}."));
            Assert.That(included.Body["replyTo"]!.GetValue<string>(), Is.EqualTo(SampleEntries.Email));
        });
    }

    [Test]
    public async Task AMailOnlyAFieldsAnswerGets_IsTheVisitorsOwn_ItsCopiesTheTeamsMail_AsARunSendsThem()
    {
        var (form, definition) = await DraftAsync();
        var (_, address, _) = ContactFields(definition);
        var alone = await PreviewAsync(form, definition, Action(definition, $$$"""{"toField":"{{{address}}}","replyToField":"{{{address}}}","subject":{"":"Hello"},"body":{"":"<p>Hi.</p>"}}"""));
        var copied = await PreviewAsync(form, definition, Action(definition, $$$"""{"toField":"{{{address}}}","replyToField":"{{{address}}}","cc":["boss@example.com"],"bcc":["audit@example.com"],"subject":{"":"Hello"},"body":{"":"<p>Hi.</p>"}}"""));
        var blind = await PreviewAsync(form, definition, Action(definition, $$$"""{"toField":"{{{address}}}","bcc":["audit@example.com","legal@example.com"],"subject":{"":"Hello"},"body":{"":"<p>Hi.</p>"}}"""));

        Assert.Multiple(() =>
        {
            Assert.That(alone.Outcome, Is.EqualTo("200"), alone.ToString());
            Assert.That((Texts(alone.Body!["to"]), alone.Body["visitor"]!.GetValue<string>()), Is.EqualTo((Array.Empty<string>(), SampleEntries.Email)), "the visitor's own mail");
            Assert.That((Texts(alone.Body["cc"]), Texts(alone.Body["bcc"]), alone.Body["replyTo"]), Is.EqualTo((Array.Empty<string>(), Array.Empty<string>(), (JsonNode?)null)), "with no copy and no reply-to");
            Assert.That(Texts(copied.Body!["to"]), Is.EqualTo(new[] { "boss@example.com" }), "the copies as the team's mail");
            Assert.That((Texts(copied.Body["cc"]), Texts(copied.Body["bcc"])), Is.EqualTo((Array.Empty<string>(), new[] { "audit@example.com" })), "a blind copy still blind");
            Assert.That(copied.Body["replyTo"]!.GetValue<string>(), Is.EqualTo(SampleEntries.Email));
            Assert.That(copied.Body["visitor"]!.GetValue<string>(), Is.EqualTo(SampleEntries.Email));
            Assert.That((Texts(blind.Body!["to"]), Texts(blind.Body["bcc"])), Is.EqualTo((new[] { "audit@example.com" }, Array.Empty<string>())), "blind copies alone each go alone; the first shows");
        });
    }

    [Test]
    public async Task APreview_WarnsOfNothingARunWould()
    {
        var (form, definition) = await DraftAsync();
        ContactFields(definition);
        var logs = GetRequiredService<LogCapture>();
        var before = logs.Entries.Count;

        // The form has no creator with an address, and the site no such view: a run would warn of both.
        var answer = await PreviewAsync(form, definition, Action(definition, """{"to":["sales@example.com"],"toCreator":true,"view":"Nowhere","subject":{"":"Hello"},"body":{"":"<p>Hi.</p>"}}"""));

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(logs.Entries.Skip(before).Where(entry => entry.Category == typeof(EmailActionType).FullName && entry.Level >= LogLevel.Warning), Is.Empty);
        });
    }

    [Test]
    public async Task AMailWithNoRecipientYet_IsShownAsItWouldBe_AndAPlainTextOne_HasNoHtml()
    {
        var (form, definition) = await DraftAsync();
        ContactFields(definition);
        var nobody = await PreviewAsync(form, definition, Action(definition, """{"subject":{"":"Hello"},"body":{"":"<p>Hi {{ field.name }}.</p>"}}"""));
        var plain = await PreviewAsync(form, definition, Action(definition, """{"to":["sales@example.com"],"plainText":true,"subject":{"":"Hello"},"body":{"":"Hi {{ field.name }}, 1 < 2."}}"""));

        Assert.Multiple(() =>
        {
            Assert.That(nobody.Outcome, Is.EqualTo("200"), nobody.ToString());
            Assert.That(Texts(nobody.Body!["to"]), Is.Empty, "nobody yet");
            Assert.That(nobody.Body["visitor"], Is.Null);
            Assert.That(nobody.Body["html"]!.GetValue<string>(), Does.Contain($"Hi {SampleEntries.GivenName} {SampleEntries.FamilyName}."));
            Assert.That(plain.Outcome, Is.EqualTo("200"), plain.ToString());
            Assert.That(plain.Body!["html"], Is.Null, "one plain-text body");
            Assert.That(plain.Body["text"]!.GetValue<string>(), Is.EqualTo($"Hi {SampleEntries.GivenName} {SampleEntries.FamilyName}, 1 < 2."), "the text as it is");
        });
    }

    [Test]
    public async Task TheActionsLayout_IsTheOneItNames()
    {
        var (form, definition) = await DraftAsync();
        const string Json = """{"to":["sales@example.com"],"subject":{"":"Hello"},"body":{"":"<p>Hi.</p>"}LAYOUT}""";
        var card = await PreviewAsync(form, definition, Action(definition, Json.Replace("LAYOUT", string.Empty, StringComparison.Ordinal)));
        var letter = await PreviewAsync(form, definition, Action(definition, Json.Replace("LAYOUT", $",\"layout\":\"{EmailLayouts.LetterKey}\"", StringComparison.Ordinal)));
        var cardByName = await PreviewAsync(form, definition, Action(definition, Json.Replace("LAYOUT", $",\"layout\":\"{EmailLayouts.CardKey}\"", StringComparison.Ordinal)));

        Assert.Multiple(() =>
        {
            Assert.That(letter.Body!["html"]!.GetValue<string>(), Is.Not.EqualTo(card.Body!["html"]!.GetValue<string>()), "the letter is no card");
            Assert.That(cardByName.Body!["html"]!.GetValue<string>(), Is.EqualTo(card.Body["html"]!.GetValue<string>()), "the site's default is the card");
        });
    }

    [Test]
    public async Task ALanguageOfTheSite_GetsItsTexts_AndItsDirection()
    {
        var languages = GetRequiredService<ILanguageService>();
        await languages.CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        await languages.CreateAsync(new Language("he-IL", "Hebrew"), Constants.Security.SuperUserKey);
        var (form, definition) = await DraftAsync();
        var action = Action(definition, """{"to":["sales@example.com"],"subject":{"":"Hello","de-DE":"Hallo"},"body":{"":"<p>Hi.</p>","de-DE":"<p>Guten Tag.</p>"}}""");

        var german = await PreviewAsync(form, definition, action, "de-DE");
        var unknown = await PreviewAsync(form, definition, action, "xx-XX");
        var hebrew = await PreviewAsync(form, definition, action, "he-IL");

        Assert.Multiple(() =>
        {
            Assert.That((german.Body!["subject"]!.GetValue<string>(), german.Body["text"]!.GetValue<string>()), Is.EqualTo(("Hallo", "Guten Tag.")));
            Assert.That(unknown.Body!["subject"]!.GetValue<string>(), Is.EqualTo("Hello"), "the default language for one the site lacks");
            Assert.That(hebrew.Body!["direction"]!.GetValue<string>(), Is.EqualTo("rtl"));
        });
    }

    [Test]
    public async Task ThePreview_QueuesAndChangesNothing_AndRefusesWhatItCannotPreview()
    {
        var (form, definition) = await DraftAsync();
        var before = (await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!.ToJsonString();
        var version = (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion;
        var action = Action(definition, """{"to":["sales@example.com"],"subject":{"":"Hello"},"body":{"":"<p>Hi.</p>"}}""");

        var shown = await PreviewAsync(form, definition, action);
        var unreadable = await CallAsync("designer", HttpMethod.Post, $"form/{form}/email-preview", new { definition = new JsonArray(), action = action["key"]!.GetValue<string>() });
        var lacking = await CallAsync("designer", HttpMethod.Post, $"form/{form}/email-preview", new { definition, action = Guid.NewGuid() });
        var none = await CallAsync("designer", HttpMethod.Post, $"form/{Guid.NewGuid()}/email-preview", new { definition, action = action["key"]!.GetValue<string>() });
        var webhook = Action(definition, """{"url":"https://hooks.example.com/in"}""", type: "webhook");
        var notEmail = await PreviewAsync(form, definition, webhook);
        var viewless = await PreviewAsync(form, definition, Action(definition, """{"to":["sales@example.com"],"subject":{"":"Hello"},"view":"Nowhere"}"""));
        // Written before the editor kept a text per language: the form check says where, and the preview waits.
        var unfit = await PreviewAsync(form, definition, Action(definition, """{"to":"sales@example.com","subject":"Hello","body":{"":"<p>Hi.</p>"}}"""));

        Assert.Multiple(async () =>
        {
            Assert.That(shown.Outcome, Is.EqualTo("200"), shown.ToString());
            Assert.That(unreadable.Status, Is.EqualTo(HttpStatusCode.BadRequest), unreadable.ToString());
            Assert.That(lacking.Status, Is.EqualTo(HttpStatusCode.NotFound), lacking.ToString());
            Assert.That(lacking.Body?["code"]?.GetValue<string>(), Is.EqualTo(FormBuilderProblems.ActionNotFoundCode));
            Assert.That(none.Status, Is.EqualTo(HttpStatusCode.NotFound), none.ToString());
            Assert.That(notEmail.Status, Is.EqualTo(HttpStatusCode.Conflict), notEmail.ToString());
            Assert.That(notEmail.Body?["code"]?.GetValue<string>(), Is.EqualTo(FormBuilderProblems.ActionNotEmailCode));
            Assert.That(viewless.Status, Is.EqualTo(HttpStatusCode.Conflict), viewless.ToString());
            Assert.That(viewless.Body?["code"]?.GetValue<string>(), Is.EqualTo(FormBuilderProblems.EmailViewMissingCode));
            Assert.That(unfit.Status, Is.EqualTo(HttpStatusCode.Conflict), unfit.ToString());
            Assert.That(unfit.Body?["code"]?.GetValue<string>(), Is.EqualTo(FormBuilderProblems.ActionSettingsInvalidCode));
            Assert.That(await GetRequiredService<IEmailRepository>().DueAsync(10), Is.Empty, "nothing queued");
            Assert.That((await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion, Is.EqualTo(version), "nothing saved");
            Assert.That((await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!.ToJsonString(), Is.EqualTo(before));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        configuration[Locks + "0:Kind"] = "action";
        configuration[Locks + "0:Type"] = "email";
        configuration[Locks + "0:Setting"] = "from";
        configuration[Locks + "0:Value"] = Locked;
    }

    private async Task<(Guid Form, JsonNode Definition)> DraftAsync()
    {
        var form = (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Previewed"))).Key;
        var draft = await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        Assert.That(draft.Outcome, Is.EqualTo("200"), draft.ToString());
        return (form, draft.Body!["definition"]!.DeepClone());
    }

    private Task<ApiAnswer> PreviewAsync(Guid form, JsonNode definition, JsonNode action, string? culture = null) =>
        CallAsync("designer", HttpMethod.Post, $"form/{form}/email-preview", new { definition, action = action["key"]!.GetValue<string>(), culture });

    // The definition's one action, replaced by this one with a key of its own.
    private static JsonObject Action(JsonNode definition, string settings, string type = "email")
    {
        var action = new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["type"] = type, ["settings"] = JsonNode.Parse(settings) };
        definition["actions"] = new JsonArray(action);
        return action;
    }

    // A name, an email and a topic of two options, Delivery first; the keys of the three.
    private static (string Name, string Email, string Topic) ContactFields(JsonNode definition, bool sensitiveEmail = false)
    {
        var name = Field("name", "name", "Your name");
        var email = Field("email", "email", "Email");
        email["sensitive"] = sensitiveEmail;
        var topic = Field("dropdown", "topic", "Topic");
        topic["options"] = JsonNode.Parse("""{"items":[{"value":"delivery","label":{"":"Delivery"}},{"value":"press","label":{"":"Press"}}]}""");
        definition["pages"]![0]!["groups"]![0]!["fields"] = new JsonArray(name, email, topic);
        return (name["key"]!.GetValue<string>(), email["key"]!.GetValue<string>(), topic["key"]!.GetValue<string>());
    }

    private static JsonObject Field(string type, string alias, string label) =>
        new() { ["key"] = Guid.NewGuid().ToString(), ["type"] = type, ["alias"] = alias, ["label"] = new JsonObject { [string.Empty] = label } };

    private static string[] Texts(JsonNode? list) => [.. list!.AsArray().Select(item => item!.GetValue<string>())];
}
