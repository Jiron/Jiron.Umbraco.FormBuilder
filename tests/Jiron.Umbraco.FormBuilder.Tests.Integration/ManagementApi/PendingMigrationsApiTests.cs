using System.Net;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.HealthChecks;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// While Jiron Form Builder's migration plan is behind (ADR-0033), every management endpoint answers
/// <c>503 migrations_pending</c> with <c>Retry-After</c>, without touching a table, and answers normally once the plan
/// ran (ADR-0050). The site boots without the plan: the test hosts keep unattended package migrations off.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PendingMigrationsApiTests : ConfiguredTestServerTestBase
{
    private const string Editors = "jfbEditors";

    [Test]
    public async Task EveryEndpoint_AnswersMigrationsPending_WhileThePlanIsBehind_AndNormallyOnceItRan()
    {
        // A group with the Forms section, as a site has that ran an earlier version, while an update's plan waits; the
        // administrators' group lets the user through every check once the tables are there.
        await using (var scope = Services.CreateAsyncScope())
        {
            await ActivatorUtilities.CreateInstance<TestPeople>(scope.ServiceProvider)
                .EnsureUserGroupAsync(new TestUserGroup { Alias = Editors, Name = "Form editors", Sections = { AccessPersonas.Section } });
        }

        var api = new ManagementApiCaller(Client, Services, _ => [Editors, Constants.Security.AdminGroupAlias]);
        var endpoints = ManagementApiEndpoints.All(Services);
        var anonymous = await api.CallAsync(null, HttpMethod.Get, "tree/root");
        var pending = await CallEachAsync(api, endpoints, "while pending");

        var check = GetRequiredService<HealthCheckCollection>().OfType<MigrationsHealthCheck>().Single();
        var whilePending = (await check.GetStatusAsync()).Single();

        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);
        Assert.That(results.Single().Successful, Is.True, results.Single().Exception?.ToString());
        GetRequiredService<IFormBuilderMigrationState>().Invalidate();
        var onceRun = (await check.GetStatusAsync()).Single();
        var after = await CallEachAsync(api, endpoints, "after the plan ran");

        Assert.Multiple(() =>
        {
            Assert.That(endpoints, Is.Not.Empty);
            Assert.That(whilePending.ResultType, Is.EqualTo(StatusResultType.Warning), "the health check fails while the plan is behind");
            Assert.That(whilePending.Message, Does.Contain("migrations are pending"));
            Assert.That(onceRun.ResultType, Is.EqualTo(StatusResultType.Success), "and passes once it has run");
            Assert.That(anonymous.Status, Is.EqualTo(HttpStatusCode.Unauthorized), "the sign-in comes first");
            Assert.That(pending.Values, Is.All.EqualTo("503 migrations_pending, retry after 10 s"), string.Join("\n", pending));
            Assert.That(after.Values, Is.All.Not.StartsWith("503").And.Not.StartsWith("5"), string.Join("\n", after));
            Assert.That(after.Keys, Is.EquivalentTo(pending.Keys));
        });
    }

    // Calls every endpoint with a key that names nothing, and writes each answer to the test's output.
    private static async Task<Dictionary<string, string>> CallEachAsync(ManagementApiCaller api, List<ManagementApiEndpoint> endpoints, string when)
    {
        var answers = new Dictionary<string, string>();
        foreach (var endpoint in endpoints)
        {
            var answer = await api.CallAsync("editor", endpoint, Guid.NewGuid());
            answers[endpoint.Name] = answer.RetryAfter is { } retry ? $"{answer.Outcome}, retry after {retry.TotalSeconds} s" : answer.Outcome;
            await TestContext.Out.WriteLineAsync($"{when}: {endpoint.Name}: {answers[endpoint.Name]}");
        }

        return answers;
    }
}
