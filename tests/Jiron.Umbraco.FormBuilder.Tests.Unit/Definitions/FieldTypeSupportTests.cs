using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// What each built-in field type supports (section 4.3.2, ADR-0035), written out here apart from the code: publishing
/// refuses exactly the common properties, checks, rules and messages a type does not support (ADR-0034), and accepts
/// every one it does.
/// </summary>
public sealed class FieldTypeSupportTests
{
    private const string FieldPath = "$.pages[0].groups[0].fields[0]";
    private const string Other = "30000000-0000-4000-8000-000000000002";
    private const string Input = "value label hideLabel help required show";
    private const string TextInput = Input + " placeholder defaultValue prefill autocomplete pattern matchesField uniquePerForm";

    // The type, what it supports, and the error kinds it raises.
    private static readonly Dictionary<string, (string Supports, string Messages)> Matrix = new(StringComparer.Ordinal)
    {
        ["text"] = (TextInput, "required minLength maxLength"),
        ["textarea"] = (TextInput, "required minLength maxLength minWords maxWords"),
        ["email"] = (TextInput, "required invalid"),
        ["phone"] = (TextInput, "required invalid"),
        ["url"] = (TextInput, "required invalid"),
        ["number"] = (Input + " placeholder defaultValue prefill autocomplete matchesField uniquePerForm", "required invalid min max step"),
        ["date"] = (Input + " defaultValue prefill autocomplete matchesField uniquePerForm", "required invalid dateWindow"),
        ["time"] = (Input + " defaultValue prefill matchesField uniquePerForm", "required invalid min max step"),
        ["datetime"] = (Input + " defaultValue prefill matchesField uniquePerForm", "required invalid dateWindow step"),
        ["rating"] = (Input + " defaultValue prefill", "required invalid"),
        ["scale"] = (Input + " defaultValue prefill", "required invalid"),
        ["slider"] = (Input + " defaultValue prefill", "required invalid min max step"),
        ["calculation"] = ("value label hideLabel help show", string.Empty),
        ["dropdown"] = (Input + " defaultValue prefill autocomplete options places", "required invalid selections full"),
        ["radio"] = (Input + " defaultValue prefill options places", "required invalid full"),
        ["checkboxes"] = (Input + " defaultValue prefill options places", "required invalid selections full"),
        ["grid"] = (Input + " options rows", "required invalid"),
        ["ranking"] = (Input + " options", "required invalid"),
        ["checkbox"] = (Input + " defaultValue prefill", "required"),
        ["yesno"] = (Input + " defaultValue prefill", "required"),
        ["consent"] = (Input, "required"),
        ["country"] = (Input + " defaultValue prefill autocomplete", "required invalid"),
        ["name"] = (Input + " prefill", "required invalid"),
        ["address"] = (Input + " prefill", "required invalid"),
        ["file"] = (Input, "required fileType fileSize fileCount"),
        ["signature"] = (Input, "required invalid"),
        ["repeater"] = ("value label hideLabel help show fields", string.Empty),
        ["hidden"] = ("value label defaultValue prefill show", string.Empty),
        ["heading"] = ("show", string.Empty),
        ["paragraph"] = ("show", string.Empty),
        ["divider"] = ("show", string.Empty),
        ["image"] = ("show", string.Empty),
        ["captcha"] = (string.Empty, string.Empty),
    };

    private static readonly string[] MessageKinds =
    [
        "required", "invalid", "minLength", "maxLength", "minWords", "maxWords", "min", "max", "step", "dateWindow",
        "selections", "fileType", "fileSize", "fileCount", "full",
    ];

    public static IEnumerable<TestCaseData> Types() =>
        Matrix.Keys.Select(type => new TestCaseData(type).SetName($"Supports({type})"));

    [Test]
    public void TheBuiltInTypes_AreThoseOfSection432()
    {
        Assert.That(FieldTypeCatalog.BuiltIn.All.Select(type => type.Alias).Order(StringComparer.Ordinal), Is.EqualTo(Matrix.Keys.Order(StringComparer.Ordinal)));
    }

    [TestCaseSource(nameof(Types))]
    public void AFieldType_RefusesExactlyWhatItDoesNotSupport(string type)
    {
        var supports = Matrix[type].Supports.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var messages = Matrix[type].Messages.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        Assert.That(Problems(Base(type, supports)), Is.Empty, "the field as it starts");

        Assert.Multiple(() =>
        {
            foreach (var (feature, change, path, code) in Changes(supports))
            {
                var field = Base(type, supports);
                change(field);
                var expected = supports.Contains(feature) ? [] : new[] { $"{code} at {FieldPath}{path}" };
                Assert.That(Problems(field), Is.EqualTo(expected), $"{type}: {feature}");
            }

            foreach (var kind in MessageKinds)
            {
                var field = Base(type, supports);
                field["messages"] = new JsonObject { [kind] = new JsonObject { [string.Empty] = "A message." } };
                var expected = messages.Contains(kind) ? [] : new[] { $"{DefinitionErrors.PropertyNotSupported} at {FieldPath}.messages.{kind}" };
                Assert.That(Problems(field), Is.EqualTo(expected), $"{type}: the {kind} message");
            }
        });
    }

    // Each change sets one property to a value other than its default, and names the feature that allows it.
    private static IEnumerable<(string Feature, Action<JsonObject> Change, string Path, string Code)> Changes(HashSet<string> supports)
    {
        const string NotSupported = DefinitionErrors.PropertyNotSupported;
        static JsonObject Text(string text) => new() { [string.Empty] = text };
        static JsonArray One(JsonObject item) => new(item);

        yield return ("label", f => f["label"] = Text("Label"), ".label", NotSupported);
        yield return ("hideLabel", f => f["hideLabel"] = true, ".hideLabel", NotSupported);
        yield return ("help", f => f["help"] = Text("Help"), ".help", NotSupported);
        yield return ("placeholder", f => f["placeholder"] = Text("Placeholder"), ".placeholder", NotSupported);
        yield return ("required", f => f["required"] = true, ".required", NotSupported);
        yield return ("defaultValue", f => f["defaultValue"] = "a", ".defaultValue", NotSupported);
        yield return ("prefill", f => f["prefill"] = new JsonObject { ["query"] = "ref" }, ".prefill", NotSupported);
        yield return ("autocomplete", f => f["autocomplete"] = "on", ".autocomplete", NotSupported);
        yield return ("pattern", f => f["validation"] = One(new JsonObject { ["kind"] = "pattern", ["pattern"] = "a" }), ".validation[0]", NotSupported);
        yield return ("matchesField", f => f["validation"] = One(new JsonObject { ["kind"] = "matchesField", ["field"] = Other }), ".validation[0]", NotSupported);
        yield return ("uniquePerForm", f => f["validation"] = One(new JsonObject { ["kind"] = "uniquePerForm" }), ".validation[0]", NotSupported);
        yield return ("show", f => f["rules"] = One(new JsonObject { ["key"] = "40000000-0000-4000-8000-000000000001", ["effect"] = "show" }), ".rules[0].effect", DefinitionErrors.RuleEffectInvalid);
        yield return ("required", f => f["rules"] = One(new JsonObject { ["key"] = "40000000-0000-4000-8000-000000000001", ["effect"] = "require" }), ".rules[0].effect", DefinitionErrors.RuleEffectInvalid);
        yield return ("value", f => f["formerAliases"] = new JsonArray("formerName"), ".formerAliases", NotSupported);
        yield return ("value", f => f["sensitive"] = true, ".sensitive", NotSupported);
        yield return ("value", f => f["includeInEmails"] = false, ".includeInEmails", NotSupported);
        yield return ("value", f => f["keep"] = "untilActionsRun", ".keep", NotSupported);
        if (!supports.Contains("value"))
        {
            yield return ("value", f => f["alias"] = "subject", ".alias", NotSupported);
        }

        if (supports.Contains("options"))
        {
            yield return ("places", f => f["options"] = OptionsWith(new JsonObject { ["value"] = "a", ["places"] = 5 }), ".options.items[0].places", NotSupported);
        }
        else
        {
            yield return ("options", f => f["options"] = OptionsWith(new JsonObject { ["value"] = "a" }), ".options", NotSupported);
        }

        if (!supports.Contains("rows"))
        {
            yield return ("rows", f => f["rows"] = One(new JsonObject { ["key"] = "31000000-0000-4000-8000-000000000001" }), ".rows", NotSupported);
        }

        if (!supports.Contains("fields"))
        {
            yield return ("fields", f => f["fields"] = One(Child()), ".fields", NotSupported);
        }
    }

    private static JsonObject OptionsWith(JsonObject item) => new() { ["items"] = new JsonArray(item) };

    private static JsonObject Child() => new() { ["key"] = "30000000-0000-4000-8000-000000000003", ["type"] = "text", ["alias"] = "child" };

    // A field of the type with the parts it cannot do without.
    private static JsonObject Base(string type, HashSet<string> supports)
    {
        var field = new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000001", ["type"] = type };
        if (supports.Contains("value"))
        {
            field["alias"] = "subject";
        }

        if (type is "dropdown" or "radio" or "checkboxes" or "ranking")
        {
            field["options"] = OptionsWith(new JsonObject { ["value"] = "a" });
        }

        if (supports.Contains("rows"))
        {
            field["rows"] = new JsonArray(new JsonObject { ["key"] = "31000000-0000-4000-8000-000000000001" });
        }

        if (supports.Contains("fields"))
        {
            field["fields"] = new JsonArray(Child());
        }

        return field;
    }

    private static List<string> Problems(JsonObject field)
    {
        var document = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["key"] = "f0000000-0000-4000-8000-000000000001",
            ["settings"] = new JsonObject { ["developer"] = new JsonObject { ["queryValues"] = new JsonArray("ref") } },
            ["pages"] = new JsonArray(new JsonObject
            {
                ["key"] = "10000000-0000-4000-8000-000000000001",
                ["groups"] = new JsonArray(new JsonObject
                {
                    ["key"] = "20000000-0000-4000-8000-000000000001",
                    ["fields"] = new JsonArray(field, new JsonObject { ["key"] = Other, ["type"] = "text", ["alias"] = "other" }),
                }),
            }),
        };
        return StructureTests.Publish(document.ToJsonString()).Select(problem => $"{problem.Code} at {problem.Path}").ToList();
    }
}
