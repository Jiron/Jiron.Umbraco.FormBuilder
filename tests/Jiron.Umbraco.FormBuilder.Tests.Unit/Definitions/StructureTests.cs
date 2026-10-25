using System.Reflection;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The structural validation that publishing runs (ROADMAP 4.2b, ADR-0035). Each case changes one thing in a
/// publishable document, and publishing reports exactly that one problem, with its code and its JSON path.
/// </summary>
public sealed class StructureTests
{
    private const string Group = "$.pages[0].groups[0]";
    private const string Group2 = "$.pages[0].groups[1]";
    private const string Nowhere = "30000000-0000-4000-8000-0000000000ff";

    /// <summary>
    /// A document that publishes: text with a pattern and a query prefill, two email fields that must match, a country
    /// that filters the cities, a rule on a choice, a name, a heading, a grid, a repeatable group, a checkbox required
    /// by a rule, the captcha placement, a second page shown by a query value, a check across fields, an action with a
    /// run rule, and a redirect and a message outcome.
    /// </summary>
    internal const string Publishable = """
        {
          "schemaVersion": 1,
          "key": "f0000000-0000-4000-8000-000000000001",
          "settings": {
            "submission": { "verification": { "emailField": "30000000-0000-4000-8000-000000000002" } },
            "developer": { "queryValues": [ "ref" ], "contextValues": [ "campaign" ] }
          },
          "pages": [ {
            "key": "10000000-0000-4000-8000-000000000001",
            "groups": [ {
              "key": "20000000-0000-4000-8000-000000000001",
              "fields": [
                { "key": "30000000-0000-4000-8000-000000000001", "type": "text", "alias": "name", "prefill": { "query": "ref" },
                  "validation": [ { "kind": "pattern", "pattern": "[A-Za-z ]+" } ] },
                { "key": "30000000-0000-4000-8000-000000000002", "type": "email", "alias": "email" },
                { "key": "30000000-0000-4000-8000-000000000003", "type": "email", "alias": "confirmEmail",
                  "validation": [ { "kind": "matchesField", "field": "30000000-0000-4000-8000-000000000002" } ] },
                { "key": "30000000-0000-4000-8000-000000000004", "type": "radio", "alias": "country",
                  "options": { "items": [ { "value": "ch" }, { "value": "de" } ] } },
                { "key": "30000000-0000-4000-8000-000000000005", "type": "dropdown", "alias": "city",
                  "options": { "items": [ { "value": "Zurich", "parentValues": [ "ch" ] }, { "value": "Berlin", "parentValues": [ "de" ] } ],
                    "parent": "30000000-0000-4000-8000-000000000004" } },
                { "key": "30000000-0000-4000-8000-000000000006", "type": "number", "alias": "guests",
                  "rules": [ { "key": "40000000-0000-4000-8000-000000000001", "effect": "show",
                    "conditions": [ { "field": "30000000-0000-4000-8000-000000000004", "op": "equals", "value": "ch" } ] } ] },
                { "key": "30000000-0000-4000-8000-000000000007", "type": "name", "alias": "fullName" },
                { "key": "30000000-0000-4000-8000-000000000008", "type": "heading", "settings": { "level": 2 } }
              ]
            }, {
              "key": "20000000-0000-4000-8000-000000000002",
              "rules": [ { "key": "40000000-0000-4000-8000-000000000002", "effect": "hide",
                "conditions": [ { "field": "30000000-0000-4000-8000-000000000001", "op": "isEmpty" } ] } ],
              "fields": [
                { "key": "30000000-0000-4000-8000-000000000009", "type": "grid", "alias": "rating",
                  "options": { "items": [ { "value": "1" }, { "value": "2" } ] }, "rows": [ { "key": "31000000-0000-4000-8000-000000000001" } ] },
                { "key": "30000000-0000-4000-8000-00000000000a", "type": "repeater", "alias": "people",
                  "fields": [ { "key": "30000000-0000-4000-8000-00000000000b", "type": "text", "alias": "personName" } ] },
                { "key": "30000000-0000-4000-8000-00000000000c", "type": "checkbox", "alias": "newsletter",
                  "rules": [ { "key": "40000000-0000-4000-8000-000000000003", "effect": "require",
                    "conditions": [ { "field": "30000000-0000-4000-8000-000000000002", "op": "isNotEmpty" } ] } ] },
                { "key": "30000000-0000-4000-8000-00000000000d", "type": "captcha" }
              ]
            } ]
          }, {
            "key": "10000000-0000-4000-8000-000000000002",
            "rules": [ { "key": "40000000-0000-4000-8000-000000000004", "effect": "show",
              "conditions": [ { "context": "query.ref", "op": "startsWith", "value": "partner" } ] } ],
            "groups": [ { "key": "20000000-0000-4000-8000-000000000003",
              "fields": [ { "key": "30000000-0000-4000-8000-00000000000e", "type": "textarea", "alias": "notes" } ] } ]
          } ],
          "validationRules": [ { "key": "41000000-0000-4000-8000-000000000001", "field": "30000000-0000-4000-8000-000000000006",
            "message": { "": "At most ten." },
            "conditions": [ { "field": "30000000-0000-4000-8000-000000000006", "op": "greaterThan", "value": "10" } ] } ],
          "actions": [ { "key": "50000000-0000-4000-8000-000000000001", "type": "email",
            "rules": [ { "key": "40000000-0000-4000-8000-000000000005", "effect": "run",
              "conditions": [ { "field": "30000000-0000-4000-8000-00000000000c", "op": "isChecked" } ] } ] } ],
          "outcomes": [
            { "key": "60000000-0000-4000-8000-000000000001", "kind": "redirect",
              "redirect": { "url": "https://example.com/thanks?ref={{ query.ref | url }}" },
              "rules": [ { "key": "40000000-0000-4000-8000-000000000006", "effect": "use",
                "conditions": [ { "context": "culture", "op": "startsWith", "value": "de" } ] } ] },
            { "key": "60000000-0000-4000-8000-000000000002", "kind": "message", "message": { "": "<p>Thanks</p>" },
              "redirect": { "content": "73000000-0000-4000-8000-000000000001", "delaySeconds": 5 } }
          ]
        }
        """;

    // Field n of the publishable document, by its key's last digits.
    private static string K(int n) => $"30000000-0000-4000-8000-{n:x12}";

    private static string F(int index) => $"{Group}.fields[{index}]";

    private static string F2(int index) => $"{Group2}.fields[{index}]";

    private static string C(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static JsonObject Field(JsonObject d, int index) => d["pages"]![0]!["groups"]![0]!["fields"]![index]!.AsObject();

    private static JsonObject Field2(JsonObject d, int index) => d["pages"]![0]!["groups"]![1]!["fields"]![index]!.AsObject();

    private static JsonObject Guests(JsonObject d) => Field(d, 5)["rules"]![0]!["conditions"]![0]!.AsObject();

    private static JsonObject QueryRule(JsonObject d) => d["pages"]![1]!["rules"]![0]!["conditions"]![0]!.AsObject();

    private static JsonObject CheckAcross(JsonObject d) => d["validationRules"]![0]!["conditions"]![0]!.AsObject();

    private static JsonArray Items(JsonObject d, int field) => Field(d, field)["options"]!["items"]!.AsArray();

    private static JsonObject Developer(JsonObject d) => d["settings"]!["developer"]!.AsObject();

    // A hidden field at the end of the first group: its value is set on the server when the entry is submitted.
    private static void AddHidden(JsonObject d) => d["pages"]![0]!["groups"]![0]!["fields"]!.AsArray().Add(new JsonObject
    {
        ["key"] = K(0x20),
        ["type"] = "hidden",
        ["alias"] = "source",
        ["defaultValue"] = "{{ member.email }}",
    });

    private static JsonObject Rule(string key, string effect, params JsonNode[] conditions) =>
        new() { ["key"] = key, ["effect"] = effect, ["conditions"] = new JsonArray(conditions) };

    public static IEnumerable<TestCaseData> Changes()
    {
        TestCaseData Case(string name, Action<JsonObject> change, string code, string path) =>
            new TestCaseData(change, code, path).SetName($"Refuses({name})");

        // Parts an element cannot do without.
        yield return Case("a form without pages", d =>
        {
            d.Clear();
            d["schemaVersion"] = 1;
            d["key"] = "f0000000-0000-4000-8000-000000000001";
        }, DefinitionErrors.PartMissing, "$.pages");
        yield return Case("a page without groups", d => d["pages"]!.AsArray().Add(new JsonObject { ["key"] = "10000000-0000-4000-8000-000000000003" }), DefinitionErrors.PartMissing, "$.pages[2].groups");
        yield return Case("a dropdown without options", d => Field(d, 4).Remove("options"), DefinitionErrors.PartMissing, $"{F(4)}.options");
        yield return Case("own options without one", d => Field(d, 4)["options"] = new JsonObject { ["source"] = "items" }, DefinitionErrors.PartMissing, $"{F(4)}.options.items");
        yield return Case("options from a list without the list", d => Field(d, 4)["options"] = new JsonObject { ["source"] = "list" }, DefinitionErrors.PartMissing, $"{F(4)}.options.list");
        yield return Case("a grid without rows", d => Field2(d, 0).Remove("rows"), DefinitionErrors.PartMissing, $"{F2(0)}.rows");
        yield return Case("a repeatable group without fields", d => Field2(d, 1).Remove("fields"), DefinitionErrors.PartMissing, $"{F2(1)}.fields");
        yield return Case("a form for member groups without one", d => d["settings"]!["availability"] = new JsonObject { ["members"] = "groups" }, DefinitionErrors.PartMissing, "$.settings.availability.memberGroups");
        yield return Case("the captcha level without a provider", d => d["settings"]!["protection"] = new JsonObject { ["level"] = "captcha" }, DefinitionErrors.PartMissing, "$.settings.protection.captcha");
        yield return Case("a redirect outcome without a redirect", d => d["outcomes"]![0]!.AsObject().Remove("redirect"), DefinitionErrors.PartMissing, "$.outcomes[0].redirect");
        yield return Case("a redirect to nowhere", d => d["outcomes"]![1]!["redirect"] = new JsonObject { ["delaySeconds"] = 5 }, DefinitionErrors.PartMissing, "$.outcomes[1].redirect");

        // Field types and the properties they support.
        yield return Case("a type the site lacks", d => Field(d, 7)["type"] = "colourPicker", DefinitionErrors.UnknownType, $"{F(7)}.type");
        yield return Case("a type the site lacks, with fields and rules that name it", d => Field(d, 3)["type"] = "colourPicker", DefinitionErrors.UnknownType, $"{F(3)}.type");
        yield return Case("a type the site lacks, with a row a rule names", d =>
        {
            Field2(d, 0)["type"] = "colourPicker";
            Guests(d).ReplaceWith(new JsonObject { ["field"] = "31000000-0000-4000-8000-000000000001", ["op"] = "equals", ["value"] = "1" });
        }, DefinitionErrors.UnknownType, $"{F2(0)}.type");
        yield return Case("a label on a heading", d => Field(d, 7)["label"] = new JsonObject { [string.Empty] = "Hello" }, DefinitionErrors.PropertyNotSupported, $"{F(7)}.label");
        yield return Case("an alias on a heading", d => Field(d, 7)["alias"] = "title", DefinitionErrors.PropertyNotSupported, $"{F(7)}.alias");
        yield return Case("former aliases on a heading", d => Field(d, 7)["formerAliases"] = new JsonArray("title"), DefinitionErrors.PropertyNotSupported, $"{F(7)}.formerAliases");
        yield return Case("a sensitive heading", d => Field(d, 7)["sensitive"] = true, DefinitionErrors.PropertyNotSupported, $"{F(7)}.sensitive");
        yield return Case("a heading left out of mail", d => Field(d, 7)["includeInEmails"] = false, DefinitionErrors.PropertyNotSupported, $"{F(7)}.includeInEmails");
        yield return Case("a heading kept until the actions run", d => Field(d, 7)["keep"] = "untilActionsRun", DefinitionErrors.PropertyNotSupported, $"{F(7)}.keep");
        yield return Case("a required heading", d => Field(d, 7)["required"] = true, DefinitionErrors.PropertyNotSupported, $"{F(7)}.required");
        yield return Case("a placeholder on radio buttons", d => Field(d, 3)["placeholder"] = new JsonObject { [string.Empty] = "Pick" }, DefinitionErrors.PropertyNotSupported, $"{F(3)}.placeholder");
        yield return Case("a default on a grid", d => Field2(d, 0)["defaultValue"] = "1", DefinitionErrors.PropertyNotSupported, $"{F2(0)}.defaultValue");
        yield return Case("a list as the default of one choice", d => Field(d, 3)["defaultValue"] = new JsonArray("ch"), DefinitionErrors.PropertyNotSupported, $"{F(3)}.defaultValue");
        yield return Case("a prefilled grid", d => Field2(d, 0)["prefill"] = new JsonObject { ["query"] = "ref" }, DefinitionErrors.PropertyNotSupported, $"{F2(0)}.prefill");
        yield return Case("an autocomplete token on a checkbox", d => Field2(d, 2)["autocomplete"] = "on", DefinitionErrors.PropertyNotSupported, $"{F2(2)}.autocomplete");
        yield return Case("a word limit message on a text field", d => Field(d, 0)["messages"] = new JsonObject { ["minWords"] = new JsonObject { [string.Empty] = "More." } }, DefinitionErrors.PropertyNotSupported, $"{F(0)}.messages.minWords");
        yield return Case("a pattern check on a number", d => Field(d, 5)["validation"] = new JsonArray(new JsonObject { ["kind"] = "pattern", ["pattern"] = "1" }), DefinitionErrors.PropertyNotSupported, $"{F(5)}.validation[0]");
        yield return Case("options on a text field", d => Field(d, 0)["options"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["value"] = "a" }) }, DefinitionErrors.PropertyNotSupported, $"{F(0)}.options");
        yield return Case("places on a grid's columns", d => Field2(d, 0)["options"]!["items"]![0]!["places"] = 3, DefinitionErrors.PropertyNotSupported, $"{F2(0)}.options.items[0].places");
        yield return Case("rows on a text field", d => Field(d, 0)["rows"] = new JsonArray(new JsonObject { ["key"] = "31000000-0000-4000-8000-000000000009" }), DefinitionErrors.PropertyNotSupported, $"{F(0)}.rows");
        yield return Case("child fields on a text field", d => Field(d, 0)["fields"] = new JsonArray(new JsonObject { ["key"] = K(0x20), ["type"] = "text", ["alias"] = "inner" }), DefinitionErrors.PropertyNotSupported, $"{F(0)}.fields");
        yield return Case("member groups for anyone", d => d["settings"]!["availability"] = new JsonObject { ["memberGroups"] = new JsonArray("70000000-0000-4000-8000-000000000001") }, DefinitionErrors.PropertyNotSupported, "$.settings.availability.memberGroups");
        yield return Case("a captcha provider below the captcha level", d => d["settings"]!["protection"] = new JsonObject { ["level"] = "standard", ["captcha"] = new JsonObject { ["provider"] = "hcaptcha" } }, DefinitionErrors.PropertyNotSupported, "$.settings.protection.captcha");
        yield return Case("a message on a redirect outcome", d => d["outcomes"]![0]!["message"] = new JsonObject { [string.Empty] = "Hi" }, DefinitionErrors.PropertyNotSupported, "$.outcomes[0].message");
        yield return Case("answers on a redirect outcome", d => d["outcomes"]![0]!["showAnswers"] = true, DefinitionErrors.PropertyNotSupported, "$.outcomes[0].showAnswers");
        yield return Case("a delay on a redirect outcome", d => d["outcomes"]![0]!["redirect"]!["delaySeconds"] = 3, DefinitionErrors.PropertyNotSupported, "$.outcomes[0].redirect.delaySeconds");

        // Where a type may be.
        yield return Case("a repeatable group in a repeatable group", d => Field2(d, 1)["fields"]!.AsArray().Add(new JsonObject
        {
            ["key"] = K(0x21),
            ["type"] = "repeater",
            ["alias"] = "inner",
            ["fields"] = new JsonArray(new JsonObject { ["key"] = K(0x22), ["type"] = "text", ["alias"] = "innerName" }),
        }), DefinitionErrors.FieldNotAllowed, $"{F2(1)}.fields[1].type");
        yield return Case("the captcha in a repeatable group", d =>
        {
            d["pages"]![0]!["groups"]![1]!["fields"]!.AsArray().RemoveAt(3);
            Field2(d, 1)["fields"]!.AsArray().Add(new JsonObject { ["key"] = K(0x21), ["type"] = "captcha" });
        }, DefinitionErrors.FieldNotAllowed, $"{F2(1)}.fields[1].type");
        yield return Case("a second captcha", d => d["pages"]![0]!["groups"]![1]!["fields"]!.AsArray().Add(new JsonObject { ["key"] = K(0x21), ["type"] = "captcha" }), DefinitionErrors.FieldNotAllowed, $"{F2(4)}.type");

        // Aliases.
        yield return Case("a text field without an alias", d => Field(d, 0).Remove("alias"), DefinitionErrors.AliasInvalid, $"{F(0)}.alias");
        yield return Case("an alias with a capital first", d => Field(d, 0)["alias"] = "Name", DefinitionErrors.AliasInvalid, $"{F(0)}.alias");
        yield return Case("an alias with a hyphen", d => Field(d, 1)["alias"] = "e-mail", DefinitionErrors.AliasInvalid, $"{F(1)}.alias");
        yield return Case("an alias with a letter outside ASCII", d => Field(d, 1)["alias"] = "m" + C(0xE9) + "l", DefinitionErrors.AliasInvalid, $"{F(1)}.alias");
        yield return Case("an alias of 65 characters", d => Field(d, 0)["alias"] = new string('a', 65), DefinitionErrors.AliasInvalid, $"{F(0)}.alias");
        yield return Case("a former alias with a space", d => Field(d, 0)["formerAliases"] = new JsonArray("full name"), DefinitionErrors.AliasInvalid, $"{F(0)}.formerAliases[0]");
        yield return Case("the reserved alias form", d => Field(d, 0)["alias"] = "form", DefinitionErrors.AliasReserved, $"{F(0)}.alias");
        yield return Case("a reserved alias in other letters", d => Field(d, 0)["alias"] = "pAGE", DefinitionErrors.AliasReserved, $"{F(0)}.alias");
        yield return Case("the reserved alias ufprt", d => Field(d, 0)["alias"] = "ufprt", DefinitionErrors.AliasReserved, $"{F(0)}.alias");
        yield return Case("a reserved former alias", d => Field(d, 0)["formerAliases"] = new JsonArray("member"), DefinitionErrors.AliasReserved, $"{F(0)}.formerAliases[0]");
        yield return Case("two fields with one alias", d => Field(d, 1)["alias"] = "name", DefinitionErrors.AliasDuplicate, $"{F(1)}.alias");
        yield return Case("aliases that differ only by case", d => Field(d, 1)["alias"] = "nAME", DefinitionErrors.AliasDuplicate, $"{F(1)}.alias");
        yield return Case("an alias that is another field's former alias", d =>
        {
            Field(d, 0)["formerAliases"] = new JsonArray("mail");
            Field(d, 1)["alias"] = "mail";
        }, DefinitionErrors.AliasDuplicate, $"{F(1)}.alias");
        yield return Case("a child with a field's alias in other letters", d => Field2(d, 1)["fields"]![0]!["alias"] = "eMAIL", DefinitionErrors.AliasDuplicate, $"{F2(1)}.fields[0].alias");
        yield return Case("a former alias twice", d => Field(d, 0)["formerAliases"] = new JsonArray("old", "oLD"), DefinitionErrors.AliasDuplicate, $"{F(0)}.formerAliases[1]");

        // Names of query and context values.
        yield return Case("a query value with a space", d => Developer(d)["queryValues"] = new JsonArray("ref", "utm source"), DefinitionErrors.NameInvalid, "$.settings.developer.queryValues[1]");
        yield return Case("a context value with a dot", d => Developer(d)["contextValues"] = new JsonArray("campaign", "a.b"), DefinitionErrors.NameInvalid, "$.settings.developer.contextValues[1]");
        yield return Case("a query value twice in two cases", d => Developer(d)["queryValues"] = new JsonArray("ref", "Ref"), DefinitionErrors.NameDuplicate, "$.settings.developer.queryValues[1]");

        // Option values.
        yield return Case("an option value with a space before it", d => Items(d, 4)[1]!["value"] = " Berlin", DefinitionErrors.OptionValueInvalid, $"{F(4)}.options.items[1].value");
        yield return Case("an option value of spaces", d => Items(d, 4)[1]!["value"] = "  ", DefinitionErrors.OptionValueInvalid, $"{F(4)}.options.items[1].value");
        yield return Case("an option value not in composed form", d => Items(d, 4)[0]!["value"] = "Zu" + C(0x65) + C(0x308) + "rich", DefinitionErrors.OptionValueInvalid, $"{F(4)}.options.items[0].value");
        yield return Case("option values that differ only by case", d => Items(d, 4)[1]!["value"] = "zURICH", DefinitionErrors.OptionValueDuplicate, $"{F(4)}.options.items[1].value");
        yield return Case("option values with a capital and a small Turkish i", d =>
        {
            Items(d, 4)[0]!["value"] = C(0x130) + "stanbul";
            Items(d, 4)[1]!["value"] = "istanbul";
        }, DefinitionErrors.OptionValueDuplicate, $"{F(4)}.options.items[1].value");
        yield return Case("option values with a final and a medial sigma", d =>
        {
            Items(d, 4)[0]!["value"] = C(0x3A3) + C(0x391) + C(0x3A3);
            Items(d, 4)[1]!["value"] = C(0x3C3) + C(0x3B1) + C(0x3C2);
        }, DefinitionErrors.OptionValueDuplicate, $"{F(4)}.options.items[1].value");

        // Options and their sources.
        yield return Case("own options with a library list", d => Field(d, 4)["options"]!["list"] = "72000000-0000-4000-8000-000000000001", DefinitionErrors.OptionsInvalid, $"{F(4)}.options.list");
        yield return Case("own options with a configuration", d => Field(d, 4)["options"]!["config"] = new JsonObject { ["root"] = 1 }, DefinitionErrors.OptionsInvalid, $"{F(4)}.options.config");
        yield return Case("a library list with own items", d => Field(d, 4)["options"] = new JsonObject { ["source"] = "list", ["list"] = "72000000-0000-4000-8000-000000000001", ["items"] = new JsonArray(new JsonObject { ["value"] = "x" }) }, DefinitionErrors.OptionsInvalid, $"{F(4)}.options.items");
        yield return Case("a library list with a configuration", d => Field(d, 4)["options"] = new JsonObject { ["source"] = "list", ["list"] = "72000000-0000-4000-8000-000000000001", ["config"] = new JsonObject { ["root"] = 1 } }, DefinitionErrors.OptionsInvalid, $"{F(4)}.options.config");
        yield return Case("a source with own items", d => Field(d, 4)["options"] = new JsonObject { ["source"] = "countries", ["items"] = new JsonArray(new JsonObject { ["value"] = "x" }) }, DefinitionErrors.OptionsInvalid, $"{F(4)}.options.items");
        yield return Case("a source with a library list", d => Field(d, 4)["options"] = new JsonObject { ["source"] = "content", ["list"] = "72000000-0000-4000-8000-000000000001" }, DefinitionErrors.OptionsInvalid, $"{F(4)}.options.list");
        yield return Case("parent values without a parent", d =>
        {
            Field(d, 4)["options"]!.AsObject().Remove("parent");
            Items(d, 4)[1]!.AsObject().Remove("parentValues");
        }, DefinitionErrors.OptionsInvalid, $"{F(4)}.options.items[0].parentValues");

        // References to fields and contexts.
        yield return Case("a condition on no field", d => Guests(d)["field"] = Nowhere, DefinitionErrors.UnknownField, $"{F(5)}.rules[0].conditions[0].field");
        yield return Case("a condition on a heading", d => Guests(d)["field"] = K(8), DefinitionErrors.UnknownField, $"{F(5)}.rules[0].conditions[0].field");
        yield return Case("a condition on a name, which rules cannot compare", d => Guests(d)["field"] = K(7), DefinitionErrors.UnknownField, $"{F(5)}.rules[0].conditions[0].field");
        yield return Case("an answer compared with no field", d => Guests(d)["value"] = new JsonObject { ["field"] = Nowhere }, DefinitionErrors.UnknownField, $"{F(5)}.rules[0].conditions[0].value.field");
        yield return Case("a list operand with no field", d =>
        {
            Guests(d)["op"] = "isOneOf";
            Guests(d)["value"] = new JsonArray("ch", new JsonObject { ["field"] = Nowhere });
        }, DefinitionErrors.UnknownField, $"{F(5)}.rules[0].conditions[0].value[1].field");
        yield return Case("a nested condition on no field", d => Guests(d).ReplaceWith(new JsonObject
        {
            ["match"] = "any",
            ["conditions"] = new JsonArray(new JsonObject { ["field"] = Nowhere, ["op"] = "isEmpty" }),
        }), DefinitionErrors.UnknownField, $"{F(5)}.rules[0].conditions[0].conditions[0].field");
        yield return Case("a check that matches no field", d => Field(d, 2)["validation"]![0]!["field"] = Nowhere, DefinitionErrors.UnknownField, $"{F(2)}.validation[0].field");
        yield return Case("a validation rule on no field", d => d["validationRules"]![0]!["field"] = Nowhere, DefinitionErrors.UnknownField, "$.validationRules[0].field");
        yield return Case("a validation rule on a heading", d => d["validationRules"]![0]!["field"] = K(8), DefinitionErrors.UnknownField, "$.validationRules[0].field");
        yield return Case("verification of no field", d => d["settings"]!["submission"]!["verification"]!["emailField"] = Nowhere, DefinitionErrors.UnknownField, "$.settings.submission.verification.emailField");
        yield return Case("verification of a heading", d => d["settings"]!["submission"]!["verification"]!["emailField"] = K(8), DefinitionErrors.UnknownField, "$.settings.submission.verification.emailField");
        yield return Case("a parent that is no field", d => Field(d, 4)["options"]!["parent"] = Nowhere, DefinitionErrors.UnknownField, $"{F(4)}.options.parent");
        yield return Case("an unknown context", d => QueryRule(d)["context"] = "weather", DefinitionErrors.UnknownField, "$.pages[1].rules[0].conditions[0].context");
        yield return Case("a query value the form does not allow", d => QueryRule(d)["context"] = "query.campaign", DefinitionErrors.UnknownField, "$.pages[1].rules[0].conditions[0].context");

        // References to the wrong kind of element.
        yield return Case("a field that must match itself", d => Field(d, 2)["validation"]![0]!["field"] = K(3), DefinitionErrors.InvalidReference, $"{F(2)}.validation[0].field");
        yield return Case("a field that must match a name", d => Field(d, 2)["validation"]![0]!["field"] = K(7), DefinitionErrors.InvalidReference, $"{F(2)}.validation[0].field");
        yield return Case("options filtered by their own answer", d => Field(d, 4)["options"]!["parent"] = K(5), DefinitionErrors.InvalidReference, $"{F(4)}.options.parent");
        yield return Case("a parent that is no choice field", d => Field(d, 4)["options"]!["parent"] = K(1), DefinitionErrors.InvalidReference, $"{F(4)}.options.parent");
        yield return Case("a parent value the parent lacks", d => Items(d, 4)[1]!["parentValues"] = new JsonArray("at"), DefinitionErrors.InvalidReference, $"{F(4)}.options.items[1].parentValues[0]");
        yield return Case("verification of a text field", d => d["settings"]!["submission"]!["verification"]!["emailField"] = K(1), DefinitionErrors.InvalidReference, "$.settings.submission.verification.emailField");
        yield return Case("verification of an email field in a repeatable group", d =>
        {
            Field2(d, 1)["fields"]![0]!["type"] = "email";
            d["settings"]!["submission"]!["verification"]!["emailField"] = K(0xb);
        }, DefinitionErrors.InvalidReference, "$.settings.submission.verification.emailField");
        yield return Case("a prefill from a query value the form does not allow", d => Field(d, 0)["prefill"]!["query"] = "utm", DefinitionErrors.InvalidReference, $"{F(0)}.prefill.query");

        // The conditions both runtimes check before they evaluate (ADR-0005).
        yield return Case("a condition on a field and a context", d => Guests(d)["context"] = "culture", DefinitionErrors.InvalidCondition, $"{F(5)}.rules[0].conditions[0]");
        yield return Case("a condition on neither", d => Guests(d).Remove("field"), DefinitionErrors.InvalidCondition, $"{F(5)}.rules[0].conditions[0]");
        yield return Case("an operator that does not exist", d => Guests(d)["op"] = "isBigger", DefinitionErrors.UnknownOperator, $"{F(5)}.rules[0].conditions[0].op");
        yield return Case("contains on a number", d => CheckAcross(d)["op"] = "contains", DefinitionErrors.OperatorNotForKind, "$.validationRules[0].conditions[0].op");
        yield return Case("greater than on a choice", d => Guests(d)["op"] = "greaterThan", DefinitionErrors.OperatorNotForKind, $"{F(5)}.rules[0].conditions[0].op");
        yield return Case("equals without a value", d => Guests(d).Remove("value"), DefinitionErrors.InvalidOperand, $"{F(5)}.rules[0].conditions[0].value");
        yield return Case("between with one bound", d =>
        {
            CheckAcross(d)["op"] = "between";
            CheckAcross(d)["value"] = new JsonArray("1");
        }, DefinitionErrors.InvalidOperand, "$.validationRules[0].conditions[0].value");
        yield return Case("a count that is a word", d =>
        {
            Guests(d)["op"] = "countAtLeast";
            Guests(d)["value"] = "two";
        }, DefinitionErrors.InvalidOperand, $"{F(5)}.rules[0].conditions[0].value");
        yield return Case("is empty with a value", d => Guests(d)["op"] = "isEmpty", DefinitionErrors.InvalidOperand, $"{F(5)}.rules[0].conditions[0].value");
        yield return Case("a relative date that is not one", d =>
        {
            QueryRule(d)["context"] = "today";
            QueryRule(d)["op"] = "lessThan";
            QueryRule(d)["value"] = new JsonObject { ["today"] = "soon" };
        }, DefinitionErrors.InvalidOperand, "$.pages[1].rules[0].conditions[0].value");
        yield return Case("a pattern check outside the safe subset", d => Field(d, 0)["validation"]![0]!["pattern"] = "(?i)name", DefinitionErrors.UnsafePattern, $"{F(0)}.validation[0].pattern");
        yield return Case("a pattern check that counts past its limit", d => Field(d, 0)["validation"]![0]!["pattern"] = "a{1001}", DefinitionErrors.UnsafePattern, $"{F(0)}.validation[0].pattern");
        yield return Case("a matches condition with an unsafe pattern", d =>
        {
            Guests(d)["field"] = K(1);
            Guests(d)["op"] = "matches";
            Guests(d)["value"] = "(a";
        }, DefinitionErrors.UnsafePattern, $"{F(5)}.rules[0].conditions[0].value");

        // Rules per element.
        yield return Case("a require rule on a page", d => d["pages"]![1]!["rules"]![0]!["effect"] = "require", DefinitionErrors.RuleEffectInvalid, "$.pages[1].rules[0].effect");
        yield return Case("a use rule on a group", d => d["pages"]![0]!["groups"]![1]!["rules"]![0]!["effect"] = "use", DefinitionErrors.RuleEffectInvalid, $"{Group2}.rules[0].effect");
        yield return Case("a show rule on an action", d => d["actions"]![0]!["rules"]![0]!["effect"] = "show", DefinitionErrors.RuleEffectInvalid, "$.actions[0].rules[0].effect");
        yield return Case("a run rule on an outcome", d => d["outcomes"]![0]!["rules"]![0]!["effect"] = "run", DefinitionErrors.RuleEffectInvalid, "$.outcomes[0].rules[0].effect");
        // A value the server sets, in rules and checks the visitor's browser runs too.
        yield return Case("a show rule on a hidden field's value", d =>
        {
            AddHidden(d);
            Guests(d).ReplaceWith(new JsonObject { ["field"] = K(0x20), ["op"] = "isNotEmpty" });
        }, DefinitionErrors.InvalidReference, $"{F(5)}.rules[0].conditions[0].field");
        yield return Case("a require rule that compares an answer with a hidden field's value", d =>
        {
            AddHidden(d);
            Field2(d, 2)["rules"]![0]!["conditions"]![0]!.ReplaceWith(new JsonObject { ["field"] = K(1), ["op"] = "equals", ["value"] = new JsonObject { ["field"] = K(0x20) } });
        }, DefinitionErrors.InvalidReference, $"{F2(2)}.rules[0].conditions[0].value.field");
        yield return Case("a check across fields on a hidden field's value", d =>
        {
            AddHidden(d);
            CheckAcross(d).ReplaceWith(new JsonObject { ["field"] = K(0x20), ["op"] = "isNotEmpty" });
        }, DefinitionErrors.InvalidReference, "$.validationRules[0].conditions[0].field");
        yield return Case("an email that must match a hidden field's value", d =>
        {
            AddHidden(d);
            Field(d, 2)["validation"]![0]!["field"] = K(0x20);
        }, DefinitionErrors.InvalidReference, $"{F(2)}.validation[0].field");

        yield return Case("a require rule on a heading", d => Field(d, 7)["rules"] = new JsonArray(Rule("40000000-0000-4000-8000-000000000010", "require")), DefinitionErrors.RuleEffectInvalid, $"{F(7)}.rules[0].effect");
        yield return Case("a rule on the captcha", d => Field2(d, 3)["rules"] = new JsonArray(Rule("40000000-0000-4000-8000-000000000010", "show")), DefinitionErrors.RuleEffectInvalid, $"{F2(3)}.rules[0].effect");
        yield return Case("two show rules on a field", d => Field(d, 5)["rules"]!.AsArray().Add(Rule("40000000-0000-4000-8000-000000000010", "show")), DefinitionErrors.RuleDuplicate, $"{F(5)}.rules[1]");
        yield return Case("a show and a hide rule on a group", d => d["pages"]![0]!["groups"]![1]!["rules"]!.AsArray().Add(Rule("40000000-0000-4000-8000-000000000010", "show")), DefinitionErrors.RuleDuplicate, $"{Group2}.rules[1]");
        yield return Case("two require rules on a field", d => Field2(d, 2)["rules"]!.AsArray().Add(Rule("40000000-0000-4000-8000-000000000010", "require")), DefinitionErrors.RuleDuplicate, $"{F2(2)}.rules[1]");
        yield return Case("two run rules on an action", d => d["actions"]![0]!["rules"]!.AsArray().Add(Rule("40000000-0000-4000-8000-000000000010", "run")), DefinitionErrors.RuleDuplicate, "$.actions[0].rules[1]");

        // Cycles.
        yield return Case("a field shown by its own answer", d =>
        {
            Guests(d)["field"] = K(6);
            Guests(d)["op"] = "isNotEmpty";
            Guests(d).Remove("value");
        }, DefinitionErrors.Cycle, $"{F(5)}.rules[0]");
        yield return Case("a field shown by comparing an answer with its own", d => Guests(d)["value"] = new JsonObject { ["field"] = K(6) }, DefinitionErrors.Cycle, $"{F(5)}.rules[0]");
        yield return Case("a group hidden by a field in it", d =>
        {
            var condition = d["pages"]![0]!["groups"]![1]!["rules"]![0]!["conditions"]![0]!.AsObject();
            condition["field"] = K(0xc);
            condition["op"] = "isChecked";
        }, DefinitionErrors.Cycle, $"{Group2}.rules[0]");
        yield return Case("a group hidden by a grid row in it", d =>
        {
            var condition = d["pages"]![0]!["groups"]![1]!["rules"]![0]!["conditions"]![0]!.AsObject();
            condition["field"] = "31000000-0000-4000-8000-000000000001";
        }, DefinitionErrors.Cycle, $"{Group2}.rules[0]");
        yield return Case("a page shown by a field on it", d => QueryRule(d).ReplaceWith(new JsonObject { ["field"] = K(0xe), ["op"] = "isNotEmpty" }), DefinitionErrors.Cycle, "$.pages[1].rules[0]");
        yield return Case("two fields shown by each other", d =>
        {
            Field(d, 0)["rules"] = new JsonArray(Rule("40000000-0000-4000-8000-000000000010", "show", new JsonObject { ["field"] = K(6), ["op"] = "isNotEmpty" }));
            Guests(d)["field"] = K(1);
            Guests(d)["op"] = "isNotEmpty";
            Guests(d).Remove("value");
        }, DefinitionErrors.Cycle, $"{F(0)}.rules[0]");
        yield return Case("options filtered round in a circle", d => Field(d, 3)["options"]!["parent"] = K(5), DefinitionErrors.Cycle, $"{F(3)}.options.parent");
        yield return Case("options filtered by a circle they are not part of", d =>
        {
            // The country is filtered by the city, which the region filters, which the city filters.
            Field(d, 3)["options"]!["parent"] = K(5);
            Field(d, 4)["options"]!["parent"] = K(0x21);
            Field(d, 0).Parent!.AsArray().Add(new JsonObject
            {
                ["key"] = K(0x21),
                ["type"] = "dropdown",
                ["alias"] = "region",
                ["options"] = new JsonObject
                {
                    ["items"] = new JsonArray(new JsonObject { ["value"] = "ch", ["parentValues"] = new JsonArray("Zurich") }, new JsonObject { ["value"] = "de" }),
                    ["parent"] = K(5),
                },
            });
        }, DefinitionErrors.Cycle, $"{F(4)}.options.parent");

        // Redirects.
        yield return Case("a redirect to a page and a URL", d => d["outcomes"]![1]!["redirect"]!["url"] = "/thanks", DefinitionErrors.InvalidRedirect, "$.outcomes[1].redirect");
        yield return Case("a redirect over http", d => d["outcomes"]![0]!["redirect"]!["url"] = "http://example.com/thanks", DefinitionErrors.InvalidRedirect, "$.outcomes[0].redirect.url");
        yield return Case("a merge tag right after the first slash", d => d["outcomes"]![0]!["redirect"]!["url"] = "/{{ field.city }}/{{ field.name }}", DefinitionErrors.InvalidRedirect, "$.outcomes[0].redirect.url");
        yield return Case("a redirect to another site by its path", d => d["outcomes"]![0]!["redirect"]!["url"] = "//elsewhere.example/thanks", DefinitionErrors.InvalidRedirect, "$.outcomes[0].redirect.url");
        yield return Case("a merge tag in the host", d => d["outcomes"]![0]!["redirect"]!["url"] = "https://{{ query.ref }}.example.com/", DefinitionErrors.InvalidRedirect, "$.outcomes[0].redirect.url");
    }

    public static IEnumerable<TestCaseData> Accepted()
    {
        TestCaseData Case(string name, Action<JsonObject> change) => new TestCaseData(change).SetName($"Accepts({name})");

        yield return Case("option values in a script that does not fold", d =>
        {
            // Georgian capital and small an: runtimes case them differently, so they compare exactly (ADR-0005).
            Items(d, 4)[0]!["value"] = C(0x1C90);
            Items(d, 4)[1]!["value"] = C(0x10D0);
        });
        yield return Case("option values with a sharp s and with ss", d =>
        {
            Items(d, 4)[0]!["value"] = "Stra" + C(0xDF) + "e";
            Items(d, 4)[1]!["value"] = "Strasse";
        });
        yield return Case("a switched-off rule on its own field", d =>
        {
            Field(d, 5)["rules"]![0]!["enabled"] = false;
            Guests(d)["field"] = K(6);
            Guests(d)["op"] = "isNotEmpty";
            Guests(d).Remove("value");
        });
        yield return Case("a require rule on its own field", d => Field2(d, 2)["rules"]![0]!["conditions"]![0]!["field"] = K(0xc));
        yield return Case("a field shown by a field on an earlier page", d => d["pages"]![1]!["groups"]![0]!["fields"]![0]!["rules"] = new JsonArray(Rule("40000000-0000-4000-8000-000000000010", "show", new JsonObject { ["field"] = K(1), ["op"] = "isNotEmpty" })));
        yield return Case("a condition on a grid row", d => Guests(d).ReplaceWith(new JsonObject { ["field"] = "31000000-0000-4000-8000-000000000001", ["op"] = "equals", ["value"] = "1" }));
        yield return Case("a condition on a repeatable group's child", d => Guests(d).ReplaceWith(new JsonObject { ["field"] = K(0xb), ["op"] = "isNotEmpty" }));
        yield return Case("a list as the default of several choices", d => Field2(d, 0).Parent!.AsArray().Add(new JsonObject
        {
            ["key"] = K(0x21),
            ["type"] = "checkboxes",
            ["alias"] = "topics",
            ["defaultValue"] = new JsonArray("a", "b"),
            ["options"] = new JsonObject { ["items"] = new JsonArray(new JsonObject { ["value"] = "a" }, new JsonObject { ["value"] = "b" }) },
        }));
        yield return Case("a site-relative redirect with merge tags", d => d["outcomes"]![0]!["redirect"]!["url"] = "/events/{{ field.city | lower }}/thanks?n={{ field.name }}");
        yield return Case("an action rule on a hidden field's value", d =>
        {
            AddHidden(d);
            d["actions"]![0]!["rules"]![0]!["conditions"]![0]!.ReplaceWith(new JsonObject { ["field"] = K(0x20), ["op"] = "isNotEmpty" });
        });
        yield return Case("an outcome rule on a hidden field's value", d =>
        {
            AddHidden(d);
            d["outcomes"]![0]!["rules"]![0]!["conditions"]![0]!.ReplaceWith(new JsonObject { ["field"] = K(0x20), ["op"] = "equals", ["value"] = "newsletter" });
        });
        yield return Case("a redirect outcome that leads to a page", d => d["outcomes"]![0]!["redirect"] = new JsonObject { ["content"] = "73000000-0000-4000-8000-000000000002" });
    }

    [Test]
    public void ThePublishableDocument_HasNoProblems()
    {
        Assert.That(Publish(Publishable), Is.Empty);
    }

    [Test]
    public void TheFullFixture_HasNoProblems()
    {
        Assert.That(Publish(DefinitionFixtures.Full), Is.Empty);
    }

    [TestCaseSource(nameof(Changes))]
    public void Publishing_RefusesOneChange_WithItsCodeAndPath(Action<JsonObject> change, string code, string path) =>
        AssertOneProblem(Publish(Changed(change)), code, path);

    [TestCaseSource(nameof(Accepted))]
    public void Publishing_AcceptsWhatFitsTogether(Action<JsonObject> change) =>
        Assert.That(Publish(Changed(change)), Is.Empty);

    [Test]
    public void EveryCodeOfTheStructuralValidation_HasACase()
    {
        var tested = Changes().Select(data => (string)data.Arguments[1]!).ToHashSet(StringComparer.Ordinal);

        Assert.That(PublishCodes().Except(tested).Order(StringComparer.Ordinal), Is.Empty);
    }

    [Test]
    public void Publishing_RefusesWhatASaveRefuses_First()
    {
        var definition = DefinitionFixtures.Read(Publishable);
        var action = definition.Actions[0] with { Rules = [] };
        var twoKeys = definition with { Actions = [action, action] };
        var serializer = DefinitionFixtures.Serializer(new DefinitionLimits { MaxActions = 0 });

        Assert.Multiple(() =>
        {
            AssertOneProblem(DefinitionFixtures.Serializer().CheckPublish(twoKeys, FieldTypeCatalogue.BuiltIn), DefinitionErrors.DuplicateKey, "$.actions[1].key");
            AssertOneProblem(serializer.CheckPublish(definition, FieldTypeCatalogue.BuiltIn), DefinitionErrors.LimitExceeded, "$.actions");
        });
    }

    [Test]
    public void ProblemsPastOneHundred_AreNotListed()
    {
        var definition = DefinitionFixtures.Read(Publishable);
        var many = Enumerable.Range(0, 150).Select(i => new FormField { Key = Guid.NewGuid(), Type = "colourPicker" }).ToList();
        var page = definition.Pages[1] with { Groups = [definition.Pages[1].Groups[0] with { Fields = many }] };

        Assert.That(Publish(definition with { Pages = [definition.Pages[0], page] }), Has.Count.EqualTo(100));
    }

    [Test]
    public void GeneratedDefinitions_AreCheckedWithoutThrowing_AndEveryProblemNamesAPlaceInThem()
    {
        var codes = PublishCodes();
        for (var seed = 0; seed < 500; seed++)
        {
            var definition = new DefinitionGenerator(seed).Next();
            var problems = DefinitionStructureChecks.Check(definition, FieldTypeCatalogue.BuiltIn);
            var document = JsonNode.Parse(FormDefinitionSerializer.Write(definition))!;
            Assert.Multiple(() =>
            {
                Assert.That(problems, Has.Count.LessThanOrEqualTo(100), $"seed {seed}");
                Assert.That(problems.Select(problem => problem.Code).Except(codes), Is.Empty, $"seed {seed}: codes of the structural validation");
                Assert.That(problems.Where(problem => !JsonPathResolves(document, problem.Path)).Select(problem => problem.Path), Is.Empty, $"seed {seed}: paths into the document");
                Assert.That(problems.Where(problem => problem.Message.Length == 0), Is.Empty, $"seed {seed}: messages");
                Assert.That(DefinitionStructureChecks.Check(definition, FieldTypeCatalogue.BuiltIn), Is.EqualTo(problems), $"seed {seed}: the same problems every time");
            });
        }
    }

    /// <summary>The publishable document with one change made.</summary>
    internal static string Changed(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(Publishable)!.AsObject();
        change(document);
        return document.ToJsonString();
    }

    /// <summary>The codes publishing adds to those of a save and of an export file (ADR-0046).</summary>
    internal static HashSet<string> PublishCodes()
    {
        string[] save =
        [
            DefinitionErrors.InvalidJson, DefinitionErrors.UnknownProperty, DefinitionErrors.MissingProperty, DefinitionErrors.DuplicateProperty,
            DefinitionErrors.InvalidType, DefinitionErrors.InvalidValue, DefinitionErrors.DuplicateKey, DefinitionErrors.UnsupportedSchemaVersion,
            DefinitionErrors.LimitExceeded, DefinitionErrors.UnsupportedFormat,
        ];
        return typeof(DefinitionErrors).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (string)field.GetValue(null)!)
            .Except(save)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Publishes a document that a save accepts and returns the problems.</summary>
    internal static IReadOnlyList<DefinitionProblem> Publish(string json) => Publish(DefinitionFixtures.Read(json));

    /// <summary>Publishes a definition and returns the problems.</summary>
    internal static IReadOnlyList<DefinitionProblem> Publish(FormDefinition definition) =>
        DefinitionFixtures.Serializer().CheckPublish(definition, FieldTypeCatalogue.BuiltIn);

    internal static void AssertOneProblem(IReadOnlyList<DefinitionProblem> problems, string code, string path)
    {
        Assert.That(problems.Select(problem => $"{problem.Code} at {problem.Path}"), Is.EqualTo(new[] { $"{code} at {path}" }), string.Join(" | ", problems.Select(problem => problem.Message)));
        Assert.That(problems[0].Message, Is.Not.Empty);
    }

    // Whether a problem's path leads into the canonical document: every step but the last exists, and the last may be a
    // property the document leaves out (a missing alias, an absent list).
    private static bool JsonPathResolves(JsonNode root, string path)
    {
        var steps = JsonPathSteps.Parse(path);
        JsonNode? node = root;
        for (var i = 0; i < steps.Count; i++)
        {
            var last = i == steps.Count - 1;
            node = steps[i] switch
            {
                int index when node is JsonArray array => index < array.Count ? array[index] : null,
                string name when node is JsonObject obj => obj.TryGetPropertyValue(name, out var value) ? value : null,
                _ => null,
            };
            if (node is null)
            {
                return last && steps[i] is string;
            }
        }

        return true;
    }
}
