using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.ManagementApi;

/// <summary>
/// The add-on's OpenAPI document <c>jfb-ai</c> (ADR-0018) matches its committed snapshot <c>docs/api/jfb-ai.json</c>,
/// which the add-on's backoffice client is generated from; and the core's document <c>jfb</c> holds none of the add-on's
/// endpoints, so the core's client is the same with or without it. After a deliberate change, run the fixture with
/// <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails; regenerate the client and commit both.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AIOpenApiSnapshotTests : AITestBase
{
    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(AIOpenApiSnapshotTests).Assembly, "docs", "api", "jfb-ai.json");

    [Test]
    public async Task TheDocument_MatchesTheReviewedSnapshot()
    {
        var generated = await GenerateAsync("jfb-ai");
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(SnapshotPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, regenerate the client, commit both, and run again without the variable.");
        }

        Assert.That(File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n"), Is.EqualTo(generated), "the add-on's API changed: run with JFB_UPDATE_SNAPSHOTS=1, review the diff and regenerate the client");
    }

    [Test]
    public async Task TheAddOnsEndpoints_AreInItsOwnDocument_UnderItsPath_AndNotInTheCores()
    {
        var addOn = JsonNode.Parse(await GenerateAsync("jfb-ai"))!["paths"]!.AsObject().Select(path => path.Key).ToList();
        var core = JsonNode.Parse(await GenerateAsync("jfb"))!["paths"]!.AsObject().Select(path => path.Key).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(addOn, Is.Not.Empty.And.All.StartWith("/umbraco/jfb/management/api/v1/ai/"));
            Assert.That(addOn, Does.Contain("/umbraco/jfb/management/api/v1/ai/draft").And.Contain("/umbraco/jfb/management/api/v1/ai/form/{key}/translation"));
            Assert.That(core, Is.Not.Empty.And.None.Contains("/ai/"), "the core's document and client are the same with or without the add-on");
        });
    }

    private async Task<string> GenerateAsync(string name)
    {
        var document = GetRequiredService<ISwaggerProvider>().GetSwagger(name);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await document.SerializeAsync(new OpenApiJsonWriter(writer), OpenApiSpecVersion.OpenApi3_0);
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonNode.Parse(writer.ToString())!.ToJsonString(options).ReplaceLineEndings("\n") + "\n";
    }
}
