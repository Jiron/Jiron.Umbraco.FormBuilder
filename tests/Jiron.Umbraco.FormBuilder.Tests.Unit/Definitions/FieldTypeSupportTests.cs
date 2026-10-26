using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// What each built-in field type supports and declares (section 4.3.2, ADR-0035, ADR-0055), written out here apart from
/// the code: publishing refuses exactly the common properties, checks, rules and messages a type does not support
/// (ADR-0034), and accepts every one it does; the registry gives each type the palette group, storage, rule kind and
/// settings written here.
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
        ["email"] = (TextInput, "required invalid confirmation"),
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

    // The type's palette group, how its answer is stored, how rules compare it, what it may do of the three ("multiple"
    // setting, "onePerForm", "serverOnlyDefault"), its settings (ADR-0002), and those of them visitors do not see.
    private static readonly Dictionary<string, (string Palette, string Storage, string? Kind, string Traits, string Settings, string Private)> Declared = new(StringComparer.Ordinal)
    {
        ["text"] = ("Text", "Text", "Text", "", "minLength maxLength prefix suffix counter", ""),
        ["textarea"] = ("Text", "LongText", "Text", "", "rows autoGrow minLength maxLength minWords maxWords counter", ""),
        ["email"] = ("Text", "Text", "Text", "", "confirm confirmLabel domains", "domains"),
        ["phone"] = ("Text", "Text", "Text", "", "defaultCountry", ""),
        ["url"] = ("Text", "Text", "Text", "", "schemes", ""),
        ["number"] = ("NumbersAndDates", "Number", "Number", "", "min max step decimals prefix suffix", ""),
        ["date"] = ("NumbersAndDates", "Date", "Date", "", "min max weekdaysOnly excluded defaultToday", ""),
        ["time"] = ("NumbersAndDates", "Text", "Time", "", "min max stepMinutes", ""),
        ["datetime"] = ("NumbersAndDates", "Date", "DateTime", "", "min max weekdaysOnly excluded defaultToday stepMinutes", ""),
        ["rating"] = ("NumbersAndDates", "Number", "Number", "", "count symbol lowLabel highLabel", ""),
        ["scale"] = ("NumbersAndDates", "Number", "Number", "", "from to lowLabel highLabel", ""),
        ["slider"] = ("NumbersAndDates", "Number", "Number", "", "min max step unit showValue", ""),
        ["calculation"] = ("NumbersAndDates", "Number", "Number", "", "formula format visible", ""),
        ["dropdown"] = ("Choices", "Text", "Choice", "multiple", "prompt multiple minSelections maxSelections", ""),
        ["radio"] = ("Choices", "Text", "Choice", "", "layout other otherLabel", ""),
        ["checkboxes"] = ("Choices", "Text", "Choices", "", "layout minSelections maxSelections other otherLabel", ""),
        ["grid"] = ("Choices", "None", null, "multiple", "multiple scale", ""),
        ["ranking"] = ("Choices", "Text", "Choices", "", "top", ""),
        ["checkbox"] = ("Choices", "Bool", "Checkbox", "", "text", ""),
        ["yesno"] = ("Choices", "Text", "Choice", "", "yesLabel noLabel style", ""),
        ["consent"] = ("Choices", "Text", "Checkbox", "", "text", ""),
        ["country"] = ("Choices", "Text", "Choice", "", "allowed top default", ""),
        ["name"] = ("People", "Json", null, "", "parts requiredParts", ""),
        ["address"] = ("People", "Json", null, "", "parts requiredParts", ""),
        ["file"] = ("Files", "File", "Files", "", "types extensions maxBytes maxFiles dropZone thumbnails camera", ""),
        ["signature"] = ("Files", "File", "Files", "", "width height", ""),
        ["repeater"] = ("Structure", "None", null, "", "minRows maxRows addLabel removeLabel", ""),
        ["hidden"] = ("Structure", "Text", "Text", "serverOnlyDefault", "", ""),
        ["heading"] = ("Content", "None", null, "", "text level", ""),
        ["paragraph"] = ("Content", "None", null, "", "html", ""),
        ["divider"] = ("Content", "None", null, "", "", ""),
        ["image"] = ("Content", "None", null, "", "media alt size", ""),
        ["captcha"] = ("Protection", "None", null, "onePerForm", "", ""),
    };

    private static readonly string[] MessageKinds =
    [
        "required", "invalid", "minLength", "maxLength", "minWords", "maxWords", "min", "max", "step", "dateWindow",
        "selections", "fileType", "fileSize", "fileCount", "full", "confirmation",
    ];

    public static IEnumerable<TestCaseData> Types() =>
        Matrix.Keys.Select(type => new TestCaseData(type).SetName($"Supports({type})"));

    public static IEnumerable<TestCaseData> DeclaredTypes() =>
        Declared.Keys.Select(type => new TestCaseData(type).SetName($"Declares({type})"));

    [Test]
    public void TheBuiltInTypes_AreThoseOfSection432()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TestFieldTypes.Registry.All.Select(type => type.Alias).Order(StringComparer.Ordinal), Is.EqualTo(Matrix.Keys.Order(StringComparer.Ordinal)));
            Assert.That(Declared.Keys.Order(StringComparer.Ordinal), Is.EqualTo(Matrix.Keys.Order(StringComparer.Ordinal)), "both tables");
        });
    }

    [TestCaseSource(nameof(DeclaredTypes))]
    public void AFieldType_DeclaresWhatSection432Says(string alias)
    {
        static string[] Split(string list) => list.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var type = TestFieldTypes.Registry.FindType(alias)!;
        var traits = TestFieldTypes.Registry.Find(alias)!;
        var (palette, storage, kind, flags, settings, hidden) = Declared[alias];

        Assert.Multiple(() =>
        {
            Assert.That(type.Declaration.PaletteGroup.ToString(), Is.EqualTo(palette), "palette group");
            Assert.That(traits.HoldsValue, Is.EqualTo(Matrix[alias].Supports.Contains("value", StringComparison.Ordinal)), "holds a value");
            Assert.That(traits.Storage.ToString(), Is.EqualTo(storage), "storage");
            Assert.That(traits.Kind?.ToString(), Is.EqualTo(kind), "rule kind");
            Assert.That(traits.MultipleSetting, Is.EqualTo(Split(flags).Contains("multiple")), "multiple setting");
            Assert.That(traits.OnePerForm, Is.EqualTo(Split(flags).Contains("onePerForm")), "one per form");
            Assert.That(traits.ServerOnlyDefault, Is.EqualTo(Split(flags).Contains("serverOnlyDefault")), "server-only default");
            Assert.That(type.SettingsSchema.Settings.Select(setting => setting.Name), Is.EquivalentTo(Split(settings)), "settings");
            Assert.That(traits.PublicSettings, Is.EquivalentTo(Split(settings).Except(Split(hidden))), "public settings");
        });
    }

    [Test]
    public void TheMessageList_HoldsEveryMessageOfTheRecord_EachWithItsOwnKind()
    {
        var info = (JsonTypeInfo<FieldMessages>)DefinitionJsonContext.Default.Options.GetTypeInfo(typeof(FieldMessages));
        var properties = info.Properties.Select(property => property.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(FieldMessageProperties.All.Select(message => message.Name), Is.EqualTo(properties), "every message of the record, in its order");
            Assert.That(FieldMessageProperties.All.Select(message => message.Kind), Is.EquivalentTo(Enum.GetValues<FieldMessageKinds>().Where(kind => kind != FieldMessageKinds.None)), "every error kind, once");
            Assert.That(MessageKinds, Is.EqualTo(properties), "this test's list too");
            foreach (var message in FieldMessageProperties.All)
            {
                var messages = new JsonObject { [message.Name] = new JsonObject { [string.Empty] = "A message." } }.Deserialize(info)!;
                Assert.That(FieldMessageProperties.All.Where(each => each.Read(messages) is not null).Select(each => each.Name), Is.EqualTo(new[] { message.Name }), $"{message.Name} reads its own message");
            }
        });
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

    // A field of the type with the parts it cannot do without, its required settings among them (ADR-0056).
    private static JsonObject Base(string type, HashSet<string> supports)
    {
        static JsonObject Text(string text) => new() { [string.Empty] = text };
        var field = new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000001", ["type"] = type };
        if (supports.Contains("value"))
        {
            field["alias"] = "subject";
        }

        // A grid's columns come from its options here.
        if (type is "dropdown" or "radio" or "checkboxes" or "ranking" or "grid")
        {
            field["options"] = OptionsWith(new JsonObject { ["value"] = "a" });
        }

        JsonObject? settings = type switch
        {
            "calculation" => new() { ["formula"] = "amount * 2" },
            "consent" => new() { ["text"] = Text("I agree to the terms.") },
            "heading" => new() { ["text"] = Text("Your visit") },
            "paragraph" => new() { ["html"] = Text("<p>Welcome.</p>") },
            "image" => new() { ["media"] = "5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c", ["alt"] = Text("A view of the lake") },
            _ => null,
        };
        if (settings is not null)
        {
            field["settings"] = settings;
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
                    ["fields"] = new JsonArray(
                        field,
                        new JsonObject { ["key"] = Other, ["type"] = "text", ["alias"] = "other" },
                        new JsonObject { ["key"] = "30000000-0000-4000-8000-000000000004", ["type"] = "number", ["alias"] = "amount" }),
                }),
            }),
        };
        return StructureTests.Publish(document.ToJsonString()).Select(problem => $"{problem.Code} at {problem.Path}").ToList();
    }
}
