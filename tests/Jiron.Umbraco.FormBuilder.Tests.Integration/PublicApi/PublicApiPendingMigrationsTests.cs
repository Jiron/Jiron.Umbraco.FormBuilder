using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// While Jiron Form Builder's migration plan is behind (ADR-0033), every endpoint of the public API answers
/// <c>503 migrations_pending</c> with <c>Retry-After</c>, without touching a table or storing a file, also when it is
/// sent a body it takes, and answers normally once the plan ran (ADR-0084). The site boots without the plan: the test
/// hosts keep unattended package migrations off.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicApiPendingMigrationsTests : ConfiguredTestServerTestBase
{
    [Test]
    public async Task EveryEndpoint_AnswersMigrationsPending_WhileThePlanIsBehind_AndNormallyOnceItRan()
    {
        var endpoints = PublicApiEndpoints.All(Services);
        var root = ((PhysicalFileStore)GetRequiredService<IFormBuilderFileStore>()).Root;
        List<string> Stored() => Directory.Exists(root) ? [.. Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)] : [];
        var before = Stored();
        var pending = await CallEachAsync(endpoints, "while pending");
        var stored = Stored().Except(before).ToList();

        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);
        Assert.That(results.Single().Successful, Is.True, results.Single().Exception?.ToString());
        GetRequiredService<IFormBuilderMigrationState>().Invalidate();
        var after = await CallEachAsync(endpoints, "after the plan ran");

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Is.Not.Empty);
            Assert.That(endpoints.Where(endpoint => endpoint.TakesBody), Is.Not.Empty, "an endpoint that takes a body is sent one");
            Assert.That(stored, Is.Empty, "no file reaches the store while the tables are not there");
            Assert.That(pending.Values, Is.All.EqualTo("503 migrations_pending, retry after 10 s"), string.Join("\n", pending));
            Assert.That(after.Values, Is.All.Not.StartWith("5"), string.Join("\n", after));
            Assert.That(after["GET (no endpoint)"], Is.EqualTo("404 not_found"));
        });
    }

    // Calls every endpoint with a key that names nothing and a body it takes, and an address that names no endpoint, and
    // writes each answer to the test's output.
    private async Task<Dictionary<string, string>> CallEachAsync(List<PublicApiEndpoint> endpoints, string when)
    {
        var calls = endpoints.Select(endpoint => (endpoint.Name, endpoint.Method, Path: endpoint.PathFor(Guid.NewGuid()), Content: (Func<HttpContent?>)endpoint.Body))
            .Append(("GET (no endpoint)", "GET", "/jfb/api/v1/nothing", () => null));
        var answers = new Dictionary<string, string>();
        foreach (var (name, method, path, content) in calls)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = content() };
            using var response = await Client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            var code = body.Length > 0 && JsonNode.Parse(body) is JsonObject problem ? problem["code"]?.GetValue<string>() : null;
            var answer = $"{(int)response.StatusCode} {code ?? "(no code)"}";
            answers[name] = response.Headers.RetryAfter?.Delta is { } retry ? $"{answer}, retry after {retry.TotalSeconds} s" : answer;
            await TestContext.Out.WriteLineAsync($"{when}: {name}: {answers[name]}");
        }

        return answers;
    }
}
