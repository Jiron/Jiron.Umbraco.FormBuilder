using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The public projection and its hash (ROADMAP 4.2b, ADR-0002, ADR-0035): built from an allowlist, it never holds
/// action settings, recipients, option-source configuration or any other private part, and its hash moves only when
/// what visitors get does.
/// </summary>
public sealed partial class ProjectionTests
{
    private const string Marker = "JFB_TEST_PRIVATE";
    private const int Definitions = 500;

    // The projection's properties, one per line: arrays as [], a repeatable group's children as its fields. Texts,
    // conditions, default values, messages and settings are values of their own types and end the path.
    private static readonly string[] Allowlist =
    [
        "$.key",
        "$.settings", "$.settings.submitLabel", "$.settings.nextLabel", "$.settings.backLabel", "$.settings.summaryPage",
        "$.settings.progress", "$.settings.resume", "$.settings.closedMessage", "$.settings.membersOnly", "$.settings.protection",
        "$.settings.captchaProvider", "$.settings.theme", "$.settings.requiredMarker", "$.settings.validationSummary",
        "$.settings.choiceLayout", "$.settings.cssClass", "$.settings.queryValues",
        "$.pages", "$.pages[].key", "$.pages[].title", "$.pages[].description", "$.pages[].groups",
        "$.pages[].rules", "$.pages[].rules[].key", "$.pages[].rules[].effect", "$.pages[].rules[].match", "$.pages[].rules[].conditions",
        "$.pages[].groups[].key", "$.pages[].groups[].title", "$.pages[].groups[].description", "$.pages[].groups[].columns",
        "$.pages[].groups[].style", "$.pages[].groups[].fields",
        "$.pages[].groups[].rules", "$.pages[].groups[].rules[].key", "$.pages[].groups[].rules[].effect",
        "$.pages[].groups[].rules[].match", "$.pages[].groups[].rules[].conditions",
        "$.pages[].groups[].fields[].key", "$.pages[].groups[].fields[].type", "$.pages[].groups[].fields[].alias",
        "$.pages[].groups[].fields[].span", "$.pages[].groups[].fields[].label", "$.pages[].groups[].fields[].hideLabel",
        "$.pages[].groups[].fields[].help", "$.pages[].groups[].fields[].placeholder", "$.pages[].groups[].fields[].required",
        "$.pages[].groups[].fields[].messages", "$.pages[].groups[].fields[].defaultValue",
        "$.pages[].groups[].fields[].prefill", "$.pages[].groups[].fields[].prefill.query",
        "$.pages[].groups[].fields[].prefill.fromMember", "$.pages[].groups[].fields[].prefill.memberMode",
        "$.pages[].groups[].fields[].autocomplete", "$.pages[].groups[].fields[].cssClass",
        "$.pages[].groups[].fields[].validation", "$.pages[].groups[].fields[].validation[].kind",
        "$.pages[].groups[].fields[].validation[].pattern", "$.pages[].groups[].fields[].validation[].field",
        "$.pages[].groups[].fields[].validation[].message",
        "$.pages[].groups[].fields[].rules", "$.pages[].groups[].fields[].rules[].key", "$.pages[].groups[].fields[].rules[].effect",
        "$.pages[].groups[].fields[].rules[].match", "$.pages[].groups[].fields[].rules[].conditions",
        "$.pages[].groups[].fields[].options", "$.pages[].groups[].fields[].options.items",
        "$.pages[].groups[].fields[].options.items[].value", "$.pages[].groups[].fields[].options.items[].label",
        "$.pages[].groups[].fields[].options.items[].description", "$.pages[].groups[].fields[].options.items[].image",
        "$.pages[].groups[].fields[].options.items[].parentValues", "$.pages[].groups[].fields[].options.fromSource",
        "$.pages[].groups[].fields[].options.parent",
        "$.pages[].groups[].fields[].rows", "$.pages[].groups[].fields[].rows[].key", "$.pages[].groups[].fields[].rows[].label",
        "$.pages[].groups[].fields[].rows[].required",
        "$.pages[].groups[].fields[].fields", "$.pages[].groups[].fields[].settings",
        "$.validationRules", "$.validationRules[].key", "$.validationRules[].field", "$.validationRules[].message",
        "$.validationRules[].match", "$.validationRules[].conditions",
    ];

    private static readonly HashSet<string> Opaque = new(StringComparer.Ordinal)
    {
        "title", "description", "label", "help", "placeholder", "message", "messages", "submitLabel", "nextLabel", "backLabel",
        "closedMessage", "defaultValue", "conditions", "settings",
    };

    public static IEnumerable<TestCaseData> PrivateChanges()
    {
        TestCaseData Case(string name, Action<JsonObject> change, Action<JsonObject>? setup = null) =>
            new TestCaseData(change, setup ?? (_ => { })).SetName($"KeepsTheHash({name})");

        static JsonObject Settings(JsonObject d, string group) => d["settings"]![group]!.AsObject();
        static JsonObject Field(JsonObject d, int page, int group, int field) => d["pages"]![page]!["groups"]![group]!["fields"]![field]!.AsObject();

        yield return Case("an action's recipients", d => d["actions"]![0]!["settings"]!["to"] = new JsonArray("other@example.com"));
        yield return Case("an action's secret reference", d => d["actions"]![1]!["settings"]!["secret"] = "$JironFormBuilder:Secrets:OtherToken");
        yield return Case("an action removed", d => d["actions"]!.AsArray().RemoveAt(1));
        yield return Case("an outcome's message", d => d["outcomes"]![1]!["message"]![string.Empty] = "<p>Thank you!</p>");
        yield return Case("an outcome's redirect", d => d["outcomes"]![1]!["redirect"]!["url"] = "/elsewhere");
        yield return Case("the entry title", d => Settings(d, "submission")["entryTitle"] = "{{ field.email }}");
        yield return Case("the entry reference", d => Settings(d, "submission")["entryReference"]!["prefix"] = "R-");
        yield return Case("storage", d => Settings(d, "submission")["storeDays"] = 30);
        yield return Case("approval", d => Settings(d, "submission")["approval"] = "automatic");
        yield return Case("the verification link's days", d => Settings(d, "submission")["verification"]!["days"] = 3);
        yield return Case("the resume link's days", d => Settings(d, "submission")["resume"]!["days"] = 20);
        yield return Case("when the form opens and closes", d =>
        {
            Settings(d, "availability")["opensAt"] = "2026-12-01T08:00:00Z";
            Settings(d, "availability")["closesAt"] = "2027-02-01T00:00:00Z";
        });
        yield return Case("the entry limit", d => Settings(d, "availability")["maxEntries"] = 50);
        yield return Case("the member groups", d => Settings(d, "availability")["memberGroups"]!.AsArray().RemoveAt(1));
        yield return Case("one entry per member", d => Settings(d, "availability")["onePerMember"] = false);
        yield return Case("the captcha's settings", d => Settings(d, "protection")["captcha"]!["settings"]!["size"] = "normal");
        yield return Case("the content rules", d => Settings(d, "protection")["contentRules"] = "normal");
        yield return Case("retention", d => Settings(d, "privacy")["retentionDays"]!["spam"] = 1);
        yield return Case("IP handling", d => Settings(d, "privacy")["ip"] = "none");
        yield return Case("insights", d => Settings(d, "privacy")["insights"] = true);
        yield return Case("headless access", d => Settings(d, "developer")["headless"] = false);
        yield return Case("the context values", d => Settings(d, "developer")["contextValues"] = new JsonArray("source"));
        yield return Case("a former alias", d => Field(d, 0, 0, 0)["formerAliases"] = new JsonArray("visitorName"));
        yield return Case("a field made sensitive", d => Field(d, 0, 0, 0)["sensitive"] = true);
        yield return Case("a field left out of mail", d => Field(d, 0, 0, 0)["includeInEmails"] = false);
        yield return Case("how long a value is kept", d => Field(d, 0, 0, 0)["keep"] = "untilActionsRun");
        yield return Case("the member property a field is prefilled from", d => Field(d, 0, 0, 0)["prefill"]!["member"] = "nickname");
        yield return Case("an email field's allowed domains", d => Field(d, 0, 0, 1)["settings"]!["domains"]!["mode"] = "allow");
        yield return Case("an option's places", d => Field(d, 0, 1, 0)["options"]!["items"]![0]!["places"] = 150);
        yield return Case("the library list of a field", d => Field(d, 0, 1, 1)["options"]!["list"] = "72000000-0000-4000-8000-000000000002");
        yield return Case("an option source's configuration", d => Field(d, 0, 1, 2)["options"]!["config"]!["top"] = new JsonArray("FR"));
        yield return Case("a switched-off rule's conditions", d => d["pages"]![0]!["groups"]![0]!["rules"]![0]!["match"] = "all");
        yield return Case("a hidden field's default", d => d["pages"]![1]!["groups"]![0]!["fields"]!.AsArray().Last()!["defaultValue"] = "{{ member.email }}", d => Field(d, 1, 0, 0).Parent!.AsArray().Add(Hidden()));
    }

    public static IEnumerable<TestCaseData> PublicChanges()
    {
        TestCaseData Case(string name, Action<JsonObject> change) => new TestCaseData(change, (Action<JsonObject>)(_ => { })).SetName($"ChangesTheHash({name})");

        static JsonObject Field(JsonObject d, int page, int group, int field) => d["pages"]![page]!["groups"]![group]!["fields"]![field]!.AsObject();

        yield return Case("a label", d => Field(d, 0, 0, 0)["label"]![string.Empty] = "Full name");
        yield return Case("a label in one more culture", d => Field(d, 0, 0, 0)["label"]!["fr"] = "Nom");
        yield return Case("a field made required", d => Field(d, 0, 0, 2)["required"] = true);
        yield return Case("an option added", d => Field(d, 0, 1, 0)["options"]!["items"]!.AsArray().Add(new JsonObject { ["value"] = "vip" }));
        yield return Case("a rule switched on", d => d["pages"]![0]!["groups"]![0]!["rules"]![0]!["enabled"] = true);
        yield return Case("a public setting", d => Field(d, 0, 0, 0)["settings"]!["maxLength"] = 90);
        yield return Case("two fields swapped", d =>
        {
            var fields = d["pages"]![0]!["groups"]![0]!["fields"]!.AsArray();
            var first = fields[1]!.DeepClone();
            fields[1] = fields[2]!.DeepClone();
            fields[2] = first;
        });
        yield return Case("the theme", d => d["settings"]!["appearance"]!["theme"] = "plain");
        yield return Case("the submit label", d => d["settings"]!["submission"]!["submitLabel"]![string.Empty] = "Register");
        yield return Case("a query value allowed", d => d["settings"]!["developer"]!["queryValues"]!.AsArray().Add("utm_campaign"));
        yield return Case("the protection level", d =>
        {
            d["settings"]!["protection"]!["level"] = "standard";
            d["settings"]!["protection"]!.AsObject().Remove("captcha");
        });
        yield return Case("a hidden field added", d => Field(d, 1, 0, 0).Parent!.AsArray().Add(Hidden()));
    }

    [Test]
    public void GeneratedDefinitions_NeverProjectActionSettingsRecipientsOrOptionSourceConfiguration()
    {
        for (var seed = 0; seed < Definitions; seed++)
        {
            var privateKeys = new List<Guid>();
            var definition = Mark(new DefinitionGenerator(seed).Next(), seed, privateKeys);
            var json = Encoding.UTF8.GetString(PublicProjection.Write(PublicProjection.Create(definition, FieldTypeCatalogue.BuiltIn)));
            var publicKeys = definition.Pages.SelectMany(page => page.Groups.SelectMany(group => AllFields(group.Fields)).Select(field => field.Key).Append(page.Key));

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Not.Contain(Marker), $"seed {seed}: no private text");
                Assert.That(privateKeys.Where(key => json.Contains(key.ToString(), StringComparison.Ordinal)), Is.Empty, $"seed {seed}: no private key");
                Assert.That(publicKeys.Where(key => !json.Contains(key.ToString(), StringComparison.Ordinal)), Is.Empty, $"seed {seed}: every page and field");
            });
        }
    }

    [Test]
    public void TheProjection_NamesExactlyTheAllowlistedProperties()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var seed = 0; seed < Definitions; seed++)
        {
            var projection = PublicProjection.Write(PublicProjection.Create(new DefinitionGenerator(seed).Next(), FieldTypeCatalogue.BuiltIn));
            Collect(JsonNode.Parse(projection)!, "$", seen);
        }

        Assert.Multiple(() =>
        {
            Assert.That(seen.Except(Allowlist).Order(StringComparer.Ordinal), Is.Empty, "properties beyond the allowlist");
            Assert.That(Allowlist.Except(seen).Order(StringComparer.Ordinal), Is.Empty, "allowlisted properties no generated projection holds");
        });
    }

    [Test]
    public void TheFullFixture_ProjectsAsReviewed()
    {
        var projection = PublicProjection.Write(PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), FieldTypeCatalogue.BuiltIn));

        Assert.That(Encoding.UTF8.GetString(projection), Is.EqualTo(DefinitionFixtures.Minify(DefinitionFixtures.FullProjection)));
    }

    [Test]
    public void ThePublicHash_IsTheSha256OfTheCanonicalProjection()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var projection = PublicProjection.Create(definition, FieldTypeCatalogue.BuiltIn);

        Assert.Multiple(() =>
        {
            Assert.That(PublicProjection.Hash(definition, FieldTypeCatalogue.BuiltIn), Is.EqualTo(CanonicalJson.Hash(PublicProjection.Write(projection))));
            Assert.That(PublicProjection.Hash(projection), Does.Match("^[0-9a-f]{64}$"));
            Assert.That(PublicProjection.Hash(projection), Is.Not.EqualTo(CanonicalJson.Hash(FormDefinitionSerializer.Write(definition))), "the projection is not the definition");
        });
    }

    [TestCaseSource(nameof(PrivateChanges))]
    public void ThePublicHash_StaysTheSame_WhenOnlyAPrivatePartChanges(Action<JsonObject> change, Action<JsonObject> setup)
    {
        var (before, after) = Hashes(setup, change);

        Assert.That(after, Is.EqualTo(before));
    }

    [TestCaseSource(nameof(PublicChanges))]
    public void ThePublicHash_Changes_WhenWhatVisitorsGetChanges(Action<JsonObject> change, Action<JsonObject> setup)
    {
        var (before, after) = Hashes(setup, change);

        Assert.That(after, Is.Not.EqualTo(before));
    }

    [Test]
    public void SwitchedOffRules_AndValidationRules_StayOutOfTheProjection()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var off = definition with { ValidationRules = [definition.ValidationRules[0] with { Enabled = false }] };

        var projection = PublicProjection.Create(off, FieldTypeCatalogue.BuiltIn);

        Assert.Multiple(() =>
        {
            Assert.That(projection.Pages[0].Groups[0].Rules, Is.Empty, "the group's rule is switched off");
            Assert.That(projection.Pages[1].Rules.Select(rule => rule.Key), Is.EqualTo(definition.Pages[1].Rules.Select(rule => rule.Key)), "the page's rule is on");
            Assert.That(projection.ValidationRules, Is.Empty);
        });
    }

    [Test]
    public void OptionsFromASource_SayOnlyThatTheyComeFromOne()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var fields = definition.Pages[0].Groups[1].Fields;
        var withItems = fields[1] with { Options = fields[1].Options! with { Items = [new FieldOption { Value = Marker }] } };

        var projection = PublicProjection.Create(definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0], definition.Pages[0].Groups[1] with { Fields = [fields[0], withItems, fields[2]] }] }, definition.Pages[1]] }, FieldTypeCatalogue.BuiltIn);
        var options = projection.Pages[0].Groups[1].Fields[1].Options!;

        Assert.Multiple(() =>
        {
            Assert.That(options.FromSource, Is.True);
            Assert.That(options.Items, Is.Empty, "items a library list does not use");
            Assert.That(options.Parent, Is.EqualTo(fields[1].Options!.Parent));
            Assert.That(projection.Pages[0].Groups[1].Fields[0].Options!.Items.Select(item => item.Value), Is.EqualTo(fields[0].Options!.Items.Select(item => item.Value)), "own options");
        });
    }

    [Test]
    public void APrefillFromTheMember_SaysOnlyThatItIsOne()
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), FieldTypeCatalogue.BuiltIn);

        Assert.That(projection.Pages[0].Groups[0].Fields[0].Prefill, Is.EqualTo(new PublicPrefill { Query = "name", FromMember = true, MemberMode = PrefillMemberMode.Hide }));
    }

    [Test]
    public void AFieldTypeTheSiteLacks_KeepsItsSettingsPrivate()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var name = definition.Pages[0].Groups[0].Fields[0];
        var unknown = name with { Type = "acme.colourPicker", Settings = SettingsObject.Parse("{\"maxLength\":3}"), DefaultValue = FieldDefaultValue.Of(Marker) };
        var group = definition.Pages[0].Groups[0] with { Fields = [unknown] };

        var projection = PublicProjection.Create(definition with { Pages = [definition.Pages[0] with { Groups = [group] }] }, FieldTypeCatalogue.BuiltIn);

        Assert.Multiple(() =>
        {
            Assert.That(projection.Pages[0].Groups[0].Fields[0].Settings, Is.Null);
            Assert.That(projection.Pages[0].Groups[0].Fields[0].DefaultValue, Is.Null, "a type the site lacks may keep its default on the server");
        });
    }

    private static JsonObject Hidden() => new() { ["key"] = "30000000-0000-4000-8000-0000000000f1", ["type"] = "hidden", ["alias"] = "source" };

    // The hashes of the full fixture before and after the change, both after the setup.
    private static (string Before, string After) Hashes(Action<JsonObject> setup, Action<JsonObject> change)
    {
        var document = JsonNode.Parse(DefinitionFixtures.Full)!.AsObject();
        setup(document);
        var before = PublicProjection.Hash(DefinitionFixtures.Read(document.ToJsonString()), FieldTypeCatalogue.BuiltIn);
        change(document);
        return (before, PublicProjection.Hash(DefinitionFixtures.Read(document.ToJsonString()), FieldTypeCatalogue.BuiltIn));
    }

    private static IEnumerable<FormField> AllFields(IEnumerable<FormField> fields) =>
        fields.SelectMany(field => AllFields(field.Fields).Prepend(field));

    private static void Collect(JsonNode node, string path, HashSet<string> seen)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    var child = Children().Replace(path + "." + name, ".fields[]");
                    seen.Add(child);
                    // The form's own settings are a public type; a field's settings are a settings object.
                    if ((!Opaque.Contains(name) || path == "$") && value is not null)
                    {
                        Collect(value, child, seen);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    Collect(item!, path + "[]", seen);
                }

                break;
        }
    }

    // Marks every private part of a definition, so a test can show that none reaches the projection: action
    // settings with their recipients and secret references, option-source configuration and library lists, member
    // prefill mappings, former aliases, settings no type makes public, a hidden field's default, and the settings
    // and parts of the form that stay on the server.
    private static FormDefinition Mark(FormDefinition definition, int seed, List<Guid> privateKeys)
    {
        var n = 0;
        string Text(string what) => $"{Marker}_{what}_{seed}_{n++}";
        Guid Key()
        {
            var key = Guid.NewGuid();
            privateKeys.Add(key);
            return key;
        }

        SettingsObject Settings(string what) => SettingsObject.Parse(
            $"{{\"to\":[\"{Text(what)}@example.invalid\"],\"cc\":\"{Text(what)}@example.invalid\",\"secret\":\"$JironFormBuilder:Secrets:{Text(what)}\",\"subject\":\"{Text(what)}\"}}");

        FormRule OffRule(FormRule rule) => rule.Enabled
            ? rule
            : rule with { Conditions = [new RuleCondition { Field = Key(), Operator = Text("op"), Value = new ConditionOperand.Text(Text("operand")) }] };

        FormField Field(FormField field)
        {
            var settings = field.Settings is null ? new JsonObject() : JsonNode.Parse(field.Settings.ToString())!.AsObject();
            settings["domains"] = new JsonObject { ["list"] = new JsonArray(Text("domain")) };
            settings["zz" + Marker] = Text("setting");
            return field with
            {
                FormerAliases = [.. field.FormerAliases, Text("former")],
                DefaultValue = field.Type == "hidden" ? FieldDefaultValue.Of(Text("hidden")) : field.DefaultValue,
                Prefill = field.Prefill is { } prefill ? prefill with { Member = Text("member") } : null,
                Options = field.Options is { } options ? options with { Config = Settings("config"), OptionList = Key() } : null,
                Rules = [.. field.Rules.Select(OffRule)],
                Fields = [.. field.Fields.Select(Field)],
                Settings = SettingsObject.Parse(settings.ToJsonString()),
            };
        }

        var submission = definition.Settings.Submission with
        {
            EntryTitle = Text("title"),
            EntryReference = new EntryReferenceFormat { Prefix = Text("prefix") },
        };
        var availability = definition.Settings.Availability with { MemberGroups = [Key(), Key()] };
        var protection = definition.Settings.Protection with
        {
            Captcha = definition.Settings.Protection.Captcha is { } captcha ? captcha with { Settings = Settings("captcha") } : null,
        };
        var developer = definition.Settings.Developer with { ContextValues = [Text("context")] };
        return definition with
        {
            Settings = definition.Settings with { Submission = submission, Availability = availability, Protection = protection, Developer = developer },
            Pages = [.. definition.Pages.Select(page => page with
            {
                Rules = [.. page.Rules.Select(OffRule)],
                Groups = [.. page.Groups.Select(group => group with { Rules = [.. group.Rules.Select(OffRule)], Fields = [.. group.Fields.Select(Field)] })],
            })],
            ValidationRules = [.. definition.ValidationRules.Select(rule => rule.Enabled ? rule : rule with { Message = new LocalizedText([new(string.Empty, Text("check"))]) })],
            Actions = [.. definition.Actions.Select(action => action with { Name = Text("action"), Settings = Settings("action"), Rules = [.. action.Rules.Select(OffRule)] }),
                new FormAction { Key = Key(), Type = "email", Name = Text("action"), Settings = Settings("recipient") }],
            Outcomes = [.. definition.Outcomes.Select(outcome => outcome with
            {
                Message = new LocalizedHtml([new(string.Empty, Text("outcome"))]),
                Redirect = new OutcomeRedirect { Url = "/" + Text("redirect"), Content = Key() },
            })],
        };
    }

    [GeneratedRegex(@"(\.fields\[\])+")]
    private static partial Regex Children();
}
