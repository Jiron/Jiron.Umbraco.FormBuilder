using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// Server-key submissions on a real site (ROADMAP 6.6b, ADR-0126): a headless server's key submits, and the entry
/// records the key's name as <c>jfbServerKey</c>, never a posted one; the receipt answers that key and no other; a key
/// that matches none, or a malformed one, answers <c>401 server_key_invalid</c> on every endpoint and stores nothing;
/// and neither the key nor its hash reaches a log or an answer.
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
    public async Task AReceipt_AnswersTheKeyThatSubmitted_AndNoOther()
    {
        var key = await PublishAsync();
        var submission = Guid.NewGuid();
        using var sent = await PostAsync($"{Root}/forms/{key}/entries", $$$"""{"submissionId":"{{{submission}}}","revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + ShopKey);
        var body = $$$"""{"submissionId":"{{{submission}}}"}""";
        using var same = await PostAsync($"{Root}/forms/{key}/receipt", body, "Jfb-Key " + ShopKey);
        using var other = await PostAsync($"{Root}/forms/{key}/receipt", body, "Jfb-Key " + AppKey);
        using var retry = await PostAsync($"{Root}/forms/{key}/entries", $$$"""{"submissionId":"{{{submission}}}","revision":1,"values":{"name":"Ada"}}""", "Jfb-Key " + AppKey);

        Assert.Multiple(async () =>
        {
            Assert.That(same.StatusCode, Is.EqualTo(HttpStatusCode.OK), await same.Content.ReadAsStringAsync());
            Assert.That(await same.Content.ReadAsStringAsync(), Is.EqualTo(await sent.Content.ReadAsStringAsync()), "the receipt and the outcome the submission got");
            Assert.That(await RefusalAsync(other), Is.EqualTo("404 not_found"), "another key meets the same miss as an unknown submission");
            Assert.That(await RefusalAsync(retry), Is.EqualTo("409 duplicate_submission"), "the same submission from another key is not its retry");
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
