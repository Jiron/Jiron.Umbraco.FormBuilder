using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Testing;
using Json.Schema;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The JSON schema of a definition (schemas/jfb-form.v1.schema.json, ADR-0034, ROADMAP 4.2a): every written definition
/// validates against it, it describes the same objects as the model with the same properties and required ones, and it
/// refuses what the reader refuses, except for the few things a schema cannot say.
/// </summary>
public sealed partial class DefinitionSchemaTests
{
    private const int Definitions = 500;

    // The refusals of RefusalTests.Changes() that a schema cannot say; the schema's description names each of them.
    private static readonly HashSet<string> BeyondTheSchema = new(StringComparer.Ordinal)
    {
        "two pages with one key",
        "a grid row with a field's key",
        "an action with a rule's key",
        "a span wider than the group",
        "a culture twice in two cases",
        "an instant that does not exist",
    };

    private static readonly Lazy<string> SchemaText = new(() =>
        File.ReadAllText(RepositoryRoot.Path(typeof(DefinitionSchemaTests).Assembly, "schemas", $"jfb-form.v{FormDefinition.CurrentSchemaVersion}.schema.json")));

    private static readonly Lazy<JsonSchema> Schema = new(() =>
        JsonSchema.FromText(SchemaText.Value, new BuildOptions { SchemaRegistry = new SchemaRegistry() }));

    private static readonly Lazy<JsonDocument> SchemaDocument = new(() => JsonDocument.Parse(SchemaText.Value));

    private static JsonElement Defs => SchemaDocument.Value.RootElement.GetProperty("$defs");

    public static IEnumerable<TestCaseData> Refusals() =>
        from data in RefusalTests.Changes()
        let name = CaseName(data)
        where !BeyondTheSchema.Contains(name)
        select new TestCaseData(data.Arguments[0], data.Arguments[2]).SetName($"TheSchema_Refuses({name})");

    [Test]
    public void TheFullFixture_Validates()
    {
        using var document = JsonDocument.Parse(DefinitionFixtures.Full);

        AssertValid(document.RootElement, "v1-full.json");
    }

    [Test]
    public void GeneratedDefinitions_Validate()
    {
        for (var seed = 0; seed < Definitions; seed++)
        {
            using var document = JsonDocument.Parse(FormDefinitionSerializer.Write(new DefinitionGenerator(seed).Next()));
            AssertValid(document.RootElement, $"seed {seed}");
        }
    }

    [Test]
    public void TheSchema_IsForTheCurrentSchemaVersion()
    {
        var root = SchemaDocument.Value.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("$schema").GetString(), Is.EqualTo("https://json-schema.org/draft/2020-12/schema"));
            Assert.That(root.GetProperty("$id").GetString(), Is.EqualTo($"urn:jiron:form-builder:schema:jfb-form:v{FormDefinition.CurrentSchemaVersion}"));
            Assert.That(root.GetProperty("$ref").GetString(), Is.EqualTo("#/$defs/FormDefinition"));
            Assert.That(Defs.GetProperty("FormDefinition").GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32(), Is.EqualTo(FormDefinition.CurrentSchemaVersion));
        });
    }

    [Test]
    public void TheSchemaAndTheModel_NameTheSameProperties_WithTheSameRequiredOnes()
    {
        var bases = ModelProperties.Derived.Keys.Select(key => key.Base).Append("RuleItem").ToHashSet(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (name, properties) in ModelProperties.Types)
        {
            if (!Defs.TryGetProperty(name, out var definition))
            {
                problems.Add($"{name}: no definition in the schema");
                continue;
            }

            if (bases.Contains(name))
            {
                var expected = name == "RuleItem"
                    ? ["RuleCondition", "RuleGroup"]
                    : ModelProperties.Derived.Where(pair => pair.Key.Base == name).Select(pair => pair.Value);
                Compare($"{name} oneOf", References(definition.GetProperty("oneOf")), expected, problems);
                continue;
            }

            var derived = ModelProperties.Derived.Values.Contains(name);
            Expect(definition, "type", "object", name, problems);
            Expect(definition, "additionalProperties", "false", name, problems);
            Compare($"{name} properties", definition.GetProperty("properties").EnumerateObject().Select(property => property.Name), properties.Keys, problems);
            var required = definition.TryGetProperty("required", out var list) ? list.EnumerateArray().Select(item => item.GetString()!) : [];
            Compare($"{name} required", required, ModelProperties.Required(name).Concat(derived ? ["kind"] : []), problems);
            ComparePropertyTypes(name, definition.GetProperty("properties"), problems);
        }

        var objects = Defs.EnumerateObject()
            .Where(definition => definition.Value.TryGetProperty("properties", out _) || definition.Value.TryGetProperty("oneOf", out _))
            .Select(definition => definition.Name);
        Compare("object definitions", objects, ModelProperties.Types.Keys, problems);

        Assert.That(problems, Is.Empty);
    }

    [Test]
    public void TheSchemaAndTheModel_NameTheSameWords()
    {
        var enums = typeof(FormDefinition).Assembly.GetTypes()
            .Where(type => type.IsEnum && type.IsPublic && type.Namespace == typeof(FormDefinition).Namespace)
            .ToList();
        var problems = new List<string>();
        foreach (var type in enums)
        {
            var words = Enum.GetNames(type).Select(JsonNamingPolicy.CamelCase.ConvertName);
            if (!Defs.TryGetProperty(type.Name, out var definition))
            {
                problems.Add($"{type.Name}: no definition in the schema");
                continue;
            }

            Compare(type.Name, definition.GetProperty("enum").EnumerateArray().Select(word => word.GetString()!), words, problems);
        }

        Assert.Multiple(() =>
        {
            Assert.That(enums, Has.Count.EqualTo(17), "the words of the model are found");
            Assert.That(problems, Is.Empty);
        });
    }

    [TestCaseSource(nameof(Refusals))]
    public void TheSchema_RefusesWhatTheReaderRefuses(Action<JsonObject> change, string path)
    {
        using var document = JsonDocument.Parse(RefusalTests.Changed(change));
        var results = Schema.Value.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        var pointer = Pointer(path);
        var parent = pointer.Length == 0 ? pointer : pointer[..pointer.LastIndexOf('/')];
        var located = Failures(results)
            .Select(result => result.InstanceLocation.ToString())
            .Distinct()
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(results.IsValid, Is.False, "the schema refuses it");
            Assert.That(
                located,
                Has.Some.Matches<string>(location => location == pointer || location == parent || location.StartsWith(pointer + "/", StringComparison.Ordinal)),
                $"an error at {pointer} or around it; found {string.Join(", ", located)}");
        });
    }

    [Test]
    public void TheSchema_AllowsOnlyWhatItCannotSay()
    {
        var cases = RefusalTests.Changes().ToDictionary(CaseName, data => (Action<JsonObject>)data.Arguments[0]!, StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            foreach (var name in BeyondTheSchema)
            {
                Assert.That(cases, Does.ContainKey(name), "a refusal of RefusalTests");
                if (cases.TryGetValue(name, out var change))
                {
                    using var document = JsonDocument.Parse(RefusalTests.Changed(change));
                    Assert.That(Schema.Value.Evaluate(document.RootElement).IsValid, Is.True, $"{name}: the schema says it after all, so it belongs to the refusals the schema makes");
                }
            }
        });
    }

    private static string CaseName(TestCaseData data) => CaseNamePattern().Match(data.TestName!).Groups[1].Value;

    private static void AssertValid(JsonElement instance, string what)
    {
        var results = Schema.Value.Evaluate(instance);
        if (results.IsValid)
        {
            return;
        }

        var details = Schema.Value.Evaluate(instance, new EvaluationOptions { OutputFormat = OutputFormat.Hierarchical });
        var errors = Failures(details).Select(result => $"{result.InstanceLocation} ({result.EvaluationPath}): {(result.Errors is { } messages ? string.Join("; ", messages.Values) : "no message")}");
        Assert.Fail($"{what} does not validate against the schema:{Environment.NewLine}{string.Join(Environment.NewLine, errors)}");
    }

    // The results of a hierarchical output that made the document invalid: failing results reached through failing
    // results only (a branch that fails inside an anyOf or a not that passes does not count), each with errors of its
    // own or without a failing result below it (a not that fails has no error message).
    private static IEnumerable<EvaluationResults> Failures(EvaluationResults results)
    {
        if (results.IsValid)
        {
            return [];
        }

        var below = (results.Details ?? []).SelectMany(Failures).ToList();
        return results.Errors is { Count: > 0 } || below.Count == 0 ? below.Prepend(results) : below;
    }

    // The JSON pointer of a reader path such as $.pages[0].title[''].
    private static string Pointer(string path)
    {
        var pointer = new StringBuilder();
        foreach (Match part in PathPartPattern().Matches(path[1..]))
        {
            var name = part.Groups["index"].Success ? part.Groups["index"].Value : part.Groups["name"].Success ? part.Groups["name"].Value : part.Groups["quoted"].Value;
            pointer.Append('/').Append(name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal));
        }

        return pointer.ToString();
    }

    private static IEnumerable<string> References(JsonElement list) =>
        list.EnumerateArray().Select(item => item.GetProperty("$ref").GetString()!.Replace("#/$defs/", string.Empty, StringComparison.Ordinal));

    // Each property that holds an object or a word of the model refers to that object's or word's definition.
    private static void ComparePropertyTypes(string name, JsonElement properties, List<string> problems)
    {
        var info = DefinitionJsonContext.Default.Options.GetTypeInfo(ModelProperties.Clr[name]);
        foreach (var property in info.Properties)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var held = ModelProperties.Types[name][property.Name] ?? (type.IsEnum ? type.Name : null);
            if (held is not null && !properties.GetProperty(property.Name).GetRawText().Contains($"\"#/$defs/{held}\"", StringComparison.Ordinal))
            {
                problems.Add($"{name}.{property.Name}: does not refer to {held}");
            }
        }
    }

    private static void Expect(JsonElement definition, string keyword, string value, string name, List<string> problems)
    {
        if (!definition.TryGetProperty(keyword, out var found) || (found.ValueKind == JsonValueKind.String ? found.GetString() : found.GetRawText()) != value)
        {
            problems.Add($"{name}: {keyword} is not {value}");
        }
    }

    private static void Compare(string what, IEnumerable<string> schema, IEnumerable<string> model, List<string> problems)
    {
        var inSchema = schema.ToHashSet(StringComparer.Ordinal);
        var inModel = model.ToHashSet(StringComparer.Ordinal);
        problems.AddRange(inSchema.Except(inModel).Select(name => $"{what}: {name} is only in the schema"));
        problems.AddRange(inModel.Except(inSchema).Select(name => $"{what}: {name} is only in the model"));
    }

    [GeneratedRegex(@"^Refuses\((.*)\)$")]
    private static partial Regex CaseNamePattern();

    [GeneratedRegex(@"\.(?<name>[A-Za-z_][A-Za-z0-9_]*)|\[(?<index>[0-9]+)\]|\['(?<quoted>[^']*)'\]")]
    private static partial Regex PathPartPattern();
}
