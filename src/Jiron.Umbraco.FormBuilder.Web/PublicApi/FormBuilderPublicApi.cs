using System.Globalization;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.OpenApi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Api.Common.OpenApi;
using Umbraco.Cms.Core.DependencyInjection;

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

    /// <summary>The extension of an operation in the document that takes the preview frame's requests (ADR-0203).</summary>
    public const string PreviewExtension = "x-jfb-preview";
}

/// <summary>
/// The base of every controller of the public API (ADR-0084): anonymous, in the document <c>jfb-public</c>, below the
/// base path that <see cref="FormBuilderPublicApiRoutes"/> puts before each controller's own route, and behind the
/// filter that applies each action's declarations. Every action declares its rate-limit policy, body limit and time
/// limit (<see cref="FormBuilderRateLimitAttribute"/>, <see cref="FormBuilderBodyLimitAttribute"/>,
/// <see cref="FormBuilderTimeLimitAttribute"/>), and a test fails for one that does not. Each response names its media type,
/// <c>application/json</c> (<c>text/html</c> for a form's markup) or, for a refusal, <c>application/problem+json</c>.
/// The controllers are internal; the public API's registration adds them to MVC. <c>[ApiExplorerSettings]</c> lists them in the
/// document: Umbraco 18's generator describes only the actions ASP.NET Core's API explorer lists, which without
/// <c>[ApiController]</c> (and its automatic answers, which the public API does not use) are those that ask (ADR-0250); the
/// actions that stay out of the document say so themselves.
/// </summary>
[MapToApi(FormBuilderPublicApi.ApiName)]
[ApiExplorerSettings]
[AllowAnonymous]
[ServiceFilter(typeof(FormBuilderPublicApiFilter))]
internal abstract class FormBuilderPublicApiController : ControllerBase;

/// <summary>The rate-limit policies of the public API (ADR-0012): which partitions count an endpoint's requests.</summary>
internal static class FormBuilderRateLimitPolicies
{
    /// <summary>
    /// The client a request counts for in the per-client partitions: the server key that signed it
    /// (<c>key:&lt;name&gt;</c>, ADR-0126), otherwise its address (<c>address:&lt;ip&gt;</c>, from the connection only,
    /// an IPv6 address as its /64 network, ADR-0012).
    /// </summary>
    public static string ClientOf(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return ServerKeyRequest.NameOf(http) is { } key
            ? "key:" + key
            : "address:" + RateLimiting.RateLimitClients.KeyOf(http.Connection.RemoteIpAddress);
    }

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
/// The rate-limit policy an endpoint's requests count against (ADR-0012), which Jiron Form Builder's own middleware
/// applies after routing.
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
/// An action that takes the preview frame's requests (ADR-0203): with a ticket that holds for the form its route names
/// by its key, it reads the version the ticket names, as though it were published, in place of the published one. Every
/// other action answers a preview's request with <c>401 preview_ticket_invalid</c>. The document marks it with
/// <see cref="FormBuilderPublicApi.PreviewExtension"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderPreviewAttribute : Attribute;

/// <summary>
/// An action that reads its <c>multipart/form-data</c> body itself, as it streams (ADR-0087): a
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
/// An action that reads its JSON body itself, strictly (ADR-0088): the document describes the body with this model,
/// and the public API's form feature keeps MVC's own binding from reading it.
/// </summary>
/// <param name="model">The model that describes the body.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderJsonBodyAttribute(Type model) : Attribute
{
    /// <summary>The media type of the body.</summary>
    public const string MediaType = "application/json";

    /// <summary>The model that describes the body.</summary>
    public Type Model { get; } = model ?? throw new ArgumentNullException(nameof(model));
}

/// <summary>
/// An action that reads a beacon's body itself (ADR-0211): JSON in any content type, as <c>navigator.sendBeacon</c> sends
/// it as <c>text/plain</c>. The document describes the body with this model under both media types.
/// </summary>
/// <param name="model">The model that describes the body.</param>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
internal sealed class FormBuilderBeaconBodyAttribute(Type model) : Attribute
{
    /// <summary>The media type a beacon of text sends.</summary>
    public const string TextMediaType = "text/plain";

    /// <summary>The model that describes the body.</summary>
    public Type Model { get; } = model ?? throw new ArgumentNullException(nameof(model));
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
/// The document <c>jfb-public</c> on Umbraco 18 (ADR-0250): Umbraco's document builder over Microsoft.AspNetCore.OpenApi,
/// served at <c>/umbraco/openapi/jfb-public.json</c> outside production, without the backoffice's sign-in (the public API
/// is anonymous), and with each operation's declarations as extensions, so the test that lists the endpoints from the
/// document finds a missing one.
/// </summary>
internal static class FormBuilderPublicApiDocument
{
    /// <summary>Registers the document once.</summary>
    /// <param name="builder">The Umbraco builder.</param>
    public static IUmbracoBuilder AddFormBuilderPublicApiDocument(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!FormBuilderOpenApiDocuments.Claim(builder.Services, FormBuilderPublicApi.ApiName))
        {
            return builder;
        }

        return builder.AddBackOfficeOpenApiDocument(FormBuilderPublicApi.ApiName, document => document
            .WithTitle("Jiron Form Builder public API")
            .ConfigureOpenApiOptions(options =>
            {
                options.AddDocumentTransformer(FormBuilderOpenApiDocuments.Describe(
                    "1.0",
                    "What visitors' browsers and headless sites call: forms, their options, uploads, the checks of a page, entries and their receipts. Anonymous; errors are ProblemDetails with a stable code."));
                options.AddOperationTransformer<FormBuilderOperationIds>();
                options.AddOperationTransformer<FormBuilderPublicDeclarations>();
                FormBuilderOpenApiSchemas.Apply(options);
            }));
    }
}

/// <summary>
/// Writes an operation's declarations into the document <c>jfb-public</c> as extensions, the body of an upload or of a
/// JSON action, and the refusals the frame answers for every operation: <c>401 preview_ticket_invalid</c>,
/// <c>413 payload_too_large</c>, <c>429 rate_limited</c>, <c>500 server_error</c> and <c>504 timeout</c>, and
/// <c>403 origin_denied</c> for one a browser on another origin may not send.
/// </summary>
/// <param name="host">The host's limits for files, which give the largest upload.</param>
internal sealed class FormBuilderPublicDeclarations(FieldHostLimits host) : IOpenApiOperationTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Description.ActionDescriptor is not ControllerActionDescriptor action || !typeof(FormBuilderPublicApiController).IsAssignableFrom(action.ControllerTypeInfo))
        {
            return;
        }

        var method = action.MethodInfo;
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

        if (method.GetCustomAttributes(typeof(FormBuilderJsonBodyAttribute), false).FirstOrDefault() is FormBuilderJsonBodyAttribute json)
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [FormBuilderJsonBodyAttribute.MediaType] = new() { Schema = await ReferenceAsync(context, json.Model, cancellationToken) },
                },
            };
        }

        if (method.GetCustomAttributes(typeof(FormBuilderBeaconBodyAttribute), false).FirstOrDefault() is FormBuilderBeaconBodyAttribute beacon)
        {
            var schema = await ReferenceAsync(context, beacon.Model, cancellationToken);
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    [FormBuilderBeaconBodyAttribute.TextMediaType] = new() { Schema = schema },
                    [FormBuilderJsonBodyAttribute.MediaType] = new() { Schema = schema },
                },
            };
        }

        if (method.GetCustomAttributes(typeof(FormBuilderTimeLimitAttribute), false).FirstOrDefault() is FormBuilderTimeLimitAttribute time)
        {
            operation.Extensions[FormBuilderPublicApi.TimeLimitExtension] = new JsonNodeExtension(JsonValue.Create((int)time.Limit.TotalSeconds));
        }

        var preview = method.GetCustomAttributes(typeof(FormBuilderPreviewAttribute), false).Length > 0;
        if (preview)
        {
            operation.Extensions[FormBuilderPublicApi.PreviewExtension] = new JsonNodeExtension(JsonValue.Create(true));
        }

        var problem = await ReferenceAsync(context, typeof(ProblemDetails), cancellationToken);
        operation.Responses ??= [];
        var verb = context.Description.HttpMethod ?? string.Empty;
        if (!HttpMethods.IsGet(verb) && !HttpMethods.IsHead(verb))
        {
            Refusal(operation.Responses, problem, StatusCodes.Status403Forbidden, "Forbidden", "a browser on an origin the site does not allow sent it (origin_denied).");
        }

        Refusal(operation.Responses, problem, StatusCodes.Status401Unauthorized, "Unauthorized", preview
            ? "the preview frame's ticket (Authorization: Jfb-Preview) does not hold for this form (preview_ticket_invalid)."
            : "a preview frame sent it, which this endpoint does not take (preview_ticket_invalid).");
        Refusal(operation.Responses, problem, StatusCodes.Status413PayloadTooLarge, "Payload Too Large", "the body is longer than the endpoint takes (payload_too_large).");
        Refusal(operation.Responses, problem, StatusCodes.Status429TooManyRequests, "Too Many Requests", "a rate limit is reached; Retry-After says when to send again (rate_limited).");
        Refusal(operation.Responses, problem, StatusCodes.Status500InternalServerError, "Internal Server Error", "something failed unexpectedly (server_error).");
        Refusal(operation.Responses, problem, StatusCodes.Status504GatewayTimeout, "Gateway Timeout", "the endpoint did not answer within its time limit (timeout).");
    }

    /// <summary>
    /// A reference to a model's schema in the document's components, which it adds when the document lacks it, as the
    /// generator does for the models an action names itself.
    /// </summary>
    /// <param name="context">The operation's context.</param>
    /// <param name="type">The model.</param>
    /// <param name="cancellationToken">Cancels the generation.</param>
    internal static async Task<IOpenApiSchema> ReferenceAsync(OpenApiOperationTransformerContext context, Type type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(type);
        var schema = await context.GetOrCreateSchemaAsync(type, null, cancellationToken);
        if (context.Document is not { } document || FormBuilderOpenApiSchemas.ReferenceId(type, () => type.Name) is not { } id)
        {
            return schema;
        }

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        if (!document.Components.Schemas.ContainsKey(id))
        {
            document.AddComponent(id, schema);
        }

        return new OpenApiSchemaReference(id, document);
    }

    // The parts of an upload: the challenge, which 7.1 requires, and the file.
    private static OpenApiRequestBody UploadBody() => new()
    {
        Required = true,
        Description = "A challenge part, which only a server key leaves out, then the file part with the file's name. Nothing may follow the file.",
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

    // A refusal of the frame; when the action describes the status for its own refusals too, its description adds the
    // frame's reason.
    private static void Refusal(OpenApiResponses responses, IOpenApiSchema problem, int status, string phrase, string reason)
    {
        var key = status.ToString(CultureInfo.InvariantCulture);
        if (responses.TryGetValue(key, out var declared))
        {
            if (declared is OpenApiResponse response)
            {
                response.Description = $"{response.Description}; also when {reason}";
            }

            return;
        }

        responses[key] = new OpenApiResponse
        {
            Description = $"{phrase}: {reason}",
            Content = new Dictionary<string, OpenApiMediaType> { [FormBuilderProblemResult.MediaType] = new() { Schema = problem } },
        };
    }
}
