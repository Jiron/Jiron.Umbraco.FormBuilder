using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The JSON schema of the element endpoint's answer (ADR-0128), made from the metadata the answer is written with: a
/// definition per model in the order its properties are written, a property that may be null out of <c>required</c>
/// (null is never written), a field's control as one of the control models, each with its <c>kind</c> first under a
/// name of its own (<c>SelectControl</c>), since a model may also be written without one (an address's countries are a
/// <c>SelectControlModel</c>). A type the metadata cannot look into fails, so a new converter is described here.
/// </summary>
internal static class ElementAnswerSchema
{
    /// <summary>Where the schema is kept, below the repository's root.</summary>
    public static readonly string[] Path = ["src", "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "element", "src", "answer.schema.json"];

    /// <summary>The name of a control model's definition with its kind: its type's name without <c>Model</c>.</summary>
    /// <param name="type">The control model.</param>
    public static string ControlName(Type type) => type.Name[..^"Model".Length];

    /// <summary>The schema, its root the answer.</summary>
    public static JsonObject Build()
    {
        var definitions = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        var root = new Walker(definitions).Of(typeof(ElementAnswer));
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "urn:jiron:form-builder:schema:jfb-element-answer:v1",
            ["title"] = "Jiron Form Builder element answer, version 1",
            ["description"] = "What the element endpoint answers with (GET /jfb/api/v1/forms/{key}/element): the view model of a form's first render as the built-in views get it, without what only a post without JavaScript uses, for the headless element of the same install (ADR-0128). A property that may be null is left out instead. The element's TypeScript types in answer.d.ts are made from this schema.",
            ["$ref"] = root["$ref"]!.GetValue<string>(),
            ["$defs"] = new JsonObject(definitions.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, pair.Value))),
        };
    }

    private sealed class Walker(SortedDictionary<string, JsonNode> definitions)
    {
        private readonly HashSet<string> _started = new(StringComparer.Ordinal);

        // The schema of a value of the type where a property holds it.
        public JsonObject Of(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (Plain(type) is { } plain)
            {
                return plain;
            }

            if (type.IsEnum)
            {
                return new JsonObject { ["enum"] = new JsonArray([.. Enum.GetValues(type).Cast<object>().Select(value => JsonNode.Parse(JsonSerializer.Serialize(value, type, ElementJson.Options)))]) };
            }

            if (type == typeof(object))
            {
                return new JsonObject
                {
                    ["oneOf"] = new JsonArray([.. ElementJson.Kinds.OrderBy(pair => pair.Value, StringComparer.Ordinal).Select(pair =>
                    {
                        var name = ControlName(pair.Key);
                        Define(name, () => Record(ElementJson.Options.GetTypeInfo(pair.Key), pair.Value));
                        return (JsonNode)Reference(name);
                    })]),
                };
            }

            var info = ElementJson.Options.GetTypeInfo(type);
            switch (info.Kind)
            {
                case JsonTypeInfoKind.Enumerable:
                    return new JsonObject { ["type"] = "array", ["items"] = Of(info.ElementType!) };
                case JsonTypeInfoKind.Dictionary:
                    return new JsonObject { ["type"] = "object", ["additionalProperties"] = Of(info.ElementType!) };
                case JsonTypeInfoKind.Object:
                    Define(type.Name, () => Record(info, null));
                    return Reference(type.Name);
                default:
                    throw new InvalidOperationException($"The element answer's schema has no description of {type.FullName}, which a converter writes; add it to ElementAnswerSchema.");
            }
        }

        private static JsonObject Reference(string name) => new() { ["$ref"] = "#/$defs/" + name };

        private void Define(string name, Func<JsonObject> make)
        {
            if (_started.Add(name))
            {
                definitions[name] = make();
            }
        }

        // A model: its properties in the order they are written, a control's kind first.
        private JsonObject Record(JsonTypeInfo info, string? kind)
        {
            var properties = new JsonObject();
            var required = new JsonArray();
            if (kind is not null)
            {
                properties[ElementJson.KindName] = new JsonObject { ["const"] = kind };
                required.Add(ElementJson.KindName);
            }

            foreach (var property in info.Properties)
            {
                properties[property.Name] = Of(property.PropertyType);
                if (!property.IsGetNullable)
                {
                    required.Add(property.Name);
                }
            }

            return new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false };
        }

        private static JsonObject Uuid() => new() { ["type"] = "string", ["format"] = "uuid" };

        // The values a converter or the serializer writes as plain JSON: texts and rich texts, a site's own type's
        // settings, and the primitives.
        private static JsonObject? Plain(Type type) => type switch
        {
            _ when type == typeof(string) || typeof(IHtmlContent).IsAssignableFrom(type) => new JsonObject { ["type"] = "string" },
            _ when type == typeof(Guid) => Uuid(),
            _ when type == typeof(bool) => new JsonObject { ["type"] = "boolean" },
            _ when type == typeof(int) || type == typeof(long) => new JsonObject { ["type"] = "integer" },
            _ when type == typeof(JsonElement) => new JsonObject(),
            _ => null,
        };
    }
}
