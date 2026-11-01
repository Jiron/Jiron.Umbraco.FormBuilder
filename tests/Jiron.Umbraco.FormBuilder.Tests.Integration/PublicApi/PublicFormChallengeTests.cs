using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The challenge on a real site (ROADMAP 7.1b, ADR-0012, ADR-0138): the endpoint and what it carries; its signature,
/// expiry and form on entries, page checks, receipts and uploads; one entry per challenge, whose replay gets the original
/// answer and nobody else hears of it; and a receipt that finds an entry by its challenge for a day past its expiry. On a
/// clock that stands still until a test moves it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormChallengeTests : PublicApiTestBase
{
    private const string Body = """{"revision":1,"values":{"name":"Ada"}}""";
    private const string ShopKey = "JFB_FAKE_shop_server_key_0123456789abcdefgh";

    private static readonly FormField Seat = new()
    {
        Key = Guid.Parse("71b00000-0000-4000-8000-000000000010"),
        Type = "radio",
        Alias = "seat",
        Label = new LocalizedText("Seat"),
        Options = new FieldOptions
        {
            Items =
            [
                new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 2 },
                new FieldOption { Value = "back", Label = new LocalizedText("Back"), Places = 1 },
                new FieldOption { Value = "any", Label = new LocalizedText("Any") },
            ],
        },
    };

    private static readonly FormField Attachment = new()
    {
        Key = Guid.Parse("71b00000-0000-4000-8000-000000000011"),
        Type = "file",
        Alias = "attachment",
        Label = new LocalizedText("Attachment"),
        Settings = SettingsObject.Parse("""{"types":["pdf"],"maxBytes":2048}"""),
    };

    private StoppedClock _clock = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task TheEndpoint_AnswersAChallengeAndTheFillTime_NoStore_ForAVisitorWithoutAMembersValues()
    {
        var key = await PublishAsync();

        using var answer = await PostAsync($"{Root}/forms/{key}/challenge", null);
        var body = await JsonAsync(answer);

        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Header(answer, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(body["challenge"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(body["minFillSeconds"]!.GetValue<int>(), Is.EqualTo(3));
            Assert.That(body["member"], Is.Null);
            Assert.That(body["places"], Is.Null, "no option here has places");
        });
    }

    [Test]
    public async Task TheEndpoint_RefusesWhatItCannotRead_AndAFormItDoesNotServe()
    {
        var key = await PublishAsync();

        using var other = await PostAsync($"{Root}/forms/{key}/challenge", """{"previous":"x","extra":1}""");
        using var missing = await PostAsync($"{Root}/forms/{Guid.NewGuid()}/challenge", null);
        using var noKey = await PostAsync($"{Root}/forms/contact/challenge", null);

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(other), Is.EqualTo("400 invalid_request"));
            Assert.That(await RefusalAsync(missing), Is.EqualTo("404 form_not_found"));
            Assert.That(await RefusalAsync(noKey), Is.EqualTo("404 form_not_found"));
        });
    }

    [Test]
    public async Task TheEndpoint_TellsThePlacesLeft_OfTheOptionsThatHaveThem_AsEntriesTakeThem()
    {
        var key = await PublishAsync(fields: Seat);
        var before = await PlacesAsync(key);

        using var sent = await SendEntryAsync(key, """{"revision":1,"values":{"seat":"front"}}""", await ChallengeAsync(key));
        var after = await PlacesAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(before["front"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(before["back"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(before["any"], Is.Null, "an option without places");
            Assert.That(after["front"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(after["back"]!.GetValue<int>(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheEndpoint_TellsThePlacesLeft_OfAListsOptions_ButNotOfOneThatHangsOnAParentsAnswer()
    {
        var listKey = Guid.Parse("71b00000-0000-4000-8000-000000000030");
        await GetRequiredService<OptionListService>().CreateAsync(listKey, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","places":1},{"value":"m"}]"""));
        var size = new FormField
        {
            Key = Guid.Parse("71b00000-0000-4000-8000-000000000031"),
            Type = "dropdown",
            Alias = "size",
            Label = new LocalizedText("Size"),
            Options = new FieldOptions { Source = OptionSource.List, OptionList = listKey },
        };
        var childKey = Guid.Parse("71b00000-0000-4000-8000-000000000036");
        await GetRequiredService<OptionListService>().CreateAsync(childKey, new OptionListInput("fits", "Fits", OptionListKind.Manual, Items: """[{"value":"always","places":1},{"value":"slim","places":1,"parentValues":["s"]}]"""));
        var filtered = size with
        {
            Key = Guid.Parse("71b00000-0000-4000-8000-000000000032"),
            Alias = "filtered",
            Options = new FieldOptions { Source = OptionSource.List, OptionList = childKey, Parent = size.Key },
        };
        var ranked = size with
        {
            Key = Guid.Parse("71b00000-0000-4000-8000-000000000035"),
            Type = "ranking",
            Alias = "ranked",
        };
        var key = await PublishAsync(fields: [size, filtered, ranked]);

        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge");
        var places = (await JsonAsync(answer))["places"]!;
        using var sent = await SendEntryAsync(key, """{"revision":1,"values":{"size":"s","filtered":"slim","ranked":["s","m"]}}""", await ChallengeAsync(key));
        using var after = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge");

        Assert.Multiple(async () =>
        {
            Assert.That(places[size.Key.ToString("D")]!["s"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(places[size.Key.ToString("D")]!["m"], Is.Null, "an option without places");
            Assert.That(places[filtered.Key.ToString("D")]!["always"]!.GetValue<int>(), Is.EqualTo(1), "no parent's answer filters it");
            Assert.That(places[filtered.Key.ToString("D")]!["slim"], Is.Null, "it hangs on an answer the endpoint does not have");
            Assert.That(places[ranked.Key.ToString("D")], Is.Null, "a type without places");
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
            Assert.That((await JsonAsync(after))["places"]![size.Key.ToString("D")]!["s"]!.GetValue<int>(), Is.Zero, "the entry took it");
        });
    }

    [Test]
    public async Task TheEndpoint_StillGivesAChallenge_WhenAListIsMissing()
    {
        var key = await PublishAsync(fields: new FormField
        {
            Key = Guid.Parse("71b00000-0000-4000-8000-000000000033"),
            Type = "dropdown",
            Alias = "size",
            Label = new LocalizedText("Size"),
            Options = new FieldOptions { Source = OptionSource.List, OptionList = Guid.Parse("71b00000-0000-4000-8000-000000000034") },
        });

        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge");
        var body = await JsonAsync(answer);

        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body["challenge"], Is.Not.Null);
            Assert.That(body["places"], Is.Null);
        });
    }

    [TestCase("entries", Body)]
    [TestCase("validate", """{"revision":1,"pageKey":"00000000-0000-4000-8000-000000000001","values":{}}""")]
    public async Task ARequestWithoutAChallengeThatCounts_IsRefused_AndStoresNothing(string endpoint, string body)
    {
        var key = await PublishAsync();
        var other = await PublishAsync("Other");
        var forOtherForm = await ChallengeAsync(other);
        var expired = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromHours(24));

        var answers = new List<string>();
        foreach (var challenge in new[] { "null", "\"forged\"", $"\"{forOtherForm}\"", $"\"{expired}\"" })
        {
            var posted = JsonNode.Parse(body)!.AsObject();
            posted["challenge"] = JsonNode.Parse(challenge);
            using var answer = await PostAsync($"{Root}/forms/{key}/{endpoint}", posted.ToJsonString());
            answers.Add(await RefusalAsync(answer));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.All.EqualTo("401 challenge_required"));
            Assert.That(await CountAsync(key), Is.Zero);
        });
    }

    [Test]
    public async Task AUsedChallenge_IsRefused_BeforeItsAnswersAreRead()
    {
        var key = await PublishAsync();
        var challenge = await ChallengeAsync(key);
        using var first = await SendEntryAsync(key, """{"revision":1,"values":{"name":"Ada"}}""", challenge);

        using var again = await SendEntryAsync(key, """{"revision":99,"values":{}}""", challenge);

        Assert.That(await RefusalAsync(again), Is.EqualTo("401 challenge_required"));
    }

    [Test]
    public async Task AChallenge_MakesOneEntry_AndItsIdIsStored()
    {
        var key = await PublishAsync();
        var challenge = await ChallengeAsync(key);

        using var first = await SendEntryAsync(key, """{"submissionId":"71b00000-0000-4000-8000-0000000000a1","revision":1,"values":{"name":"Ada"}}""", challenge);
        using var again = await SendEntryAsync(key, """{"submissionId":"71b00000-0000-4000-8000-0000000000a2","revision":1,"values":{"name":"Bob"}}""", challenge);
        using var noId = await SendEntryAsync(key, """{"revision":1,"values":{"name":"Cy"}}""", challenge);

        var row = (await FormRows.GetAsync(key))!;
        var stored = await Entries.GetBySubmissionAsync(row.Id, Guid.Parse("71b00000-0000-4000-8000-0000000000a1"));

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await RefusalAsync(again), Is.EqualTo("401 challenge_required"), "with an unknown submission id");
            Assert.That(await RefusalAsync(noId), Is.EqualTo("401 challenge_required"));
            Assert.That(await CountAsync(key), Is.EqualTo(1));
            Assert.That(stored!.ChallengeId, Has.Length.EqualTo(32));
        });
    }

    [Test]
    public async Task AReplay_WithItsOwnChallenge_GetsTheOriginalAnswer_EvenOnceTheFormHasClosed_OrItsOptionIsFull()
    {
        var key = await PublishAsync(fields: Seat);
        var challenge = await ChallengeAsync(key);
        const string Sent = """{"submissionId":"71b00000-0000-4000-8000-0000000000b1","revision":1,"values":{"seat":"back"}}""";
        using var first = await SendEntryAsync(key, Sent, challenge);
        var receipt = (await JsonAsync(first))["receipt"]!["reference"]!.GetValue<string>();

        // Its option is full now, and then the form closes.
        using var full = await SendEntryAsync(key, Sent, challenge);
        await SaveAndPublishAsync(key, definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { ClosesAt = _clock.GetUtcNow().UtcDateTime.AddDays(-1) } },
        });
        using var closed = await SendEntryAsync(key, Sent, challenge);

        Assert.Multiple(async () =>
        {
            Assert.That(full.StatusCode, Is.EqualTo(HttpStatusCode.OK), await full.Content.ReadAsStringAsync());
            Assert.That((await JsonAsync(full))["receipt"]!["reference"]!.GetValue<string>(), Is.EqualTo(receipt));
            Assert.That(closed.StatusCode, Is.EqualTo(HttpStatusCode.OK), await closed.Content.ReadAsStringAsync());
            Assert.That((await JsonAsync(closed))["receipt"]!["reference"]!.GetValue<string>(), Is.EqualTo(receipt));
            Assert.That(await CountAsync(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ThatSubmissionId_WithItsOwnChallenge_AndOtherAnswers_Gets409WithoutAReceipt()
    {
        var key = await PublishAsync();
        var challenge = await ChallengeAsync(key);
        using var first = await SendEntryAsync(key, """{"submissionId":"71b00000-0000-4000-8000-0000000000c1","revision":1,"values":{"name":"Ada"}}""", challenge);

        using var other = await SendEntryAsync(key, """{"submissionId":"71b00000-0000-4000-8000-0000000000c1","revision":1,"values":{"name":"Bob"}}""", challenge);

        Assert.That(await RefusalAsync(other), Is.EqualTo("409 duplicate_submission"));
    }

    [Test]
    public async Task ThatSubmissionId_AndTheSameBody_WithAnotherChallengeOrServerKey_Gets409_WithNeitherReceiptNorOutcome()
    {
        var key = await PublishAsync();
        const string Sent = """{"submissionId":"71b00000-0000-4000-8000-0000000000d1","revision":1,"values":{"name":"Ada"}}""";
        using var first = await SendEntryAsync(key, Sent, await ChallengeAsync(key));

        using var another = await SendEntryAsync(key, Sent, await ChallengeAsync(key));
        using var signed = await PostAsync($"{Root}/forms/{key}/entries", Sent, "Jfb-Key " + ShopKey);
        var answers = new[] { await another.Content.ReadAsStringAsync(), await signed.Content.ReadAsStringAsync() };

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(another), Is.EqualTo("409 duplicate_submission"));
            Assert.That(await RefusalAsync(signed), Is.EqualTo("409 duplicate_submission"));
            Assert.That(answers, Has.None.Contains("receipt"));
            Assert.That(answers, Has.None.Contains("outcome"));
            Assert.That(await CountAsync(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AServerKey_NeedsNoChallenge()
    {
        var key = await PublishAsync();

        using var sent = await PostAsync($"{Root}/forms/{key}/entries", Body, "Jfb-Key " + ShopKey);

        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task APageCheck_TakesASolvedChallengeToo_AndUsesNoneUp()
    {
        var key = await PublishAsync();
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;
        var challenge = await ChallengeAsync(key);
        var check = $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada"},"challenge":"{{{challenge}}}"}""";

        using var before = await PostAsync($"{Root}/forms/{key}/validate", check);
        using var sent = await SendEntryAsync(key, Body, challenge);
        using var after = await PostAsync($"{Root}/forms/{key}/validate", check);

        Assert.Multiple(() =>
        {
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a check did not use it up");
            Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task AReceipt_FindsItsEntryByItsChallenge_ForADayPastItsExpiry_ThenAnswers404()
    {
        var key = await PublishAsync();
        var challenge = await ChallengeAsync(key);
        const string Id = "71b00000-0000-4000-8000-0000000000e1";
        using var first = await SendEntryAsync(key, $$$"""{"submissionId":"{{{Id}}}","revision":1,"values":{"name":"Ada"}}""", challenge);
        var reference = (await JsonAsync(first))["receipt"]!["reference"]!.GetValue<string>();

        using var atOnce = await ReceiptAsync(key, Id, challenge);
        _clock.Advance(TimeSpan.FromHours(47));
        using var inTheGrace = await ReceiptAsync(key, Id, challenge);
        using var theExpiredPost = await SendEntryAsync(key, $$$"""{"submissionId":"{{{Id}}}","revision":1,"values":{"name":"Ada"}}""", challenge);
        _clock.Advance(TimeSpan.FromHours(1));
        using var pastIt = await ReceiptAsync(key, Id, challenge);

        // Past the grace, a fresh challenge's post keeps the submission id and is told it was sent already.
        using var fresh = await SendEntryAsync(key, $$$"""{"submissionId":"{{{Id}}}","revision":1,"values":{"name":"Ada"}}""", await ChallengeAsync(key));

        Assert.Multiple(async () =>
        {
            Assert.That((await JsonAsync(atOnce))["receipt"]!["reference"]!.GetValue<string>(), Is.EqualTo(reference));
            Assert.That((await JsonAsync(inTheGrace))["receipt"]!["reference"]!.GetValue<string>(), Is.EqualTo(reference));
            Assert.That(await RefusalAsync(theExpiredPost), Is.EqualTo("401 challenge_required"), "the post itself no longer counts");
            Assert.That(await RefusalAsync(pastIt), Is.EqualTo("404 not_found"));
            Assert.That(await RefusalAsync(fresh), Is.EqualTo("409 duplicate_submission"));
        });
    }

    [Test]
    public async Task AReceipt_MissesTheSameWay_WithAnotherChallenge_NoChallenge_AForgedOne_AndAnUnknownId()
    {
        var key = await PublishAsync();
        var challenge = await ChallengeAsync(key);
        const string Id = "71b00000-0000-4000-8000-0000000000f1";
        using var first = await SendEntryAsync(key, $$$"""{"submissionId":"{{{Id}}}","revision":1,"values":{"name":"Ada"}}""", challenge);

        using var another = await ReceiptAsync(key, Id, await ChallengeAsync(key));
        using var none = await PostAsync($"{Root}/forms/{key}/receipt", $$$"""{"submissionId":"{{{Id}}}","challenge":null}""");
        using var forged = await ReceiptAsync(key, Id, "forged");
        using var unknown = await ReceiptAsync(key, Guid.NewGuid().ToString(), challenge);
        var bodies = new[] { await another.Content.ReadAsStringAsync(), await none.Content.ReadAsStringAsync(), await forged.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync() };

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(another), Is.EqualTo("404 not_found"));
            Assert.That(await RefusalAsync(none), Is.EqualTo("404 not_found"));
            Assert.That(await RefusalAsync(forged), Is.EqualTo("404 not_found"));
            Assert.That(await RefusalAsync(unknown), Is.EqualTo("404 not_found"));
            Assert.That(bodies.Distinct().Count(), Is.EqualTo(1), "every miss the same, so ids cannot be probed");
        });
    }

    [Test]
    public async Task ARenewal_ThroughTheEndpoint_CarriesTheIssueTimeOver()
    {
        var key = await PublishAsync();
        var first = await ChallengeAsync(key);
        var tokens = GetRequiredService<Web.PublicApi.ChallengeTokens>();
        var issued = tokens.Read(first, key, TimeSpan.Zero)!.IssuedUtc;
        _clock.Advance(TimeSpan.FromDays(2));

        using var renewed = await PostAsync($"{Root}/forms/{key}/challenge", $$$"""{"previous":"{{{first}}}"}""");
        var token = (await JsonAsync(renewed))["challenge"]!.GetValue<string>();
        var read = tokens.Read(token, key, TimeSpan.Zero)!;

        Assert.Multiple(async () =>
        {
            Assert.That(read.IssuedUtc, Is.EqualTo(issued));
            using var sent = await SendEntryAsync(key, Body, token);
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a renewed challenge counts as a new one");
        });
    }

    [Test]
    public async Task AnUpload_ChecksItsChallenge_BeforeTheFile()
    {
        var key = await PublishAsync(fields: Attachment);
        var path = $"{Root}/forms/{key}/uploads?fieldKey={Attachment.Key}";
        var other = await ChallengeAsync(await PublishAsync("Other"));
        var expired = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromHours(24));
        var current = await ChallengeAsync(key);

        using var late = await SendAsync(HttpMethod.Post, path, request => request.Content = Upload(expired));
        using var inTime = await SendAsync(HttpMethod.Post, path, request => request.Content = Upload(current));
        var stored = GetRequiredService<MemoryFileStore>().Ids.Count;
        using var none = await SendAsync(HttpMethod.Post, path, request => request.Content = WithoutAChallenge());
        using var forged = await SendAsync(HttpMethod.Post, path, request => request.Content = Upload("forged"));
        using var foreign = await SendAsync(HttpMethod.Post, path, request => request.Content = Upload(other));

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(none), Is.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(forged), Is.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(foreign), Is.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(late), Is.EqualTo("401 challenge_required"), "the day is over");
            Assert.That(inTime.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a challenge of today");
            Assert.That(none.Headers.ConnectionClose, Is.True, "the rest of its body is never read");
            Assert.That(GetRequiredService<MemoryFileStore>().Ids, Has.Count.EqualTo(stored), "nothing else was stored");
            Assert.That(stored, Is.EqualTo(1), "only the file with a challenge that counts");
        });
    }

    [Test]
    public async Task AnUploadsFile_IsClaimedOnlyWithTheChallengeItCameWith()
    {
        var key = await PublishAsync(fields: Attachment);
        var path = $"{Root}/forms/{key}/uploads?fieldKey={Attachment.Key}";
        var challenge = await ChallengeAsync(key);
        using var uploaded = await SendAsync(HttpMethod.Post, path, request => request.Content = Upload(challenge));
        var token = (await JsonAsync(uploaded))["token"]!.GetValue<string>();
        var body = $$$"""{"revision":1,"values":{"attachment":["{{{token}}}"]}}""";

        using var another = await SendEntryAsync(key, body, await ChallengeAsync(key));
        using var own = await SendEntryAsync(key, body, challenge);

        Assert.Multiple(async () =>
        {
            Assert.That(uploaded.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await RefusalAsync(another), Is.EqualTo("422 validation_failed"));
            Assert.That(own.StatusCode, Is.EqualTo(HttpStatusCode.OK), await own.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AFormForMembers_GivesNoChallengeToAVisitor_NorToAServerKey()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        });

        using var visitor = await PostAsync($"{Root}/forms/{key}/challenge", null);
        using var signed = await PostAsync($"{Root}/forms/{key}/challenge", null, "Jfb-Key " + ShopKey);
        using var sent = await SendEntryAsync(key, Body, "forged");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(visitor), Is.EqualTo("403 member_required"));
            Assert.That(await RefusalAsync(signed), Is.EqualTo("403 member_required"));
            Assert.That(await RefusalAsync(sent), Is.EqualTo("403 member_required"), "no member is signed in");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration["JironFormBuilder:PublicApi:ServerKeys:0:Name"] = "shop";
        configuration["JironFormBuilder:PublicApi:ServerKeys:0:Sha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ShopKey)));
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _clock = new StoppedClock();
        builder.Services.AddUnique<TimeProvider>(_clock);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
    }

    // The file alone, as raw bytes the tests' helper leaves as they are.
    private static ByteArrayContent WithoutAChallenge()
    {
        const string Boundary = "jfb-no-challenge";
        var content = new ByteArrayContent(Encoding.ASCII.GetBytes($"--{Boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.pdf\"\r\n\r\n%PDF-1.7 a file\r\n--{Boundary}--\r\n"));
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={Boundary}");
        return content;
    }

    private static MultipartFormDataContent Upload(string challenge)
    {
        var content = new MultipartFormDataContent { { new StringContent(challenge), "challenge" } };
        var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7 a file"));
        file.Headers.TryAddWithoutValidation("Content-Disposition", "form-data; name=\"file\"; filename=\"a.pdf\"");
        content.Add(file);
        return content;
    }

    private async Task<JsonNode> PlacesAsync(Guid key)
    {
        using var answer = await PostAsync($"{Root}/forms/{key}/challenge", null);
        return (await JsonAsync(answer))["places"]![Seat.Key.ToString("D")]!;
    }

    private Task<HttpResponseMessage> SendEntryAsync(Guid key, string body, string challenge)
    {
        var posted = JsonNode.Parse(body)!.AsObject();
        posted["challenge"] = challenge;
        return PostAsync($"{Root}/forms/{key}/entries", posted.ToJsonString());
    }

    private Task<HttpResponseMessage> ReceiptAsync(Guid key, string id, string challenge) =>
        PostAsync($"{Root}/forms/{key}/receipt", $$$"""{"submissionId":"{{{id}}}","challenge":"{{{challenge}}}"}""");

    private Task<HttpResponseMessage> PostAsync(string path, string? body, string? authorization = null) =>
        SendAsync(HttpMethod.Post, path, request =>
        {
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            if (authorization is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }
        });

    private async Task<int> CountAsync(Guid key) => (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50)).Count;

    // A clock that stands still until a test moves it.
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
