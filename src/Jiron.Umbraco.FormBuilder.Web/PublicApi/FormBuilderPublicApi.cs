using System.Globalization;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.OpenApi;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The public API (section 4.6, ADR-0012, ADR-0084): anonymous, under <c>{JironFormBuilder:PublicApi:BasePath}/v1</c>
/// (<c>/jfb/api/v1</c> by default), never under <c>/umbraco</c>, with the OpenAPI document <c>jfb-public</c>.
/// </summary>
internal static class FormBuilderPublicApi
{
    /// <summary>The OpenAPI document's name.</summary>
    public const string ApiName = "jfb-public";

    /// <summary>The version's segment after the base path.</summary>
    public const string Version = "v1";

    /// <summary>The extension of an operation in the document that names its rate-limit policy.</summary>
    public const string RateLimitExtension = "x-jfb-rate-limit";

    /// <summary>The extension of an operation in the document that gives its body limit in bytes.</summary>
    public const string BodyLimitExtension = "x-jfb-body-limit";

    /// <summary>The extension of an operation in the document that gives its time limit in seconds.</summary>
    public const string TimeLimitExtension = "x-jfb-time-limit";
}

/// <summary>
/// The base of every controller of the public API (ADR-0084): anonymous, in the document <c>jfb-public</c>, below the
/// base path that <see cref="FormBuilderPublicApiRoutes"/> puts before each controller's own route, and behind the
/// filter that applies each action's declarations. Every action declares its rate-limit policy, body limit and time
/// limit (<see cref="FormBuilderRateLimitAttribute"/>, <see cref="FormBuilderBodyLimitAttribute"/>,
/// <see cref="FormBuilderTimeLimitAttribute"/>), and a test fails for one that does not. Each response names its media type,
/// <c>application/json</c> or, for a refusal, <c>application/problem+json</c>. The controllers are internal; the
/// public API's registration adds them to MVC.
/// </summary>
[MapToApi(FormBuilderPublicApi.ApiName)]
[AllowAnonymous]
[ServiceFilter(typeof(FormBuilderPublicApiFilter))]
internal abstract class FormBuilderPublicApiController : ControllerBase;

/// <summary>The rate-limit policies of the public API (ADR-0012): which partitions count an endpoint's requests.</summary>
internal static class FormBuilderRateLimitPolicies
{
    /// <summary>Reading a form, its markup and its options.</summary>
    public const string Read = "read";

    /// <summary>Asking for a challenge.</summary>
    public const string Challenge = "challenge";

    /// <summary>Validating, submitting, saving a draft and asking for a receipt.</summary>
    public const string Submit = "submit";

    /// <summary>Uploading a file.</summary>
    public const string Upload = "upload";

    /// <summary>The insights beacon.</summary>
    public const string Events = "events";

    /// <summary>Confirming an address.</summary>
    public const string Verify = "verify";

    /// <summary>Every policy.</summary>
    public static readonly IReadOnlyList<string> All = [Read, Challenge, Submit, Upload, Events, Verify];
}

/// <summary>
/// The rate-limit policy an endpoint's requests count against (ADR-0012). The limits apply from step 7.1 on, through
/// Jiron Form Builder's own middleware; the declaration is there from the endpoint's first day.
/// </summary>
/// <param name="policy">One of <see cref="FormBuilderRateLimitPolicies"/>.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderRateLimitAttribute(string policy) : Attribute
{
    /// <summary>The policy.</summary>
    public string Policy { get; } = FormBuilderRateLimitPolicies.All.Contains(policy) ? policy : throw new ArgumentOutOfRangeException(nameof(policy), policy, "Not a rate-limit policy.");
}

/// <summary>
/// The most bytes an endpoint's body may hold (ADR-0012): a declared length above it is refused before a byte is read,
/// and a body without one is counted while it is read, both with <c>413 payload_too_large</c>. 0 takes no body.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderBodyLimitAttribute : Attribute
{
    /// <summary>
    /// The largest upload the host takes: <c>Files:MaxFileBytes</c> and what a request carries besides its file
    /// (ADR-0087), which the upload endpoint lowers to its field's limit.
    /// </summary>
    public const string LargestUpload = "largestUpload";

    /// <summary>A limit in bytes.</summary>
    /// <param name="bytes">The limit, 0 or more.</param>
    public FormBuilderBodyLimitAttribute(long bytes) =>
        Bytes = bytes >= 0 ? bytes : throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "A body limit is 0 or more bytes.");

    /// <summary>A limit that the host's configuration gives (<see cref="BytesFor"/>).</summary>
    /// <param name="limit"><see cref="LargestUpload"/>.</param>
    public FormBuilderBodyLimitAttribute(string limit)
    {
        if (limit != LargestUpload)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Not a limit the host gives.");
        }
    }

    /// <summary>The limit in bytes; null for one that the host's configuration gives.</summary>
    public long? Bytes { get; }

    /// <summary>The limit in bytes on a host with these limits for files.</summary>
    /// <param name="host">The host's limits for files.</param>
    public long BytesFor(FieldHostLimits host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return Bytes ?? host.MaxFileBytes + FileUploads.OverheadBytes;
    }
}



/// <summary>
/// An action that reads its <c>multipart/form-data</c> body itself, as it streams (ADR-0087): an optional
/// <c>challenge</c> part, then the <c>file</c> part. The document describes the body; the public API's form feature
/// (<see cref="NoFormFeature"/>) keeps every other part of the site from reading or buffering it.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderUploadBodyAttribute : Attribute
{
    /// <summary>The media type of the body.</summary>
    public const string MediaType = "multipart/form-data";

    /// <summary>The name of the optional first part, the challenge.</summary>
    public const string ChallengePart = "challenge";

    /// <summary>The name of the part that holds the file.</summary>
    public const string FilePart = "file";
}

/// <summary>
/// How long an endpoint may take (ADR-0012), on the site's <see cref="TimeProvider"/>: past it, the request is
/// cancelled and answers <c>504 timeout</c>, unless the visitor left first.
/// </summary>
/// <param name="seconds">The limit, 1 to 300 seconds.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderTimeLimitAttribute(int seconds) : Attribute
{
    /// <summary>The limit.</summary>
    public TimeSpan Limit { get; } = seconds is >= 1 and <= 300 ? TimeSpan.FromSeconds(seconds) : throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A time limit is 1 to 300 seconds.");
}

/// <summary>
/// The document <c>jfb-public</c>, with each operation's declarations as extensions, so the test that lists the
/// endpoints from the document finds a missing one. It is registered here only, because Umbraco 18 replaces Swashbuckle
/// (ROADMAP 3.4, Phase 19).
/// </summary>
internal sealed class ConfigureFormBuilderPublicApiSwagger : IConfigureOptions<SwaggerGenOptions>
{
    /// <inheritdoc />
    public void Configure(SwaggerGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SwaggerDoc(FormBuilderPublicApi.ApiName, new OpenApiInfo
        {
            Title = "Jiron Form Builder public API",
            Version = "1.0",
            Description = "What visitors' browsers and headless sites call: forms, their options, uploads and entries. Anonymous; errors are ProblemDetails with a stable code.",
        });
        options.OperationFilter<FormBuilderPublicDeclarations>();
        options.DocumentFilter<FormBuilderPublicDocument>();
    }
}

/// <summary>Leaves the backoffice's sign-in out of the document <c>jfb-public</c>: the public API is anonymous.</summary>
internal sealed class FormBuilderPublicDocument : IDocumentFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);
        ArgumentNullException.ThrowIfNull(context);
        if (context.DocumentName == FormBuilderPublicApi.ApiName)
        {
            swaggerDoc.Components?.SecuritySchemes?.Clear();
            swaggerDoc.Security?.Clear();
        }
    }
}

/// <summary>
/// Writes an operation's declarations into the document <c>jfb-public</c> as extensions, the body of an upload, and the
/// refusals the frame answers for every operation: <c>413 payload_too_large</c>, <c>500 server_error</c> and
/// <c>504 timeout</c>, and <c>403 origin_denied</c> for one a browser on another origin may not send.
/// </summary>
/// <param name="host">The host's limits for files, which give the largest upload.</param>
internal sealed class FormBuilderPublicDeclarations(FieldHostLimits host) : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        if (context.DocumentName != FormBuilderPublicApi.ApiName)
        {
            return;
        }

        var method = context.MethodInfo;
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        if (method.GetCustomAttributes(typeof(FormBuilderRateLimitAttribute), false).FirstOrDefault() is FormBuilderRateLimitAttribute rate)
        {
            operation.Extensions[FormBuilderPublicApi.RateLimitExtension] = new JsonNodeExtension(JsonValue.Create(rate.Policy));
        }

        if (method.GetCustomAttributes(typeof(FormBuilderBodyLimitAttribute), false).FirstOrDefault() is FormBuilderBodyLimitAttribute body)
        {
            operation.Extensions[FormBuilderPublicApi.BodyLimitExtension] = new JsonNodeExtension(JsonValue.Create(body.BytesFor(host)));
        }

        if (method.GetCustomAttributes(typeof(FormBuilderUploadBodyAttribute), false).Length > 0)
        {
            operation.RequestBody = UploadBody();
        }

        if (method.GetCustomAttributes(typeof(FormBuilderTimeLimitAttribute), false).FirstOrDefault() is FormBuilderTimeLimitAttribute time)
        {
            operation.Extensions[FormBuilderPublicApi.TimeLimitExtension] = new JsonNodeExtension(JsonValue.Create((int)time.Limit.TotalSeconds));
        }

        var problem = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository);
        operation.Responses ??= [];
        if (!HttpMethods.IsGet(context.ApiDescription.HttpMethod ?? string.Empty) && !HttpMethods.IsHead(context.ApiDescription.HttpMethod ?? string.Empty))
        {
            Refusal(operation.Responses, problem, StatusCodes.Status403Forbidden, "Forbidden: a browser on an origin the site does not allow sent it (origin_denied).");
        }

        Refusal(operation.Responses, problem, StatusCodes.Status413PayloadTooLarge, "Payload Too Large: the body is longer than the endpoint takes (payload_too_large).");
        Refusal(operation.Responses, problem, StatusCodes.Status500InternalServerError, "Internal Server Error: something failed unexpectedly (server_error).");
        Refusal(operation.Responses, problem, StatusCodes.Status504GatewayTimeout, "Gateway Timeout: the endpoint did not answer within its time limit (timeout).");
    }

    // The parts of an upload: the challenge, which 7.1 requires, and the file.
    private static OpenApiRequestBody UploadBody() => new()
    {
        Required = true,
        Description = "An optional challenge part, then the file part with the file's name. Nothing may follow the file.",
        Content = new Dictionary<string, OpenApiMediaType>
        {
            [FormBuilderUploadBodyAttribute.MediaType] = new()
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Required = new HashSet<string> { FormBuilderUploadBodyAttribute.FilePart },
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        [FormBuilderUploadBodyAttribute.ChallengePart] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Description = "The challenge, at most 4 KB, first; not a file.",
                        },
                        [FormBuilderUploadBodyAttribute.FilePart] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Format = "binary",
                            Description = "The file, with its name.",
                        },
                    },
                },
            },
        },
    };

    // A refusal of the frame, unless the action describes the status itself.
    private static void Refusal(OpenApiResponses responses, IOpenApiSchema problem, int status, string description) =>
        responses.TryAdd(status.ToString(CultureInfo.InvariantCulture), new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType> { [FormBuilderProblemResult.MediaType] = new() { Schema = problem } },
        });
}

/// <summary>
/// Names the document's operations after their controller and action (<c>FormDefinition</c>), as the management API
/// names its own; Umbraco's own handler names only its own API's operations.
/// </summary>
internal sealed class FormBuilderPublicOperationIdHandler : IOperationIdHandler
{
    /// <inheritdoc />
    public bool CanHandle(ApiDescription apiDescription) =>
        apiDescription?.ActionDescriptor is ControllerActionDescriptor action && typeof(FormBuilderPublicApiController).IsAssignableFrom(action.ControllerTypeInfo);

    /// <inheritdoc />
    public string Handle(ApiDescription apiDescription)
    {
        ArgumentNullException.ThrowIfNull(apiDescription);
        var action = (ControllerActionDescriptor)apiDescription.ActionDescriptor;
        return string.Create(CultureInfo.InvariantCulture, $"{action.ControllerName}{action.ActionName}");
    }
}
