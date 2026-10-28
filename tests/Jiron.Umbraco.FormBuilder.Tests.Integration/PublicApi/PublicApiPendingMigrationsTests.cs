using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// While Jiron Form Builder's migration plan is behind (ADR-0033), every endpoint of the public API answers
/// <c>503 migrations_pending</c> with <c>Retry-After</c>, without touching a table, and answers normally once the plan ran
/// (ADR-0084). The site boots without the plan: the test hosts keep unattended package migrations off.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicApiPendingMigrationsTests : ConfiguredTestServerTestBase
{
    [Test]
    public async Task EveryEndpoint_AnswersMigrationsPending_WhileThePlanIsBehind_AndNormallyOnceItRan()
    {
        var endpoints = PublicApiEndpoints.All(Services);
        var pending = await CallEachAsync(endpoints, "while pending");

        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);
        Assert.That(results.Single().Successful, Is.True, results.Single().Exception?.ToString());
        GetRequiredService<IFormBuilderMigrationState>().Invalidate();
        var after = await CallEachAsync(endpoints, "after the plan ran");

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Is.Not.Empty);
            Assert.That(endpoints.Where(endpoint => endpoint.TakesBody), Is.Empty, "a body for each comes with the first endpoint that takes one (6.1d)");
            Assert.That(pending.Values, Is.All.EqualTo("503 migrations_pending, retry after 10 s"), string.Join("\n", pending));
            Assert.That(after.Values, Is.All.Not.StartWith("5"), string.Join("\n", after));
            Assert.That(after["GET (no endpoint)"], Is.EqualTo("404 not_found"));
        });
    }

    // Calls every endpoint with a key that names nothing, and an address that names no endpoint, and writes each answer to
    // the test's output.
    private async Task<Dictionary<string, string>> CallEachAsync(List<PublicApiEndpoint> endpoints, string when)
    {
        var calls = endpoints.Select(endpoint => (endpoint.Name, endpoint.Method, Path: endpoint.PathFor(Guid.NewGuid())))
            .Append(("GET (no endpoint)", "GET", "/jfb/api/v1/nothing"));
        var answers = new Dictionary<string, string>();
        foreach (var (name, method, path) in calls)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
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
