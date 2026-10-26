using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// The NUnit vector runner (ROADMAP section 4.3.3, ADR-0005): one test case per vector of every tests/vectors/*.json
/// file, read in place, against Core. The Vitest runner in packages/logic runs the same files.
/// </summary>
[TestFixture]
public sealed class VectorTests
{
    private static readonly string[] Runnable = ["condition", "rule", "number", "calc", "formula", "form", "field"];

    private static readonly Lazy<IReadOnlyDictionary<string, JsonDocument>> Files = new(Load);

    public static IEnumerable<TestCaseData> FileNames() =>
        Files.Value.Keys.Select(name => new TestCaseData(name).SetName($"VectorFile({name})"));

    public static IEnumerable<TestCaseData> Cases() =>
        from file in Files.Value
        from vector in file.Value.RootElement.GetProperty("cases").EnumerateArray()
        let id = vector.GetProperty("id").GetString()!
        select new TestCaseData(file.Key, id).SetName($"Vector({Path.GetFileNameWithoutExtension(file.Key)}/{id})");

    [Test]
    public void VectorFiles_InTheRepository_IncludeTheSpikeFile()
    {
        Assert.That(Files.Value.Keys, Does.Contain("spike.json"));
    }

    [TestCaseSource(nameof(FileNames))]
    public void VectorFile_IsJfbVectors1_NamedAfterItsSuiteWithUniqueIdsAndOneThingToRunPerCase(string name)
    {
        var root = Files.Value[name].RootElement;
        var cases = root.GetProperty("cases").EnumerateArray().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("format").GetString(), Is.EqualTo("jfb-vectors/1"));
            Assert.That(root.GetProperty("suite").GetString() + ".json", Is.EqualTo(name));
            Assert.That(cases.Select(c => c.GetProperty("id").GetString()), Is.Unique);
            foreach (var vector in cases)
            {
                Assert.That(Runnable.Count(thing => vector.TryGetProperty(thing, out _)), Is.EqualTo(1), vector.GetProperty("id").GetString());
            }
        });
    }

    [TestCaseSource(nameof(Cases))]
    public void Vector(string name, string id)
    {
        var root = Files.Value[name].RootElement;
        var vector = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var expected = JsonNode.Parse(vector.GetProperty("expect").GetRawText());

        var actual = Run(root, vector);

        Assert.That(JsonNode.DeepEquals(actual, expected), Is.True, $"expected {Show(expected)}, got {Show(actual)}");
    }

    private static Dictionary<string, JsonDocument> Load()
    {
        var folder = RepositoryRoot.Path(typeof(VectorTests).Assembly, "tests", "vectors");
        return Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => Path.GetFileName(path), path => JsonDocument.Parse(File.ReadAllText(path)), StringComparer.Ordinal);
    }

    private static string Show(JsonNode? node) => node?.ToJsonString() ?? "null";

    private static JsonObject Json(FormState state) => new()
    {
        ["hidden"] = Json(state.Hidden),
        ["required"] = Json(state.Required),
        ["messages"] = Json(state.Messages),
        ["calculations"] = Json(state.Calculations),
        ["rows"] = new JsonObject(state.Rows.Select(pair => KeyValuePair.Create(
            pair.Key,
            (JsonNode?)new JsonArray([.. pair.Value.Select(row => (JsonNode)new JsonObject
            {
                ["hidden"] = Json(row.Hidden),
                ["required"] = Json(row.Required),
                ["messages"] = Json(row.Messages),
                ["calculations"] = Json(row.Calculations),
            })])))),
        ["actions"] = Json(state.Actions),
        ["outcome"] = state.Outcome,
    };

    private static JsonArray Json(IReadOnlyList<string> texts) => new([.. texts.Select(text => (JsonNode)text)]);

    private static JsonObject Json(IReadOnlyDictionary<string, IReadOnlyList<string>> messages) =>
        new(messages.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)Json(pair.Value))));

    private static JsonObject Json(IReadOnlyDictionary<string, string?> calculations) =>
        new(calculations.Select(pair => KeyValuePair.Create(pair.Key, pair.Value is { } value ? (JsonNode?)value : null)));

    // A subject as a formula reads it: the fields' kinds and answers by alias, a hidden field without answers.
    private static FormulaScope FormulaScope(Subject subject) => new(
        alias => subject.Fields.TryGetValue(alias, out var kind) ? kind : null,
        alias => subject.Context.Hidden.Contains(alias, StringComparer.Ordinal) ? null : subject.Values.GetValueOrDefault(alias),
        () => CalendarValues.Today(subject.Context.Now, subject.Context.TimeZone));

    private static JsonNode? Run(JsonElement root, JsonElement vector)
    {
        var fields = VectorReader.Fields(root.GetProperty("fields"));
        if (vector.TryGetProperty("fields", out var overrides))
        {
            foreach (var (alias, kind) in VectorReader.Fields(overrides))
            {
                fields[alias] = kind;
            }
        }

        var defaults = VectorReader.Context(root.GetProperty("defaults").GetProperty("context"));
        var subject = new Subject(
            fields,
            vector.TryGetProperty("values", out var values) ? VectorReader.Values(values) : [],
            vector.TryGetProperty("context", out var context) ? VectorReader.Context(context, defaults) : defaults);
        try
        {
            if (vector.TryGetProperty("condition", out var condition))
            {
                return Rules.EvaluateCondition(VectorReader.Condition(condition), subject);
            }

            if (vector.TryGetProperty("rule", out var rule))
            {
                return Rules.EvaluateRule(VectorReader.Rule(rule), subject) is { } result ? JsonValue.Create(result) : null;
            }

            if (vector.TryGetProperty("number", out var number))
            {
                return Calculations.StoredNumber(number.GetString()!);
            }

            if (vector.TryGetProperty("field", out var field))
            {
                return FieldVector.Run(vector.GetProperty("id").GetString()!, field, vector.GetProperty("answers"), values, subject.Context);
            }

            if (vector.TryGetProperty("form", out var form))
            {
                var answers = new FormAnswers(subject.Values, vector.TryGetProperty("rows", out var rows) ? VectorReader.Rows(rows) : []);
                return Json(FormRules.Evaluate(VectorReader.Form(form), answers, subject.Context));
            }

            if (vector.TryGetProperty("formula", out var formula))
            {
                return Calculations.Evaluate(formula.GetString()!, FormulaScope(subject));
            }

            return Calculations.Calculate(VectorReader.Calculation(vector.GetProperty("calc")), FormulaScope(subject))?.ToString();
        }
        catch (LogicException exception)
        {
            return exception.At is { } at ? new JsonObject { ["error"] = exception.Code, ["at"] = at } : new JsonObject { ["error"] = exception.Code };
        }
    }
}
