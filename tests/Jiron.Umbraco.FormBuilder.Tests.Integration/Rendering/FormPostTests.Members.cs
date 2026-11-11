using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.DataProtection;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// A form for members without JavaScript (ROADMAP 14.3, ADR-0012, ADR-0231): the member's page carries a challenge issued
// to them and refuses another origin's frames; the post with it is the member's entry, and a reload of it leads to the
// same outcome without a second entry; without it, with an expired or another member's, the page shows the answers again
// with a fresh challenge; another origin, or an opaque one the browser does not vouch for, gets a bare 403 whatever the
// post holds; and the outcome is the member's alone.
public sealed partial class FormPostTests
{
    private const string MembersPage = "/post-members/";

    [Test]
    public async Task AMembersPage_CarriesTheirChallenge_AndRefusesAnotherOriginsFrames()
    {
        var key = await PublishFormAsync("postMembers", ForMembers, NameField);
        _ = await PagesAsync("post-members");
        using var visitor = await GetAsync(MembersPage);
        await SignInMemberAsync("member@example.com");
        var member = GetRequiredService<IMemberService>().GetByUsername(MemberName)!.Key;
        using var signedIn = await GetAsync(MembersPage);
        var form = FormOf(await ParseAsync(signedIn), "jfb-1");
        var challenge = form.QuerySelector($"input[type='hidden'][name='{FormPostFields.Challenge}']")?.GetAttribute("value");

        Assert.Multiple(() =>
        {
            Assert.That(challenge, Is.Not.Null, "the member's page carries a challenge");
            Assert.That(GetRequiredService<ChallengeTokens>().Read(challenge, key, TimeSpan.Zero)?.Member, Is.EqualTo(member), "issued to the member signed in");
            Assert.That(Header(signedIn, "Content-Security-Policy"), Does.Contain(PageFraming.FrameAncestors));
            Assert.That(Header(signedIn, "X-Frame-Options"), Is.EqualTo(PageFraming.SameOrigin));
            Assert.That(Header(signedIn, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(Header(visitor, "Content-Security-Policy"), Does.Contain(PageFraming.FrameAncestors), "the notice's page too: it says who may fill the form in");
            Assert.That(Header(signedIn, "Set-Cookie"), Is.Null, "no cookie is set for it (ADR-0004)");
        });
    }

    [Test]
    public async Task AMembersPost_WithTheChallengeItsPageCarries_IsTheirEntry_AndAReloadLeadsToTheSameOutcome()
    {
        var key = await PublishFormAsync("postMembers", ForMembers, NameField);
        _ = await PagesAsync("post-members");
        await SignInMemberAsync("member@example.com");
        var member = GetRequiredService<IMemberService>().GetByUsername(MemberName)!.Key;
        var form = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        Fill(form, "name", "Ada");
        var posted = Posted(form, FormPostFields.Submit);

        using var sent = await PostPairsAsync(MembersPage, posted, OwnOrigin, MembersPage);
        using var again = await PostPairsAsync(MembersPage, posted, OwnOrigin, MembersPage);
        var stored = await EntriesOfAsync(key);
        var (_, outcome) = await PageAndHtmlAsync(again.Headers.Location!.OriginalString);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a reload of the post finds its entry");
            Assert.That(again.Headers.Location!.OriginalString, Does.StartWith(MembersPage + "?jfb-sent=jfb-1."));
            Assert.That(stored, Has.Count.EqualTo(1), "one entry for two posts");
            Assert.That(stored[0].MemberKey, Is.EqualTo(member), "the member's entry");
            Assert.That(stored[0].State, Is.Not.EqualTo(EntryState.Spam), "sent at once after its page rendered, as the minimum fill time does not apply without JavaScript (ADR-0012)");
            Assert.That(outcome.QuerySelector("#jfb-1 [data-jfb-outcome]"), Is.Not.Null, "the page shows its outcome");
            Assert.That(outcome.QuerySelector("form#jfb-1 input[name='name']"), Is.Null, "in the form's place");
        });
    }

    [Test]
    public async Task AMembersPost_WithoutTheirOwnLiveChallenge_ShowsTheAnswersAgain_WithAFreshChallenge()
    {
        var key = await PublishFormAsync("postMembers", ForMembers, NameField);
        _ = await PagesAsync("post-members");
        await SignInMemberAsync("member@example.com");
        var member = GetRequiredService<IMemberService>().GetByUsername(MemberName)!.Key;
        var form = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        Fill(form, "name", "Ada");
        var expired = new ChallengeTokens(GetRequiredService<IDataProtectionProvider>(), new TestTimeProvider(DateTimeOffset.UtcNow - TimeSpan.FromHours(25))).Issue(key, member);
        var cases = new Dictionary<string, string?>
        {
            ["none"] = null,
            ["expired"] = expired,
            ["another member's"] = GetRequiredService<ChallengeTokens>().Issue(key, Guid.NewGuid()),
            ["a visitor's"] = GetRequiredService<ChallengeTokens>().Issue(key, null),
        };

        var shown = new Dictionary<string, (string? Notice, string? Name, string? Challenge)>();
        foreach (var (name, challenge) in cases)
        {
            var posted = Posted(form, FormPostFields.Submit).Where(pair => pair.Key != FormPostFields.Challenge).ToList();
            if (challenge is not null)
            {
                posted.Add(new(FormPostFields.Challenge, challenge));
            }

            using var response = await PostPairsAsync(MembersPage, posted, OwnOrigin, MembersPage);
            var again = FormOf(await ParseAsync(response), "jfb-1");
            shown[name] = (
                again.QuerySelector("[data-jfb-notice]")?.GetAttribute("data-jfb-notice"),
                again.QuerySelector("[name='name']")?.GetAttribute("value"),
                again.QuerySelector($"input[name='{FormPostFields.Challenge}']")?.GetAttribute("value"));
        }

        Assert.Multiple(async () =>
        {
            foreach (var (name, (notice, answer, fresh)) in shown)
            {
                Assert.That(notice, Is.EqualTo(FormPostHandler.NotSentCode), name);
                Assert.That(answer, Is.EqualTo("Ada"), name + ": the answers are kept");
                Assert.That(GetRequiredService<ChallengeTokens>().Read(fresh, key, TimeSpan.Zero)?.Member, Is.EqualTo(member), name + ": with a fresh challenge of the member's");
            }

            Assert.That(await EntriesOfAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task AMembersPost_FromAnotherOrigin_OrAnOpaqueOneTheBrowserDoesNotVouchFor_IsABare403_WhateverItHolds()
    {
        var key = await PublishFormAsync("postMembers", ForMembers, NameField);
        _ = await PagesAsync("post-members");
        await SignInMemberAsync("member@example.com");
        var form = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        Fill(form, "name", "Ada");
        var good = Posted(form, FormPostFields.Submit);
        var badChallenge = good.Select(pair => pair.Key == FormPostFields.Challenge ? new KeyValuePair<string, string>(pair.Key, "forged") : pair).ToList();
        var wrongAnswer = good.Select(pair => pair.Key == "name" ? new KeyValuePair<string, string>(pair.Key, new string('x', 5000)) : pair).ToList();

        var answers = new List<(string Case, HttpStatusCode Status, string Body)>();
        foreach (var (name, pairs) in new[] { ("a bad challenge", badChallenge), ("an answer the form refuses", wrongAnswer), ("a good post", good) })
        {
            foreach (var (origin, site) in new (string?, string?)[] { ("https://evil.test", null), ("null", null), ("null", "cross-site"), (null, null) })
            {
                using var response = await SendAsync(HttpMethod.Post, MembersPage, request =>
                {
                    request.Content = new FormUrlEncodedContent(pairs);
                    if (origin is not null)
                    {
                        request.Headers.TryAddWithoutValidation("Origin", origin);
                    }

                    if (site is not null)
                    {
                        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
                    }
                });
                answers.Add(($"{name} from {origin ?? "no origin"} ({site ?? "unsaid"})", response.StatusCode, await response.Content.ReadAsStringAsync()));
            }
        }

        Assert.Multiple(async () =>
        {
            foreach (var (name, status, body) in answers)
            {
                Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), name);
                Assert.That(body, Does.Not.Contain("Ada").And.Not.Contain("xxxxx").And.Not.Contain(FormPostFields.Challenge), name + ": nothing of the post, and no fresh challenge");
            }

            Assert.That(await EntriesOfAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task TheOutcomeOfAMembersEntry_IsTheirsAlone()
    {
        var key = await PublishFormAsync("postMembers", ForMembers, NameField);
        _ = await PagesAsync("post-members");
        await SignInMemberAsync("member@example.com");
        var form = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        Fill(form, "name", "Ada");
        using var sent = await PostPairsAsync(MembersPage, Posted(form, FormPostFields.Submit), OwnOrigin, MembersPage);
        var location = sent.Headers.Location!.OriginalString;
        var (_, theirs) = await PageAndHtmlAsync(location);
        var submission = (await EntriesOfAsync(key)).Single().SubmissionId;
        var submissions = GetRequiredService<Core.Submissions.FormSubmissions>();
        var own = await submissions.ReceiptAsync(key, submission, (await EntriesOfAsync(key)).Single().MemberKey);
        var another = await submissions.ReceiptAsync(key, submission, Guid.NewGuid());
        var nobody = await submissions.ReceiptAsync(key, submission, null);

        Assert.Multiple(() =>
        {
            Assert.That(theirs.QuerySelector("#jfb-1 [data-jfb-outcome]"), Is.Not.Null, "the member who sent it sees its outcome");
            Assert.That(own.Receipt, Is.Not.Null);
            Assert.That((another.Refusal, nobody.Refusal), Is.EqualTo((Core.Submissions.SubmissionRefusal.EntryNotFound, Core.Submissions.SubmissionRefusal.EntryNotFound)), "anyone else meets the miss of an unknown submission");
        });
    }

    [Test]
    public async Task OnAFormForMembersWhoseProtectionNeedsJavaScript_TheirPost_GoesToSpam_NeverRefusedForThePuzzle()
    {
        var key = await PublishFormAsync(
            "postMembers",
            definition => ForMembers(definition) is var members
                ? members with { Settings = members.Settings with { Protection = members.Settings.Protection with { Level = ProtectionLevel.ProofOfWork } } }
                : definition,
            NameField);
        _ = await PagesAsync("post-members");
        await SignInMemberAsync("member@example.com");
        var form = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        Fill(form, "name", "Ada");

        using var sent = await PostPairsAsync(MembersPage, Posted(form, FormPostFields.Submit), OwnOrigin, MembersPage);
        var stored = await EntriesOfAsync(key);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(stored.Select(entry => (entry.State, entry.SpamReasons)), Is.EqualTo(new[] { (EntryState.Spam, "no_script") }), "the rendered challenge carries no puzzle, so the post is scored as any post without script is (ADR-0231)");
        });
    }

    [Test]
    public async Task AMemberWhoHasSentAFormThatTakesOneEntryPerMember_SeesThatInItsPlace_AndTheirSecondPostMakesNoEntry()
    {
        var key = await PublishFormAsync(
            "postMembers",
            definition => ForMembers(definition) is var members
                ? members with { Settings = members.Settings with { Availability = members.Settings.Availability with { OnePerMember = true } } }
                : definition,
            NameField);
        _ = await PagesAsync("post-members");
        await SignInMemberAsync("member@example.com");
        var first = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        var second = FormOf(await DocumentAsync(MembersPage), "jfb-1");
        Fill(first, "name", "Ada");
        Fill(second, "name", "Ada again");

        using var sent = await PostPairsAsync(MembersPage, Posted(first, FormPostFields.Submit), OwnOrigin, MembersPage);
        using var again = await PostPairsAsync(MembersPage, Posted(second, FormPostFields.Submit), OwnOrigin, MembersPage);
        var afterSecond = await ParseAsync(again);
        var later = await DocumentAsync(MembersPage);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), await sent.Content.ReadAsStringAsync());
            Assert.That(afterSecond.QuerySelector("#jfb-1 .jfb-notice h2")?.TextContent, Is.EqualTo("You've already sent this form"), "a second post, from a page opened before the first was sent");
            Assert.That(afterSecond.Body!.InnerHtml, Does.Not.Contain("Ada again"), "its answers are not shown again");
            Assert.That(later.QuerySelector("#jfb-1 .jfb-notice h2")?.TextContent, Is.EqualTo("You've already sent this form"), "the page from then on");
            Assert.That(later.QuerySelector("#jfb-1 .jfb-notice p")?.TextContent, Is.EqualTo("Each member can send it once."));
            Assert.That(later.QuerySelector($"input[name='{FormPostFields.Challenge}']"), Is.Null, "and no challenge, as nothing is left to send");
            Assert.That(await EntriesOfAsync(key), Has.Count.EqualTo(1));
        });
    }

    private static FormDefinition ForMembers(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
    };

    // The form's entries, oldest first.
    private async Task<List<StoredEntry>> EntriesOfAsync(Guid key)
    {
        var ids = await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10);
        var stored = new List<StoredEntry>();
        foreach (var id in ids.Order())
        {
            stored.Add((await Entries.GetByIdAsync(id))!);
        }

        return stored;
    }
}
