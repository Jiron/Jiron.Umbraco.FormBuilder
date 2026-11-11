using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The rate limits on a real site (ROADMAP 7.1a, ADR-0012, ADR-0137): every endpoint's policy answers
/// <c>429 rate_limited</c> with <c>Retry-After</c> and nothing behind it runs; a flood of 20,000 addresses gets past
/// neither the site-wide limit nor the bound; a server key counts by itself; and only the connection's address counts,
/// whatever a request's headers say. On a clock that stands still, with the limits each test names.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicApiRateLimitTests : PublicApiTestBase
{
    // A fake key for the test site only (JFB_FAKE): 43 characters, as scripts/server-key.mjs makes them.
    private const string ShopKey = "JFB_FAKE_shop_server_key_0123456789abcdefgh";
    private const string TestAddressHeader = "X-Jfb-Test-Address";
    private const string Limits = FormBuilderOptions.SectionName + ":Limits:";

    // What a test's name asks of its host, read when the host is built.
    private static readonly Dictionary<string, string> Flood = new()
    {
        [Limits + "MaxTrackedClients"] = "1000",
        [Limits + "Read:PerAddressPerMinute"] = "5",
        [Limits + "Read:SitePerSecond"] = "50",
    };

    private StoppedClock _clock = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private FormBuilderRateLimiter Limiter => GetRequiredService<FormBuilderRateLimiter>();

    [Test]
    public async Task EntriesOverTheLimit_Answer429WithRetryAfter_StoreNothing_AndFitAgainThen()
    {
        var key = await PublishAsync();
        using var first = await PostEntryAsync(key, "203.0.113.7");
        using var second = await PostEntryAsync(key, "203.0.113.7");
        using var refused = await PostEntryAsync(key, "203.0.113.7");
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        var wait = refused.Headers.RetryAfter!.Delta!.Value;
        var stored = await CountEntriesAsync(key);

        _clock.Advance(wait);
        using var later = await PostEntryAsync(key, "203.0.113.7");

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(refused.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("rate_limited"));
            Assert.That(refused.Headers.CacheControl!.NoStore, Is.True);
            Assert.That(wait, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(wait, Is.LessThanOrEqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(stored, Is.EqualTo(2), "the refused one stored nothing");
            Assert.That(later.StatusCode, Is.EqualTo(HttpStatusCode.OK), "after Retry-After a send fits");
        });
    }

    [TestCase("entries", """{"revision":1,"values":{"name":"Ada"}}""")]
    [TestCase("validate", """{"revision":1,"pageKey":"00000000-0000-4000-8000-000000000001","values":{}}""")]
    [TestCase("receipt", """{"submissionId":"00000000-0000-4000-8000-000000000002"}""")]
    public async Task EachSubmittingEndpoint_CountsInTheSamePolicy_PerAddressAndForm(string endpoint, string body)
    {
        var key = await PublishAsync();
        using var one = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "203.0.113.7");
        using var two = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "203.0.113.7");

        using var refused = await PostAsync($"{Root}/forms/{key}/{endpoint}", body, "203.0.113.7");
        using var otherAddress = await PostAsync($"{Root}/forms/{key}/{endpoint}", body, "203.0.113.8");

        Assert.Multiple(() =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(otherAddress.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
        });
    }

    [Test]
    public async Task ALinksPages_CountInTheirOwnPolicy_PerAddress()
    {
        var path = $"{Root}/verify?t=made-up";
        for (var count = 0; count < 5; count++)
        {
            using var each = await GetAsync(path, "203.0.113.7");
            if (each.StatusCode == HttpStatusCode.TooManyRequests)
            {
                using var resume = await PostAsync($"{Root}/drafts/resume", """{"token":"made-up"}""", "203.0.113.7");
                using var other = await GetAsync(path, "203.0.113.8");
                Assert.Multiple(() =>
                {
                    Assert.That(resume.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "a draft's link takes the same limit");
                    Assert.That(other.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
                });
                return;
            }
        }

        Assert.Fail("A link's page never reached its limit.");
    }

    [Test]
    public async Task AnUpload_CountsInItsOwnPolicy()
    {
        var key = await PublishAsync();

        using var one = await UploadAsync(key, "203.0.113.7");
        using var refused = await UploadAsync(key, "203.0.113.7");
        using var entry = await PostEntryAsync(key, "203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(one.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(refused.Headers.RetryAfter, Is.Not.Null);
            Assert.That(entry.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the upload's limit is not the submission's");
        });
    }

    [TestCase("forms/contact-us")]
    [TestCase("forms/71a00000-0000-4000-8000-000000000001/options/71a00000-0000-4000-8000-000000000002")]
    [TestCase("forms/71a00000-0000-4000-8000-000000000001/render?page=/")]
    [TestCase("forms/71a00000-0000-4000-8000-000000000001/element")]
    [TestCase("no/such/endpoint")]
    public async Task EachReadingEndpoint_CountsInTheReadPolicy(string path)
    {
        using var one = await GetAsync($"{Root}/{path}", "203.0.113.7");
        using var two = await GetAsync($"{Root}/{path}", "203.0.113.7");

        using var refused = await GetAsync($"{Root}/{path}", "203.0.113.7");
        using var otherAddress = await GetAsync($"{Root}/{path}", "203.0.113.8");

        Assert.Multiple(() =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(otherAddress.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
        });
    }

    [Test]
    public async Task APreflight_CountsForNothing()
    {
        foreach (var _ in Enumerable.Range(0, 5))
        {
            using var preflight = await SendAsync(HttpMethod.Options, $"{Root}/forms/contact-us", request =>
            {
                request.Headers.TryAddWithoutValidation(TestAddressHeader, "203.0.113.7");
                request.Headers.TryAddWithoutValidation("Origin", "https://elsewhere.example");
                request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");
            });
            Assert.That(preflight.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
        }

        using var read = await GetAsync($"{Root}/forms/contact-us", "203.0.113.7");
        Assert.That(read.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task AFloodFrom20000Addresses_DoesNotBypassTheSiteWideLimit()
    {
        var passed = await FloodAsync();

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.EqualTo(50), "the site-wide limit of the second that stands still");
            Assert.That(Limiter.TrackedWindows, Is.LessThanOrEqualTo(1000), "memory stays bounded");
        });
    }

    [Test]
    public async Task AFloodFrom20000Addresses_WithNoSiteWideLimit_SharesTheOverflow_AndLeavesTheBoundFreeAfterwards()
    {
        var passed = await FloodAsync();
        var kept = Limiter.TrackedWindows;
        using var during = await GetAsync($"{Root}/no/such/endpoint", "198.51.100.9");

        _clock.Advance(TimeSpan.FromMinutes(3));
        using var after = await GetAsync($"{Root}/no/such/endpoint", "198.51.100.9");

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.LessThanOrEqualTo(1000), "memory stays bounded");
            Assert.That(passed, Is.EqualTo(1005), "each kept address, then the overflow's one limit");
            Assert.That(during.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "fails closed for a new address");
            Assert.That(after.StatusCode, Is.Not.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(Limiter.TrackedWindows, Is.EqualTo(1), "the flood's windows are swept");
        });
    }

    [Test]
    public async Task AServerKeysLimit_Holds_AndDoesNotTouchTheAddresss()
    {
        var key = await PublishAsync();
        var signed = new List<HttpStatusCode>();
        foreach (var _ in Enumerable.Range(0, 5))
        {
            using var sent = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "203.0.113.7", "Jfb-Key " + ShopKey);
            signed.Add(sent.StatusCode);
        }

        using var anonymous = await PostEntryAsync(key, "203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(signed.Count(status => status == HttpStatusCode.OK), Is.EqualTo(4), "twice the address's two");
            Assert.That(signed[^1], Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the same address, unsigned, has its own");
        });
    }

    [Test]
    public async Task AMadeUpKey_CostsItsAddress_NoFormsWindow_AndNoneOfTheSitesLimit()
    {
        var key = await PublishAsync();
        var path = $"{Root}/forms/{key}";
        using var first = await GetAsync(path, "203.0.113.7");
        var windows = Limiter.TrackedWindows;

        using var madeUp = await GetAsync($"{Root}/forms/{Guid.NewGuid()}", "203.0.113.7");
        using var another = await GetAsync($"{Root}/forms/{Guid.NewGuid()}", "203.0.113.7");
        using var pastItsWindow = await GetAsync($"{Root}/forms/{Guid.NewGuid()}", "203.0.113.7");
        var afterThem = Limiter.TrackedWindows;
        using var second = await GetAsync(path, "203.0.113.7");
        using var third = await GetAsync(path, "203.0.113.7");
        using var pastTheSite = await GetAsync(path, "203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((madeUp.StatusCode, another.StatusCode), Is.EqualTo((HttpStatusCode.NotFound, HttpStatusCode.NotFound)));
            Assert.That(pastItsWindow.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "looking up made-up keys has a limit of its own");
            Assert.That(afterThem, Is.EqualTo(windows), "no window for a form that does not exist");
            Assert.That((second.StatusCode, third.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)), "the site's limit is what it was");
            Assert.That(pastTheSite.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "and it still holds");
        });
    }

    [Test]
    public async Task ARouteValueThatCanBeNoForm_CostsItsAddress_AndNoLookup()
    {
        // The site's limit and the lookups' are one a second here, so a second lookup of either kind would answer 429.
        var noAlias = $"{Root}/forms/{new string('x', 300)}";
        using var noKey = await GetAsync($"{Root}/forms/not-a-key/render", "203.0.113.7");
        using var noKeyAgain = await GetAsync($"{Root}/forms/not-a-key/render", "203.0.113.7");
        using var noSuchAlias = await GetAsync(noAlias, "203.0.113.8");
        using var noSuchAliasAgain = await GetAsync(noAlias, "203.0.113.8");
        using var emptyKey = await GetAsync($"{Root}/forms/{Guid.Empty}/render", "203.0.113.9");
        using var emptyKeyAgain = await GetAsync($"{Root}/forms/{Guid.Empty}/render", "203.0.113.9");
        var windows = Limiter.TrackedWindows;
        using var overItsAddress = await GetAsync($"{Root}/forms/not-a-key/render", "203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(new[] { noKey.StatusCode, noKeyAgain.StatusCode, noSuchAlias.StatusCode, noSuchAliasAgain.StatusCode, emptyKey.StatusCode, emptyKeyAgain.StatusCode }, Has.None.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(windows, Is.EqualTo(3), "each address's");
            Assert.That(overItsAddress.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
        });
    }

    [Test]
    public async Task AnAliasOfNoForm_HasTheLookupsLimit_AndSpendsNothingOfTheSites_WhileAKeptOneReadsNothing()
    {
        var key = await PublishAsync();
        var alias = (await FormRows.GetAsync(key))!.Alias;
        using var first = await GetAsync($"{Root}/forms/{alias}", "203.0.113.7");
        using var second = await GetAsync($"{Root}/forms/{alias}", "203.0.113.7");

        // The first read's lookup and two more fill the lookups' window of three, as the kept one's second read took none of it.
        using var one = await GetAsync($"{Root}/forms/noSuchAlias1", "203.0.113.7");
        using var two = await GetAsync($"{Root}/forms/noSuchAlias2", "203.0.113.7");
        using var pastItsWindow = await GetAsync($"{Root}/forms/noSuchAlias3", "203.0.113.7");
        using var third = await GetAsync($"{Root}/forms/{alias}", "203.0.113.7");
        using var pastTheSite = await GetAsync($"{Root}/forms/{alias}", "203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That((first.StatusCode, second.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That((one.StatusCode, two.StatusCode), Is.EqualTo((HttpStatusCode.NotFound, HttpStatusCode.NotFound)));
            Assert.That(pastItsWindow.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(third.StatusCode, Is.EqualTo(HttpStatusCode.OK), "aliases of no form spent none of the site's limit");
            Assert.That(pastTheSite.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "and it still holds");
        });
    }

    [TestCase("Contact us")]
    // An alias of 32 hex digits, which Guid.TryParse would take for a key: a key is read in its "D" form only.
    [TestCase("abcdef0123456789abcdef0123456789")]
    public async Task AnAlias_CountsAsItsForm(string name)
    {
        var key = await PublishAsync(name);
        var other = await PublishAsync("Another");
        var alias = (await FormRows.GetAsync(key))!.Alias;

        using var byKey = await GetAsync($"{Root}/forms/{key}", "203.0.113.7");
        using var byAlias = await GetAsync($"{Root}/forms/{alias}", "203.0.113.8");
        using var overTheForm = await GetAsync($"{Root}/forms/{alias}", "203.0.113.9");
        using var byKeyOver = await GetAsync($"{Root}/forms/{key}", "203.0.113.9");
        using var anotherForm = await GetAsync($"{Root}/forms/{other}", "203.0.113.9");
        using var noSuchAlias = await GetAsync($"{Root}/forms/noSuchAlias", "203.0.113.9");

        Assert.Multiple(() =>
        {
            Assert.That((byKey.StatusCode, byAlias.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)));
            Assert.That(overTheForm.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests), "the key's and the alias's reads share one window");
            Assert.That(byKeyOver.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(anotherForm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(noSuchAlias.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "an alias of no form spends nothing of a form's");
        });
    }

    [Test]
    public async Task AChallengeOverItsLimit_Answers429WithRetryAfter()
    {
        var key = await PublishAsync();

        using var first = await PostAsync($"{Root}/forms/{key}/challenge", "{}", "203.0.113.7");
        using var refused = await PostAsync($"{Root}/forms/{key}/challenge", "{}", "203.0.113.7");
        var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()).RootElement;
        using var otherAddress = await PostAsync($"{Root}/forms/{key}/challenge", "{}", "203.0.113.8");

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
            Assert.That(problem.GetProperty("code").GetString(), Is.EqualTo("rate_limited"));
            Assert.That(refused.Headers.RetryAfter?.Delta, Is.GreaterThan(TimeSpan.Zero));
            Assert.That(otherAddress.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task AForwardedHeader_ChangesNothing_WithoutTheSitesForwardedHeaders()
    {
        var key = await PublishAsync();
        using var one = await PostEntryAsync(key, "203.0.113.7", forwarded: "198.51.100.1");
        using var two = await PostEntryAsync(key, "203.0.113.7", forwarded: "198.51.100.2");

        using var refused = await PostEntryAsync(key, "203.0.113.7", forwarded: "198.51.100.3");

        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration[Limits + "Submit:PerAddressPerMinute"] = "0";
        configuration[Limits + "Submit:PerAddressPerHour"] = "0";
        configuration[Limits + "Submit:PerAddressAndFormPerMinute"] = "2";
        configuration[Limits + "Submit:PerFormPerMinute"] = "0";
        configuration[Limits + "Submit:SitePerSecond"] = "0";
        configuration[Limits + "Upload:PerAddressAndFormPerMinute"] = "1";
        configuration[Limits + "Upload:PerAddressPerMinute"] = "0";
        configuration[Limits + "Verify:PerAddressPerMinute"] = "2";
        configuration[Limits + "Upload:SitePerSecond"] = "0";
        configuration[Limits + "Read:PerAddressPerMinute"] = "2";
        configuration[Limits + "Read:SitePerSecond"] = "0";
        configuration[Limits + "ServerKeyFactor"] = "2";
        configuration[FormBuilderOptions.SectionName + ":PublicApi:ServerKeys:0:Name"] = "shop";
        configuration[FormBuilderOptions.SectionName + ":PublicApi:ServerKeys:0:Sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ShopKey)));

        var name = TestContext.CurrentContext.Test.MethodName ?? string.Empty;
        if (name == nameof(ARouteValueThatCanBeNoForm_CostsItsAddress_AndNoLookup))
        {
            configuration[Limits + "Read:SitePerSecond"] = "1";
        }

        if (name is nameof(AMadeUpKey_CostsItsAddress_NoFormsWindow_AndNoneOfTheSitesLimit) or nameof(AnAliasOfNoForm_HasTheLookupsLimit_AndSpendsNothingOfTheSites_WhileAKeptOneReadsNothing))
        {
            configuration[Limits + "Read:PerAddressPerMinute"] = "0";
            configuration[Limits + "Read:PerFormPerMinute"] = "100";
            configuration[Limits + "Read:SitePerSecond"] = "3";
        }

        if (name == nameof(AnAlias_CountsAsItsForm))
        {
            configuration[Limits + "Read:PerAddressPerMinute"] = "0";
            configuration[Limits + "Read:PerFormPerMinute"] = "2";
        }

        if (name == nameof(AChallengeOverItsLimit_Answers429WithRetryAfter))
        {
            configuration[Limits + "Challenge:PerAddressAndFormPerMinute"] = "1";
        }

        if (name.StartsWith("AFloodFrom20000Addresses", StringComparison.Ordinal))
        {
            foreach (var (key, value) in Flood)
            {
                configuration[key] = value;
            }

            if (name.Contains("WithNoSiteWideLimit", StringComparison.Ordinal))
            {
                configuration[Limits + "Read:SitePerSecond"] = "0";
            }
        }
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        _clock = new StoppedClock();
        builder.Services.AddUnique<TimeProvider>(_clock);
        builder.Services.AddTransient<IStartupFilter, TestAddressStartupFilter>();
    }

    // Asks once per address, as a flood of distinct addresses does, and counts what got past the limits.
    private async Task<int> FloodAsync()
    {
        var passed = 0;
        foreach (var i in Enumerable.Range(0, 20_000))
        {
            using var response = await GetAsync($"{Root}/no/such/endpoint", $"10.{(i >> 16) & 255}.{(i >> 8) & 255}.{i & 255}");
            passed += response.StatusCode == HttpStatusCode.TooManyRequests ? 0 : 1;
        }

        return passed;
    }

    private Task<HttpResponseMessage> PostEntryAsync(Guid key, string address, string? forwarded = null) =>
        PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", address, forwarded: forwarded);

    private Task<HttpResponseMessage> PostAsync(string path, string body, string address, string? authorization = null, string? forwarded = null) =>
        SendAsync(HttpMethod.Post, path, request =>
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation(TestAddressHeader, address);
            if (authorization is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }

            if (forwarded is not null)
            {
                request.Headers.TryAddWithoutValidation("X-Forwarded-For", forwarded);
            }
        });

    private Task<HttpResponseMessage> GetAsync(string path, string address) =>
        SendAsync(HttpMethod.Get, path, request => request.Headers.TryAddWithoutValidation(TestAddressHeader, address));

    private Task<HttpResponseMessage> UploadAsync(Guid key, string address) =>
        SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads", request =>
        {
            request.Headers.TryAddWithoutValidation(TestAddressHeader, address);
            request.Content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.txt" } };
        });

    private async Task<int> CountEntriesAsync(Guid key) => (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 50)).Count;

    // Gives a request the address a test names, as the connection would.
    private sealed class TestAddressStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, go) =>
            {
                if (context.Request.Headers[TestAddressHeader] is [{ } address])
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address);
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
