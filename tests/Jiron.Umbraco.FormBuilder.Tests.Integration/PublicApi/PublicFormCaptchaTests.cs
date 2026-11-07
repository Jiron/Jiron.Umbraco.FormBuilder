using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A form at the captcha level (ROADMAP 7.2b, ADR-0012, ADR-0142), by its own choice and through its site's, against the
/// stub's verification: a good token makes its entry; a bad or a missing one is refused and stores nothing; a provider that
/// cannot answer sends the entry to Spam; a server key and a retry spend no token; and a challenge tells a page the public
/// key and never the secret.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormCaptchaTests : PublicApiTestBase
{
    private const string Hcaptcha = HcaptchaProvider.ChosenAs;
    private const string Scored = RecaptchaV3Provider.ChosenAs;
    private const string SiteKey = "JFB_FAKE_captcha_site_key";
    private const string SecretKey = "JFB_FAKE_captcha_secret_key";
    private const string OutageToken = "JFB_FAKE-captcha-outage";

    private readonly StoppedClock _clock = new();
    private readonly StubTransport _transport;

    /// <summary>A fixture whose clock the stub's answers and the site share.</summary>
    public PublicFormCaptchaTests() => _transport = new StubTransport(_clock);

    private FormBuilderOptions Options => GetRequiredService<IOptions<FormBuilderOptions>>().Value;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    [SetUp]
    public void ForgetTheCalls()
    {
        _transport.Forget();
        SendsWithServerKey = false;
        Options.PublicApi.AllowedOrigins.Clear();
    }

    [Test]
    public async Task AGoodToken_MakesItsEntry_AfterOneCall()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));

        using var answer = await SendAsync(key, StubCaptcha.Pass);

        var entry = (await StoredAsync(key)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That(entry.State, Is.Not.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamReasons ?? string.Empty, Does.Not.Contain("captcha_unavailable"));
            Assert.That(_transport.Calls, Is.EqualTo(1));
        });
    }

    [TestCase(StubCaptcha.Fail, TestName = "An invalid token")]
    [TestCase(StubCaptcha.Expired, TestName = "A used token")]
    [TestCase(null, TestName = "No token")]
    [TestCase(" ", TestName = "A blank token")]
    public async Task ATokenThatDoesNotPass_IsRefused_AndStoresNothing(string? token)
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));

        using var answer = await SendAsync(key, token);

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(answer), Is.EqualTo("403 captcha_failed"));
            Assert.That(await StoredAsync(key), Is.Empty);
        });
    }

    [TestCase(null, TestName = "No token")]
    [TestCase(" ", TestName = "A blank token")]
    public async Task WithoutAToken_NobodyIsCalled(string? token)
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));

        using var answer = await SendAsync(key, token);

        Assert.That(_transport.Calls, Is.Zero);
    }

    [Test]
    public async Task ATokenMadeForAnotherHost_IsRefused_OnceTheSiteHasHosts()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));
        Options.PublicApi.AllowedOrigins.Add("https://shop.example");

        using var elsewhere = await SendAsync(key, StubCaptcha.Pass);
        Options.PublicApi.AllowedOrigins.Add("http://localhost:5500");
        using var here = await SendAsync(key, StubCaptcha.Pass);

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(elsewhere), Is.EqualTo("403 captcha_failed"));
            Assert.That(here.StatusCode, Is.EqualTo(HttpStatusCode.OK), await here.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AFormsThreshold_ReachesItsProvider()
    {
        var key = await PublishAsync(change: Choosing(Scored, SettingsObject.Parse("""{"threshold":"0.9"}""")));

        using var low = await SendAsync(key, StubCaptcha.ScorePrefix + "0.8");
        using var high = await SendAsync(key, StubCaptcha.ScorePrefix + "0.9");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(low), Is.EqualTo("403 captcha_failed"));
            Assert.That(high.StatusCode, Is.EqualTo(HttpStatusCode.OK), await high.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AProviderThatCannotAnswer_SendsTheEntryToSpam_AndLosesNoLead()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));
        var challenge = await ChallengeAsync(key);
        _clock.Advance(TimeSpan.FromSeconds(3));

        using var answer = await SendAsync(key, OutageToken, new JsonObject { ["challenge"] = challenge });

        var entry = (await StoredAsync(key)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
            Assert.That(entry.SpamScore, Is.EqualTo(100));
            Assert.That(entry.SpamReasons, Is.EqualTo("captcha_unavailable"));
        });
    }

    [Test]
    public async Task AProviderOfTheSitesOwnThatThrows_IsAnOutage()
    {
        var key = await PublishAsync(change: Choosing(ThrowingCaptcha.Chosen));

        using var answer = await SendAsync(key, "token");

        Assert.That((await StoredAsync(key)).Single().State, Is.EqualTo(EntryState.Spam));
    }

    [TestCase(null, TestName = "No token")]
    [TestCase("token", TestName = "A token from before")]
    public async Task AProviderThatLostItsKeys_ShowsNoCaptcha_AndItsEntriesGoToSpam_WithoutAsking(string? token)
    {
        var key = await PublishAsync(change: Choosing(RecaptchaV2Provider.ChosenAs));

        var (_, captcha) = await AskAsync(key);
        using var answer = await SendAsync(key, token);

        Assert.Multiple(async () =>
        {
            Assert.That(captcha, Is.Null);
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That((await StoredAsync(key)).Single().SpamReasons!.Split(','), Does.Contain("captcha_unavailable"));
            Assert.That(_transport.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task TheChallenge_TellsAPageThePublicKey_AndNoSecret()
    {
        var key = await PublishAsync(change: Choosing(Scored));

        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json("{}"));

        var text = await answer.Content.ReadAsStringAsync();
        var captcha = JsonNode.Parse(text)!["captcha"]!;
        Assert.Multiple(() =>
        {
            Assert.That(captcha["provider"]!.GetValue<string>(), Is.EqualTo(Scored));
            Assert.That(captcha["siteKey"]!.GetValue<string>(), Is.EqualTo(SiteKey));
            Assert.That(captcha["scriptAddress"]!.GetValue<string>(), Is.EqualTo($"https://www.google.com/recaptcha/api.js?render={SiteKey}"));
            Assert.That(captcha["action"]!.GetValue<string>(), Is.EqualTo("submit"));
            Assert.That(captcha["consent"]!.GetValue<string>(), Is.EqualTo("explicit"), "unless a site chose another");
            Assert.That(captcha["category"], Is.Null, "no category to ask about");
            Assert.That(text, Does.Not.Contain(SecretKey));
            Assert.That(JsonNode.Parse(text)!["proofOfWork"], Is.Null, "the level has a captcha, not a puzzle");
        });
    }

    [Test]
    public async Task AFormWithoutACaptcha_TellsOfNone_AndAsksForNoToken()
    {
        var key = await PublishAsync();

        var (_, captcha) = await AskAsync(key);
        using var answer = await SendAsync(key, null);

        Assert.Multiple(async () =>
        {
            Assert.That(captcha, Is.Null);
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That(_transport.Calls, Is.Zero);
        });
    }

    [TestCase(nameof(CaptchaConsentMode.Cmp), "cmp", null)]
    [TestCase(nameof(CaptchaConsentMode.Cookiebot), "cookiebot", "marketing")]
    public async Task TheChallenge_TellsThePageTheSitesConsentMode(string mode, string told, string? category)
    {
        var current = await Settings.GetAsync<ProtectionSettings>();
        _ = await Settings.SaveAsync(new ProtectionSettings { CaptchaConsent = Enum.Parse<CaptchaConsentMode>(mode), CookiebotCategory = "marketing" }, current.Version, null);

        var key = await PublishAsync(change: Choosing(Scored));
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json("{}"));
        var captcha = JsonNode.Parse(await answer.Content.ReadAsStringAsync())!["captcha"]!;

        Assert.Multiple(() =>
        {
            Assert.That(captcha["consent"]!.GetValue<string>(), Is.EqualTo(told));
            Assert.That(captcha["category"]?.GetValue<string>(), Is.EqualTo(category));
        });
    }

    [Test]
    public async Task AStrictSitesCaptcha_HoldsForAPostThatNamesNoPageOfTheForm()
    {
        await Settings.SaveSiteAsync(Guid.NewGuid(), new ProtectionSettings { Level = ProtectionLevel.Captcha, CaptchaProvider = Hcaptcha }, ["level", "captchaProvider"], 0, null);
        var key = await PublishAsync();

        var (_, shown) = await AskAsync(key);
        using var missing = await SendAsync(key, null);
        using var good = await SendAsync(key, StubCaptcha.Pass);

        Assert.Multiple(async () =>
        {
            Assert.That(shown!["provider"]!.GetValue<string>(), Is.EqualTo(Hcaptcha));
            Assert.That(await RefusalAsync(missing), Is.EqualTo("403 captcha_failed"));
            Assert.That(good.StatusCode, Is.EqualTo(HttpStatusCode.OK), await good.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AFormThatInherits_TakesItsSitesCaptcha_AndAFormAtTheStandardLevelDoesNot()
    {
        await SiteAsync(new ProtectionSettings { Level = ProtectionLevel.Captcha, CaptchaProvider = Hcaptcha });
        var inherits = await PublishAsync();
        var standard = await PublishAsync(change: definition => definition with { Settings = definition.Settings with { Protection = definition.Settings.Protection with { Level = ProtectionLevel.Standard } } });

        var (_, shown) = await AskAsync(inherits);
        using var missing = await SendAsync(inherits, null);
        using var good = await SendAsync(inherits, StubCaptcha.Pass);
        var (_, none) = await AskAsync(standard);
        using var plain = await SendAsync(standard, null);

        Assert.Multiple(async () =>
        {
            Assert.That(shown?["provider"]?.GetValue<string>(), Is.EqualTo(Hcaptcha));
            Assert.That(await RefusalAsync(missing), Is.EqualTo("403 captcha_failed"));
            Assert.That(good.StatusCode, Is.EqualTo(HttpStatusCode.OK), await good.Content.ReadAsStringAsync());
            Assert.That(none, Is.Null);
            Assert.That(plain.StatusCode, Is.EqualTo(HttpStatusCode.OK), await plain.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task AnswersThatCannotBeStored_SpendNoToken()
    {
        var other = NameField with { Key = Guid.Parse("30000000-0000-4000-8000-000000000085"), Alias = "other" };
        var key = await PublishAsync(change: Choosing(Hcaptcha), fields: [NameField with { Required = true }, other]);

        using var answer = await SendAsync(key, StubCaptcha.Pass, values: new JsonObject { ["other"] = "Ada" });

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That((await JsonAsync(answer))["errors"]!.AsArray(), Has.Count.EqualTo(1), "the required answer, not an empty submission");
            Assert.That(_transport.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task ARetryOfASubmission_SpendsNoSecondToken_AndHearsTheFirstAnswer()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));
        var challenge = await ChallengeAsync(key);
        var same = new JsonObject { ["submissionId"] = Guid.NewGuid().ToString(), ["challenge"] = challenge };

        using var first = await SendAsync(key, StubCaptcha.Pass, same);
        using var retry = await SendAsync(key, StubCaptcha.Pass, same);

        Assert.Multiple(async () =>
        {
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()));
            Assert.That(_transport.Calls, Is.EqualTo(1));
            Assert.That(await StoredAsync(key), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AServerKey_SkipsTheCaptcha()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));
        SendsWithServerKey = true;

        using var answer = await SendRawAsync(key, "entries", """{"revision":1,"values":{"name":"Ada"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That((await StoredAsync(key)).Single().State, Is.Not.EqualTo(EntryState.Spam));
            Assert.That(_transport.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task APreviewsTicket_SkipsTheCaptcha_ForItsTestEntry()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));
        var ticket = GetRequiredService<PreviewTickets>().Issue(key, (await Forms.GetDraftAsync(key))!.DraftVersion).Value;
        void FromFrame(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("Origin", "null");
            request.Headers.TryAddWithoutValidation("Authorization", $"{PreviewTickets.Scheme} {ticket}");
        }

        using var challenged = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request =>
        {
            FromFrame(request);
            request.Content = Json("{}");
        });
        var challenge = (await JsonAsync(challenged))["challenge"]!.GetValue<string>();
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
        {
            FromFrame(request);
            request.Content = Json($$"""{"revision":1,"values":{"name":"Ada"},"challenge":"{{challenge}}"}""");
        });

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
            Assert.That((await StoredAsync(key)).Single() is { IsTest: true, State: not EntryState.Spam }, Is.True, "a test entry, never in Spam");
            Assert.That(_transport.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task APagesCheck_AsksForNoToken()
    {
        var key = await PublishAsync(change: Choosing(Hcaptcha));
        var page = (await Forms.GetPublishedAsync(key))!.Definition.Pages[0].Key;

        using var check = await SendRawAsync(key, "validate", $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(check.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await check.Content.ReadAsStringAsync());
            Assert.That(_transport.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task AProviderTheSiteLacks_DoesNotPublish()
    {
        var created = await Forms.CreateAsync(new NewFormRequest("Chosen", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(created.Key))!;
        var saved = (await Forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, Choosing("acme")(WithFields(draft.Definition!, NameField)), null, Editor))!;

        FormServiceTesting.AssertCode(async () => await Forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor), DefinitionErrors.UnknownType, "$.settings.protection.captcha.provider");
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.Configure<FormBuilderOptions>(options =>
        {
            foreach (var keys in new[] { options.Captcha.Hcaptcha, options.Captcha.RecaptchaV3 })
            {
                keys.SiteKey = SiteKey;
                keys.SecretKey = SecretKey;
            }
        });
        builder.Services.AddUnique<IFormBuilderHttpTransport>(_transport);
        builder.Services.AddUnique<TimeProvider>(_clock);
        builder.JironFormBuilder().CaptchaProviders().Append<ThrowingCaptcha>();
    }

    private static Func<FormDefinition, FormDefinition> Choosing(string provider, SettingsObject? settings = null) => definition => definition with
    {
        Settings = definition.Settings with
        {
            Protection = definition.Settings.Protection with
            {
                Level = ProtectionLevel.Captcha,
                Captcha = new CaptchaChoice { Provider = provider, Settings = settings },
            },
        },
    };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private Task<HttpResponseMessage> SendAsync(Guid key, string? token, JsonObject? members = null, JsonObject? values = null)
    {
        var posted = new JsonObject { ["revision"] = 1, ["values"] = values ?? new JsonObject { ["name"] = "Ada" } };
        if (token is not null)
        {
            posted["captchaToken"] = token;
        }

        foreach (var (name, value) in members ?? [])
        {
            posted[name] = value?.DeepClone();
        }

        return SendRawAsync(key, "entries", posted.ToJsonString());
    }

    private Task<HttpResponseMessage> SendRawAsync(Guid key, string endpoint, string body) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/{endpoint}", request => request.Content = Json(body));

    private async Task<(string Challenge, JsonNode? Captcha)> AskAsync(Guid key)
    {
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", request => request.Content = Json("{}"));
        var read = await JsonAsync(answer);
        return (read["challenge"]!.GetValue<string>(), read["captcha"]);
    }

    private async Task SiteAsync(ProtectionSettings settings)
    {
        var current = await Settings.GetAsync<ProtectionSettings>();
        await Settings.SaveAsync(settings, current.Version, null);
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

    // The stub's verification in place of the calls to other hosts; the outage's token fails as a call does.
    private sealed class StubTransport(TimeProvider time) : IFormBuilderHttpTransport
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Forget() => Interlocked.Exchange(ref _calls, 0);

        public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (body.Contains(OutageToken, StringComparison.Ordinal))
            {
                throw new HttpRequestException("The provider cannot be reached.");
            }

            var answer = StubCaptcha.Verify(body, request.Content.Headers.ContentType?.ToString(), time.GetUtcNow());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    // A site's own provider that breaks its promise not to throw.
    private sealed class ThrowingCaptcha : IFormBuilderCaptchaProvider
    {
        public const string Chosen = "acme.throwing";

        public string Name => Chosen;

        public bool IsConfigured => true;

        public FormBuilderCaptchaClient? Client => null;

        public Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken) => throw new InvalidOperationException("Broken.");
    }
}
