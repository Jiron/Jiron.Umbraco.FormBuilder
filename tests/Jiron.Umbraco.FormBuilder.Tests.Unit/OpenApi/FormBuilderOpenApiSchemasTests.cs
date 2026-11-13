using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.OpenApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OpenApi;

/// <summary>
/// The rules that keep Jiron Form Builder's OpenAPI documents on Umbraco 18 describing what the API sends, so the
/// generated clients keep their types (ROADMAP 3.4, Phase 19, ADR-0250): its own models named as Umbraco names its own,
/// files and form files as binary strings wherever they are referred to, JSON values as any value, validation problems
/// with their errors, numbers as numbers, nullable properties not required, responses with only the media types that
/// have a body, and each document registered once with its version and description.
/// </summary>
public sealed class FormBuilderOpenApiSchemasTests
{
    [Test]
    public void FormFiles_AreBinaryStringsEverywhere_AndTheirSchemaGoes()
    {
        var upload = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema> { ["file"] = FormFile(), ["note"] = new OpenApiSchema { Type = JsonSchemaType.String } },
        };
        var many = new OpenApiSchema { Type = JsonSchemaType.Array, Items = FormFile() };
        var model = new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["attachment"] = FormFile() } };
        var composed = new OpenApiSchema { AllOf = [FormFile()] };
        var document = Document(
            new Dictionary<string, IOpenApiSchema> { [nameof(IFormFile)] = new OpenApiSchema { Type = JsonSchemaType.Object }, ["UploadModel"] = model },
            upload,
            many,
            composed);

        FormBuilderOpenApiSchemas.InlineFormFiles(document);

        Assert.Multiple(() =>
        {
            Assert.That(document.Components!.Schemas!.ContainsKey(nameof(IFormFile)), Is.False, "nothing refers to it any more");
            AssertBinary(upload.Properties!["file"], "a form's field");
            Assert.That(upload.Properties!["note"].Type, Is.EqualTo(JsonSchemaType.String), "other fields stay");
            AssertBinary(many.Items!, "an array's items");
            AssertBinary(model.Properties!["attachment"], "a component's property");
            AssertBinary(composed.AllOf![0], "a composition");
        });
    }

    [Test]
    public void FormFiles_LeaveADocumentWithoutThemAsItIs()
    {
        var field = new OpenApiSchema { Type = JsonSchemaType.String };
        var body = new OpenApiSchema { Type = JsonSchemaType.Object, Properties = new Dictionary<string, IOpenApiSchema> { ["name"] = field } };
        var document = Document(new Dictionary<string, IOpenApiSchema> { ["Other"] = new OpenApiSchema { Type = JsonSchemaType.Object } }, body);

        FormBuilderOpenApiSchemas.InlineFormFiles(document);

        Assert.Multiple(() =>
        {
            Assert.That(document.Components!.Schemas!.Keys, Is.EquivalentTo(new[] { "Other" }));
            Assert.That(body.Properties!["name"], Is.SameAs(field));
        });
    }

    [Test]
    public void AValidationProblem_HasItsFieldsAndItsErrors()
    {
        var schema = new OpenApiSchema();

        FormBuilderOpenApiSchemas.ValidationProblem(schema);

        Assert.Multiple(() =>
        {
            Assert.That(schema.Type, Is.EqualTo(JsonSchemaType.Object));
            Assert.That(schema.Properties!.Keys, Is.EquivalentTo(new[] { "type", "title", "status", "detail", "instance", "errors" }));
            Assert.That(schema.Properties!["status"].Type, Is.EqualTo(JsonSchemaType.Integer | JsonSchemaType.Null));
            Assert.That(schema.Properties!["errors"].AdditionalProperties!.Type, Is.EqualTo(JsonSchemaType.Array));
            Assert.That(schema.Properties!["errors"].AdditionalProperties!.Items!.Type, Is.EqualTo(JsonSchemaType.String));
            Assert.That(schema.Required, Is.EquivalentTo(new[] { "errors" }));
            Assert.That(schema.AdditionalPropertiesAllowed, Is.True, "a problem may carry extensions");
        });
    }

    [Test]
    public void ANumber_IsANumber_WithoutTheStringAndItsPattern()
    {
        var integer = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.String, Format = "int32", Pattern = "^-?(?:0|[1-9]\\d*)$" };
        var nullable = new OpenApiSchema { Type = JsonSchemaType.Number | JsonSchemaType.String | JsonSchemaType.Null, Pattern = "x" };
        var text = new OpenApiSchema { Type = JsonSchemaType.String, Pattern = "^[a-z]+$" };

        FormBuilderOpenApiSchemas.Numbers(integer);
        FormBuilderOpenApiSchemas.Numbers(nullable);
        FormBuilderOpenApiSchemas.Numbers(text);

        Assert.Multiple(() =>
        {
            Assert.That(integer.Type, Is.EqualTo(JsonSchemaType.Integer));
            Assert.That(integer.Pattern, Is.Null);
            Assert.That(integer.Format, Is.EqualTo("int32"));
            Assert.That(nullable.Type, Is.EqualTo(JsonSchemaType.Number | JsonSchemaType.Null));
            Assert.That(text.Type, Is.EqualTo(JsonSchemaType.String), "a string stays a string");
            Assert.That(text.Pattern, Is.EqualTo("^[a-z]+$"), "with its own pattern");
        });
    }

    [Test]
    public void AProperty_ThatMayBeNull_IsNotRequired_UnlessTheModelRequiresIt()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
        var type = options.GetTypeInfo(typeof(Sample));
        var schema = new OpenApiSchema { Type = JsonSchemaType.Object, Required = new HashSet<string> { "name", "note", "count", "limit", "kept", "keptToo" } };

        FormBuilderOpenApiSchemas.Optional(schema, type);

        Assert.That(schema.Required, Is.EquivalentTo(new[] { "name", "count", "kept", "keptToo" }), "the nullable reference and the nullable value go, a required member stays");
    }

    [Test]
    public void AResponse_ListsOnlyTheMediaTypesWithABody()
    {
        var json = new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.Object } };
        var operation = new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = json, ["text/plain"] = new OpenApiMediaType() } },
                ["304"] = new OpenApiResponse { Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new OpenApiMediaType() } },
            },
        };

        FormBuilderOpenApiSchemas.WithBodies(operation);

        Assert.Multiple(() =>
        {
            Assert.That(operation.Responses["200"].Content!.Keys, Is.EquivalentTo(new[] { "application/json" }));
            Assert.That(operation.Responses["304"].Content, Is.Empty);
        });
    }

    [TestCase(typeof(FileContentResult), true)]
    [TestCase(typeof(FileStreamResult), true)]
    [TestCase(typeof(Stream), true)]
    [TestCase(typeof(MemoryStream), true)]
    [TestCase(typeof(IFormFile), true)]
    [TestCase(typeof(FormFile), true)]
    [TestCase(typeof(string), false)]
    [TestCase(typeof(byte[]), false)]
    public void AFile_IsWhatTheApiSendsOrTakesAsBytes(Type type, bool file) => Assert.That(FormBuilderOpenApiSchemas.IsFile(type), Is.EqualTo(file));

    [TestCase(typeof(JsonElement), true)]
    [TestCase(typeof(JsonElement?), true)]
    [TestCase(typeof(JsonDocument), false)]
    [TestCase(typeof(string), false)]
    public void AJsonValue_IsAJsonElement(Type type, bool json) => Assert.That(FormBuilderOpenApiSchemas.IsJson(type), Is.EqualTo(json));

    [Test]
    public void AModel_IsNamedAsUmbracoNamesItsOwn_AFileOrAJsonValueGoesInline_AndAnythingElseKeepsItsName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderOpenApiSchemas.ReferenceId(typeof(FormBuilderOpenApiSchemasTests), () => "theirs"), Is.EqualTo("FormBuilderOpenApiSchemasTestsModel"), "Umbraco ends each name in Model");
            Assert.That(FormBuilderOpenApiSchemas.ReferenceId(typeof(ProblemDetails), () => "theirs"), Is.EqualTo("theirs"));
            Assert.That(FormBuilderOpenApiSchemas.ReferenceId(typeof(JsonElement?), () => "theirs"), Is.Null);
            Assert.That(FormBuilderOpenApiSchemas.ReferenceId(typeof(MemoryStream), () => "theirs"), Is.Null);
        });
    }

    [TestCase(typeof(FormBuilderOpenApiSchemasTests), true)]
    [TestCase(typeof(FormBuilderOpenApiDocument), true)]
    [TestCase(typeof(ProblemDetails), false)]
    [TestCase(typeof(string), false)]
    public void OwnTypes_AreInJironFormBuildersNamespaceOrBelow(Type type, bool own) => Assert.That(FormBuilderOpenApiSchemas.IsOwn(type), Is.EqualTo(own));

    [Test]
    public void ADocument_IsClaimedOncePerName()
    {
        var services = new ServiceCollection();

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderOpenApiDocuments.Claim(services, FormBuilderManagementApi.ApiName), Is.True);
            Assert.That(FormBuilderOpenApiDocuments.Claim(services, FormBuilderManagementApi.ApiName), Is.False, "a second registration of the same name");
            Assert.That(FormBuilderOpenApiDocuments.Claim(services, "jfb-public"), Is.True, "another name");
            Assert.That(services, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task ADocument_HasItsVersionAndDescription()
    {
        var document = new OpenApiDocument { Info = new OpenApiInfo { Title = "Kept" } };

        await FormBuilderOpenApiDocuments.Describe("1.0", "What it holds.")(document, null!, CancellationToken.None);

        Assert.That((document.Info.Title, document.Info.Version, document.Info.Description), Is.EqualTo(("Kept", "1.0", "What it holds.")));
    }

    private static OpenApiSchemaReference FormFile() => new(nameof(IFormFile));

    private static void AssertBinary(IOpenApiSchema schema, string where) =>
        Assert.That(schema is OpenApiSchema { Type: JsonSchemaType.String, Format: "binary" }, Is.True, $"{where} is a binary string");

    private static OpenApiDocument Document(Dictionary<string, IOpenApiSchema> components, params OpenApiSchema[] bodies)
    {
        var paths = new OpenApiPaths();
        for (var index = 0; index < bodies.Length; index++)
        {
            var operation = new OpenApiOperation
            {
                RequestBody = new OpenApiRequestBody { Content = new Dictionary<string, OpenApiMediaType> { ["multipart/form-data"] = new() { Schema = bodies[index] } } },
            };
            paths[$"/upload/{index}"] = new OpenApiPathItem { Operations = new Dictionary<HttpMethod, OpenApiOperation> { [HttpMethod.Post] = operation } };
        }

        return new OpenApiDocument { Paths = paths, Components = new OpenApiComponents { Schemas = components } };
    }

    private sealed record Sample(string Name, string? Note, int Count, int? Limit)
    {
        public required string? Kept { get; init; }

        public required int? KeptToo { get; init; }
    }
}
