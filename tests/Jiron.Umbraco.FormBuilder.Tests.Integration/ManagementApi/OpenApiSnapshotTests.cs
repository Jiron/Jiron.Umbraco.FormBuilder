using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The management API's OpenAPI document <c>jfb</c> (ADR-0050) matches its committed snapshot <c>docs/api/jfb.json</c>,
/// which the backoffice's client is generated from, so a change to the API cannot drift from the client unnoticed.
/// After a deliberate change, run the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so
/// the diff gets reviewed; then regenerate the client (<c>npm run generate-client -w @jfb/backoffice</c>) and commit both.
/// The snapshot is only written to disk, never served, so readable escaping is safe.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OpenApiSnapshotTests : FormBuilderTestServerTestBase
{
    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(OpenApiSnapshotTests).Assembly, "docs", "api", "jfb.json");

    [Test]
    public async Task TheDocument_MatchesTheReviewedSnapshot()
    {
        var generated = await GenerateAsync();
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, regenerate the client, commit both, and run again without the variable.");
        }

        Assert.That(File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n"), Is.EqualTo(generated), "the management API changed: run with JFB_UPDATE_SNAPSHOTS=1, review the diff and regenerate the client");
    }

    [Test]
    public async Task TheDocument_HoldsOnlyTheManagementApi_UnderItsPath_WithAnOperationNameEach()
    {
        var document = JsonNode.Parse(await GenerateAsync())!;
        var paths = document["paths"]!.AsObject();
        var operations = paths.SelectMany(path => path.Value!.AsObject().Select(operation => operation.Value!["operationId"]?.GetValue<string>())).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(paths.Select(path => path.Key), Is.Not.Empty.And.All.StartWith("/umbraco/jfb/management/api/v1/"));
            Assert.That(operations, Is.All.Not.Null);
            Assert.That(operations, Is.Unique);
            Assert.That(operations, Does.Contain("FolderCreate").And.Contain("TreeRoot").And.Contain("TrashEmpty"));
            Assert.That(GetRequiredService<IEnumerable<ISchemaIdHandler>>().OfType<FormBuilderSchemaIdHandler>().Count(), Is.EqualTo(1), "its own models are named by its own handler");
        });
    }

    private async Task<string> GenerateAsync()
    {
        var document = GetRequiredService<ISwaggerProvider>().GetSwagger("jfb");
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await document.SerializeAsync(new OpenApiJsonWriter(writer), OpenApiSpecVersion.OpenApi3_0);
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonNode.Parse(writer.ToString())!.ToJsonString(options).ReplaceLineEndings("\n") + "\n";
    }
}
