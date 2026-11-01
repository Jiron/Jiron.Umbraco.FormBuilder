using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// Server-key submissions on a real site (ROADMAP 6.6b, ADR-0126): a headless server's key submits, and the entry
/// records the key's name as <c>jfbServerKey</c>, never a posted one; the receipt answers that key and no other; a key
/// that matches none, or a malformed one, answers <c>401 server_key_invalid</c> on every endpoint and stores nothing;
/// neither the key nor its hash reaches a log or an answer; and a changed key takes effect only when the site restarts,
/// as every host option does (6.8a).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicServerKeyTests : PublicApiTestBase
{
    // Fake keys for the test site only (JFB_FAKE): 43 characters each, as scripts/server-key.mjs makes them.
    private const string ShopKey = "JFB_FAKE_shop_server_key_0123456789abcdefgh";
    private const string AppKey = "JFB_FAKE_app_server_key_0123456789abcdefghi";
    private const string RevokedKey = "JFB_FAKE_revoked_server_key_0123456789abcde";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task AServerKey_Submits_AndTheEntryRecordsItsName_NeverAPostedOne()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Settings = definition.Settings with { Developer = definition.Settings.Developer with { ContextValues = ["campaign"] } },
        });
        using var sent = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"},"context":{"campaign":"autumn","jfbServerKey":"forged"}}""", "Jfb-Key " + ShopKey);
        using var schemeInOtherCase = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Grace"}}""", "JFB-KEY " + AppKey);
        using var anonymous = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Alan"},"context":{"jfbServerKey":"forged"}}""", authorization: null);
        var contexts = await ContextsAsync(key);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
            Assert.That(schemeInOtherCase.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the scheme in any letter case");
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.OK), "without the scheme, an ordinary request");
            Assert.That(contexts, Is.EqualTo(new[]
            {
                """{"campaign":"autumn","jfbServerKey":"shop"}""",
                """{"jfbServerKey":"app"}""",
                "{}",
            }), "the key's name, never a posted one, which no form may list");
        });
    }

    [Test]
    public async Task AKeyThatMatchesNone_OrAMalformedOne_Answers401OnEveryEndpoint_AndStoresNothing()
    {
        var key = await PublishAsync();
        var answers = new List<(string What, HttpResponseMessage Response)>
        {
            ("a revoked key", await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + RevokedKey)),
            ("a short key", await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "Jfb-Key short")),
            ("the scheme alone", await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "Jfb-Key")),
            ("a key with a space", await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + ShopKey[..20] + " " + ShopKey[20..])),
            ("a read", await GetAsync($"{Root}/forms/{key}", request => request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + RevokedKey))),
        };
        var logged = string.Join("\n", GetRequiredService<LogCapture>().Entries.Select(entry => entry.Message));
        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(FormBuilderPublicApiMiddleware).FullName && entry.Level == LogLevel.Warning).ToList();

        try
        {
            Assert.Multiple(async () =>
            {
                foreach (var (what, response) in answers)
                {
                    Assert.That(await RefusalAsync(response), Is.EqualTo("401 server_key_invalid"), what);
                    Assert.That(response.Headers.WwwAuthenticate.ToString(), Is.EqualTo("Jfb-Key"), what);
                    Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("JFB_FAKE"), $"{what}: the answer holds no key");
                }

                Assert.That(await ContextsAsync(key), Is.Empty, "nothing stored");
                Assert.That(logged, Does.Contain("carried a server key that matches none"));
                Assert.That(warnings, Has.Count.EqualTo(1), "a key that matches none comes before any limit, so its warning is bounded");
                Assert.That(warnings[0].Message, Does.Not.Contain("/forms"), "and names no path a caller chose");
                Assert.That(logged, Does.Not.Contain("JFB_FAKE").And.Not.Contain(Hash(RevokedKey)).And.Not.Contain(Hash(ShopKey)), "neither a key nor a hash in a log");
            });
        }
        finally
        {
            foreach (var (_, response) in answers)
            {
                response.Dispose();
            }
        }
    }

    [Test]
    public async Task AnUploadByAKey_LeavesTheKeysName_AndAVisitorsLeavesNone()
    {
        var attachment = new Core.Definitions.FormField { Key = Guid.NewGuid(), Type = "file", Alias = "attachment", Label = new Core.Definitions.LocalizedText("Attachment") };
        var key = await PublishAsync(fields: [NameField, attachment]);
        using var content = new MultipartFormDataContent { { new ByteArrayContent([.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8]), "file", "a.pdf" } };

        using var sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads?fieldKey={attachment.Key}", request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Authorization", "Jfb-Key " + ShopKey);
        });
        var logged = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(UploadRequests).FullName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(logged, Has.Count.EqualTo(1));
            Assert.That(logged[0].Message, Does.Contain("shop").And.Not.Contain("a.pdf").And.Not.Contain("JFB_FAKE"));
        });
    }

    [Test]
    public async Task AReceipt_AnswersTheKeyThatSubmitted_AndNoOther_NorARequestWithoutAKey()
    {
        var key = await PublishAsync();
        var submission = Guid.NewGuid();
        using var sent = await PostAsync($"{Root}/forms/{key}/entries", $$$"""{"submissionId":"{{{submission}}}","revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + ShopKey);
        var body = $$$"""{"submissionId":"{{{submission}}}"}""";
        using var same = await PostAsync($"{Root}/forms/{key}/receipt", body, "Jfb-Key " + ShopKey);
        using var other = await PostAsync($"{Root}/forms/{key}/receipt", body, "Jfb-Key " + AppKey);
        using var none = await PostAsync($"{Root}/forms/{key}/receipt", body, null);
        var plain = Guid.NewGuid();
        var plainBody = $$$"""{"submissionId":"{{{plain}}}"}""";
        using var plainSent = await PostAsync($"{Root}/forms/{key}/entries", $$$"""{"submissionId":"{{{plain}}}","revision":1,"values":{"name":"Ada"}}""", null);
        using var plainReceipt = await PostAsync($"{Root}/forms/{key}/receipt", plainBody, null);
        using var plainAskedByAKey = await PostAsync($"{Root}/forms/{key}/receipt", plainBody, "Jfb-Key " + ShopKey);
        using var retry = await PostAsync($"{Root}/forms/{key}/entries", $$$"""{"submissionId":"{{{submission}}}","revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + AppKey);

        Assert.Multiple(async () =>
        {
            Assert.That(same.StatusCode, Is.EqualTo(HttpStatusCode.OK), await same.Content.ReadAsStringAsync());
            Assert.That(await same.Content.ReadAsStringAsync(), Is.EqualTo(await sent.Content.ReadAsStringAsync()), "the receipt and the outcome the submission got");
            Assert.That(await RefusalAsync(other), Is.EqualTo("404 not_found"), "another key meets the same miss as an unknown submission");
            Assert.That(await RefusalAsync(none), Is.EqualTo("404 not_found"), "and so does a request without a key (6.8a)");
            Assert.That(plainReceipt.StatusCode, Is.EqualTo(HttpStatusCode.OK), "an entry no key made answers a request without one");
            Assert.That(await plainReceipt.Content.ReadAsStringAsync(), Is.EqualTo(await plainSent.Content.ReadAsStringAsync()));
            Assert.That(await RefusalAsync(plainAskedByAKey), Is.EqualTo("404 not_found"), "and no key");
            Assert.That(await RefusalAsync(retry), Is.EqualTo("409 duplicate_submission"), "the same submission from another key is not its retry");
        });
    }

    [Test]
    public async Task AChangedServerKey_TakesEffectWhenTheSiteRestarts_AsEveryHostOptionDoes()
    {
        var key = await PublishAsync();
        using var before = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + ShopKey);
        GetRequiredService<KeyChange>().Change(Hash(RevokedKey));

        using var kept = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Grace"}}""", "Jfb-Key " + ShopKey);
        using var changed = await PostAsync($"{Root}/forms/{key}/entries", """{"revision":1,"values":{"name":"Alan"}}""", "Jfb-Key " + RevokedKey);

        Assert.Multiple(async () =>
        {
            Assert.That(GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.PublicApi.ServerKeys[0].Sha256, Is.EqualTo(Hash(RevokedKey)), "the configuration did change");
            Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.OK), await before.Content.ReadAsStringAsync());
            Assert.That(kept.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the key the site started with still submits");
            Assert.That(await RefusalAsync(changed), Is.EqualTo("401 server_key_invalid"), "the new one waits for a restart");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var keys = FormBuilderOptions.SectionName + ":PublicApi:ServerKeys:";
        configuration[keys + "0:Name"] = "shop";
        configuration[keys + "0:Sha256"] = Hash(ShopKey);
        configuration[keys + "1:Name"] = "app";
        configuration[keys + "1:Sha256"] = Hash(AppKey).ToUpperInvariant();
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.Services.AddSingleton<KeyChange>();
        builder.Services.AddSingleton<IPostConfigureOptions<FormBuilderOptions>>(provider => provider.GetRequiredService<KeyChange>());
        builder.Services.AddSingleton<IOptionsChangeTokenSource<FormBuilderOptions>>(provider => provider.GetRequiredService<KeyChange>());
    }

    private static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    private async Task<HttpResponseMessage> PostAsync(string path, string json, string? authorization) =>
        await SendAsync(HttpMethod.Post, path, request =>
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (authorization is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            }
        });

    // A change of the site's configuration while it runs, as a reload of appsettings.json makes one: the first server
    // key's hash replaced, and the options told through their change token. It is this host's own: a value set on the
    // site's configuration would reach the configuration the test harness shares between hosts.
    private sealed class KeyChange : IPostConfigureOptions<FormBuilderOptions>, IOptionsChangeTokenSource<FormBuilderOptions>
    {
        private CancellationTokenSource _change = new();
        private string? _hash;

        public string Name => Options.DefaultName;

        public IChangeToken GetChangeToken() => new CancellationChangeToken(_change.Token);

        public void PostConfigure(string? name, FormBuilderOptions options)
        {
            if (_hash is not null)
            {
                options.PublicApi.ServerKeys[0].Sha256 = _hash;
            }
        }

        public void Change(string hash)
        {
            _hash = hash;
            var changed = _change;
            _change = new CancellationTokenSource();
            changed.Cancel();
            changed.Dispose();
        }
    }

    // Each entry's stored context, oldest first, its names in order.
    private async Task<List<string>> ContextsAsync(Guid form)
    {
        var ids = await Entries.ListIdsAsync((await FormRows.GetAsync(form))!.Id, 10);
        var contexts = new List<string>();
        foreach (var id in ids.Order())
        {
            using var context = JsonDocument.Parse((await Entries.GetByIdAsync(id))!.Context);
            contexts.Add(JsonSerializer.Serialize(context.RootElement.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToDictionary(property => property.Name, property => property.Value.GetString())));
        }

        return contexts;
    }
}

/// <summary>
/// A real site whose server key is configured with the key in place of its hash (ADR-0126): the start-up checks the host
/// runs as it starts refuse it, naming the setting and never the value, so the site serves nothing with a key it cannot
/// take (6.8a). The site's own validator is wrapped so that the test keeps what it threw.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.None)]
public sealed class PublicServerKeyStartTests : UmbracoIntegrationTest
{
    // A fake key (JFB_FAKE) where its hash belongs.
    private const string Key = "JFB_FAKE_pasted_server_key_0123456789abcdef";

    /// <summary>The site's one server key holds the key itself.</summary>
    public PublicServerKeyStartTests()
    {
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":PublicApi:ServerKeys:0:Name"] = "shop";
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":PublicApi:ServerKeys:0:Sha256"] = Key;
    }

    [Test]
    public void AServerKeyThatIsNoHash_StopsTheStart_NamingItsSetting()
    {
        var validator = (RecordingStartupValidator)GetRequiredService<IStartupValidator>();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Runs, Is.EqualTo(1), "the host ran it as it started");
            Assert.That(validator.Refusal?.Message, Does.Contain(FormBuilderOptions.SectionName + ":PublicApi:ServerKeys:0:Sha256 must be the SHA-256 of the key"));
            Assert.That(validator.Refusal?.Message, Does.Not.Contain(Key), "and never the value");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddComposers();
        RecordingStartupValidator.Wrap(builder.Services);
    }
}
