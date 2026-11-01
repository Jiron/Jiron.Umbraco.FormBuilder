using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// Generates the appsettings JSON schema for IntelliSense (ADR-0008, ADR-0042) from the options classes and their XML
/// documentation: a description for every key, the default of every value, the allowed texts of a value that takes
/// only some, and no keys beyond the options'. The schema of the options sits under their section in the file, so its
/// references are rebased to point there.
/// </summary>
internal static partial class AppSettingsSchema
{
    /// <summary>The file's name, which Umbraco's build references from the site's own schema.</summary>
    public const string FileName = "appsettings-schema.Jiron.Umbraco.FormBuilder.json";

    /// <summary>The schema of <see cref="FormBuilderOptions"/> as the committed file holds it: LF line endings and a final newline.</summary>
    public static string Generate() => Generate(typeof(FormBuilderOptions), FormBuilderOptions.SectionName, "JironUmbracoFormBuilderSchema");

    /// <summary>The schema of <paramref name="options"/> under the section <paramref name="section"/>.</summary>
    public static string Generate(Type options, string section, string title)
    {
        var docs = XmlDocs.Of(options.Assembly);
        var defaults = options.GetConstructor(Type.EmptyTypes) is null ? null : Activator.CreateInstance(options);
        var serializer = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var exporter = new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (node is not JsonObject schema)
                {
                    return node;
                }

                var property = context.PropertyInfo?.AttributeProvider as PropertyInfo;
                if (docs.Summary(property is not null ? XmlDocs.Id(property) : XmlDocs.Id(context.TypeInfo.Type)) is { } description)
                {
                    schema.Insert(0, "description", Readable(description));
                }

                if (property?.GetCustomAttribute<AllowedValuesAttribute>() is { } allowed)
                {
                    schema["enum"] = new JsonArray([.. allowed.Values.Select(value => JsonValue.Create(value?.ToString()))]);
                }

                if (property is not null && Default(property, context.Path, defaults, serializer) is { } value)
                {
                    schema["default"] = value;
                }

                return schema;
            },
        };

        var node = serializer.GetJsonSchemaAsNode(options, exporter);
        Rebase(node, $"#/properties/{section}");
        var root = new JsonObject
        {
            ["$schema"] = "http://json-schema.org/draft-04/schema#",
            ["title"] = title,
            ["type"] = "object",
            ["properties"] = new JsonObject { [section] = node },
        };
        var output = new JsonSerializerOptions { WriteIndented = true, IndentSize = 2, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        return root.ToJsonString(output).ReplaceLineEndings("\n") + "\n";
    }

    /// <summary>Every <c>$ref</c> in a schema, in document order.</summary>
    public static IEnumerable<string> References(JsonNode? node) => node switch
    {
        JsonArray array => array.SelectMany(References),
        JsonObject obj => obj.SelectMany(pair => pair.Key == "$ref" && pair.Value is JsonValue value ? [value.GetValue<string>()] : References(pair.Value)),
        _ => [],
    };

    /// <summary>The node a reference within the document points to (RFC 6901), or null when it points nowhere.</summary>
    public static JsonNode? Resolve(JsonNode document, string reference)
    {
        if (reference == "#")
        {
            return document;
        }

        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return null;
        }

        JsonNode? node = document;
        foreach (var part in reference[2..].Split('/').Select(token => Uri.UnescapeDataString(token).Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)))
        {
            node = node switch
            {
                JsonObject obj => obj.TryGetPropertyValue(part, out var child) ? child : null,
                JsonArray array => int.TryParse(part, out var index) && index >= 0 && index < array.Count ? array[index] : null,
                _ => null,
            };
        }

        return node;
    }

    // The exporter's references point into the options' own schema, which sits under their section in the file.
    private static void Rebase(JsonNode? node, string prefix)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                Rebase(item, prefix);
            }
        }
        else if (node is JsonObject obj)
        {
            if (obj["$ref"] is JsonValue reference && reference.GetValue<string>() is var pointer && pointer.StartsWith('#'))
            {
                obj["$ref"] = prefix + pointer[1..];
            }

            foreach (var (_, child) in obj.ToList())
            {
                Rebase(child, prefix);
            }
        }
    }

    // A value's default, as new options hold it, since a section can set its own values' defaults, as each rate policy does. A
    // value in a list's items, which new options do not reach, reads a new instance of its class. Lists and sections have none.
    private static JsonNode? Default(PropertyInfo property, ReadOnlySpan<string> path, object? defaults, JsonSerializerOptions serializer)
    {
        if (!TryValueAt(path, defaults, out var value))
        {
            var owner = property.DeclaringType!;
            if (owner.GetConstructor(Type.EmptyTypes) is null)
            {
                return null;
            }

            value = property.GetValue(Activator.CreateInstance(owner));
        }

        return value is string or bool or int or long or double or decimal or Enum ? JsonSerializer.SerializeToNode(value, property.PropertyType, serializer) : null;
    }

    // Walks new options by a schema's path, of "properties" and a name each; false for a path that goes anywhere else.
    private static bool TryValueAt(ReadOnlySpan<string> path, object? defaults, out object? value)
    {
        value = defaults;
        for (var at = 0; at < path.Length; at += 2)
        {
            if (value is null || path[at] != "properties" || at + 1 == path.Length)
            {
                return false;
            }

            value = value.GetType().GetProperty(path[at + 1])?.GetValue(value);
        }

        return value is not null || defaults is not null;
    }

    // The documentation's references to the roadmap and the decisions mean nothing in a site's editor.
    private static string Readable(string description) => Citations().Replace(description, string.Empty);

    [GeneratedRegex(@" \((?:section [0-9.]+|ADR-[0-9]{4})(?:, (?:section [0-9.]+|ADR-[0-9]{4}))*\)")]
    private static partial Regex Citations();
}
