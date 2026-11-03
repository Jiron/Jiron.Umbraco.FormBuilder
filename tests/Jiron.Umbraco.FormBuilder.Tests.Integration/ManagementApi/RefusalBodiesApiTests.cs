using System.Net.Http.Headers;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Every kind of refusal of the management API keeps its ProblemDetails body through Umbraco's pipeline (ADR-0050,
/// ROADMAP 3.3: Umbraco's error handling can drop a body and keep the status, and its validation answers drop the code): the
/// status, the code, a title for editors and what the kind adds, each from a real call.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RefusalBodiesApiTests : ManagementApiTestBase
{
    /// <summary>The name a test handler refuses folders by.</summary>
    public const string RefusedName = "Not here";

    [Test]
    public async Task EachKindOfRefusal_AnswersWithItsProblem_AndItsCode()
    {
        var live = await FormAsync("Live");
        var liveVersion = (await GetRequiredService<IFormBuilderFormService>().GetAsync(live))!.ConcurrencyVersion;
        var unreadable = new StringContent("{ \"name\": ", Encoding.UTF8, new MediaTypeHeaderValue("application/json"));

        var answers = new Dictionary<string, ApiAnswer>
        {
            ["400 invalid_value"] = await CallAsync("admin", HttpMethod.Post, "folder", new { name = "   " }),
            ["400 invalid_request"] = await CallAsync("admin", HttpMethod.Post, "folder", unreadable),
            ["403 no_section"] = await CallAsync("outsider", HttpMethod.Get, "tree/root"),
            ["403 forbidden"] = await CallAsync("viewer", HttpMethod.Post, "folder", new { name = "Plans" }),
            ["404 folder_not_found"] = await CallAsync("admin", HttpMethod.Get, $"folder/{Guid.NewGuid()}"),
            ["409 not_trashed"] = await CallAsync("admin", HttpMethod.Delete, $"form/{live}?concurrencyVersion={liveVersion}"),
            ["409 cancelled"] = await CallAsync("admin", HttpMethod.Post, "folder", new { name = RefusedName }),
        };

        Assert.Multiple(() =>
        {
            foreach (var (expected, answer) in answers)
            {
                Assert.That(answer.Outcome, Is.EqualTo(expected), answer.ToString());
                Assert.That(answer.MediaType, Is.EqualTo("application/problem+json"), expected);
                Assert.That(answer.Body?["status"]?.GetValue<int>(), Is.EqualTo((int)answer.Status), $"{expected}: the body's status");
                Assert.That(answer.Body?["title"]?.GetValue<string>(), Is.Not.Empty.And.EndsWith("."), $"{expected}: a sentence for editors");
            }

            Assert.That(answers["400 invalid_value"].Body!["errors"]![0]!.ToJsonString(), Is.EqualTo("""{"code":"invalid_value","path":"name","message":"A folder needs a name that is not only white space."}"""));
            Assert.That(answers["400 invalid_request"].Body!["errors"]!.AsArray(), Is.Not.Empty);
            Assert.That(answers["403 forbidden"].Body!["permission"]!.GetValue<string>(), Is.EqualTo("design"));
            Assert.That(answers["409 cancelled"].Body!["messages"]!.AsArray().Select(message => message!.GetValue<string>()), Is.EqualTo(new[] { "Folders are not made with that name here." }));
            Assert.That(answers["409 cancelled"].Body!["detail"]!.GetValue<string>(), Is.EqualTo("Folders are not made with that name here."));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.AddNotificationAsyncHandler<FolderCreatingNotification, RefuseFoldersNamedNotHere>();
    }

    /// <summary>Cancels the creation of folders named <see cref="RefusedName"/>, with a message.</summary>
    internal sealed class RefuseFoldersNamedNotHere : INotificationAsyncHandler<FolderCreatingNotification>
    {
        /// <inheritdoc />
        public Task HandleAsync(FolderCreatingNotification notification, CancellationToken cancellationToken)
        {
            if (notification.Name == RefusedName)
            {
                notification.CancelOperation(new EventMessage("Test", "Folders are not made with that name here.", EventMessageType.Error));
            }

            return Task.CompletedTask;
        }
    }
}
