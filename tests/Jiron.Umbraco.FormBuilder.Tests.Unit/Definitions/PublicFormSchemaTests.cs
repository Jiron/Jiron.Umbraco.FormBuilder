using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Testing;
using Json.Schema;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The published schema of the visitor form (ADR-0125): <c>schemas/jfb-public-form.v1.schema.json</c> is what the
/// projection's records make, written with the visitor context's options, a definition per record; and every visitor
/// form the projection writes fits it. After a deliberate change, run with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the
/// file and fails, so the diff gets reviewed, and <c>scripts/generate-public-types.mjs</c> makes the types from it.
/// </summary>
public sealed class PublicFormSchemaTests
{
    private static readonly string SchemaPath = RepositoryRoot.Path(typeof(PublicFormSchemaTests).Assembly, "schemas", "jfb-public-form.v1.schema.json");

    private static readonly Lazy<JsonSchema> Published = new(() =>
        JsonSchema.FromText(File.ReadAllText(SchemaPath), new BuildOptions { SchemaRegistry = new SchemaRegistry() }));

    [Test]
    public async Task TheSchema_IsWhatTheVisitorFormsRecordsMake_AsSchemasHoldsIt()
    {
        var written = PublicFormSchema.Build().ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(SchemaPath, written, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SchemaPath}. Review the diff, run scripts/generate-public-types.mjs, and run again without the variable.");
        }

        Assert.That(File.Exists(SchemaPath), Is.True, $"the reviewed schema {SchemaPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SchemaPath)).ReplaceLineEndings("\n"), Is.EqualTo(written), "the projection's records changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public void TheFullFixturesVisitorForm_FitsTheSchema_InEachCulture()
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), TestFieldTypes.Registry);

        Assert.Multiple(() =>
        {
            foreach (var culture in new[] { "en-US", "de" })
            {
                AssertFits(projection, culture, "the full fixture");
            }
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void AGeneratedDefinitionsVisitorForm_FitsTheSchema(int seed)
    {
        var generator = new DefinitionGenerator(seed);

        Assert.Multiple(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                AssertFits(PublicProjection.Create(generator.Next(), TestFieldTypes.Registry), "en-US", $"seed {seed}, definition {i}");
            }
        });
    }

    [Test]
    public void TheSchema_NamesADefinitionPerRecord_AndAllowsNothingElse()
    {
        var schema = PublicFormSchema.Build();
        var definitions = schema["$defs"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(schema["$ref"]!.GetValue<string>(), Is.EqualTo("#/$defs/PublicForm"));
            Assert.That(definitions.Select(pair => pair.Key), Is.SupersetOf(new[] { "PublicForm", "PublicPage", "PublicGroup", "PublicField", "PublicPrefill", "RuleCondition", "RuleGroup", "ConditionOperand" }));
            Assert.That(definitions.Where(pair => pair.Value!["type"]?.GetValue<string>() == "object" && pair.Value["properties"] is not null).Select(pair => pair.Value!["additionalProperties"]?.GetValue<bool>()), Is.All.False, "a record allows no other property");
            Assert.That(definitions["PublicPrefill"]!["properties"]!.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "query", "context", "fromMember", "memberMode" }));
        });
    }

    // A projection written as the definition endpoint writes it for a culture, checked against the published schema.
    private static void AssertFits(PublicForm projection, string culture, string what)
    {
        var visitor = VisitorForm.Map(projection, text => text.Resolve(culture), TestFieldTypes.Registry);
        using var document = JsonDocument.Parse(VisitorForm.Write(visitor));
        var result = Published.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.That(result.IsValid, Is.True, () => $"{what} ({culture}): " + string.Join("; ", (result.Details ?? []).Where(detail => detail.Errors is { Count: > 0 }).Take(5).Select(detail => $"{detail.InstanceLocation}: {string.Join(", ", detail.Errors!.Values)}")));
    }
}

/// <summary>
/// The JSON schema of the visitor form, made from the projection's records through the visitor context's metadata
/// (ADR-0125): a definition per record, its properties in the order they are written, those that may be null left
/// out of <c>required</c> (null is never written); a polymorphic record as one of its kinds; and one table for the types
/// whose converters the metadata cannot look into. A type that is in neither fails, so a new converter is described.
/// </summary>
internal static class PublicFormSchema
{
    private static readonly JsonSerializerOptions Options = VisitorJsonContext.Default.Options;

    /// <summary>The schema, its root the visitor form.</summary>
    public static JsonObject Build()
    {
        var definitions = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        var root = new Walker(definitions).Of(typeof(PublicForm));
        var schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "urn:jiron:form-builder:schema:jfb-public-form:v1",
            ["title"] = "Jiron Form Builder visitor form, version 1",
            ["description"] = "The form the definition endpoint answers with (GET /jfb/api/v1/forms/{keyOrAlias}, its form property): the public projection with each text in the page's culture (ADR-0002, ADR-0035, ADR-0125). A property that may be null is left out instead. The TypeScript types in jfb-public-form.v1.d.ts are made from this schema.",
            ["$ref"] = root["$ref"]!.GetValue<string>(),
            ["$defs"] = new JsonObject(definitions.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, pair.Value))),
        };
        return schema;
    }

    private sealed class Walker(SortedDictionary<string, JsonNode> definitions)
    {
        private readonly HashSet<Type> _started = [];

        // The schema of a value of the type where a property holds it: a reference for a record, the table's entry for
        // a converter's type, otherwise its JSON kind.
        public JsonObject Of(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (Plain(type) is { } plain)
            {
                return plain;
            }

            if (type.IsEnum)
            {
                return new JsonObject { ["enum"] = new JsonArray([.. Enum.GetValues(type).Cast<object>().Select(value => JsonNode.Parse(JsonSerializer.Serialize(value, type, Options)))]) };
            }

            if (type == typeof(RuleItem))
            {
                return new JsonObject { ["oneOf"] = new JsonArray(Of(typeof(RuleCondition)), Of(typeof(RuleGroup))) };
            }

            if (type == typeof(ConditionOperand))
            {
                Define(type, () => Operand());
                return Reference(type);
            }

            var info = Options.GetTypeInfo(type);
            switch (info.Kind)
            {
                case JsonTypeInfoKind.Enumerable:
                    return new JsonObject { ["type"] = "array", ["items"] = Of(info.ElementType!) };
                case JsonTypeInfoKind.Dictionary:
                    return new JsonObject { ["type"] = "object", ["additionalProperties"] = Of(info.ElementType!) };
                case JsonTypeInfoKind.Object:
                    Define(type, () => Record(info));
                    return Reference(type);
                default:
                    throw new InvalidOperationException($"The visitor form's schema has no description of {type.FullName}, which a converter writes; add it to PublicFormSchema.");
            }
        }

        private static JsonObject Reference(Type type) => new() { ["$ref"] = "#/$defs/" + type.Name };

        private void Define(Type type, Func<JsonObject> make)
        {
            if (_started.Add(type))
            {
                definitions[type.Name] = make();
            }
        }

        // A record: its properties in the order they are written, a polymorphic one as one of its kinds, each kind with
        // its discriminator first.
        private JsonObject Record(JsonTypeInfo info, (string Name, string Value)? discriminator = null)
        {
            if (info.PolymorphismOptions is { } polymorphism)
            {
                return new JsonObject
                {
                    ["oneOf"] = new JsonArray([.. polymorphism.DerivedTypes.Select(derived =>
                    {
                        Define(derived.DerivedType, () => Record(Options.GetTypeInfo(derived.DerivedType), (polymorphism.TypeDiscriminatorPropertyName, (string)derived.TypeDiscriminator!)));
                        return (JsonNode)Reference(derived.DerivedType);
                    })]),
                };
            }

            var properties = new JsonObject();
            var required = new JsonArray();
            if (discriminator is { } kind)
            {
                properties[kind.Name] = new JsonObject { ["const"] = kind.Value };
                required.Add(kind.Name);
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

        // A condition's operand (ADR-0002): a text, another answer, a date relative to today, or a list of those.
        private static JsonObject Operand()
        {
            JsonObject Item() => new JsonObject
            {
                ["oneOf"] = new JsonArray(
                    new JsonObject { ["type"] = "string" },
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["field"] = Uuid() }, ["required"] = new JsonArray("field"), ["additionalProperties"] = false },
                    new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["today"] = new JsonObject { ["type"] = "string" } }, ["required"] = new JsonArray("today"), ["additionalProperties"] = false }),
            };
            var any = Item();
            any["oneOf"]!.AsArray().Add(new JsonObject { ["type"] = "array", ["items"] = Item(), ["minItems"] = 1 });
            return any;
        }

        private static JsonObject Uuid() => new() { ["type"] = "string", ["format"] = "uuid" };

        // The values a converter or the serializer writes as plain JSON: texts in the page's culture, a field's default,
        // settings, and the primitives.
        private static JsonObject? Plain(Type type) => type switch
        {
            _ when type == typeof(string) || type == typeof(LocalizedText) || type == typeof(LocalizedHtml) => new JsonObject { ["type"] = "string" },
            _ when type == typeof(Guid) => Uuid(),
            _ when type == typeof(bool) => new JsonObject { ["type"] = "boolean" },
            _ when type == typeof(int) || type == typeof(long) => new JsonObject { ["type"] = "integer" },
            _ when type == typeof(decimal) || type == typeof(double) => new JsonObject { ["type"] = "number" },
            _ when type == typeof(DateTimeOffset) || type == typeof(DateTime) => new JsonObject { ["type"] = "string", ["format"] = "date-time" },
            _ when type == typeof(FieldDefaultValue) => new JsonObject { ["oneOf"] = new JsonArray(new JsonObject { ["type"] = "string" }, new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }) },
            _ when type == typeof(SettingsObject) || type == typeof(JsonElement) => new JsonObject { ["type"] = "object" },
            _ => null,
        };
    }
}
