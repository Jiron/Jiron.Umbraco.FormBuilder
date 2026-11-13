using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The public API's OpenAPI document <c>jfb-public</c> (ADR-0084) matches its committed snapshot
/// <c>docs/api/jfb-public.json</c>, which headless sites read, and every operation in it declares its rate-limit policy,
/// body limit and time limit (section 4.6) and the refusals the frame may answer it with. After a deliberate change, run
/// the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so the diff gets reviewed. The
/// snapshot is only written to disk, never served, so readable escaping is safe.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicOpenApiSnapshotTests : FormBuilderTestServerTestBase
{
    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(PublicOpenApiSnapshotTests).Assembly, "docs", "api", "jfb-public.json");

    [Test]
    public async Task TheDocument_MatchesTheReviewedSnapshot()
    {
        var generated = await GenerateAsync();
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, generated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n"), Is.EqualTo(generated), "the public API changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task TheDocument_HoldsOnlyThePublicApi_UnderItsPath_WithAnOperationNameEach()
    {
        var document = JsonNode.Parse(await GenerateAsync())!;
        var paths = document["paths"]!.AsObject();
        var operations = Operations(document).Select(operation => operation["operationId"]?.GetValue<string>()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(paths.Select(path => path.Key), Is.Not.Empty.And.All.StartWith("/jfb/api/v1/"));
            Assert.That(operations, Is.All.Not.Null);
            Assert.That(operations, Is.Unique);
            Assert.That(operations, Does.Contain("FormDefinition").And.Contain("FormRender").And.Contain("FormOptions").And.Contain("FormUpload").And.Contain("FormEntries").And.Contain("FormValidate").And.Contain("FormReceipt"));
            Assert.That(operations, Has.Count.EqualTo(PublicApiEndpoints.All(Services).Count), "every endpoint, once");
            Assert.That(document["components"]!["schemas"]!.AsObject().Select(schema => schema.Key), Is.All.Matches<string>(name => name.EndsWith("RequestModel", StringComparison.Ordinal) || name.EndsWith("ResponseModel", StringComparison.Ordinal) || name == "ProblemDetails"), "only its own models, never a framework type described by its properties");
        });
    }

    [Test]
    public async Task EveryOperation_DeclaresItsRateLimitPolicy_BodyLimit_AndTimeLimit_AndTheFramesRefusals()
    {
        var document = JsonNode.Parse(await GenerateAsync())!;
        var missing = new List<string>();
        foreach (var (method, operation) in document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject().Where(entry => entry.Key != "parameters").Select(entry => (entry.Key, entry.Value!))))
        {
            var id = operation["operationId"]?.GetValue<string>();
            var refusals = method is "get" or "head" ? new[] { "413", "500", "504" } : ["403", "413", "500", "504"];
            missing.AddRange(refusals.Where(status => operation["responses"]?[status]?["content"]?[FormBuilderProblemResult.MediaType] is null).Select(status => $"{id}: {status}"));
            if (operation[FormBuilderPublicApi.RateLimitExtension]?.GetValue<string>() is not { } policy || !FormBuilderRateLimitPolicies.All.Contains(policy))
            {
                missing.Add($"{id}: {FormBuilderPublicApi.RateLimitExtension}");
            }

            if (operation[FormBuilderPublicApi.BodyLimitExtension] is not JsonValue body || !body.TryGetValue<long>(out var bytes) || bytes < 0)
            {
                missing.Add($"{id}: {FormBuilderPublicApi.BodyLimitExtension}");
            }

            if (operation[FormBuilderPublicApi.TimeLimitExtension] is not JsonValue time || !time.TryGetValue<int>(out var seconds) || seconds is < 1 or > 300)
            {
                missing.Add($"{id}: {FormBuilderPublicApi.TimeLimitExtension}");
            }
        }

        // Every action declares them, the one the document leaves out included.
        var undeclared = typeof(FormBuilderPublicApiController).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(FormBuilderPublicApiController)) && !type.IsAbstract)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<NonActionAttribute>() is null)
            .Where(method => method.GetCustomAttribute<FormBuilderRateLimitAttribute>() is null || method.GetCustomAttribute<FormBuilderBodyLimitAttribute>() is null || method.GetCustomAttribute<FormBuilderTimeLimitAttribute>() is null)
            .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Operations(document), Is.Not.Empty);
            Assert.That(missing, Is.Empty, "every operation in the document");
            Assert.That(undeclared, Is.Empty, "every action");
        });
    }

    private static IEnumerable<JsonNode> Operations(JsonNode document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject().Where(entry => entry.Key != "parameters").Select(entry => entry.Value!));

    private async Task<string> GenerateAsync()
    {
        var document = await Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(FormBuilderPublicApi.ApiName).GetOpenApiDocumentAsync();
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        // As Umbraco 18 serves it: OpenAPI 3.1 (ADR-0250).
        await document.SerializeAsync(new OpenApiJsonWriter(writer), OpenApiSpecVersion.OpenApi3_1);
        var options = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return JsonNode.Parse(writer.ToString())!.ToJsonString(options).ReplaceLineEndings("\n") + "\n";
    }
}
