using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The tests a user runs in an hour (16.1's review): past <see cref="TestLimits.PerHour"/>, each endpoint that sends a test,
/// a test mail of the settings, of an email layout or of the summary, or an action's test, answers <c>429 tests_limited</c>
/// with <c>Retry-After</c>, and sends nothing; another user's tests go on.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TestLimitApiTests : ManagementApiTestBase
{
    [Test]
    public async Task PastTheTestsOfAnHour_EveryTestEndpointAnswers429_WhileAnotherUsersTestsGoOn()
    {
        var form = await FormAsync("Contact");
        await CallAsync("admin", HttpMethod.Get, "me");
        await CallAsync("designer", HttpMethod.Get, "me");
        var users = GetRequiredService<IUserService>();
        var admin = users.GetByEmail("api-admin@jfb.test")!.Key;
        var limits = GetRequiredService<TestLimits>();
        for (var i = 0; i < TestLimits.PerHour; i++)
        {
            Assert.That(limits.Refusal(admin, new DefaultHttpContext().Response), Is.Null);
        }

        var refused = new (string What, ApiAnswer Answer)[]
        {
            ("the settings' test mail", await CallAsync("admin", HttpMethod.Post, "email/test", new { })),
            ("a layout's test mail", await CallAsync("admin", HttpMethod.Post, $"email-layout/{Guid.NewGuid()}/test", new { })),
            ("the summary's test", await CallAsync("admin", HttpMethod.Post, "entries/digest/test", new { frequency = "daily" })),
            ("an action's test", await CallAsync("admin", HttpMethod.Post, $"form/{form}/action/{Guid.NewGuid()}/test", new { confirmed = false })),
        };
        var designers = await CallAsync("designer", HttpMethod.Post, $"form/{form}/action/{Guid.NewGuid()}/test", new { confirmed = false });

        Assert.Multiple(() =>
        {
            foreach (var (what, answer) in refused)
            {
                Assert.That(answer.Outcome, Is.EqualTo("429 tests_limited"), $"{what}: {answer}");
                Assert.That(answer.RetryAfter, Is.GreaterThan(TimeSpan.Zero), what);
            }

            Assert.That(designers.Status, Is.Not.EqualTo(System.Net.HttpStatusCode.TooManyRequests), "another user's tests go on");
        });
    }
}
