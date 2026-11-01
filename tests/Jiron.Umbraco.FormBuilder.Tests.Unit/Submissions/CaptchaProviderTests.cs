using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Submissions;

/// <summary>
/// The built-in captcha providers (ROADMAP 7.2b, ADR-0012, ADR-0142) against the stub's verification: what goes out, what
/// an answer has to hold to pass, and that an outage and the site's own wrong keys are never the visitor's failure.
/// </summary>
public sealed class CaptchaProviderTests
{
    private const string V2 = RecaptchaV2Provider.ChosenAs;
    private const string V3 = RecaptchaV3Provider.ChosenAs;
    private const string H = HcaptchaProvider.ChosenAs;
    private const string SiteKey = "JFB_FAKE_captcha_site_key";
    private const string SecretKey = "JFB_FAKE_captcha_secret_key";

    private static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlySet<string> Localhost = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost" };

    [TestCase(V2, StubCaptcha.Pass)]
    [TestCase(V3, StubCaptcha.ScorePrefix + "0.9")]
    [TestCase(H, StubCaptcha.Pass)]
    public async Task AProvider_PassesAGoodToken_AndFailsAnInvalidAndAUsedOne(string name, string good)
    {
        var provider = Provider(name, StubTransport());

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.VerifyAsync(Check(good), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Passed));
            Assert.That(await provider.VerifyAsync(Check(StubCaptcha.Fail), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Failed));
            Assert.That(await provider.VerifyAsync(Check(StubCaptcha.Expired), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Failed));
        });
    }

    [TestCase("0.5", null, FormBuilderCaptchaOutcome.Passed, TestName = "The default score")]
    [TestCase("0.49", null, FormBuilderCaptchaOutcome.Failed, TestName = "Below the default score")]
    [TestCase("0.9", """{"threshold":"0.9"}""", FormBuilderCaptchaOutcome.Passed, TestName = "The form's threshold")]
    [TestCase("0.89", """{"threshold":"0.9"}""", FormBuilderCaptchaOutcome.Failed, TestName = "Below the form's threshold")]
    [TestCase("0.1", """{"threshold":"0"}""", FormBuilderCaptchaOutcome.Passed, TestName = "The lowest threshold")]
    [TestCase("0.9", """{"threshold":"1"}""", FormBuilderCaptchaOutcome.Failed, TestName = "Below the highest threshold")]
    [TestCase("1", """{"threshold":"1"}""", FormBuilderCaptchaOutcome.Passed, TestName = "The highest threshold")]
    [TestCase("0.4", """{"threshold":"1.5"}""", FormBuilderCaptchaOutcome.Failed, TestName = "A threshold above one is the default")]
    [TestCase("0.6", """{"threshold":"1.5"}""", FormBuilderCaptchaOutcome.Passed, TestName = "A threshold above one lets the default's score pass")]
    [TestCase("0.4", """{"threshold":"-0.1"}""", FormBuilderCaptchaOutcome.Failed, TestName = "A threshold below zero is the default")]
    [TestCase("0.6", """{"threshold":"-0.1"}""", FormBuilderCaptchaOutcome.Passed, TestName = "A threshold below zero lets the default's score pass")]
    [TestCase("0.4", """{"threshold":"high"}""", FormBuilderCaptchaOutcome.Failed, TestName = "A threshold that is no number is the default")]
    [TestCase("0.6", """{"threshold":1}""", FormBuilderCaptchaOutcome.Passed, TestName = "A threshold that is no text is the default")]
    [TestCase("0.4", """{"other":"0.1"}""", FormBuilderCaptchaOutcome.Failed, TestName = "Settings without a threshold are the default")]
    public async Task AScore_IsHeldToTheFormsThreshold_OrTheDefault(string score, string? settings, FormBuilderCaptchaOutcome outcome)
    {
        var chosen = settings is null ? null : SettingsObject.Parse(settings);

        var answer = await Provider(V3, StubTransport()).VerifyAsync(Check(StubCaptcha.ScorePrefix + score, Localhost, chosen), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(outcome));
    }

    [TestCase(V3, """ "action":"submit" """, FormBuilderCaptchaOutcome.Failed, TestName = "A scoring provider's answer without a score")]
    [TestCase(V3, """ "action":"submit","score":"0.9" """, FormBuilderCaptchaOutcome.Failed, TestName = "A score that is text")]
    [TestCase(V3, """ "score":0.9 """, FormBuilderCaptchaOutcome.Failed, TestName = "A scoring provider's answer without its action")]
    [TestCase(V3, """ "action":"login","score":0.9 """, FormBuilderCaptchaOutcome.Failed, TestName = "A token made for another action")]
    [TestCase(V3, """ "action":"submit","score":0.9 """, FormBuilderCaptchaOutcome.Passed, TestName = "A scoring provider's whole answer")]
    [TestCase(V2, """ "action":"login","score":0.1 """, FormBuilderCaptchaOutcome.Passed, TestName = "A checkbox ignores an action and a score")]
    [TestCase(H, """ "action":"login","score":0.1 """, FormBuilderCaptchaOutcome.Passed, TestName = "hCaptcha ignores an action and a score")]
    public async Task AnAnswersActionAndScore_AreReadAsItsProviderMeansThem(string name, string members, FormBuilderCaptchaOutcome outcome)
    {
        var answer = await Provider(name, Answering($$"""{"success":true,"challenge_ts":"{{Stamp(Now)}}","hostname":"localhost",{{members}}}""")).VerifyAsync(Check("token"), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(outcome));
    }

    [TestCase("localhost", FormBuilderCaptchaOutcome.Passed, TestName = "One of the site's hosts")]
    [TestCase("LOCALHOST", FormBuilderCaptchaOutcome.Passed, TestName = "A host in other letters")]
    [TestCase("other.example", FormBuilderCaptchaOutcome.Failed, TestName = "Another site's host")]
    public async Task ATokensHost_IsHeldToTheSites(string host, FormBuilderCaptchaOutcome outcome)
    {
        var answer = await Provider(V2, Answering($$"""{"success":true,"challenge_ts":"{{Stamp(Now)}}","hostname":"{{host}}"}""")).VerifyAsync(Check("token"), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(outcome));
    }

    [Test]
    public async Task AnAnswerWithoutAHost_FailsOnASiteWithHosts_AndPassesOnOneWithNoneToCompare()
    {
        var provider = Provider(V2, Answering($$"""{"success":true,"challenge_ts":"{{Stamp(Now)}}"}"""));

        Assert.Multiple(async () =>
        {
            Assert.That(await provider.VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Failed));
            Assert.That(await provider.VerifyAsync(Check("token", new HashSet<string>()), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Passed));
        });
    }

    [TestCase(V2, 330, FormBuilderCaptchaOutcome.Passed, TestName = "A checkbox's token at five minutes and the skew")]
    [TestCase(V2, 331, FormBuilderCaptchaOutcome.Failed, TestName = "A checkbox's token past them")]
    [TestCase(H, 330, FormBuilderCaptchaOutcome.Passed, TestName = "An hCaptcha token at five minutes and the skew")]
    [TestCase(H, 331, FormBuilderCaptchaOutcome.Failed, TestName = "An hCaptcha token past them")]
    [TestCase(V3, 150, FormBuilderCaptchaOutcome.Passed, TestName = "A scored token at two minutes and the skew")]
    [TestCase(V3, 151, FormBuilderCaptchaOutcome.Failed, TestName = "A scored token past them")]
    [TestCase(V2, -30, FormBuilderCaptchaOutcome.Passed, TestName = "A token from the skew ahead")]
    [TestCase(V2, -31, FormBuilderCaptchaOutcome.Failed, TestName = "A token from past the skew ahead")]
    public async Task ATokensAge_IsHeldToItsProvidersLimit(string name, int seconds, FormBuilderCaptchaOutcome outcome)
    {
        var solved = Stamp(Now - TimeSpan.FromSeconds(seconds));

        var answer = await Provider(name, Answering($$"""{"success":true,"challenge_ts":"{{solved}}","hostname":"localhost","action":"submit","score":0.9}""")).VerifyAsync(Check("token"), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(outcome));
    }

    [TestCase("""{"success":true,"hostname":"localhost"}""", TestName = "No time")]
    [TestCase("""{"success":true,"hostname":"localhost","challenge_ts":"yesterday"}""", TestName = "A time that is none")]
    [TestCase("""{"success":true,"hostname":"localhost","challenge_ts":1}""", TestName = "A time that is a number")]
    public async Task AToken_WithoutATimeItWasSolvedAt_Fails(string answer) =>
        Assert.That(await Provider(V2, Answering(answer)).VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Failed));

    [Test]
    public async Task OnlyTheSecretAndTheToken_GoOut()
    {
        string? body = null;
        string? type = null;
        var transport = new ScriptedTransport(async (request, token) =>
        {
            body = await request.Content!.ReadAsStringAsync(token);
            type = request.Content.Headers.ContentType?.MediaType;
            return ScriptedTransport.Json("""{"success":false}""");
        });

        _ = await Provider(H, transport).VerifyAsync(Check("the token&more"), CancellationToken.None);

        var sent = transport.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(sent.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(sent.Accept, Is.EqualTo("application/json"));
            Assert.That(type, Is.EqualTo("application/x-www-form-urlencoded"));
            Assert.That(body, Is.EqualTo($"secret={SecretKey}&response=the+token%26more"), "no address of the visitor");
        });
    }

    [TestCase(V2, false, "https://www.google.com/recaptcha/api/siteverify", "https://www.google.com/recaptcha/api.js", null)]
    [TestCase(V2, true, "https://www.recaptcha.net/recaptcha/api/siteverify", "https://www.recaptcha.net/recaptcha/api.js", null)]
    [TestCase(V3, false, "https://www.google.com/recaptcha/api/siteverify", "https://www.google.com/recaptcha/api.js?render=JFB_FAKE_captcha_site_key", "submit")]
    [TestCase(V3, true, "https://www.recaptcha.net/recaptcha/api/siteverify", "https://www.recaptcha.net/recaptcha/api.js?render=JFB_FAKE_captcha_site_key", "submit")]
    [TestCase(H, false, "https://api.hcaptcha.com/siteverify", "https://js.hcaptcha.com/1/api.js", null)]
    [TestCase(H, true, "https://api.hcaptcha.com/siteverify", "https://js.hcaptcha.com/1/api.js", null)]
    public async Task AProvider_IsReachedAtItsOwnAddresses(string name, bool recaptchaNet, string verify, string script, string? action)
    {
        var transport = Answering("""{"success":false}""");
        var provider = Provider(name, transport, change: options => options.Captcha.UseRecaptchaNet = recaptchaNet);

        _ = await provider.VerifyAsync(Check("token"), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(transport.Requests.Single().Address.ToString(), Is.EqualTo(verify));
            Assert.That(provider.Name, Is.EqualTo(name));
            Assert.That(provider.Client, Is.EqualTo(new FormBuilderCaptchaClient(SiteKey, script, action)), "the public key only");
        });
    }

    [Test]
    public async Task ATestHostsAddress_IsWhereItVerifies()
    {
        var transport = Answering("""{"success":false}""");

        _ = await Provider(H, transport, change: options => options.Captcha.Hcaptcha.VerifyAddress = "http://localhost:5560/captcha/hcaptcha/siteverify").VerifyAsync(Check("token"), CancellationToken.None);

        Assert.That(transport.Requests.Single().Address.ToString(), Is.EqualTo("http://localhost:5560/captcha/hcaptcha/siteverify"));
    }

    [TestCase(V2)]
    [TestCase(V3)]
    [TestCase(H)]
    public async Task AProviderWithoutBothKeys_IsNotConfigured_ShowsNothing_AndCallsNobody(string name)
    {
        var transport = Answering("""{"success":true}""");
        var none = Provider(name, transport, keyed: false);
        var noSecret = Provider(name, transport, change: options =>
        {
            KeysOf(options, name).SecretKey = " ".Trim();
        });
        var noSiteKey = Provider(name, transport, change: options =>
        {
            KeysOf(options, name).SiteKey = null;
        });

        foreach (var provider in new[] { none, noSecret, noSiteKey })
        {
            Assert.Multiple(async () =>
            {
                Assert.That(provider.IsConfigured, Is.False);
                Assert.That(provider.Client, Is.Null);
                Assert.That(await provider.VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable));
            });
        }

        Assert.That(transport.Requests, Is.Empty);
    }

    [Test]
    public void AProvidersKeys_AreItsOwn()
    {
        var options = new FormBuilderOptions();
        options.Captcha.RecaptchaV2.SiteKey = "v2";
        options.Captcha.RecaptchaV3.SiteKey = "v3";
        options.Captcha.Hcaptcha.SiteKey = "h";
        foreach (var keys in new[] { options.Captcha.RecaptchaV2, options.Captcha.RecaptchaV3, options.Captcha.Hcaptcha })
        {
            keys.SecretKey = SecretKey;
        }

        var transport = Answering("""{"success":false}""");
        Assert.Multiple(() =>
        {
            Assert.That(new RecaptchaV2Provider(Options.Create(options), transport, TimeProvider.System).Client!.SiteKey, Is.EqualTo("v2"));
            Assert.That(new RecaptchaV3Provider(Options.Create(options), transport, TimeProvider.System).Client!.SiteKey, Is.EqualTo("v3"));
            Assert.That(new HcaptchaProvider(Options.Create(options), transport, TimeProvider.System).Client!.SiteKey, Is.EqualTo("h"));
        });
    }

    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.NotFound)]
    public async Task AStatusThatIsNoSuccess_IsAnOutage(HttpStatusCode status)
    {
        var answer = await Provider(V2, new ScriptedTransport(_ => ScriptedTransport.Json("""{"success":true}""", status))).VerifyAsync(Check("token"), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable));
    }

    [TestCase("this is no JSON", TestName = "Text")]
    [TestCase("[]", TestName = "An array")]
    [TestCase("""{"hostname":"localhost"}""", TestName = "No success")]
    [TestCase("""{"success":"yes"}""", TestName = "A success that is text")]
    [TestCase("""{"success":false,"error-codes":[1]}""", TestName = "A code that is a number")]
    public async Task AnAnswerItCannotRead_IsAnOutage(string body) =>
        Assert.That(await Provider(V2, Answering(body)).VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable));

    [Test]
    public async Task AnAnswerOfTheCap_IsRead_AndOnePastIt_IsAnOutage()
    {
        var answer = Encoding.UTF8.GetBytes($$"""{"success":true,"challenge_ts":"{{Stamp(Now)}}","hostname":"localhost"}""");
        var full = new byte[65_536];
        Array.Fill(full, (byte)' ');
        answer.CopyTo(full, 0);
        var past = new byte[65_537];
        full.CopyTo(past, 0);
        past[^1] = (byte)' ';

        Assert.Multiple(async () =>
        {
            Assert.That(await Provider(V2, new ScriptedTransport(_ => ScriptedTransport.Undeclared(full))).VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Passed));
            Assert.That(await Provider(V2, new ScriptedTransport(_ => ScriptedTransport.Undeclared(past))).VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable));
        });
    }

    [TestCase("missing-input-secret")]
    [TestCase("invalid-input-secret")]
    [TestCase("invalid-keys")]
    [TestCase("sitekey-secret-mismatch")]
    [TestCase("not-using-dummy-secret")]
    public async Task ACodeThatBlamesTheSitesKeys_IsAnOutage_NotTheVisitorsFailure(string code)
    {
        var answer = await Provider(H, Answering($$"""{"success":false,"error-codes":["{{code}}"]}""")).VerifyAsync(Check("token"), CancellationToken.None);

        Assert.That(answer, Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable));
    }

    [TestCase("""{"success":false,"error-codes":["invalid-input-response"]}""", TestName = "The token's code")]
    [TestCase("""{"success":false,"error-codes":[]}""", TestName = "No code")]
    [TestCase("""{"success":false,"error-codes":"invalid-keys"}""", TestName = "Codes that are no list")]
    [TestCase("""{"success":false}""", TestName = "No codes")]
    public async Task AnyOtherRefusal_IsTheVisitorsFailure(string body) =>
        Assert.That(await Provider(H, Answering(body)).VerifyAsync(Check("token"), CancellationToken.None), Is.EqualTo(FormBuilderCaptchaOutcome.Failed));

    [Test]
    public async Task ACallThatFails_IsAnOutage()
    {
        foreach (var failure in new Exception[] { new HttpRequestException("Refused."), new IOException("Broken."), new InvalidOperationException("Odd."), new FormatException("Odd.") })
        {
            var answer = await Provider(V2, new ScriptedTransport((_, _) => Task.FromException<HttpResponseMessage>(failure))).VerifyAsync(Check("token"), CancellationToken.None);

            Assert.That(answer, Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable), failure.GetType().Name);
        }
    }

    [Test]
    public async Task ACallPastItsLimitOfEightSeconds_IsAnOutage()
    {
        var timers = new ManualTimers();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ScriptedTransport(async (_, token) =>
        {
            listening.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ScriptedTransport.Json("""{"success":true}""");
        });

        var answering = Provider(V2, transport, timers).VerifyAsync(Check("token"), CancellationToken.None);
        await listening.Task.WaitAsync(TimeSpan.FromMinutes(1));
        var dueTime = timers.Timers[0].DueTime;
        timers.FireAll();

        Assert.Multiple(async () =>
        {
            Assert.That(dueTime, Is.EqualTo(TimeSpan.FromSeconds(8)));
            Assert.That(await answering.WaitAsync(TimeSpan.FromMinutes(1)), Is.EqualTo(FormBuilderCaptchaOutcome.Unavailable));
        });
    }

    [Test]
    public async Task TheSubmissionsOwnCancellation_EndsIt()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var transport = new ScriptedTransport(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ScriptedTransport.Json("""{"success":true}""");
        });

        Assert.That(async () => await Provider(V2, transport).VerifyAsync(Check("token"), cancelled.Token), Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void TheCollection_HoldsTheBuiltInOnes_AndTheLastOfANameWins()
    {
        var services = new ServiceCollection();
        var builder = new FormBuilderCaptchaProviderCollectionBuilder();
        builder.Append<SitesOwnHcaptcha>();
        TestFieldTypes.AddCaptchaProviders(services);
        builder.RegisterWith(services);
        using var provider = services.BuildServiceProvider();

        var collection = provider.GetRequiredService<FormBuilderCaptchaProviderCollection>();

        Assert.Multiple(() =>
        {
            Assert.That(collection.Select(item => item.Name), Is.EqualTo(new[] { V2, V3, H, H }));
            Assert.That(collection.Find(V3), Is.TypeOf<RecaptchaV3Provider>());
            Assert.That(collection.Find(H), Is.TypeOf<SitesOwnHcaptcha>());
            Assert.That(collection.Find("acme"), Is.Null);
            Assert.That(collection.Find("HCAPTCHA"), Is.Null, "a name is written one way");
            Assert.That(collection.Find(null), Is.Null);
        });
    }

    [Test]
    public async Task TheSitesHosts_AreItsDomainsAndAllowedOrigins_WithoutWildcards()
    {
        var domains = Substitute.For<IDomainService>();
        IDomain[] known = [Domain("www.example.com"), Domain("plain.example.com"), Domain("https://Shop.example.com/en"), Domain(" ")];
        domains.GetAllAsync(false).Returns(Task.FromResult<IEnumerable<IDomain>>(known));
        var options = new FormBuilderOptions();
        options.PublicApi.AllowedOrigins.Add("https://app.example.org");
        options.PublicApi.AllowedOrigins.Add("https://www.example.com");

        var hosts = await new CaptchaHosts(domains, Options.Create(options)).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(hosts, Is.EquivalentTo(new[] { "www.example.com", "plain.example.com", "shop.example.com", "app.example.org" }));
            Assert.That(hosts.Contains("WWW.example.com"), Is.True, "in any letters");
        });
    }

    private static IDomain Domain(string name)
    {
        var domain = Substitute.For<IDomain>();
        domain.DomainName.Returns(name);
        return domain;
    }

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static FormBuilderCaptchaCheck Check(string token, IReadOnlySet<string>? hosts = null, SettingsObject? settings = null) => new(token, hosts ?? Localhost, settings);

    private static ScriptedTransport Answering(string body) => new(_ => ScriptedTransport.Json(body));

    // The stub's verification, at the moment these tests run in.
    private static ScriptedTransport StubTransport() => new(async (request, token) =>
    {
        var body = await request.Content!.ReadAsStringAsync(token);
        return ScriptedTransport.Json(JsonSerializer.Serialize(StubCaptcha.Verify(body, request.Content.Headers.ContentType?.ToString(), Now)));
    });

    private static FormBuilderCaptchaKeys KeysOf(FormBuilderOptions options, string name) => name switch
    {
        V2 => options.Captcha.RecaptchaV2,
        V3 => options.Captcha.RecaptchaV3,
        _ => options.Captcha.Hcaptcha,
    };

    private static SiteVerifyCaptchaProvider Provider(string name, IFormBuilderHttpTransport transport, TimeProvider? time = null, bool keyed = true, Action<FormBuilderOptions>? change = null)
    {
        var options = new FormBuilderOptions();
        if (keyed)
        {
            KeysOf(options, name).SiteKey = SiteKey;
            KeysOf(options, name).SecretKey = SecretKey;
        }

        change?.Invoke(options);
        var held = Options.Create(options);
        time ??= new TestTimeProvider(Now);
        return name switch
        {
            V2 => new RecaptchaV2Provider(held, transport, time),
            V3 => new RecaptchaV3Provider(held, transport, time),
            _ => new HcaptchaProvider(held, transport, time),
        };
    }

    // A site's own provider under a built-in one's name.
    private sealed class SitesOwnHcaptcha : IFormBuilderCaptchaProvider
    {
        public string Name => H;

        public bool IsConfigured => true;

        public FormBuilderCaptchaClient? Client => null;

        public Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken) => Task.FromResult(FormBuilderCaptchaOutcome.Passed);
    }
}
