using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Umbraco.Cms.Api.Common.OpenApi;

namespace Jiron.Umbraco.FormBuilder.Web.OpenApi;

/// <summary>
/// The rules that make Jiron Form Builder's OpenAPI documents on Umbraco 18 (ROADMAP 3.4, Phase 19, ADR-0250) describe
/// what the API sends, as the Umbraco 17 line's documents did, so the backoffice's generated clients keep their types:
/// <list type="bullet">
/// <item>Jiron Form Builder's models are named as Umbraco names its own (<c>FolderResponseModel</c>), so two models of
/// one generic type never share a name;</item>
/// <item>a file (an export, a download) is a binary string, and a JSON value of any shape (<see cref="JsonElement"/>: a
/// definition, an audit entry's details) is any value, both inline, so no framework type (<c>FileContentResult</c>,
/// <c>Stream</c>, <c>JsonElement</c>) becomes a schema of its own in a generated client;</item>
/// <item>a validation problem (<see cref="HttpValidationProblemDetails"/>) has its fields and its <c>errors</c>;</item>
/// <item>a number is a number: the API writes numbers, though the web's JSON defaults also read them from strings;</item>
/// <item>a property that may be null is not required, unless the model marks it <c>required</c>;</item>
/// <item>a property the model only reads, written in place, is read-only, and null only when it may be;</item>
/// <item>a problem (<see cref="ProblemDetails"/>) may carry extensions, such as its stable <c>code</c>;</item>
/// <item>a body an action may go without is its model, which the client may leave out, rather than the model or null;</item>
/// <item>a response lists only the media types it has a body for.</item>
/// </list>
/// </summary>
internal static class FormBuilderOpenApiSchemas
{
    private const string OwnNamespace = "Jiron.Umbraco.FormBuilder";

    private static readonly ImmutableArray<Type> Files = [typeof(FileResult), typeof(Stream), typeof(IFormFile)];

    /// <summary>Applies the rules to a document's options, after Umbraco's own schema names and transformers.</summary>
    /// <param name="options">The document's options.</param>
    public static void Apply(OpenApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var names = options.CreateSchemaReferenceId;
        options.CreateSchemaReferenceId = type => ReferenceId(type.Type, () => names(type));
        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (IsFile(type))
            {
                Clear(schema);
                schema.Type = JsonSchemaType.String;
                schema.Format = "binary";
            }
            else if (IsJson(type))
            {
                Clear(schema);
            }
            else if (typeof(HttpValidationProblemDetails).IsAssignableFrom(type))
            {
                ValidationProblem(schema);
            }
            else if (typeof(ProblemDetails).IsAssignableFrom(type))
            {
                Extensible(schema);
            }

            Numbers(schema);
            Optional(schema, context.JsonTypeInfo);
            ReadOnly(schema, context.JsonTypeInfo);
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, _, _) =>
        {
            WithBodies(operation);
            OptionalBodies(operation);
            return Task.CompletedTask;
        });

        // A form's file is named by the framework itself, outside the JSON schemas above: it goes inline as well.
        options.AddDocumentTransformer((document, _, _) =>
        {
            InlineFormFiles(document);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The reference id of a type's schema: none for a file or a JSON value, which go inline; Umbraco's naming for Jiron
    /// Form Builder's own models; otherwise what the document would name it.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <param name="otherwise">The document's own name for it.</param>
    internal static string? ReferenceId(Type type, Func<string?> otherwise)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(otherwise);
        if (IsFile(type) || IsJson(type))
        {
            return null;
        }

        return IsOwn(type) ? UmbracoSchemaIdGenerator.Generate(type) : otherwise();
    }

    /// <summary>
    /// Replaces every reference to the framework's <c>IFormFile</c> schema with a binary string, wherever it is (a form's
    /// field, an array's items, a composition, another schema's property, a request's or a response's body), then drops
    /// the schema itself, which nothing refers to any more.
    /// </summary>
    /// <param name="document">The document.</param>
    internal static void InlineFormFiles(OpenApiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Components?.Schemas is not { } components || !components.ContainsKey(nameof(IFormFile)))
        {
            return;
        }

        var seen = new HashSet<OpenApiSchema>(ReferenceEqualityComparer.Instance);
        foreach (var (name, schema) in components.ToList())
        {
            components[name] = Inline(schema, seen);
        }

        var operations = document.Paths.Values.SelectMany(path => path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>());
        foreach (var operation in operations)
        {
            var requests = operation.RequestBody?.Content?.Values ?? Enumerable.Empty<OpenApiMediaType>();
            var responses = (operation.Responses?.Values ?? Enumerable.Empty<IOpenApiResponse>())
                .SelectMany(response => response.Content?.Values ?? Enumerable.Empty<OpenApiMediaType>());
            foreach (var body in requests.Concat(responses))
            {
                body.Schema = body.Schema is null ? null : Inline(body.Schema, seen);
            }
        }

        components.Remove(nameof(IFormFile));
    }

    /// <summary>
    /// A validation problem as the API writes it: the problem's fields and <c>errors</c>, the messages per field. Its
    /// JSON converter hides them from the document's generator, which would otherwise say "any value".
    /// </summary>
    /// <param name="schema">The problem's schema.</param>
    internal static void ValidationProblem(OpenApiSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Clear(schema);
        schema.Type = JsonSchemaType.Object;
        schema.Properties = new Dictionary<string, IOpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["title"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer | JsonSchemaType.Null, Format = "int32" },
            ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
            ["errors"] = new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchema { Type = JsonSchemaType.String } },
            },
        };
        schema.Required = new HashSet<string> { "errors" };
    }

    /// <summary>
    /// A property that may be null is not required, unless the model marks it <c>required</c>: the document marks every
    /// constructor parameter of a record as required, while the API's JSON reads a missing one as null. As in the Umbraco
    /// 17 line's documents, a client may leave it out, and a model's <c>required</c> member is always sent, null or not.
    /// </summary>
    /// <param name="schema">An object's schema.</param>
    /// <param name="type">The object's JSON contract.</param>
    internal static void Optional(OpenApiSchema schema, JsonTypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(type);
        if (type.Kind != JsonTypeInfoKind.Object || schema.Required is not { Count: > 0 } required)
        {
            return;
        }

        foreach (var property in type.Properties)
        {
            if (!property.IsRequired && (property.IsGetNullable || Nullable.GetUnderlyingType(property.PropertyType) is not null))
            {
                required.Remove(property.Name);
            }
        }
    }

    /// <summary>
    /// A number as a number: under the web's JSON defaults (numbers read from strings too) the document says
    /// "integer or string" with a pattern; the API writes numbers, and its clients send them.
    /// </summary>
    /// <param name="schema">A schema.</param>
    internal static void Numbers(OpenApiSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.Type is { } type && (type & (JsonSchemaType.Integer | JsonSchemaType.Number)) != 0 && (type & JsonSchemaType.String) != 0)
        {
            schema.Type = type & ~JsonSchemaType.String;
            schema.Pattern = null;
        }
    }

    /// <summary>A problem as the API writes it: its fields and the extensions a refusal adds, such as its <c>code</c>.</summary>
    /// <param name="schema">The problem's schema.</param>
    internal static void Extensible(OpenApiSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        schema.AdditionalPropertiesAllowed = true;
        schema.AdditionalProperties ??= new OpenApiSchema();
    }

    /// <summary>
    /// A property the model only reads (it has no setter and no constructor parameter, such as a constant type name) is
    /// read-only, as the Umbraco 17 line's documents said, so a client never sends it; and it is null only when the model
    /// says it may be. Only a value written in place is marked: a model or an enum has a schema of its own, which the
    /// mark would change wherever the schema is used.
    /// </summary>
    /// <param name="schema">An object's schema.</param>
    /// <param name="type">The object's JSON contract.</param>
    internal static void ReadOnly(OpenApiSchema schema, JsonTypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(type);
        if (type.Kind != JsonTypeInfoKind.Object || schema.Properties is not { } properties)
        {
            return;
        }

        foreach (var property in type.Properties.Where(OnlyRead))
        {
            if (properties.TryGetValue(property.Name, out var found) && found is OpenApiSchema read)
            {
                read.ReadOnly = true;
                if (!property.IsGetNullable && Nullable.GetUnderlyingType(property.PropertyType) is null && read.Type is { } kind)
                {
                    read.Type = kind & ~JsonSchemaType.Null;
                }
            }
        }
    }

    // Read but never written, by a setter or through the constructor, and written in place rather than as a schema of
    // its own.
    private static bool OnlyRead(JsonPropertyInfo property) =>
        property.Get is not null
        && property.Set is null
        && property.AssociatedParameter is null
        && property.Options.GetTypeInfo(property.PropertyType).Kind == JsonTypeInfoKind.None
        && !(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).IsEnum;

    /// <summary>
    /// A body an action may go without (its parameter may be null) is its model, and the client may leave it out, as the
    /// Umbraco 17 line's documents said: the generator writes "the model or null", which no client sends.
    /// </summary>
    /// <param name="operation">An operation.</param>
    internal static void OptionalBodies(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.RequestBody is not OpenApiRequestBody { Content: { } content } body)
        {
            return;
        }

        foreach (var media in content.Values)
        {
            if (media.Schema is OpenApiSchema { OneOf: [var first, var second] } && IsNull(first) != IsNull(second))
            {
                media.Schema = IsNull(first) ? second : first;
                body.Required = false;
            }
        }
    }

    /// <summary>Drops the media types a response names without a body (a 304, an error's other formats).</summary>
    /// <param name="operation">An operation.</param>
    internal static void WithBodies(OpenApiOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        foreach (var response in operation.Responses?.Values ?? Enumerable.Empty<IOpenApiResponse>())
        {
            if (response.Content is not { } content)
            {
                continue;
            }

            foreach (var mediaType in content.Where(entry => entry.Value.Schema is null).Select(entry => entry.Key).ToList())
            {
                content.Remove(mediaType);
            }
        }
    }

    /// <summary>Whether a type is a file the API sends or takes.</summary>
    /// <param name="type">The type.</param>
    internal static bool IsFile(Type type) => Files.Any(file => file.IsAssignableFrom(type));

    /// <summary>Whether a type is a JSON value of any shape.</summary>
    /// <param name="type">The type.</param>
    internal static bool IsJson(Type type) => (Nullable.GetUnderlyingType(type) ?? type) == typeof(JsonElement);

    /// <summary>Whether a type is one of Jiron Form Builder's own.</summary>
    /// <param name="type">The type.</param>
    internal static bool IsOwn(Type type) =>
        type.Namespace is { } name && (name == OwnNamespace || name.StartsWith(OwnNamespace + ".", StringComparison.Ordinal));

    private static IOpenApiSchema Inline(IOpenApiSchema schema, HashSet<OpenApiSchema> seen)
    {
        if (schema is OpenApiSchemaReference { Reference.Id: nameof(IFormFile) })
        {
            return new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
        }

        // A reference to another schema is followed where that schema is defined; a schema already walked is not again.
        if (schema is not OpenApiSchema concrete || !seen.Add(concrete))
        {
            return schema;
        }

        if (concrete.Properties is { } properties)
        {
            foreach (var (name, property) in properties.ToList())
            {
                properties[name] = Inline(property, seen);
            }
        }

        concrete.Items = concrete.Items is null ? null : Inline(concrete.Items, seen);
        concrete.AdditionalProperties = concrete.AdditionalProperties is null ? null : Inline(concrete.AdditionalProperties, seen);
        foreach (var list in new[] { concrete.AllOf, concrete.OneOf, concrete.AnyOf })
        {
            for (var index = 0; list is not null && index < list.Count; index++)
            {
                list[index] = Inline(list[index], seen);
            }
        }

        return concrete;
    }

    private static bool IsNull(IOpenApiSchema schema) => schema is OpenApiSchema { Type: JsonSchemaType.Null };

    private static void Clear(OpenApiSchema schema)
    {
        schema.Type = null;
        schema.Format = null;
        schema.Properties = null;
        schema.Required = null;
        schema.AdditionalProperties = null;
        schema.AdditionalPropertiesAllowed = true;
        schema.Items = null;
    }
}
