using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Webhooks;

/// <summary>The webhook action against the Testing assembly's stub (ROADMAP 8.4, ADR-0161): what it sends, signs and makes of each answer.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class WebhookActionTests : ActionTestBase
{
    private const string Signing = "JFB_FAKE-signing";
    private const string Path = "/hook";

    private static readonly FormField Message = new()
    {
        Key = Guid.Parse("c1000000-0000-4000-8000-000000000001"),
        Type = "textarea",
        Alias = "message",
        Label = new LocalizedText("Message"),
    };

    private static readonly FormField Note = new()
    {
        Key = Guid.Parse("c1000000-0000-4000-8000-000000000002"),
        Type = "text",
        Alias = "privateNote",
        Label = new LocalizedText("Note"),
        Sensitive = true,
    };

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly ManualTimers _callLimits = new();

    private int _port;

    private StubServer Stub => GetRequiredService<StubHostedService>().Server!;

    private string Address => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_port}{Path}");

    private static FormAction Hook(string settings) => new()
    {
        Key = Guid.NewGuid(),
        Type = WebhookActionType.Alias,
        Name = "Hook",
        Settings = SettingsObject.Parse(settings),
    };

    private string Settings(string more = "") => $$"""{"url":"{{Address}}"{{more}}}""";

    private Task<Guid> FormAsync(params FormAction[] actions) =>
        PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = actions }, fields: [NameField, Message, Note]);

    private async Task<(StubCall Call, StoredActionRun Run)> SentAsync(string settings, string values = """{"name":"Ada","message":"Hello"}""")
    {
        Stub.Answer(new StubAnswer("POST", Path));
        var form = await FormAsync(Hook(settings));
        var entry = await SubmitAsync(form, values);
        var runs = await DrainAsync(entry.Id);
        return (Stub.CallsTo(Path).Single(), runs.Single());
    }

    private async Task<StoredActionRun> AnsweredAsync(int status, string? settings = null)
    {
        Stub.Answer(new StubAnswer("POST", Path, Status: status));
        var form = await FormAsync(Hook(settings ?? Settings()));
        var entry = await SubmitAsync(form);
        await Pass.RunAsync(CancellationToken.None);
        return (await Runs.ListForEntryAsync(entry.Id)).Single();
    }

    [SetUp]
    public void ForgetTheCalls() => Stub.Reset();

    [Test]
    public async Task TheBody_SaysWhatHappened_WithEveryField_AndIsSigned()
    {
        var (call, run) = await SentAsync(Settings());

        var body = JsonNode.Parse(call.Body)!;
        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(call.Header("Content-Type"), Is.EqualTo("application/json; charset=utf-8"));
            Assert.That(body["event"]!.GetValue<string>(), Is.EqualTo("submitted"));
            Assert.That(body["delivery"]!.GetValue<string>(), Is.EqualTo(call.Header("X-Jfb-Delivery")).And.Not.Empty);
            Assert.That(body["form"]!["alias"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(body["entry"]!["number"]!.GetValue<string>(), Is.EqualTo("1"));
            Assert.That(body["entry"]!["state"]!.GetValue<string>(), Is.EqualTo("approved"));
            Assert.That(body["fields"]!["name"]!.GetValue<string>(), Is.EqualTo("Ada"));
            Assert.That(body["fields"]!["message"]!.GetValue<string>(), Is.EqualTo("Hello"));
            Assert.That(call.Header("X-Jfb-Signature"), Is.Null, "no secret names itself, so nothing is signed");
            Assert.That(call.Header("X-Jfb-Test"), Is.Null);
        });
    }

    [Test]
    public async Task TheSignature_IsTheHmacOfTheTimeAndTheBody_WithTheNamedSecret()
    {
        var (call, _) = await SentAsync(Settings(""","secret":"$JironFormBuilder:Secrets:Signing" """));

        var seconds = Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Assert.Multiple(() =>
        {
            Assert.That(call.Header("X-Jfb-Signature"), Does.StartWith($"t={seconds}, v1="));
            Assert.That(call.Header("X-Jfb-Signature"), Is.EqualTo(WebhookSignature.Of(Signing, Clock.GetUtcNow(), System.Text.Encoding.UTF8.GetBytes(call.Body))));
        });
    }

    [Test]
    public async Task TheIntegrationsSecret_SignsAnActionWithoutItsOwn()
    {
        await SaveIntegrationsAsync(new IntegrationSettings { SigningSecret = "$JironFormBuilder:Secrets:Signing" });

        var (call, _) = await SentAsync(Settings());

        Assert.That(call.Header("X-Jfb-Signature"), Is.EqualTo(WebhookSignature.Of(Signing, Clock.GetUtcNow(), System.Text.Encoding.UTF8.GetBytes(call.Body))));
    }

    [Test]
    public async Task TheFormEncodedBody_EncodesEachValue()
    {
        var (call, _) = await SentAsync(Settings(""","format":"form" """), """{"name":"Ada & Co","message":"a=b c"}""");

        var values = call.Body.Split('&').Select(pair => pair.Split('=')).ToDictionary(pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
        Assert.Multiple(() =>
        {
            Assert.That(call.Header("Content-Type"), Is.EqualTo("application/x-www-form-urlencoded"));
            Assert.That(values["fields.name"], Is.EqualTo("Ada & Co"));
            Assert.That(values["fields.message"], Is.EqualTo("a=b c"));
            Assert.That(values["event"], Is.EqualTo("submitted"));
        });
    }

    [Test]
    public async Task TheXmlBody_EscapesEachValue_AndKeepsWhatIsNoBasicCharacter()
    {
        var emoji = char.ConvertFromUtf32(0x1F600);
        var (call, _) = await SentAsync(Settings(""","format":"xml" """), $$"""{"name":"<Ada> & \"Co\"","message":"{{emoji}}"}""");

        var document = XDocument.Parse(call.Body);
        string Field(string name) => document.Root!.Elements("field").Single(field => field.Attribute("name")!.Value == name).Value;
        Assert.Multiple(() =>
        {
            Assert.That(call.Header("Content-Type"), Is.EqualTo("application/xml; charset=utf-8"));
            Assert.That(Field("name"), Is.EqualTo("<Ada> & \"Co\""));
            Assert.That(Field("message"), Is.EqualTo(emoji));
        });
    }

    [Test]
    public async Task AMapping_SendsOnlyItsNames_WithTheirTemplates()
    {
        var (call, _) = await SentAsync(Settings(""","metadata":false,"fields":{"who":"{{ field.name }} from {{ form.alias }}"} """));

        var body = JsonNode.Parse(call.Body)!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(body.Select(property => property.Key), Is.EqualTo(new[] { "who" }));
            Assert.That(body["who"]!.GetValue<string>(), Does.StartWith("Ada from "));
        });
    }

    [Test]
    public async Task AnUnansweredField_IsLeftOutOfTheBody()
    {
        var (call, _) = await SentAsync(Settings(), """{"name":"Ada"}""");

        Assert.That(JsonNode.Parse(call.Body)!["fields"]!.AsObject().Select(field => field.Key), Is.EqualTo(new[] { "name" }));
    }

    [Test]
    public async Task WithoutMetadata_TheBodyHoldsOnlyTheAnswers()
    {
        var (call, _) = await SentAsync(Settings(""","metadata":false """));

        Assert.Multiple(() =>
        {
            Assert.That(call.Body, Does.Contain("Ada"));
            Assert.That(call.Body, Does.Not.Contain("\"delivery\"").And.Not.Contain("\"event\"").And.Not.Contain("submitted"));
        });
    }

    [TestCase("form", TestName = "A form body without metadata")]
    [TestCase("xml", TestName = "An XML body without metadata")]
    public async Task WithoutMetadata_OtherFormatsHoldOnlyTheAnswersToo(string format)
    {
        var (call, _) = await SentAsync(Settings($$""","metadata":false,"format":"{{format}}" """));

        Assert.Multiple(() =>
        {
            Assert.That(call.Body, Does.Contain("Ada"));
            Assert.That(call.Body, Does.Not.Contain("delivery").And.Not.Contain("submitted"));
        });
    }

    [Test]
    public async Task WithoutAnswers_ItSaysOnlyWhatHappened()
    {
        var (call, _) = await SentAsync(Settings(""","withoutAnswers":true """));

        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.Parse(call.Body)!["fields"]!.AsObject(), Is.Empty);
            Assert.That(call.Body, Does.Not.Contain("Ada"));
        });
    }

    [Test]
    public async Task ATemplate_IsTheWholeBody_InItsFormatsLanguage()
    {
        var (call, _) = await SentAsync(Settings(""","template":"{\"said\":\"{{ field.message }}\"}" """), """{"name":"Ada","message":"\"quoted\"\nnext"}""");

        Assert.That(JsonNode.Parse(call.Body)!["said"]!.GetValue<string>(), Is.EqualTo("\"quoted\"\nnext"));
    }

    [Test]
    public async Task ASensitiveField_GoesOnlyWhereTheActionNamesIt()
    {
        var without = await SentAsync(Settings(), """{"name":"Ada","privateNote":"hidden-note"}""");
        Stub.Reset();
        var named = await SentAsync(Settings($$""","includeSensitive":["{{Note.Key}}"] """), """{"name":"Ada","privateNote":"hidden-note"}""");

        Assert.Multiple(() =>
        {
            Assert.That(without.Call.Body, Does.Not.Contain("hidden-note"));
            Assert.That(JsonNode.Parse(without.Call.Body)!["fields"]!.AsObject().ContainsKey("privateNote"), Is.False, "not even that it was answered");
            Assert.That(JsonNode.Parse(named.Call.Body)!["fields"]!["privateNote"]!.GetValue<string>(), Is.EqualTo("hidden-note"));
        });
    }

    [Test]
    public async Task AHeader_MayBeAReference_AndGoesAsItsValue()
    {
        var (call, _) = await SentAsync(Settings(""","headers":{"Authorization":"$JironFormBuilder:Secrets:Crm","X-Source":"forms"} """));

        Assert.Multiple(() =>
        {
            Assert.That(call.Header("Authorization"), Is.EqualTo("JFB_FAKE-crm"));
            Assert.That(call.Header("X-Source"), Is.EqualTo("forms"));
        });
    }

    [Test]
    public async Task TheAddress_MayBeAReference()
    {
        var (call, run) = await SentAsync("""{"url":"$JironFormBuilder:Secrets:Address"}""");

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(call.Path, Is.EqualTo(Path));
        });
    }

    [TestCase(200)]
    [TestCase(204)]
    [TestCase(299)]
    public async Task AnAnswerOf2xx_DeliversIt(int status)
    {
        var run = await AnsweredAsync(status);

        Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
    }

    [TestCase(408)]
    [TestCase(425)]
    [TestCase(429)]
    [TestCase(500)]
    [TestCase(503)]
    public async Task ABusyOrFailingHost_IsTriedAgain(int status)
    {
        var run = await AnsweredAsync(status);

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(run.NextAttemptUtc, Is.Not.Null);
            Assert.That(run.LastError?.Code, Is.EqualTo(string.Create(CultureInfo.InvariantCulture, $"http_{status}")));
        });
    }

    [TestCase(400)]
    [TestCase(401)]
    [TestCase(404)]
    [TestCase(410)]
    public async Task ARefusal_FailsItForGood(int status)
    {
        var run = await AnsweredAsync(status);

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(string.Create(CultureInfo.InvariantCulture, $"http_{status}")));
        });
    }

    [Test]
    public async Task ARedirect_IsNotFollowed()
    {
        Stub.Answer(new StubAnswer("POST", Path, Status: 302, Location: "/elsewhere"));
        Stub.Answer(new StubAnswer("POST", "/elsewhere"));
        var form = await FormAsync(Hook(Settings()));
        var entry = await SubmitAsync(form);
        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(runs.Single().State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(runs.Single().LastError?.Code, Is.EqualTo("http_302"));
            Assert.That(Stub.CallsTo("/elsewhere"), Is.Empty);
        });
    }

    [Test]
    public async Task ACallWithoutAnAnswer_InItsTime_IsUncertain_AsItMayHaveArrived()
    {
        Stub.Answer(new StubAnswer("POST", Path, DelayMs: 60_000));
        var form = await FormAsync(Hook(Settings()));
        var entry = await SubmitAsync(form);

        // The call reaches the host, which holds it; then its time limit passes.
        var pass = Pass.RunAsync(CancellationToken.None);
        var waited = Stopwatch.StartNew();
        while (Stub.CallsTo(Path).Count == 0)
        {
            Assert.That(waited.Elapsed, Is.LessThan(Patience), "the call reaches the host");
            await Task.Delay(20);
        }

        var limit = _callLimits.Timers.Last(timer => !timer.Disposed);
        limit.Fire();
        await pass.WaitAsync(Patience);

        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(limit.DueTime, Is.EqualTo(WebhookActionType.CallTimeout), "the limit of a call");
            Assert.That(run.State, Is.EqualTo(ActionRunState.Uncertain));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.TimedOut));
        });
    }

    [Test]
    public async Task AHostThatIsDown_IsTriedAgain()
    {
        // The stub's port is the one the configuration allows, and nothing listens there once the stub has stopped.
        var form = await FormAsync(Hook(Settings()));
        var entry = await SubmitAsync(form);
        await Stub.DisposeAsync();
        await Pass.RunAsync(CancellationToken.None);

        var run = (await Runs.ListForEntryAsync(entry.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Pending));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.NoConnection));
        });
    }

    [TestCase("""{"url":"$JironFormBuilder:Secrets:Missing"}""")]
    [TestCase("""{"url":"http://127.0.0.1:1/hook","secret":"$JironFormBuilder:Secrets:Missing"}""")]
    public async Task ASecretTheHostLacks_FailsIt_NamingNoValue(string settings)
    {
        var run = await AnsweredAsync(200, settings.Replace("http://127.0.0.1:1/hook", Address, StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.SecretMissing));
            Assert.That(Stub.Calls, Is.Empty);
        });
    }

    [Test]
    public void WhatASiteLacks_IsNamedByItsKey()
    {
        var type = GetRequiredService<FormBuilderActionTypeCollection>().Single(each => each.Declaration.Alias == WebhookActionType.Alias);

        var missing = type.MissingConfiguration(Hook("""{"url":"$JironFormBuilder:Secrets:Missing","secret":"$JironFormBuilder:Secrets:Signing","headers":{"Authorization":"$JironFormBuilder:Secrets:Other"}}"""));

        Assert.That(missing, Is.EquivalentTo(new[] { "JironFormBuilder:Secrets:Missing", "JironFormBuilder:Secrets:Other" }));
    }

    [TestCase("""{"url":"http://elsewhere.example/hook"}""", "url", TestName = "A host the configuration does not name")]
    [TestCase("""{"url":"not an address"}""", "url", TestName = "No address")]
    [TestCase("""{"format":"json"}""", "url", TestName = "Nothing for a plain webhook")]
    [TestCase("""{"target":"slack","url":"https://hooks.example/services/key"}""", "url", TestName = "A Slack address that is no reference")]
    [TestCase("""{"target":"teams","url":"https://hooks.example/services/key"}""", "url", TestName = "A Teams address that is no reference")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","secret":"JFB_FAKE-literal"}""", "secret", TestName = "A signing secret that is no reference")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"Authorization":"JFB_FAKE-literal"}}""", "headers", TestName = "A credential that is no reference")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"X-Api-Key":"JFB_FAKE-literal"}}""", "headers", TestName = "A key that is no reference")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"Host":"elsewhere.example"}}""", "headers", TestName = "The request's own Host")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"X-Jfb-Signature":"made up"}}""", "headers", TestName = "Our own signature")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"X-Line":"a\nb"}}""", "headers", TestName = "A line break in a header")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","fields":{"a b":"x"}}""", "fields", TestName = "A name with a space")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","fields":{"a":"{{ form.name"}}""", "fields", TestName = "A mapping's tag that does not close")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","template":"{{ form.name"}""", "template", TestName = "A template's tag that does not close")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","withoutAnswers":true,"fields":{"a":"b"}}""", "withoutAnswers", TestName = "No answers and a mapping")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","withoutAnswers":true,"template":"{}"}""", "withoutAnswers", TestName = "No answers and a body of its own")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","template":"{\"a\": {{ field.gone }}}"}""", "template", TestName = "A template's tag that renders nothing")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","fields":{"a":"{{ field.gone }}"}}""", "fields", TestName = "A mapping's tag that renders nothing")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"Content-Encoding":"gzip"}}""", "headers", TestName = "A header that misdescribes the body")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","headers":{"X-A":"1","x-a":"2"}}""", "headers", TestName = "A header said twice")]
    [TestCase("""{"url":"$JironFormBuilder:Secrets:Address","includeSensitive":["c1000000-0000-4000-8000-0000000000ff"]}""", "includeSensitive", TestName = "A field of another form")]
    public void Publishing_RefusesWhatCannotBeSentSafely(string settings, string setting)
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await FormAsync(Hook(settings)));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain($"$.actions[0].settings.{setting}"), string.Join(", ", refused.Problems));
    }

    [TestCase(0, "slack")]
    [TestCase(1, "teams")]
    public async Task ThePresets_SendAFormattedMessage_ToTheSitesAddress(int preset, string target)
    {
        await SaveIntegrationsAsync(new IntegrationSettings { SlackWebhook = "$JironFormBuilder:Secrets:SlackAddress", TeamsWebhook = "$JironFormBuilder:Secrets:TeamsAddress" });
        var type = GetRequiredService<FormBuilderActionTypeCollection>().Single(each => each.Declaration.Alias == WebhookActionType.Alias);
        var settings = type.Declaration.Presets[preset].Settings;
        Assert.That(settings, Does.Contain($"\"target\":\"{target}\""));

        var (call, run) = await SentAsync(settings, """{"name":"Ada \"the first\"","message":"Hello"}""");

        var body = JsonNode.Parse(call.Body)!;
        var text = target == "slack" ? body["text"]!.GetValue<string>() : body["attachments"]![0]!["content"]!["body"]![1]!["text"]!.GetValue<string>();
        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(call.Path, Is.EqualTo(Path));
            Assert.That(text, Does.Contain("Ada \"the first\""));
            if (target == "teams")
            {
                Assert.That(body["attachments"]![0]!["content"]!["type"]!.GetValue<string>(), Is.EqualTo("AdaptiveCard"));
            }
        });
    }

    [Test]
    public async Task ASlackMessage_KeepsWhatAVisitorTypedAsText()
    {
        var (call, _) = await SentAsync("""{"target":"slack","url":"$JironFormBuilder:Secrets:Address","template":"{\"text\":\"{{ field.message }} from {{ form.alias }}\"}"}""", """{"name":"Ada","message":"<!channel> <https://evil.example|Open> & more"}""");

        var text = JsonNode.Parse(call.Body)!["text"]!.GetValue<string>();
        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("&lt;!channel&gt; &lt;https://evil.example|Open&gt; &amp; more from "));
            Assert.That(text, Does.Not.Contain("<"));
        });
    }

    [Test]
    public async Task ATeamsMessage_KeepsALinkAVisitorTyped_FromBeingALink()
    {
        var (call, _) = await SentAsync("""{"target":"teams","url":"$JironFormBuilder:Secrets:Address","template":"{\"text\":\"{{ field.message }}\"}"}""", """{"name":"Ada","message":"[Open](https://evil.example)"}""");

        var text = JsonNode.Parse(call.Body)!["text"]!.GetValue<string>();
        Assert.Multiple(() =>
        {
            Assert.That(text.Contains("](", StringComparison.Ordinal), Is.False);
            Assert.That(text.Replace(((char)0x200b).ToString(), string.Empty, StringComparison.Ordinal), Is.EqualTo("[Open](https://evil.example)"));
        });
    }

    [Test]
    public async Task APlainWebhook_SendsWhatAVisitorTypedAsItIs()
    {
        var (call, _) = await SentAsync(Settings(), """{"name":"<Ada> & [x](y)"}""");

        Assert.That(JsonNode.Parse(call.Body)!["fields"]!["name"]!.GetValue<string>(), Is.EqualTo("<Ada> & [x](y)"));
    }

    [TestCase(""","template":"{\"said\":\"%\"}" """, TestName = "A template that would be cut")]
    [TestCase(""","fields":{"said":"%"} """, TestName = "A mapping that would be cut")]
    public async Task ABodyThatWouldBeCut_FailsIt_AndNothingGoesOut(string more)
    {
        // No one answer reaches the limit of a rendered text, so the template says it many times.
        var many = string.Concat(Enumerable.Repeat("{{ field.message }}", 200));
        Stub.Answer(new StubAnswer("POST", Path));
        var form = await FormAsync(Hook(Settings(more.Replace("%", many, StringComparison.Ordinal))));
        var entry = await SubmitAsync(form, $$"""{"name":"Ada","message":"{{new string('x', 1_000)}}"}""");

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.TooLong));
            Assert.That(Stub.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task AHeaderFromASecret_ThatHttpCannotCarry_FailsIt_NamingNoValue()
    {
        var run = await AnsweredAsync(200, Settings(""","headers":{"Authorization":"$JironFormBuilder:Secrets:BrokenLine"} """));

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.HeaderInvalid));
            Assert.That(Stub.Calls, Is.Empty);
        });
    }

    [Test]
    public void AHeaderValueHttpCannotCarry_IsRefused()
    {
        var settings = """{"url":"$JironFormBuilder:Secrets:Address","headers":{"X-Source":"B""" + (char)0xFC + """ro"}}""";

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await FormAsync(Hook(settings)));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain("$.actions[0].settings.headers"));
    }

    [TestCase("""{"secret":"JFB_FAKE-literal"}""", TestName = "A literal signing secret")]
    [TestCase("""{"headers":{"Authorization":"JFB_FAKE-literal"}}""", TestName = "A literal credential")]
    public async Task ADraftsLiteralSecret_IsNeverSent_ByATestRun(string more)
    {
        var form = await FormAsync(Hook(Settings()));
        var draft = (await Forms.GetDraftAsync(form))!;
        var literal = Hook(Settings("," + more[1..^1]));
        await Forms.SaveDraftAsync(form, draft.Form.ConcurrencyVersion, draft.Definition! with { Actions = [literal] }, null, Editor);

        var result = await GetRequiredService<ActionTestRuns>().TestAsync(form, literal.Key, new ActionTestUser(Guid.NewGuid(), "tester@jfb.test", "Tester"), confirmed: true);

        Assert.Multiple(() =>
        {
            Assert.That(result.Result!.Outcome, Is.EqualTo(ActionTestOutcome.Failed));
            Assert.That(result.Result!.Message, Is.EqualTo(WebhookActionType.SecretLiteral));
            Assert.That(Stub.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task APresetWithoutAnAddress_FailsIt_NamingWhy()
    {
        var run = await AnsweredAsync(200, """{"target":"slack"}""");

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.NoAddress));
        });
    }

    [Test]
    public async Task ATestRun_CarriesItsMark_AndSaysWhatTheHostAnswered()
    {
        Stub.Answer(new StubAnswer("POST", Path, Status: 201));
        var action = Hook(Settings());
        var form = await FormAsync(action);
        var retries = GetRequiredService<ActionTestRuns>();
        var user = new ActionTestUser(Guid.NewGuid(), "tester@jfb.test", "Tester");

        var unconfirmed = await retries.TestAsync(form, action.Key, user, confirmed: false);
        var answers = Stub.CallsTo(Path).Count;
        var confirmed = await retries.TestAsync(form, action.Key, user, confirmed: true);

        var call = Stub.CallsTo(Path).Single();
        Assert.Multiple(() =>
        {
            Assert.That(unconfirmed.Status, Is.EqualTo(ActionTestStatus.NeedsConfirmation));
            Assert.That(answers, Is.Zero);
            Assert.That(confirmed.Result!.Outcome, Is.EqualTo(ActionTestOutcome.Sent));
            Assert.That(confirmed.Result!.Message, Does.Contain("201"));
            Assert.That(call.Header("X-Jfb-Test"), Is.EqualTo("true"));
            Assert.That(call.Header("X-Jfb-Delivery"), Is.EqualTo("test"));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        _port = UrlOptionListHost.FreePort();
        UrlOptionListHost.Configure(configuration, _port);
        configuration["JironFormBuilder:Secrets:Signing"] = Signing;
        configuration["JironFormBuilder:Secrets:Crm"] = "JFB_FAKE-crm";
        configuration["JironFormBuilder:Secrets:Address"] = Address;
        configuration["JironFormBuilder:Secrets:SlackAddress"] = Address;
        configuration["JironFormBuilder:Secrets:TeamsAddress"] = Address;
        configuration["JironFormBuilder:Secrets:BrokenLine"] = "JFB_FAKE-line" + (char)10 + "next";
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        TestEndpointsHost.UseDevelopment(builder);

        // The calls' time limit fires when a test says so, not after 30 s of a shared machine's time (ROADMAP "Flaky means
        // broken": that test once waited past its margin while Umbraco's start-up jobs held SQLite).
        builder.Services.AddUnique(provider => new OutboundHttp(
            provider.GetRequiredService<IFormBuilderHttpTransport>(),
            provider.GetRequiredService<IOptions<FormBuilderOptions>>(),
            _callLimits));
    }

    private async Task SaveIntegrationsAsync(IntegrationSettings section)
    {
        var current = await GetRequiredService<IFormBuilderSettingsService>().GetAsync<IntegrationSettings>();
        await GetRequiredService<IFormBuilderSettingsService>().SaveAsync(section, current.Version, Editor);
    }
}
