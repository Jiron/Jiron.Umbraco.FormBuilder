using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A form at the proof-of-work level (ROADMAP 7.2a, ADR-0012, ADR-0141), by its own setting and through its site's: the
/// challenge carries a puzzle, a submission with its answer passes, one without or with a wrong one asks for a new
/// challenge and stores nothing, a page's check and an upload ask for none, and a server key skips it. A form at the standard
/// level carries none.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormProofOfWorkTests : PublicApiTestBase
{
    private const int Bits = 8;
    private const string Body = """{"revision":1,"values":{"name":"Ada"}}""";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    [Test]
    public async Task AFormAtTheLevel_GivesAPuzzle_OnTheChallengesOwnId_WithTheSitesDifficulty()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync(change: AtTheLevel);

        var (challenge, puzzle) = await AskAsync(key);

        var ticket = GetRequiredService<ChallengeTokens>().Read(challenge, key, TimeSpan.Zero)!;
        Assert.Multiple(() =>
        {
            Assert.That(puzzle!["bits"]!.GetValue<int>(), Is.EqualTo(Bits));
            Assert.That(puzzle["nonce"]!.GetValue<string>(), Is.EqualTo(ticket.Id.ToString("N")));
            Assert.That(ticket.ProofBits, Is.EqualTo(Bits), "the signature holds the difficulty");
        });
    }

    [Test]
    public async Task AFormAtTheStandardLevel_GivesNoPuzzle_AndAsksForNoAnswer()
    {
        await SiteAsync(ProtectionLevel.ProofOfWork);
        var key = await PublishAsync(change: definition => Level(definition, ProtectionLevel.Standard));

        var (challenge, puzzle) = await AskAsync(key);
        using var answer = await SendAsync(key, Body, challenge);

        Assert.Multiple(async () =>
        {
            Assert.That(puzzle, Is.Null);
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        });
    }

    [TestCase(ProtectionLevel.ProofOfWork, true, TestName = "A form that inherits the level")]
    [TestCase(ProtectionLevel.Standard, false, TestName = "A form that inherits the standard level")]
    public async Task AFormThatInherits_TakesItsSitesLevel(ProtectionLevel site, bool asks)
    {
        await SiteAsync(site);
        var key = await PublishAsync();

        var (_, puzzle) = await AskAsync(key);

        Assert.That(puzzle is not null, Is.EqualTo(asks));
    }

    [Test]
    public async Task TheRightAnswer_MakesItsEntry()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync(change: AtTheLevel);
        var (challenge, puzzle) = await AskAsync(key);

        using var answer = await SendAsync(key, Body, challenge, Solve(puzzle!));

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That(await StoredAsync(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task NoAnswer_AWrongOne_AndOneThatIsNoNumber_AskForANewChallenge_AndStoreNothing()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync(change: AtTheLevel);
        var (challenge, puzzle) = await AskAsync(key);
        var right = long.Parse(Solve(puzzle!), CultureInfo.InvariantCulture);
        var nonce = puzzle!["nonce"]!.GetValue<string>();
        var wrong = Enumerable.Range(0, 1000).Select(number => right + 1 + number).First(number => ProofOfWork.LeadingZeroBits(nonce, number.ToString(CultureInfo.InvariantCulture)) < Bits);

        using var none = await SendAsync(key, Body, challenge);
        using var mistaken = await SendAsync(key, Body, challenge, wrong.ToString(CultureInfo.InvariantCulture));
        using var noNumber = await SendAsync(key, Body, challenge, "not a number");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(none), Is.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(mistaken), Is.EqualTo("401 challenge_required"));
            Assert.That(await RefusalAsync(noNumber), Is.EqualTo("401 challenge_required"));
            Assert.That(await StoredAsync(key), Is.Zero);
        });
    }

    [Test]
    public async Task AChallengeWithoutAPuzzle_DoesNotPass_OnceTheFormIsAtTheLevel()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync();
        var (challenge, _) = await AskAsync(key);
        await SiteAsync(ProtectionLevel.ProofOfWork);

        using var answer = await SendAsync(key, Body, challenge, "1");

        Assert.That(await RefusalAsync(answer), Is.EqualTo("401 challenge_required"));
    }

    [Test]
    public async Task ASitesLevel_HoldsForThePagesOfThatSiteAlone()
    {
        var (shop, contact, other) = await SitesAsync();
        await SiteAsync(ProtectionLevel.Standard);
        await Settings.SaveSiteAsync(shop, new ProtectionSettings { Level = ProtectionLevel.ProofOfWork, ProofBits = Bits }, ["level", "proofBits"], 0, null);
        var key = await PublishAsync();

        var (onShop, puzzleOnShop) = await AskAsync(key, contact);
        var (_, puzzleElsewhere) = await AskAsync(key, other);
        using var unsolved = await SendAsync(key, $$$"""{"revision":1,"contentKey":"{{{contact}}}","values":{"name":"Ada"}}""", onShop);
        using var solved = await SendAsync(key, $$$"""{"revision":1,"contentKey":"{{{contact}}}","values":{"name":"Ada"}}""", onShop, Solve(puzzleOnShop!));

        Assert.Multiple(async () =>
        {
            Assert.That(puzzleElsewhere, Is.Null);
            Assert.That(await RefusalAsync(unsolved), Is.EqualTo("401 challenge_required"));
            Assert.That(solved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await solved.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task APagesCheck_AsksForNoAnswer_AndRefusesOneAsAnUnknownMember()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync(change: AtTheLevel);
        var (challenge, _) = await AskAsync(key);
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;

        using var check = await SendRawAsync(key, "validate", $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada"},"challenge":"{{{challenge}}}"}""");
        using var withAnswer = await SendRawAsync(key, "validate", $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada"},"challenge":"{{{challenge}}}","proofOfWork":"1"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(check.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await check.Content.ReadAsStringAsync());
            Assert.That(await RefusalAsync(withAnswer), Does.StartWith("400 invalid_request"));
        });
    }

    [Test]
    public async Task AServerKey_SkipsThePuzzle()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync(change: AtTheLevel);

        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
        {
            request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + TestServerKey);
            request.Content = Json(Body);
        });

        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task ARetryOfTheSameSubmission_HoldsItsAnswer()
    {
        await SiteAsync(ProtectionLevel.Standard);
        var key = await PublishAsync(change: AtTheLevel);
        var (challenge, puzzle) = await AskAsync(key);
        var body = $$$"""{"submissionId":"{{{Guid.NewGuid()}}}","revision":1,"values":{"name":"Ada"}}""";
        var solution = Solve(puzzle!);

        using var first = await SendAsync(key, body, challenge, solution);
        using var retry = await SendAsync(key, body, challenge, solution);

        Assert.Multiple(async () =>
        {
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()));
            Assert.That(await StoredAsync(key), Is.EqualTo(1));
        });
    }

    private static FormDefinition AtTheLevel(FormDefinition definition) => Level(definition, ProtectionLevel.ProofOfWork);

    private static FormDefinition Level(FormDefinition definition, ProtectionLevel level) => definition with
    {
        Settings = definition.Settings with { Protection = definition.Settings.Protection with { Level = level } },
    };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // Works the puzzle out as a page does.
    private static string Solve(JsonNode puzzle)
    {
        var nonce = puzzle["nonce"]!.GetValue<string>();
        var bits = puzzle["bits"]!.GetValue<int>();
        for (var number = 0L; ; number++)
        {
            var answer = number.ToString(CultureInfo.InvariantCulture);
            if (ProofOfWork.LeadingZeroBits(nonce, answer) >= bits)
            {
                return answer;
            }
        }
    }

    private async Task SiteAsync(ProtectionLevel level)
    {
        var current = await Settings.GetAsync<ProtectionSettings>();
        await Settings.SaveAsync(current.Value with { Level = level, ProofBits = Bits }, current.Version, null);
    }

    private async Task<(string Challenge, JsonNode? Puzzle)> AskAsync(Guid key, Guid? page = null)
    {
        var body = page is null ? "{}" : $$$"""{"contentKey":"{{{page}}}"}""";
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json(body));
        var read = await JsonAsync(answer);
        return (read["challenge"]!.GetValue<string>(), read["proofOfWork"]);
    }

    private Task<HttpResponseMessage> SendAsync(Guid key, string body, string challenge, string? solution = null)
    {
        var posted = JsonNode.Parse(body)!.AsObject();
        posted["challenge"] = challenge;
        if (solution is not null)
        {
            posted["proofOfWork"] = solution;
        }

        return SendRawAsync(key, "entries", posted.ToJsonString());
    }

    private Task<HttpResponseMessage> SendRawAsync(Guid key, string endpoint, string body) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/{endpoint}", request => request.Content = Json(body));

    private async Task<int> StoredAsync(Guid key) => (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50)).Count;

    // Two sites, each a published root; the first with a published page. In the test host, content made outside a request
    // reaches the navigation and the publish states only when they are built from the database.
    private async Task<(Guid Shop, Guid Contact, Guid Other)> SitesAsync()
    {
        var contents = GetRequiredService<global::Umbraco.Cms.Core.Services.IContentService>();
        var types = GetRequiredService<global::Umbraco.Cms.Core.Services.IContentTypeService>();
        var page = new global::Umbraco.Cms.Core.Models.ContentType(GetRequiredService<global::Umbraco.Cms.Core.Strings.IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(page, global::Umbraco.Cms.Core.Constants.Security.SuperUserKey)).Success, Is.True);
        page.AllowedContentTypes = [new global::Umbraco.Cms.Core.Models.ContentTypeSort(page.Key, 0, page.Alias)];
        Assert.That((await types.UpdateAsync(page, global::Umbraco.Cms.Core.Constants.Security.SuperUserKey)).Success, Is.True);

        global::Umbraco.Cms.Core.Models.Content Published(string name, global::Umbraco.Cms.Core.Models.Content? parent)
        {
            var content = parent is null ? new global::Umbraco.Cms.Core.Models.Content(name, -1, page) : new global::Umbraco.Cms.Core.Models.Content(name, parent, page);
            contents.Save(content);
            contents.Publish(content, ["*"]);
            return content;
        }

        var shop = Published("Shop", null);
        var contact = Published("Contact", shop);
        var other = Published("Other", null);
        await GetRequiredService<global::Umbraco.Cms.Core.Services.Navigation.IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<global::Umbraco.Cms.Core.Services.Navigation.IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return (shop.Key, contact.Key, other.Key);
    }
}
