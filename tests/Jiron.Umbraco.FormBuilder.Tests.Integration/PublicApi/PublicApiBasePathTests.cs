using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A site that puts the public API under its own base path (ADR-0084): every endpoint and the catch-all move there,
/// matched in any letter case, and the frame applies there.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicApiBasePathTests : PublicApiTestBase
{
    private const string BasePath = "/site/forms-api";

    [Test]
    public async Task TheApi_AnswersBelowTheConfiguredBasePath_AndNowhereElse()
    {
        var key = await PublishAsync();

        using var moved = await GetAsync($"{BasePath}/v1/forms/{key}");
        using var otherCase = await GetAsync($"/SITE/Forms-API/v1/forms/{key}");
        using var unknown = await GetAsync($"{BasePath}/v1/nothing");
        var routes = PublicApiEndpoints.Routes(Services);

        Assert.Multiple(async () =>
        {
            Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(otherCase.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Header(otherCase, "Vary"), Does.Contain("Origin"), "the frame matches the base path in any letter case too");
            Assert.That(await RefusalAsync(unknown), Is.EqualTo("404 not_found"));
            Assert.That(routes, Does.Contain("site/forms-api/v1/{**path}").And.Contain("site/forms-api/v1/forms/{keyOrAlias}"));
            Assert.That(routes, Is.All.StartWith("site/forms-api/v1/"));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration[FormBuilderOptions.SectionName + ":PublicApi:BasePath"] = BasePath;
    }
}
