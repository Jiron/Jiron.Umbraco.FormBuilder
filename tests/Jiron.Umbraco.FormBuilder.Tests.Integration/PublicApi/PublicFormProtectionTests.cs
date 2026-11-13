using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The spam score of the JSON endpoint's submissions (ROADMAP 7.1c, ADR-0012, ADR-0139): a filled honeypot, a send sooner
/// than the fill time and a blocked address range each score with their reason; at 100 the entry goes to Spam, answered as
/// any other though its option is full and its unique answer taken, holding neither, with no action run; a server key's
/// request is scored on the ranges alone; and a text a visitor sees never tells.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormProtectionTests : PublicApiTestBase
{
    private const string Filled = """{"revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""";
    private const string Clean = """{"revision":1,"values":{"name":"Ada"}}""";

    private static readonly FormField Seat = Field("seat", "dropdown") with
    {
        Options = new FieldOptions { Items = [new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 1 }] },
    };

    private static readonly FormField Email = Field("email", "email") with { Validation = [new UniquePerFormCheck()] };
    private static readonly FormField Note = Field("note", "textarea") with { Keep = FieldKeep.UntilActionsRun };
    private static readonly FormAction Notify = new() { Key = Guid.Parse("71c00000-0000-4000-8000-000000000001"), Type = "testing.mail", Name = "Notify" };

    private StoppedClock _clock = null!;
    private string? _address;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    [SetUp]
    public void NoAddress() => _address = null;

    [Test]
    public async Task AFilledHoneypot_GoesToSpam_AnsweredAsAnyOther_WithItsReason_AndNoActionRun()
    {
        var key = await PublishAsync(change: definition => definition with { Actions = [Notify] }, fields: [NameField, Note]);

        using var clean = await InTimeAsync(key, """{"revision":1,"values":{"name":"Ada","note":"A note"}}""");
        using var spam = await InTimeAsync(key, """{"revision":1,"values":{"name":"Ada","note":"A note"},"honeypot":"Buy now"}""");
        var entries = await StoredAsync(key);
        var form = (await FormRows.GetAsync(key))!;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        var stats = await GetRequiredService<IDailyStatRepository>().ListAsync(form.Id, today.AddDays(-1), today.AddDays(1));
        var runs = GetRequiredService<IActionRunRepository>();
        var values = await Entries.ListValuesAsync([entries[1].Id]);

        Assert.Multiple(async () =>
        {
            Assert.That(spam.StatusCode, Is.EqualTo(HttpStatusCode.OK), await spam.Content.ReadAsStringAsync());
            Assert.That(Shape(await JsonAsync(spam)), Is.EqualTo(Shape(await JsonAsync(clean))), "nothing tells them apart");
            Assert.That(entries[0].State, Is.EqualTo(EntryState.Approved));
            Assert.That(entries[0].SpamScore, Is.Zero);
            Assert.That(entries[0].SpamReasons, Is.Empty);
            Assert.That(await runs.ListForEntryAsync(entries[0].Id), Is.Not.Empty);
            Assert.That(entries[1].State, Is.EqualTo(EntryState.Spam));
            Assert.That(entries[1].SpamScore, Is.EqualTo(100));
            Assert.That(entries[1].SpamReasons, Is.EqualTo("honeypot"));
            Assert.That(await runs.ListForEntryAsync(entries[1].Id), Is.Empty);
            Assert.That(values.Select(value => value.Text), Does.Contain("A note"), "kept for the actions marking it as no spam would run");
            Assert.That(stats.Single(stat => stat.Metric == "submissions").Value, Is.EqualTo(1));
            Assert.That(stats.Single(stat => stat.Metric == "spam").Value, Is.EqualTo(1));
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task ABlankHoneypot_IsNoSignal(string blank)
    {
        var key = await PublishAsync();

        using var answer = await InTimeAsync(key, new JsonObject { ["revision"] = 1, ["values"] = new JsonObject { ["name"] = "Ada" }, ["honeypot"] = blank }.ToJsonString());

        Assert.That((await StoredAsync(key)).Single().State, Is.EqualTo(EntryState.Approved));
    }

    [Test]
    public async Task AHoneypotThatIsNoText_IsAnInvalidRequest()
    {
        var key = await PublishAsync();

        using var answer = await InTimeAsync(key, """{"revision":1,"values":{"name":"Ada"},"honeypot":1}""");

        Assert.That(await RefusalAsync(answer), Does.StartWith("400 invalid_request"));
    }

    [Test]
    public async Task ASendBeforeTheFillTime_ScoresWithItsReason_AndStaysAnEntry_AndAtTheFillTimeNot()
    {
        var key = await PublishAsync();
        var early = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(3) - TimeSpan.FromMilliseconds(1));
        using var tooFast = await SendEntryAsync(key, Clean, early);
        var exact = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(3));
        using var inTime = await SendEntryAsync(key, Clean, exact);

        var entries = await StoredAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(entries[0].SpamScore, Is.EqualTo(60));
            Assert.That(entries[0].SpamReasons, Is.EqualTo("too_fast"));
            Assert.That(entries[0].State, Is.EqualTo(EntryState.Approved), "60 alone is no spam");
            Assert.That(entries[1].SpamScore, Is.Zero);
            Assert.That(entries[1].SpamReasons, Is.Empty);
        });
    }

    [Test]
    public async Task AnOldChallenge_CarriesItsTime_ForADayPastItsExpiry_AndNoLonger()
    {
        var key = await PublishAsync();
        var first = await ChallengeAsync(key);
        var previous = new JsonObject { ["previous"] = first }.ToJsonString();
        _clock.Advance(FormChallenges.Lifetime + FormChallenges.ReceiptGrace - TimeSpan.FromSeconds(1));
        using var inTime = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json(previous));
        var carried = (await JsonAsync(inTime))["challenge"]!.GetValue<string>();
        using var keeps = await SendEntryAsync(key, Clean, carried);
        _clock.Advance(TimeSpan.FromSeconds(1));
        using var tooLate = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json(previous));
        var fresh = (await JsonAsync(tooLate))["challenge"]!.GetValue<string>();
        using var counts = await SendEntryAsync(key, Clean, fresh);

        var entries = await StoredAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].SpamScore, Is.Zero);
            Assert.That(entries[1].SpamReasons, Does.Contain("too_fast"));
        });
    }

    [Test]
    public async Task ARenewedChallenge_CountsTheFillTimeFromTheFirst()
    {
        var key = await PublishAsync();
        var first = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(2));
        using var renewal = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json(new JsonObject { ["previous"] = first }.ToJsonString()));
        var renewed = (await JsonAsync(renewal))["challenge"]!.GetValue<string>();
        _clock.Advance(TimeSpan.FromSeconds(1));

        using var answer = await SendEntryAsync(key, Clean, renewed);

        Assert.That((await StoredAsync(key)).Single().SpamScore, Is.Zero);
    }

    [Test]
    public async Task TheSignals_AddUp_InTheOrderTheyAreRead()
    {
        var key = await PublishAsync();
        await BlockAsync("203.0.113.0/24");
        _address = "203.0.113.7";

        using var answer = await SendEntryAsync(key, Filled, await ChallengeAsync(key));

        var entry = (await StoredAsync(key)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.EqualTo(260));
            Assert.That(entry.SpamReasons, Is.EqualTo("honeypot,too_fast,blocked_range"));
        });
    }

    [TestCase("203.0.113.0/24", "203.0.113.200", true, TestName = "An IPv4 address in its range")]
    [TestCase("203.0.113.0/24", "203.0.114.1", false, TestName = "An IPv4 address outside its range")]
    [TestCase("203.0.113.0/24", "::ffff:203.0.113.200", true, TestName = "An IPv4 address as a dual-stack listener reports it")]
    [TestCase("203.0.113.0/24", "2001:db8::1", false, TestName = "An IPv6 address and an IPv4 range")]
    [TestCase("2001:db8:1::/48", "2001:db8:1:2::9", true, TestName = "An IPv6 address in its range")]
    [TestCase("2001:db8:1::/48", "2001:db8:2::9", false, TestName = "An IPv6 address outside its range")]
    public async Task ABlockedRange_PutsItsAddressesIntoSpam(string range, string address, bool blocked)
    {
        var key = await PublishAsync();
        await BlockAsync(range);
        _address = address;

        using var answer = await InTimeAsync(key, Clean);

        var entry = (await StoredAsync(key)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That(entry.State, Is.EqualTo(blocked ? EntryState.Spam : EntryState.Approved));
            Assert.That(entry.SpamReasons, Is.EqualTo(blocked ? "blocked_range" : string.Empty));
            Assert.That(entry.IpAddress, Is.Null, "matched against the request and never stored");
        });
    }

    [Test]
    public async Task ASpamCheck_SeesThePageAndTheAddress_OfTheRequest()
    {
        var key = await PublishAsync();
        var (_, contact, _) = await SitesAsync();
        _address = "203.0.113.7";

        using var sent = await InTimeAsync(key, $$$"""{"revision":1,"contentKey":"{{{contact}}}","values":{"name":"Ada"}}""");

        var seen = GetRequiredService<SeenChecks>().Contexts.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.IsSuccessStatusCode, Is.True);
            Assert.That(seen.Page, Is.EqualTo(contact));
            Assert.That(seen.Address, Is.EqualTo(IPAddress.Parse("203.0.113.7")));
        });
    }

    [Test]
    public async Task ASitesRanges_HoldForItsPages()
    {
        var key = await PublishAsync();
        var (shop, contact, other) = await SitesAsync();
        await Settings.SaveSiteAsync(shop, new ProtectionSettings { BlockedRanges = ["203.0.113.0/24"] }, ["blockedRanges"], 0, null);
        await GetRequiredService<IReferenceRepository>().ReplaceContentAsync(contact, [new ContentFormReference(key, "form", string.Empty)]);
        await GetRequiredService<IReferenceRepository>().ReplaceContentAsync(other, [new ContentFormReference(key, "form", string.Empty)]);
        _address = "203.0.113.7";

        using var onShop = await InTimeAsync(key, $$$"""{"revision":1,"contentKey":"{{{contact}}}","values":{"name":"Ada"}}""");
        using var elsewhere = await InTimeAsync(key, $$$"""{"revision":1,"contentKey":"{{{other}}}","values":{"name":"Ada"}}""");

        var entries = await StoredAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].State, Is.EqualTo(EntryState.Spam));
            Assert.That(entries[1].State, Is.EqualTo(EntryState.Approved), "a page of the other site that holds the form");
        });
    }

    [Test]
    public async Task AStrictSitesRanges_HoldForAPostThatNamesNoPageOfTheForm()
    {
        var key = await PublishAsync();
        var (shop, contact, other) = await SitesAsync();
        await Settings.SaveSiteAsync(shop, new ProtectionSettings { BlockedRanges = ["203.0.113.0/24"] }, ["blockedRanges"], 0, null);
        await GetRequiredService<IReferenceRepository>().ReplaceContentAsync(contact, [new ContentFormReference(key, "form", string.Empty)]);
        _address = "203.0.113.7";

        using var elsewhere = await InTimeAsync(key, $$$"""{"revision":1,"contentKey":"{{{other}}}","values":{"name":"Ada"}}""");
        using var noPage = await InTimeAsync(key, Clean);

        var entries = await StoredAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].State, Is.EqualTo(EntryState.Spam));
            Assert.That(entries[1].State, Is.EqualTo(EntryState.Spam));
            Assert.That(entries[1].SpamReasons, Does.Contain("blocked_range"));
        });
    }

    [Test]
    public async Task AStrictSitesWords_HoldForAPostThatNamesNoPageOfTheForm()
    {
        var key = await PublishAsync();
        var (shop, _, _) = await SitesAsync();
        await Settings.SaveSiteAsync(shop, new ProtectionSettings { ContentRules = ContentRuleLevel.Strict, BlockedWords = ["casino"] }, ["contentRules", "blockedWords"], 0, null);

        using var noPage = await InTimeAsync(key, """{"revision":1,"values":{"name":"casino"}}""");

        var entries = await StoredAsync(key);
        Assert.That(entries[0].SpamReasons, Does.Contain("blocked_word"));
    }

    [Test]
    public async Task AServerKey_IsScoredOnTheRangesAlone()
    {
        var key = await PublishAsync();

        using var filled = await SignedAsync(key, Filled);
        await BlockAsync("203.0.113.0/24");
        _address = "203.0.113.7";
        using var blocked = await SignedAsync(key, Clean);

        var entries = await StoredAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].State, Is.EqualTo(EntryState.Approved), "a key's request has no honeypot and no time");
            Assert.That(entries[0].SpamScore, Is.Zero);
            Assert.That(entries[1].State, Is.EqualTo(EntryState.Spam));
            Assert.That(entries[1].SpamReasons, Is.EqualTo("blocked_range"));
        });
    }

    [Test]
    public async Task Spam_IsAnsweredAsAccepted_ThoughItsOptionIsFullAndItsUniqueAnswerTaken_AndHoldsNeither()
    {
        var key = await PublishAsync(fields: [NameField, Seat, Email]);
        const string Answers = """{"name":"Ada","seat":"front","email":"ada@example.com"}""";
        const string Others = """{"name":"Bob","seat":"front","email":"bob@example.com"}""";

        // Spam first: it takes neither, so the first real submission still gets both.
        using var first = await InTimeAsync(key, $$$"""{"revision":1,"values":{{{Answers}}},"honeypot":"Buy now"}""");
        var form = (await FormRows.GetAsync(key))!;
        var option = new OptionPlace(Seat.Key, "front");
        var takenBySpam = (await Entries.GetPlacesTakenAsync(form.Id, [option])).GetValueOrDefault(option);
        using var real = await InTimeAsync(key, $$$"""{"revision":1,"values":{{{Answers}}}}""");

        // Full and taken now: spam hears nothing of it, and a real submission does.
        using var full = await InTimeAsync(key, $$$"""{"revision":1,"values":{{{Others}}},"honeypot":"Buy now"}""");
        using var taken = await InTimeAsync(key, $$$"""{"revision":1,"values":{"name":"Cy","email":"ada@example.com"},"honeypot":"Buy now"}""");
        using var refused = await InTimeAsync(key, $$$"""{"revision":1,"values":{{{Others}}}}""");
        using var alsoRefused = await InTimeAsync(key, $$$"""{"revision":1,"values":{"name":"Cy","email":"ada@example.com"}}""");
        var left = await Entries.GetPlacesTakenAsync(form.Id, [option]);

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(takenBySpam, Is.Zero, "spam holds no place");
            Assert.That(real.StatusCode, Is.EqualTo(HttpStatusCode.OK), await real.Content.ReadAsStringAsync());
            Assert.That(full.StatusCode, Is.EqualTo(HttpStatusCode.OK), await full.Content.ReadAsStringAsync());
            Assert.That(taken.StatusCode, Is.EqualTo(HttpStatusCode.OK), await taken.Content.ReadAsStringAsync());
            Assert.That(await RefusalAsync(refused), Does.StartWith("422 validation_failed"), "a real submission is told the option is full");
            Assert.That(await RefusalAsync(alsoRefused), Does.StartWith("422 validation_failed"), "and that the answer is taken");
            Assert.That(left[option], Is.EqualTo(1), "the real entry's place alone");
            Assert.That((await StoredAsync(key)).Count(entry => entry.State == EntryState.Spam), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Spam_IsStillCheckedLikeAnyPost()
    {
        var key = await PublishAsync(fields: [NameField, Email]);

        using var invalid = await InTimeAsync(key, """{"revision":1,"values":{"name":"Ada","email":"no address"},"honeypot":"Buy now"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(invalid), Does.StartWith("422 validation_failed"), "this tells nothing of the protection");
            Assert.That(await StoredAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task APagesCheck_IsNotScored_AndStillTellsThatAnOptionIsFull()
    {
        var key = await PublishAsync(fields: [NameField, Seat]);
        using var real = await InTimeAsync(key, """{"revision":1,"values":{"name":"Ada","seat":"front"}}""");
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;
        await BlockAsync("203.0.113.0/24");
        _address = "203.0.113.7";

        using var check = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/validate", request => request.Content = Json($$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Bob","seat":"front"},"honeypot":"Buy now"}"""));

        Assert.That(await RefusalAsync(check), Does.StartWith("422 validation_failed"));
    }

    [Test]
    public async Task ARetryOfSpam_GetsItsAnswerAgain()
    {
        var key = await PublishAsync();
        var challenge = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(3));
        var body = $$$"""{"submissionId":"{{{Guid.NewGuid()}}}","revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""";

        using var first = await SendEntryAsync(key, body, challenge);
        using var retry = await SendEntryAsync(key, body, challenge);

        Assert.Multiple(async () =>
        {
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()));
            Assert.That((await StoredAsync(key)).Single().State, Is.EqualTo(EntryState.Spam));
        });
    }

    [Test]
    public async Task AnOutcomesMergeTag_ReadsSpamAsAnyOtherEntry()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Outcomes = [new FormOutcome { Key = Guid.Parse("71c00000-0000-4000-8000-000000000002"), Message = new LocalizedHtml("<p>{{ entry.state }}</p>") }],
        });
        var challenge = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(3));
        var body = $$$"""{"submissionId":"{{{Guid.NewGuid()}}}","revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""";

        using var spam = await SendEntryAsync(key, body, challenge);
        using var replay = await SendEntryAsync(key, body, challenge);

        Assert.Multiple(async () =>
        {
            Assert.That((await JsonAsync(spam))["outcome"]!["html"]!.GetValue<string>(), Is.EqualTo("<p>approved</p>"));
            Assert.That((await JsonAsync(replay))["outcome"]!["html"]!.GetValue<string>(), Is.EqualTo("<p>approved</p>"), "and again in its retry");
            Assert.That((await StoredAsync(key)).Single().State, Is.EqualTo(EntryState.Spam));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _clock = new StoppedClock();
        builder.Services.AddUnique<TimeProvider>(_clock);
        builder.Services.AddSingleton<IStartupFilter>(new AddressFilter(() => _address));
        builder.Services.AddSingleton<SeenChecks>();
        builder.JironFormBuilder().SpamChecks().Append<SeeingCheck>();
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static FormField Field(string alias, string type) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
    };

    // What a page could compare of two answers: their names, with what differs by entry left out.
    private static string Shape(JsonNode answer) => string.Join(',', answer.AsObject().Select(member => member.Key));

    // Sends with a challenge the fill time has passed for.
    private async Task<HttpResponseMessage> InTimeAsync(Guid key, string body)
    {
        var challenge = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(3));
        return await SendEntryAsync(key, body, challenge);
    }

    private Task<HttpResponseMessage> SendEntryAsync(Guid key, string body, string challenge)
    {
        var posted = JsonNode.Parse(body)!.AsObject();
        posted["challenge"] = challenge;
        return SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = Json(posted.ToJsonString()));
    }

    private Task<HttpResponseMessage> SignedAsync(Guid key, string body) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + TestServerKey);
            request.Content = Json(body);
        });

    private async Task BlockAsync(params string[] ranges)
    {
        var current = await Settings.GetAsync<ProtectionSettings>();
        await Settings.SaveAsync(current.Value with { BlockedRanges = ranges }, current.Version, null);
    }

    // The form's entries, oldest first.
    private async Task<IReadOnlyList<StoredEntry>> StoredAsync(Guid key)
    {
        var ids = await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50);
        var stored = new List<StoredEntry>();
        foreach (var id in ids.OrderBy(id => id))
        {
            stored.Add((await Entries.GetByIdAsync(id))!);
        }

        return stored;
    }

    // Two sites, each a published root; the first with a published page. In the test host, content made outside a request
    // reaches the navigation and the publish states only when they are built from the database.
    private async Task<(Guid Shop, Guid Contact, Guid Other)> SitesAsync()
    {
        var contents = GetRequiredService<IContentService>();
        var types = GetRequiredService<IContentTypeService>();
        var page = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(page, Constants.Security.SuperUserKey)).Success, Is.True);
        page.AllowedContentTypes = [new ContentTypeSort(page.Key, 0, page.Alias)];
        Assert.That((await types.UpdateAsync(page, Constants.Security.SuperUserKey)).Success, Is.True);

        Content Published(string name, Content? parent)
        {
            var content = parent is null ? new Content(name, -1, page) : new Content(name, parent, page);
            contents.Save(content);
            contents.Publish(content, ["*"]);
            return content;
        }

        var shop = Published("Shop", null);
        var contact = Published("Contact", shop);
        var other = Published("Other", null);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IDocumentPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return (shop.Key, contact.Key, other.Key);
    }

    // Gives a request the address a test names, as the connection would.
    private sealed class SeenChecks
    {
        public List<FormBuilderSpamCheckContext> Contexts { get; } = [];
    }

    private sealed class SeeingCheck(SeenChecks seen) : IFormBuilderSpamCheck
    {
        public Task<FormBuilderSpamSignal?> CheckAsync(FormBuilderSpamCheckContext context, CancellationToken cancellationToken)
        {
            seen.Contexts.Add(context);
            return Task.FromResult<FormBuilderSpamSignal?>(null);
        }
    }

    private sealed class AddressFilter(Func<string?> address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, go) =>
            {
                if (address() is { } named)
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(named);
                }

                return go();
            });
            next(app);
        };
    }

    // A clock that stands still until a test moves it.
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
