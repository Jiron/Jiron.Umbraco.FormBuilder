using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The receipt endpoint of the public API (ADR-0012, ADR-0089) on a real site: the receipt of a submission id answers
/// the receipt and the outcome that the entries endpoint answered with, for an id the visitor posted and one the server
/// made, also once the form is full, and for an entry in Spam or rejected since; every miss (an unknown id, another
/// form's entry, a draft) answers the same <c>404 not_found</c>, while a form the site does not serve and a form for
/// members answer with the form's own codes.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormReceiptTests : PublicApiTestBase
{
    private static readonly FormOutcome Thanks = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000289"),
        Message = new LocalizedHtml("<p>Thanks, {{ field.name }}, for the {{ context.campaign }} order.</p>"),
        Redirect = new OutcomeRedirect { Url = "/next/{{ field.name }}", DelaySeconds = 4 },
        ShowAnswers = true,
    };

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task AReceipt_AnswersWhatTheEntriesEndpointAnsweredWith_ForAnIdPostedOrMade_AlsoOnceTheFormIsFull()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Outcomes = [Thanks],
            Settings = definition.Settings with
            {
                Availability = definition.Settings.Availability with { MaxEntries = 2 },
                Developer = definition.Settings.Developer with { ContextValues = ["campaign"] },
            },
        });
        var posted = Guid.NewGuid();
        using var first = await SubmitAsync(key, $$$"""{"submissionId":"{{{posted}}}","revision":1,"values":{"name":"Ada"},"context":{"campaign":"autumn"}}""");
        var firstAnswer = await first.Content.ReadAsStringAsync();
        using var second = await SubmitAsync(key, """{"revision":1,"values":{"name":"Grace"}}""");
        var secondAnswer = await second.Content.ReadAsStringAsync();
        var made = Guid.Parse(JsonNode.Parse(secondAnswer)!["receipt"]!["submissionId"]!.GetValue<string>());
        using var third = await SubmitAsync(key, """{"revision":1,"values":{"name":"Alan"}}""");

        using var ofPosted = await ReceiptAsync(key, $$$"""{"submissionId":"{{{posted}}}"}""");
        using var ofMade = await ReceiptAsync(key, $$$"""{"submissionId":"{{{made}}}"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), firstAnswer);
            Assert.That(JsonNode.Parse(firstAnswer)!["outcome"]!.ToJsonString(), Does.Contain("autumn").And.Contain("delaySeconds").And.Contain("showAnswers"), "an outcome with every part");
            Assert.That(await RefusalAsync(third), Is.EqualTo("403 entry_limit_reached"), "the form is full");
            Assert.That(ofPosted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ofPosted.Content.ReadAsStringAsync());
            Assert.That(ofPosted.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(Header(ofPosted, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(await ofPosted.Content.ReadAsStringAsync(), Is.EqualTo(firstAnswer), "the receipt and the outcome the submission got");
            Assert.That(await ofMade.Content.ReadAsStringAsync(), Is.EqualTo(secondAnswer), "by the id the server made");
        });
    }

    [Test]
    public async Task EveryMiss_AnswersTheSameNotFound_AndAnEntryInSpamOrRejected_AnswersAsAnyOther()
    {
        var key = await PublishAsync("Ours");
        var other = await PublishAsync("Theirs");
        var ours = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var spam = Guid.NewGuid();
        var rejected = Guid.NewGuid();
        var draft = Guid.NewGuid();
        foreach (var (form, id) in new[] { (key, ours), (other, theirs), (key, spam), (key, rejected), (key, draft) })
        {
            using var submitted = await SubmitAsync(form, $$$"""{"submissionId":"{{{id}}}","revision":1,"values":{"name":"Ada"}}""");
            Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await submitted.Content.ReadAsStringAsync());
        }

        var row = (await FormRows.GetAsync(key))!;
        foreach (var (id, state) in new[] { (spam, EntryState.Spam), (rejected, EntryState.Rejected), (draft, EntryState.Draft) })
        {
            var entry = (await Entries.GetBySubmissionAsync(row.Id, id))!;
            Assert.That(await Entries.ChangeStateAsync(entry.Id, EntryState.Submitted, state, Editor), Is.Not.Null);
        }

        var answers = new Dictionary<string, string>();
        var bodies = new Dictionary<string, string>();
        foreach (var (what, id) in new[] { ("ours", ours), ("an unknown id", Guid.NewGuid()), ("another form's entry", theirs), ("a draft", draft), ("in Spam", spam), ("rejected", rejected) })
        {
            using var response = await ReceiptAsync(key, $$$"""{"submissionId":"{{{id}}}"}""");
            answers[what] = await RefusalOrOkAsync(response);
            bodies[what] = await response.Content.ReadAsStringAsync();
        }

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["ours"] = "200",
                ["an unknown id"] = "404 not_found",
                ["another form's entry"] = "404 not_found",
                ["a draft"] = "404 not_found",
                ["in Spam"] = "200",
                ["rejected"] = "200",
            }), string.Join(" | ", answers));
            Assert.That(new[] { bodies["another form's entry"], bodies["a draft"] }, Is.All.EqualTo(bodies["an unknown id"]), "every miss alike, so submission ids cannot be probed");
            Assert.That(JsonNode.Parse(bodies["in Spam"])!["outcome"]!.ToJsonString(), Is.EqualTo(JsonNode.Parse(bodies["ours"])!["outcome"]!.ToJsonString()), "a bot learns nothing");
        });
    }

    [Test]
    public async Task AFormTheSiteDoesNotServe_AndAFormForMembers_AnswerWithTheFormsOwnCodes()
    {
        var members = await PublishAsync("Members", change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        });
        var trashed = await PublishAsync("Trashed");
        var id = Guid.NewGuid();
        using (var submitted = await SubmitAsync(trashed, $$$"""{"submissionId":"{{{id}}}","revision":1,"values":{"name":"Ada"}}"""))
        {
            Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await submitted.Content.ReadAsStringAsync());
        }

        await Forms.TrashAsync(trashed, (await Forms.GetAsync(trashed))!.ConcurrencyVersion, Editor);

        // A form the site still serves when the receipt is asked for: it read the form before the trash, which went past
        // the form service and its cache.
        var pastTheCache = await PublishAsync("Past the cache");
        using (var submitted = await SubmitAsync(pastTheCache, $$$"""{"submissionId":"{{{id}}}","revision":1,"values":{"name":"Ada"}}"""))
        {
            Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await submitted.Content.ReadAsStringAsync());
        }

        using (var served = await GetAsync($"{Root}/forms/{pastTheCache}"))
        {
            Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var row = (await FormRows.GetAsync(pastTheCache))!;
        _ = await FormRows.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var body = $$$"""{"submissionId":"{{{id}}}"}""";
        using var forMembers = await ReceiptAsync(members, body);
        using var inTheTrash = await ReceiptAsync(trashed, body);
        using var trashedPastTheCache = await ReceiptAsync(pastTheCache, body);
        using var missing = await ReceiptAsync(Guid.NewGuid(), body);
        using var notAKey = await SendAsync(HttpMethod.Post, $"{Root}/forms/contact/receipt", request => request.Content = Json(body));

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(forMembers), Is.EqualTo("403 member_required"));
            Assert.That(await RefusalAsync(inTheTrash), Is.EqualTo("404 form_not_found"), "its entry is answered no more");
            Assert.That(await RefusalAsync(trashedPastTheCache), Is.EqualTo("404 form_not_found"), "by the form's row, read again");
            Assert.That(await RefusalAsync(missing), Is.EqualTo("404 form_not_found"));
            Assert.That(await RefusalAsync(notAKey), Is.EqualTo("404 form_not_found"));
        });
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // "200" for a receipt, otherwise the status and the code.
    private static async Task<string> RefusalOrOkAsync(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.OK ? "200" : await RefusalAsync(response);

    private async Task<HttpResponseMessage> SubmitAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = Json(json));

    private async Task<HttpResponseMessage> ReceiptAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/receipt", request => request.Content = Json(json));
}
