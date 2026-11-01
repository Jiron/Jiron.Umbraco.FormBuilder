using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The content rules and a site's own spam checks (ROADMAP 7.1d, ADR-0012, ADR-0140): each blocked word, a blocked email
/// domain and too many links score with their reason, on the texts a visitor typed; a form's strictness weighs them, or
/// turns them off; a check adds its score and reason, and one that fails or stalls counts for nothing; and every one of
/// them holds for a server key, as the honeypot and the time do not.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormContentRulesTests : PublicApiTestBase
{
    private static readonly FormField Message = Field("message", "textarea");
    private static readonly FormField Email = Field("email", "email");
    private static readonly FormField Website = Field("website", "url");

    private static readonly FormField Colour = Field("colour", "dropdown") with
    {
        Options = new FieldOptions { Items = [new FieldOption { Value = "casino", Label = new LocalizedText("Casino") }] },
    };

    private static readonly FormField Guests = Field("guests", "repeater") with
    {
        Settings = SettingsObject.Parse("""{"maxRows":9}"""),
        Fields = [Field("guest", "text")],
    };

    private TestCheck _check = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [SetUp]
    public void SignWithTheServerKey()
    {
        // The time and the honeypot are beside the point here, and a key's request meets every rule all the same.
        SendsWithServerKey = true;
        _check.Reset();
    }

    [TestCase("We run a Casino here.", 40, TestName = "A word in another case")]
    [TestCase("casino", 40, TestName = "A word that is the whole text")]
    [TestCase("(casino)", 40, TestName = "A word beside punctuation")]
    [TestCase("Occasinos are not it.", 0, TestName = "A word within another word")]
    [TestCase("Casinos are not it.", 0, TestName = "A word that only starts another")]
    [TestCase("Occasino is not it.", 0, TestName = "A word that only ends another")]
    [TestCase("Casinos and a casino.", 40, TestName = "A word after one that only holds it")]
    [TestCase("A casino, with cheap pills.", 80, TestName = "A phrase beside a word: each scores")]
    [TestCase("cheap  pills", 0, TestName = "A phrase is compared as written")]
    [TestCase("Casino casino casino.", 40, TestName = "A word counts once however often it stands")]
    public async Task EachBlockedWord_Scores(string message, int score)
    {
        await SiteAsync(new ProtectionSettings { BlockedWords = ["casino", "cheap pills"] });
        var key = await PublishAsync(fields: [Message]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = message });

        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.EqualTo(score));
            Assert.That(entry.SpamReasons, Is.EqualTo(score == 0 ? string.Empty : "blocked_word"), "stored once");
            Assert.That(entry.State, Is.EqualTo(EntryState.Submitted));
        });
    }

    [Test]
    public async Task ABlockedWord_IsCompared_AsRulesCompareTexts()
    {
        await SiteAsync(new ProtectionSettings { BlockedWords = ["STRASSE \u03a3\u039f\u03a6\u039f\u03a3"] });
        var key = await PublishAsync(fields: [Message]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "strasse \u03c3\u03bf\u03c6\u03bf\u03c2" });

        Assert.That(entry.SpamReasons, Is.EqualTo("blocked_word"));
    }

    [Test]
    public async Task EnoughBlockedWords_PutASubmissionIntoSpam()
    {
        await SiteAsync(new ProtectionSettings { BlockedWords = ["one", "two", "three"] });
        var key = await PublishAsync(fields: [Message]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "one two three" });

        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.EqualTo(120));
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
        });
    }

    [Test]
    public async Task AValueTheServerResolves_IsNotJudged()
    {
        await SiteAsync(new ProtectionSettings { BlockedWords = ["casino"] });
        var hidden = Field("campaign", "hidden") with { DefaultValue = FieldDefaultValue.Of("The casino campaign") };
        var key = await PublishAsync(fields: [Message, hidden]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello" });

        Assert.That(entry.SpamScore, Is.Zero);
    }

    [Test]
    public async Task ABlockedWord_CountsInEveryKindOfTextAnswer_AndARow_ButNotInAnOptionsValue()
    {
        await SiteAsync(new ProtectionSettings { BlockedWords = ["casino"] });
        var key = await PublishAsync(fields: [Message, Email, Website, Colour, Guests]);

        var chosen = await SentAsync(key, new JsonObject { ["colour"] = "casino" });
        var inARow = await SentAsync(key, new JsonObject { ["guests"] = new JsonArray(new JsonObject { ["guest"] = "A casino" }) });
        var inAnEmail = await SentAsync(key, new JsonObject { ["email"] = "casino@example.com" });
        var inAnAddress = await SentAsync(key, new JsonObject { ["website"] = "https://example.com/casino" });

        Assert.Multiple(() =>
        {
            Assert.That(chosen.SpamScore, Is.Zero, "no visitor typed an option's value");
            Assert.That(inARow.SpamScore, Is.EqualTo(40));
            Assert.That(inAnEmail.SpamScore, Is.EqualTo(40));
            Assert.That(inAnAddress.SpamScore, Is.EqualTo(40));
        });
    }

    [TestCase("ada@spam.example", true, TestName = "A blocked domain")]
    [TestCase("ada@SPAM.example", true, TestName = "A blocked domain in another case")]
    [TestCase("ada@mail.spam.example", true, TestName = "A domain under a blocked one")]
    [TestCase("ada@notspam.example", false, TestName = "A domain that only ends the same")]
    [TestCase("ada@spam.example.org", false, TestName = "A domain that only starts the same")]
    public async Task ABlockedDomain_InAnEmailField_PutsASubmissionIntoSpam(string email, bool blocked)
    {
        await SiteAsync(new ProtectionSettings { BlockedDomains = ["spam.example"] });
        var key = await PublishAsync(fields: [Email]);

        var entry = await SentAsync(key, new JsonObject { ["email"] = email });

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(blocked ? EntryState.Spam : EntryState.Submitted));
            Assert.That(entry.SpamScore, Is.EqualTo(blocked ? 100 : 0));
            Assert.That(entry.SpamReasons, Is.EqualTo(blocked ? "blocked_domain" : string.Empty));
        });
    }

    [Test]
    public async Task ABlockedDomain_InAnyOtherField_IsNoSignal()
    {
        await SiteAsync(new ProtectionSettings { BlockedDomains = ["spam.example"] });
        var key = await PublishAsync(fields: [Message]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Write to ada@spam.example" });

        Assert.That(entry.SpamScore, Is.Zero);
    }

    [TestCase("See https://a.example and http://b.example", 0, TestName = "As many links as allowed")]
    [TestCase("See https://a.example, http://b.example and www.c.example", 40, TestName = "One more than allowed")]
    [TestCase("HTTPS://A.EXAMPLE HTTP://B.EXAMPLE FTP://C.EXAMPLE", 40, TestName = "Links in capitals, and every scheme")]
    [TestCase("https://www.a.example https://www.b.example", 0, TestName = "A link with www counts once")]
    [TestCase("See a.example, b.example and c.example", 0, TestName = "A bare domain is no link")]
    public async Task MoreLinksThanTheSiteAllows_Score(string message, int score)
    {
        await SiteAsync(new ProtectionSettings { MaxLinks = 2 });
        var key = await PublishAsync(fields: [Message]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = message });

        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.EqualTo(score));
            Assert.That(entry.SpamReasons, Is.EqualTo(score == 0 ? string.Empty : "too_many_links"));
        });
    }

    [TestCase("Mail ada@www.example", TestName = "An address")]
    [TestCase("See wwww.example", TestName = "A word that ends the same")]
    [TestCase("See a.www.example", TestName = "A host under www")]
    [TestCase("See x-www.example", TestName = "A name with www at its end")]
    [TestCase("See http:/x", TestName = "A scheme cut short")]
    public async Task WhatOnlyLooksLikeALink_IsNone(string message)
    {
        await SiteAsync(new ProtectionSettings { MaxLinks = 0 });
        var key = await PublishAsync(fields: [Message]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = message });

        Assert.That(entry.SpamScore, Is.Zero);
    }

    [Test]
    public async Task TheLinksOfSeveralAnswers_AddUp()
    {
        await SiteAsync(new ProtectionSettings { MaxLinks = 1 });
        var key = await PublishAsync(fields: [Message, Field("other", "text")]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "https://a.example", ["other"] = "https://b.example" });

        Assert.That(entry.SpamReasons, Is.EqualTo("too_many_links"));
    }

    [Test]
    public async Task NoSiteSettings_AllowThreeLinks()
    {
        var key = await PublishAsync(fields: [Message]);

        var three = await SentAsync(key, new JsonObject { ["message"] = "http://a.example http://b.example http://c.example" });
        var four = await SentAsync(key, new JsonObject { ["message"] = "http://a.example http://b.example http://c.example http://d.example" });

        Assert.Multiple(() =>
        {
            Assert.That(three.SpamScore, Is.Zero);
            Assert.That(four.SpamReasons, Is.EqualTo("too_many_links"));
        });
    }

    [Test]
    public async Task AWebAddressField_HoldsNoLinkToCount_UntilStrictRulesAllowNone()
    {
        await SiteAsync(new ProtectionSettings { MaxLinks = 0 });
        var normal = await PublishAsync("Normal", fields: [Website]);
        var strict = await PublishAsync("Strict", change: Level(ContentRuleLevel.Strict), fields: [Website]);
        var someAllowed = await PublishAsync("Some", change: Level(ContentRuleLevel.Strict), fields: [Website, Message]);

        var inNormal = await SentAsync(normal, new JsonObject { ["website"] = "https://a.example" });
        var inStrict = await SentAsync(strict, new JsonObject { ["website"] = "https://a.example" });
        await SiteAsync(new ProtectionSettings { MaxLinks = 1 });
        var whereOneIsAllowed = await SentAsync(someAllowed, new JsonObject { ["website"] = "https://a.example", ["message"] = "https://b.example" });

        Assert.Multiple(() =>
        {
            Assert.That(inNormal.SpamScore, Is.Zero);
            Assert.That(inStrict.SpamScore, Is.EqualTo(80));
            Assert.That(inStrict.SpamReasons, Is.EqualTo("too_many_links"));
            Assert.That(whereOneIsAllowed.SpamScore, Is.Zero);
        });
    }

    [TestCase(ContentRuleLevel.Inherit, ContentRuleLevel.Normal, 140, TestName = "A form that inherits normal rules")]
    [TestCase(ContentRuleLevel.Inherit, ContentRuleLevel.Strict, 280, TestName = "A form that inherits strict rules")]
    [TestCase(ContentRuleLevel.Inherit, ContentRuleLevel.Off, 0, TestName = "A form that inherits no rules")]
    [TestCase(ContentRuleLevel.Normal, ContentRuleLevel.Strict, 140, TestName = "A normal form on a strict site")]
    [TestCase(ContentRuleLevel.Strict, ContentRuleLevel.Off, 280, TestName = "A strict form on a site without rules")]
    [TestCase(ContentRuleLevel.Off, ContentRuleLevel.Strict, 0, TestName = "A form without rules on a strict site")]
    public async Task AFormsStrictness_WeighsTheRules(ContentRuleLevel form, ContentRuleLevel site, int score)
    {
        await SiteAsync(new ProtectionSettings { ContentRules = site, BlockedWords = ["casino"], BlockedDomains = ["spam.example"], MaxLinks = 9 });
        var key = await PublishAsync(change: Level(form), fields: [Message, Email]);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "A casino", ["email"] = "ada@spam.example" });

        Assert.That(entry.SpamScore, Is.EqualTo(score));
    }

    [Test]
    public async Task ACheck_AddsItsScoreAndReason_AndSeesTheSubmission()
    {
        var key = await PublishAsync(fields: [Message, Guests]);
        _check.Answer = new FormBuilderSpamSignal(30, "known_sender");

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello", ["guests"] = new JsonArray(new JsonObject { ["guest"] = "Ada" }, new JsonObject { ["guest"] = "Bob" }) });

        var seen = _check.Seen.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.EqualTo(30));
            Assert.That(entry.SpamReasons, Is.EqualTo("known_sender"));
            Assert.That(entry.State, Is.EqualTo(EntryState.Submitted));
            Assert.That(seen.Form, Is.EqualTo(key));
            Assert.That(seen.Culture, Is.EqualTo("en-US"));
            Assert.That(seen.IsFromServerKey, Is.True);
            Assert.That(seen.Answers["message"], Is.EqualTo(new[] { "Hello" }));
            Assert.That(seen.Answers["guest"], Is.EqualTo(new[] { "Ada", "Bob" }), "the rows follow one another");
        });
    }

    [Test]
    public async Task ACheck_CanPutASubmissionIntoSpam()
    {
        var key = await PublishAsync(fields: [Message]);
        _check.Answer = new FormBuilderSpamSignal(100, "known_sender");

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello" });

        Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
    }

    [TestCase("Not A Reason!", "spam_check", TestName = "A reason it cannot store")]
    [TestCase("", "spam_check", TestName = "No reason")]
    [TestCase("with,comma", "spam_check", TestName = "A reason that would read as two")]
    [TestCase("ok_reason-1", "ok_reason-1", TestName = "A reason it can store")]
    public async Task ACheck_Reason_IsStoredAsItCanBe(string given, string stored)
    {
        var key = await PublishAsync(fields: [Message]);
        _check.Answer = new FormBuilderSpamSignal(1, given);

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello" });

        Assert.That(entry.SpamReasons, Is.EqualTo(stored));
    }

    [TestCase(-5, 0, TestName = "A score below nothing")]
    [TestCase(int.MaxValue, 1000, TestName = "A score past the most a check may add")]
    public async Task ACheck_Score_IsHeldToItsLimits(int given, int stored)
    {
        var key = await PublishAsync(fields: [Message]);
        _check.Answer = new FormBuilderSpamSignal(given, "known_sender");

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello" });

        Assert.That(entry.SpamScore, Is.EqualTo(stored));
    }

    [Test]
    public async Task ACheckThatFails_CountsForNothing_AndLosesNoEntry()
    {
        var key = await PublishAsync(fields: [Message]);
        _check.Fails = true;

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello" });

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Submitted));
            Assert.That(entry.SpamScore, Is.Zero);
        });
    }

    [Test]
    public async Task ACheckThatStalls_IsGivenUpOn_AndLosesNoEntry()
    {
        var key = await PublishAsync(fields: [Message]);
        _check.Stalls = true;
        var started = DateTimeOffset.UtcNow;

        var entry = await SentAsync(key, new JsonObject { ["message"] = "Hello" });

        Assert.Multiple(() =>
        {
            Assert.That(entry.SpamScore, Is.Zero);
            Assert.That(DateTimeOffset.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(20)), "the check's limit, not the request's");
        });
    }

    [Test]
    public async Task ACheck_IsNotAsked_OnceTheScoreHasDecided()
    {
        await SiteAsync(new ProtectionSettings { BlockedDomains = ["spam.example"] });
        var key = await PublishAsync(fields: [Email]);

        var entry = await SentAsync(key, new JsonObject { ["email"] = "ada@spam.example" });

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(_check.Seen, Is.Empty);
        });
    }

    [Test]
    public async Task APagesCheck_AsksNoCheck()
    {
        var key = await PublishAsync(fields: [Message]);
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;

        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/validate", request => request.Content = new StringContent($$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"message":"Hello"}}""", Encoding.UTF8, "application/json"));

        Assert.That(_check.Seen, Is.Empty);
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _check = new TestCheck();
        builder.Services.AddSingleton(_check);
        builder.JironFormBuilder().SpamChecks().Append<ForwardingCheck>();
    }

    private static Func<FormDefinition, FormDefinition> Level(ContentRuleLevel level) => definition => definition with
    {
        Settings = definition.Settings with { Protection = definition.Settings.Protection with { ContentRules = level } },
    };

    private static FormField Field(string alias, string type) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
    };

    private async Task SiteAsync(ProtectionSettings wanted)
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>();
        await settings.SaveAsync(wanted, current.Version, null);
    }

    // Sends the answers and gives back the entry they made.
    private async Task<StoredEntry> SentAsync(Guid key, JsonObject values)
    {
        var submissionId = Guid.NewGuid();
        var body = new JsonObject { ["submissionId"] = submissionId.ToString(), ["revision"] = 1, ["values"] = values };
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.That(answer.IsSuccessStatusCode, Is.True, await answer.Content.ReadAsStringAsync());
        return (await Entries.GetBySubmissionAsync((await FormRows.GetAsync(key))!.Id, submissionId))!;
    }

    // What a test tells the site's check to do.
    private sealed class TestCheck
    {
        public FormBuilderSpamSignal? Answer { get; set; }

        public bool Fails { get; set; }

        public bool Stalls { get; set; }

        public List<FormBuilderSpamCheckContext> Seen { get; } = [];

        public void Reset()
        {
            Answer = null;
            Fails = false;
            Stalls = false;
            Seen.Clear();
        }
    }

    // A check as a site registers it, which the collection creates through the container.
    private sealed class ForwardingCheck(TestCheck test) : IFormBuilderSpamCheck
    {
        public async Task<FormBuilderSpamSignal?> CheckAsync(FormBuilderSpamCheckContext context, CancellationToken cancellationToken)
        {
            test.Seen.Add(context);
            if (test.Fails)
            {
                throw new InvalidOperationException("JFB_FAKE a check that fails");
            }

            if (test.Stalls)
            {
                // A check that does not even listen to its token.
                await Task.Delay(TimeSpan.FromMinutes(5), CancellationToken.None);
            }

            return test.Answer;
        }
    }
}
