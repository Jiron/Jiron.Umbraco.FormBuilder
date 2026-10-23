using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Every refusal of a save (ADR-0002, ADR-0034), each with its code and the JSON path of what it refuses. Each case
/// changes one thing in a valid document, and the save reports exactly that one problem.
/// </summary>
public sealed class RefusalTests
{
    private const string Field = "$.pages[0].groups[0].fields[0]";
    private const string Group = "$.pages[0].groups[0]";

    // An escaped lone surrogate as JSON writes it, built from code points so that no tool turns it into a character.
    private static readonly string LoneSurrogate = (char)92 + "udc00";

    /// <summary>The valid document each change starts from; the schema tests start from it too.</summary>
    internal const string Valid = """
        {
          "schemaVersion": 1,
          "key": "f0000000-0000-4000-8000-000000000001",
          "settings": { "submission": { "store": "always" }, "availability": { "opensAt": "2026-11-01T08:00:00Z" } },
          "pages": [ {
            "key": "10000000-0000-4000-8000-000000000001",
            "title": { "": "Details", "de": "Angaben" },
            "groups": [ {
              "key": "20000000-0000-4000-8000-000000000001",
              "columns": 2,
              "style": "plain",
              "fields": [ {
                "key": "30000000-0000-4000-8000-000000000001",
                "type": "radio",
                "alias": "ticket",
                "label": { "": "Ticket" },
                "help": { "": "Pick one." },
                "validation": [ { "kind": "uniquePerForm" } ],
                "rules": [ { "key": "40000000-0000-4000-8000-000000000001", "effect": "show",
                  "conditions": [ { "field": "30000000-0000-4000-8000-000000000002", "op": "isOneOf", "value": [ "a", { "today": "+1d" } ] } ] } ],
                "options": { "source": "items", "items": [ { "value": "a", "label": { "": "A" } } ] },
                "settings": { "layout": "cards", "max": 3 }
              }, {
                "key": "30000000-0000-4000-8000-000000000002",
                "type": "grid",
                "rows": [ { "key": "31000000-0000-4000-8000-000000000001" } ]
              } ]
            } ]
          } ],
          "actions": [ { "key": "50000000-0000-4000-8000-000000000001", "type": "email" } ],
          "outcomes": [ { "key": "60000000-0000-4000-8000-000000000001", "kind": "message", "message": { "": "<p>Thanks</p>" } } ]
        }
        """;

    public static IEnumerable<TestCaseData> Changes()
    {
        TestCaseData Case(string name, Action<JsonObject> change, string code, string path) =>
            new TestCaseData(change, code, path).SetName($"Refuses({name})");

        static JsonObject Doc(JsonObject d) => d;
        static JsonObject Page(JsonObject d) => d["pages"]![0]!.AsObject();
        static JsonObject GroupOf(JsonObject d) => Page(d)["groups"]![0]!.AsObject();
        static JsonObject FieldOf(JsonObject d) => GroupOf(d)["fields"]![0]!.AsObject();
        static JsonObject Submission(JsonObject d) => d["settings"]!["submission"]!.AsObject();

        // Shape: unknown and missing properties, JSON types.
        yield return Case("an unknown property at the root", d => Doc(d)["name"] = "Contact", DefinitionErrors.UnknownProperty, "$.name");
        yield return Case("an unknown property on a field", d => FieldOf(d)["colour"] = "red", DefinitionErrors.UnknownProperty, $"{Field}.colour");
        yield return Case("a misspelt setting", d => Submission(d)["storeDay"] = 3, DefinitionErrors.UnknownProperty, "$.settings.submission.storeDay");
        yield return Case("an unknown property of an operand", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!["value"] = new JsonObject { ["fields"] = "x" }, DefinitionErrors.UnknownProperty, $"{Field}.rules[0].conditions[0].value.fields");
        yield return Case("no key", d => Doc(d).Remove("key"), DefinitionErrors.MissingProperty, "$.key");
        yield return Case("a field without a type", d => FieldOf(d).Remove("type"), DefinitionErrors.MissingProperty, $"{Field}.type");
        yield return Case("a rule without an effect", d => FieldOf(d)["rules"]![0]!.AsObject().Remove("effect"), DefinitionErrors.MissingProperty, $"{Field}.rules[0].effect");
        yield return Case("a condition without an operator", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!.AsObject().Remove("op"), DefinitionErrors.MissingProperty, $"{Field}.rules[0].conditions[0].op");
        yield return Case("a check without a kind", d => FieldOf(d)["validation"]![0]!.AsObject().Remove("kind"), DefinitionErrors.MissingProperty, $"{Field}.validation[0].kind");
        yield return Case("an option without a value", d => FieldOf(d)["options"]!["items"]![0]!.AsObject().Remove("value"), DefinitionErrors.MissingProperty, $"{Field}.options.items[0].value");
        yield return Case("a verification without its field", d => Submission(d)["verification"] = new JsonObject { ["days"] = 7 }, DefinitionErrors.MissingProperty, "$.settings.submission.verification.emailField");
        yield return Case("a page given as null", d => Doc(d)["pages"]!.AsArray().Add(null), DefinitionErrors.InvalidType, "$.pages[1]");
        yield return Case("a number where a string belongs", d => FieldOf(d)["type"] = 5, DefinitionErrors.InvalidType, $"{Field}.type");
        yield return Case("a string where a number belongs", d => GroupOf(d)["columns"] = "2", DefinitionErrors.InvalidType, $"{Group}.columns");
        yield return Case("a string where a boolean belongs", d => FieldOf(d)["required"] = "true", DefinitionErrors.InvalidType, $"{Field}.required");
        yield return Case("a word as a number", d => GroupOf(d)["style"] = 1, DefinitionErrors.InvalidType, $"{Group}.style");
        yield return Case("an object where a list belongs", d => Page(d)["groups"] = new JsonObject(), DefinitionErrors.InvalidType, "$.pages[0].groups");
        yield return Case("a list where an object belongs", d => FieldOf(d)["options"] = new JsonArray(), DefinitionErrors.InvalidType, $"{Field}.options");
        yield return Case("a text that is not a string", d => Page(d)["title"]!["de"] = 3, DefinitionErrors.InvalidType, "$.pages[0].title.de");
        yield return Case("a text that is a string", d => Page(d)["title"] = "Details", DefinitionErrors.InvalidType, "$.pages[0].title");
        yield return Case("an operand that is a number", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!["value"] = 4, DefinitionErrors.InvalidType, $"{Field}.rules[0].conditions[0].value");
        yield return Case("a list inside a list operand", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!["value"] = new JsonArray(new JsonArray("a")), DefinitionErrors.InvalidType, $"{Field}.rules[0].conditions[0].value[0]");
        yield return Case("a default that is a number", d => FieldOf(d)["defaultValue"] = 1, DefinitionErrors.InvalidType, $"{Field}.defaultValue");
        yield return Case("settings that are a list", d => FieldOf(d)["settings"] = new JsonArray("a"), DefinitionErrors.InvalidType, $"{Field}.settings");
        yield return Case("a null inside settings", d => FieldOf(d)["settings"]!["list"] = new JsonArray("a", null), DefinitionErrors.InvalidType, $"{Field}.settings.list[1]");
        yield return Case("a definition that is a list", _ => throw new ReplaceDocument("[]"), DefinitionErrors.InvalidType, "$");

        // Values that are not allowed.
        yield return Case("an unknown word", d => GroupOf(d)["style"] = "fancy", DefinitionErrors.InvalidValue, $"{Group}.style");
        yield return Case("a word in the wrong case", d => GroupOf(d)["style"] = "Plain", DefinitionErrors.InvalidValue, $"{Group}.style");
        yield return Case("an unknown effect", d => FieldOf(d)["rules"]![0]!["effect"] = "disable", DefinitionErrors.InvalidValue, $"{Field}.rules[0].effect");
        yield return Case("an unknown check", d => FieldOf(d)["validation"]![0]!["kind"] = "regex", DefinitionErrors.InvalidValue, $"{Field}.validation[0].kind");
        yield return Case("a key in upper case", d => Page(d)["key"] = "10000000-0000-4000-8000-00000000000A", DefinitionErrors.InvalidValue, "$.pages[0].key");
        yield return Case("a key in braces", d => Page(d)["key"] = "{10000000-0000-4000-8000-000000000001}", DefinitionErrors.InvalidValue, "$.pages[0].key");
        yield return Case("the empty GUID as a key", d => Page(d)["key"] = Guid.Empty.ToString(), DefinitionErrors.InvalidValue, "$.pages[0].key");
        yield return Case("a key with a line break after it", d => Page(d)["key"] = "10000000-0000-4000-8000-000000000001\n", DefinitionErrors.InvalidValue, "$.pages[0].key");
        yield return Case("the empty GUID as a reference", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!["field"] = Guid.Empty.ToString(), DefinitionErrors.InvalidValue, $"{Field}.rules[0].conditions[0].field");
        yield return Case("an instant with an offset", d => d["settings"]!["availability"]!["opensAt"] = "2026-11-01T09:00:00+01:00", DefinitionErrors.InvalidValue, "$.settings.availability.opensAt");
        yield return Case("an instant that does not exist", d => d["settings"]!["availability"]!["opensAt"] = "2026-02-30T00:00:00Z", DefinitionErrors.InvalidValue, "$.settings.availability.opensAt");
        yield return Case("a number too large for its property", d => GroupOf(d)["columns"] = 2147483648L, DefinitionErrors.InvalidValue, $"{Group}.columns");
        yield return Case("a culture that is not one", d => Page(d)["title"]!["de_CH"] = "Angaben", DefinitionErrors.InvalidValue, "$.pages[0].title.de_CH");
        yield return Case("a culture twice in two cases", d => Page(d)["title"]!["DE"] = "Angaben", DefinitionErrors.InvalidValue, "$.pages[0].title.DE");
        yield return Case("a default text given as null", d => Page(d)["title"]![string.Empty] = null, DefinitionErrors.InvalidValue, "$.pages[0].title");
        yield return Case("a text without its default", d => Page(d)["title"]!.AsObject().Remove(string.Empty), DefinitionErrors.InvalidValue, "$.pages[0].title");
        yield return Case("a line break in a label", d => FieldOf(d)["label"]![string.Empty] = "Ticket\ntype", DefinitionErrors.InvalidValue, $"{Field}.label['']");
        yield return Case("a line separator in a title", d => Page(d)["title"]!["de"] = "An" + (char)0x2028 + "gaben", DefinitionErrors.InvalidValue, "$.pages[0].title.de");
        yield return Case("an empty nested group", d => FieldOf(d)["rules"]![0]!["conditions"]!.AsArray().Add(new JsonObject { ["match"] = null }), DefinitionErrors.InvalidValue, $"{Field}.rules[0].conditions[1]");
        yield return Case("an empty list operand", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!["value"] = new JsonArray(), DefinitionErrors.InvalidValue, $"{Field}.rules[0].conditions[0].value");
        yield return Case("an operand naming a field and today", d => FieldOf(d)["rules"]![0]!["conditions"]![0]!["value"] = new JsonObject { ["field"] = "30000000-0000-4000-8000-000000000001", ["today"] = "+1d" }, DefinitionErrors.InvalidValue, $"{Field}.rules[0].conditions[0].value");
        yield return Case("a decimal in settings", d => FieldOf(d)["settings"]!["max"] = 2.5, DefinitionErrors.InvalidValue, $"{Field}.settings.max");
        yield return Case("a number of 16 digits in settings", d => FieldOf(d)["settings"]!["max"] = 1_000_000_000_000_000L, DefinitionErrors.InvalidValue, $"{Field}.settings.max");
        yield return Case("an empty option value", d => FieldOf(d)["options"]!["items"]![0]!["value"] = string.Empty, DefinitionErrors.InvalidValue, $"{Field}.options.items[0].value");
        yield return Case("no columns", d => GroupOf(d)["columns"] = 0, DefinitionErrors.InvalidValue, $"{Group}.columns");
        yield return Case("five columns", d => GroupOf(d)["columns"] = 5, DefinitionErrors.InvalidValue, $"{Group}.columns");
        yield return Case("a span wider than the group", d => FieldOf(d)["span"] = 3, DefinitionErrors.InvalidValue, $"{Field}.span");
        yield return Case("store for days without days", d => Submission(d)["store"] = "days", DefinitionErrors.InvalidValue, "$.settings.submission.storeDays");
        yield return Case("days without store for days", d => Submission(d)["storeDays"] = 30, DefinitionErrors.InvalidValue, "$.settings.submission.storeDays");
        yield return Case("a verification link of 91 days", d => Submission(d)["verification"] = new JsonObject { ["emailField"] = "30000000-0000-4000-8000-000000000001", ["days"] = 91 }, DefinitionErrors.InvalidValue, "$.settings.submission.verification.days");
        yield return Case("a resume link of a year and a day", d => Submission(d)["resume"] = new JsonObject { ["days"] = 366 }, DefinitionErrors.InvalidValue, "$.settings.submission.resume.days");
        yield return Case("a reference of eleven digits", d => Submission(d)["entryReference"] = new JsonObject { ["digits"] = 11 }, DefinitionErrors.InvalidValue, "$.settings.submission.entryReference.digits");
        yield return Case("an entry limit of none", d => d["settings"]!["availability"]!["maxEntries"] = 0, DefinitionErrors.InvalidValue, "$.settings.availability.maxEntries");
        yield return Case("negative places", d => FieldOf(d)["options"]!["items"]![0]!["places"] = -1, DefinitionErrors.InvalidValue, $"{Field}.options.items[0].places");
        yield return Case("a retention of no days", d => d["settings"]!["privacy"] = new JsonObject { ["retentionDays"] = new JsonObject { ["spam"] = 0 } }, DefinitionErrors.InvalidValue, "$.settings.privacy.retentionDays.spam");
        yield return Case("a redirect after ten minutes and a second", d => d["outcomes"]![0]!["redirect"] = new JsonObject { ["url"] = "/thanks", ["delaySeconds"] = 601 }, DefinitionErrors.InvalidValue, "$.outcomes[0].redirect.delaySeconds");

        // Keys.
        yield return Case("two pages with one key", d => Doc(d)["pages"]!.AsArray().Add(new JsonObject { ["key"] = "10000000-0000-4000-8000-000000000001", ["groups"] = new JsonArray(new JsonObject { ["key"] = "20000000-0000-4000-8000-000000000009" }) }), DefinitionErrors.DuplicateKey, "$.pages[1].key");
        yield return Case("a grid row with a field's key", d => GroupOf(d)["fields"]![1]!["rows"]![0]!["key"] = "30000000-0000-4000-8000-000000000001", DefinitionErrors.DuplicateKey, $"{Group}.fields[1].rows[0].key");
        yield return Case("an action with a rule's key", d => d["actions"]![0]!["key"] = "40000000-0000-4000-8000-000000000001", DefinitionErrors.DuplicateKey, "$.actions[0].key");

        // Limits of texts.
        yield return Case("a label of 501 characters", d => FieldOf(d)["label"]![string.Empty] = new string('a', 501), DefinitionErrors.LimitExceeded, $"{Field}.label['']");
        yield return Case("a help text of 2001 characters", d => FieldOf(d)["help"]![string.Empty] = new string('a', 2001), DefinitionErrors.LimitExceeded, $"{Field}.help['']");
        yield return Case("a message of 50001 characters", d => d["outcomes"]![0]!["message"]![string.Empty] = new string('a', 50001), DefinitionErrors.LimitExceeded, "$.outcomes[0].message['']");
        yield return Case("an option value of 201 characters", d => FieldOf(d)["options"]!["items"]![0]!["value"] = new string('a', 201), DefinitionErrors.LimitExceeded, $"{Field}.options.items[0].value");
        yield return Case("a type of 129 characters", d => FieldOf(d)["type"] = new string('a', 129), DefinitionErrors.LimitExceeded, $"{Field}.type");
        yield return Case("a theme of 65 characters", d => d["settings"]!["appearance"] = new JsonObject { ["theme"] = new string('a', 65) }, DefinitionErrors.LimitExceeded, "$.settings.appearance.theme");

        // Schema versions.
        yield return Case("a newer schema version", d => Doc(d)["schemaVersion"] = 2, DefinitionErrors.UnsupportedSchemaVersion, "$.schemaVersion");
        yield return Case("schema version 0", d => Doc(d)["schemaVersion"] = 0, DefinitionErrors.UnsupportedSchemaVersion, "$.schemaVersion");
        yield return Case("no schema version", d => Doc(d).Remove("schemaVersion"), DefinitionErrors.MissingProperty, "$.schemaVersion");
        yield return Case("a schema version as a string", d => Doc(d)["schemaVersion"] = "1", DefinitionErrors.InvalidType, "$.schemaVersion");
    }

    public static IEnumerable<TestCaseData> Texts()
    {
        TestCaseData Case(string name, string json, string code, string path) => new TestCaseData(json, code, path).SetName($"RefusesText({name})");

        var valid = DefinitionFixtures.Minify(Valid);
        string Replace(string from, string to)
        {
            Assert.That(valid.Split(from), Has.Length.EqualTo(2), $"one {from}");
            return valid.Replace(from, to, StringComparison.Ordinal);
        }

        yield return Case("text that is not JSON", "{\"schemaVersion\":1,", DefinitionErrors.InvalidJson, "$");
        yield return Case("a trailing comma", valid[..^1] + ",}", DefinitionErrors.InvalidJson, "$");
        yield return Case("a comment", "/* form */" + valid, DefinitionErrors.InvalidJson, "$");
        yield return Case("a property twice", Replace("\"key\":\"f0000000", "\"key\":\"f0000000-0000-4000-8000-000000000001\",\"key\":\"f0000000"), DefinitionErrors.DuplicateProperty, "$.key");
        yield return Case("schemaVersion twice", Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"), DefinitionErrors.DuplicateProperty, "$.schemaVersion");
        yield return Case("a culture twice", Replace("\"de\":\"Angaben\"", "\"de\":\"Angaben\",\"de\":\"Angaben\""), DefinitionErrors.DuplicateProperty, "$.pages[0].title.de");
        yield return Case("a setting twice", Replace("\"max\":3", "\"max\":3,\"max\":4"), DefinitionErrors.DuplicateProperty, $"{Field}.settings.max");
        yield return Case("a fraction where an integer belongs", Replace("\"columns\":2", "\"columns\":2.0"), DefinitionErrors.InvalidType, $"{Group}.columns");
        yield return Case("an exponent where an integer belongs", Replace("\"columns\":2", "\"columns\":2e0"), DefinitionErrors.InvalidType, $"{Group}.columns");
        yield return Case("a leading zero", Replace("\"columns\":2", "\"columns\":02"), DefinitionErrors.InvalidJson, "$");
        yield return Case("an escaped lone surrogate", Replace("\"Ticket\"", "\"Ti\\ud800cket\""), DefinitionErrors.InvalidValue, $"{Field}.label['']");
        yield return Case("a document nested too deeply", Replace("\"max\":3", "\"max\":" + new string('[', 70) + new string(']', 70)), DefinitionErrors.LimitExceeded, "$");
        yield return Case("a property name with a lone surrogate", Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"" + LoneSurrogate + "\":0"), DefinitionErrors.InvalidValue, "$");
        yield return Case("a setting's name with a lone surrogate", Replace("\"max\":3", "\"max\":3,\"" + LoneSurrogate + "\":1"), DefinitionErrors.InvalidValue, $"{Field}.settings");
        yield return Case("a culture with a lone surrogate", Replace("\"de\":\"Angaben\"", "\"de\":\"Angaben\",\"" + LoneSurrogate + "\":\"x\""), DefinitionErrors.InvalidValue, "$.pages[0].title");
    }

    /// <summary>The valid document with one change of <see cref="Changes"/> made.</summary>
    internal static string Changed(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(Valid)!.AsObject();
        try
        {
            change(document);
            return document.ToJsonString();
        }
        catch (ReplaceDocument replacement)
        {
            return replacement.Json;
        }
    }

    [TestCaseSource(nameof(Changes))]
    public void ASave_RefusesOneChange_WithItsCodeAndPath(Action<JsonObject> change, string code, string path) =>
        AssertRefused(DefinitionFixtures.Serializer().Read(Changed(change)), code, path);

    [TestCaseSource(nameof(Texts))]
    public void ASave_RefusesText_WithItsCodeAndPath(string json, string code, string path) =>
        AssertRefused(DefinitionFixtures.Serializer().Read(json), code, path);

    [Test]
    public void TheValidDocument_Reads()
    {
        Assert.That(DefinitionFixtures.Serializer().Read(Valid).Problems, Is.Empty);
    }

    [Test]
    public void Bytes_ThatAreNotUtf8_AreNotJson()
    {
        var bytes = Encoding.UTF8.GetBytes(DefinitionFixtures.Minify(Valid)).ToList();
        bytes.InsertRange(bytes.IndexOf((byte)'T'), new byte[] { 0xC3, 0x28 });

        AssertRefused(DefinitionFixtures.Serializer().Read(bytes.ToArray()), DefinitionErrors.InvalidJson, "$");
    }

    [Test]
    public void Text_WithALoneSurrogate_IsNotJson()
    {
        var json = DefinitionFixtures.Minify(Valid).Replace("Ticket", "Ti" + (char)0xD800 + "cket", StringComparison.Ordinal);

        AssertRefused(DefinitionFixtures.Serializer().Read(json), DefinitionErrors.InvalidJson, "$");
    }

    [Test]
    public void TheDepthLimit_RefusesTheFirstLevelPastIt_HoweverDeepTheDocument()
    {
        // The field's settings sit 8 levels deep, so 56 nested arrays reach the limit of 64 and 57 pass it.
        string Nested(int arrays) => DefinitionFixtures.Minify(Valid).Replace("\"max\":3", "\"max\":" + new string('[', arrays) + "1" + new string(']', arrays), StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionFixtures.Serializer().Read(Nested(56)).Problems, Is.Empty, "as deep as a document may be");
            AssertRefused(DefinitionFixtures.Serializer().Read(Nested(57)), DefinitionErrors.LimitExceeded, "$");
            AssertRefused(DefinitionFixtures.Serializer().Read(Nested(5000)), DefinitionErrors.LimitExceeded, "$");
        });
    }

    [Test]
    [Timeout(60_000)]
    public void ALongSettingName_OverALongArray_IsReadInLinearTime()
    {
        // Each item's path starts with the name; building every item's path as text would copy the name 400,000 times.
        var name = new string('a', 1_000_000);
        var items = string.Join(',', Enumerable.Repeat("0", 400_000));
        var valid = DefinitionFixtures.Minify(Valid).Replace("\"max\":3", $"\"{name}\":[{items}]", StringComparison.Ordinal);
        var refused = valid.Replace($"{items}]", $"{items},0.5]", StringComparison.Ordinal);
        var stopwatch = Stopwatch.StartNew();

        var read = DefinitionFixtures.Serializer().Read(valid);
        var problems = DefinitionFixtures.Serializer().Read(refused).Problems;

        Assert.Multiple(() =>
        {
            Assert.That(read.Problems, Is.Empty);
            Assert.That(problems.Select(problem => problem.Path), Is.EqualTo(new[] { $"{Field}.settings.{name}[400000]" }));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public void ADocumentLargerThanTheLimit_IsRefusedBeforeItIsRead()
    {
        var limits = new DefinitionLimits { MaxBytes = 200 };

        AssertRefused(DefinitionFixtures.Serializer(limits).Read(Valid), DefinitionErrors.LimitExceeded, "$");
        AssertRefused(DefinitionFixtures.Serializer(limits).Read(Encoding.UTF8.GetBytes(Valid)), DefinitionErrors.LimitExceeded, "$");
    }

    [TestCase(nameof(DefinitionLimits.MaxPages), "$.pages")]
    [TestCase(nameof(DefinitionLimits.MaxGroups), "$.pages[1].groups[0]")]
    [TestCase(nameof(DefinitionLimits.MaxFields), $"{Group}.fields[1].rows[0]")]
    [TestCase(nameof(DefinitionLimits.MaxOptionsPerField), $"{Field}.options.items")]
    [TestCase(nameof(DefinitionLimits.MaxActions), "$.actions")]
    [TestCase(nameof(DefinitionLimits.MaxOutcomes), "$.outcomes")]
    [TestCase(nameof(DefinitionLimits.MaxRules), $"{Field}.rules[0]")]
    public void TheElementLimits_RefuseOneMore(string limit, string path)
    {
        // The valid document has, or gets here, one more of each than the lowered limit allows.
        var document = JsonNode.Parse(Valid)!.AsObject();
        document["pages"]!.AsArray().Add(new JsonObject
        {
            ["key"] = "10000000-0000-4000-8000-000000000002",
            ["groups"] = new JsonArray(new JsonObject { ["key"] = "20000000-0000-4000-8000-000000000002" }),
        });
        document["outcomes"]!.AsArray().Add(new JsonObject { ["key"] = "60000000-0000-4000-8000-000000000002" });
        document["actions"]!.AsArray().Add(new JsonObject { ["key"] = "50000000-0000-4000-8000-000000000002", ["type"] = "webhook" });
        document["pages"]![0]!["groups"]![0]!["fields"]![0]!["options"]!["items"]!.AsArray().Add(new JsonObject { ["value"] = "b" });
        var limits = limit switch
        {
            nameof(DefinitionLimits.MaxPages) => new DefinitionLimits { MaxPages = 1 },
            nameof(DefinitionLimits.MaxGroups) => new DefinitionLimits { MaxGroups = 1 },
            nameof(DefinitionLimits.MaxFields) => new DefinitionLimits { MaxFields = 2 },
            nameof(DefinitionLimits.MaxOptionsPerField) => new DefinitionLimits { MaxOptionsPerField = 1 },
            nameof(DefinitionLimits.MaxActions) => new DefinitionLimits { MaxActions = 1 },
            nameof(DefinitionLimits.MaxOutcomes) => new DefinitionLimits { MaxOutcomes = 1 },
            _ => new DefinitionLimits { MaxRules = 0 },
        };

        Assert.That(DefinitionFixtures.Serializer().Read(document.ToJsonString()).Problems, Is.Empty, "the default limits allow it");
        AssertRefused(DefinitionFixtures.Serializer(limits).Read(document.ToJsonString()), DefinitionErrors.LimitExceeded, path);
    }

    [Test]
    public void ADocumentWithManyProblems_ReportsEachWithItsPath_UpToTheLimit()
    {
        var document = JsonNode.Parse(Valid)!.AsObject();
        var fields = document["pages"]![0]!["groups"]![0]!["fields"]!.AsArray();
        for (var i = 0; i < 150; i++)
        {
            fields.Add(new JsonObject { ["key"] = $"39000000-0000-4000-8000-{i:000000000000}", ["type"] = "text", ["colour"] = "red" });
        }

        var result = DefinitionFixtures.Serializer().Read(document.ToJsonString());

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Problems, Has.Count.EqualTo(100));
            Assert.That(result.Problems.Select(problem => problem.Path), Does.Contain($"{Group}.fields[2].colour").And.Contain($"{Group}.fields[101].colour"));
        });
    }

    private static void AssertRefused(DefinitionReadResult result, string code, string path)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.Definition, Is.Null, "refused");
            Assert.That(result.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { (code, path) }), string.Join("; ", result.Problems));
            Assert.That(result.Problems.Select(problem => problem.Message), Has.All.Not.Empty);
        });
    }

    /// <summary>Thrown by a change that replaces the whole document.</summary>
    private sealed class ReplaceDocument(string json) : Exception
    {
        public string Json { get; } = json;
    }
}
