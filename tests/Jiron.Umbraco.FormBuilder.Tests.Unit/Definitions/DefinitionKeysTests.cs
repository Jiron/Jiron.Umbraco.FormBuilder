using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The element keys of a definition and the references to them (ADR-0046): every element of the model is listed,
/// renewing gives each a new key that every reference follows, settings included, and nothing else changes.
/// </summary>
public sealed class DefinitionKeysTests
{
    private const int Definitions = 500;

    // The fixture's keys: elements start with 1 to 6 or f, other things with 7.
    private static readonly Guid Name = Fixture("30000000-0000-4000-8000-000000000001");
    private static readonly Guid Email = Fixture("30000000-0000-4000-8000-000000000002");
    private static readonly Guid Ticket = Fixture("30000000-0000-4000-8000-000000000004");
    private static readonly Guid Arrival = Fixture("30000000-0000-4000-8000-000000000007");
    private static readonly Guid Departure = Fixture("30000000-0000-4000-8000-000000000008");

    [Test]
    public void TheKeys_AreEveryKeyOfAnElement_ThatTheModelHas()
    {
        var full = DefinitionFixtures.Read(DefinitionFixtures.Full);
        Assert.Multiple(() =>
        {
            Assert.That(DefinitionKeys.Of(full), Is.EquivalentTo(ElementKeys(full)), "the full fixture");
            Assert.That(DefinitionKeys.Of(full), Is.Unique);
            Assert.That(DefinitionKeys.Of(full)[0], Is.EqualTo(full.Key), "the definition's own key first");
            Assert.That(DefinitionKeys.Of(full), Has.None.Matches<Guid>(key => key.ToString("D").StartsWith('7')), "no key of something outside the form");
            for (var seed = 0; seed < Definitions; seed++)
            {
                var definition = new DefinitionGenerator(seed).Next();
                Assert.That(DefinitionKeys.Of(definition), Is.EquivalentTo(ElementKeys(definition)), $"seed {seed}");
            }
        });
    }

    [Test]
    public void Renewing_GivesEveryElementANewKey_ThatEveryReferenceFollows_AndChangesNothingElse()
    {
        var serializer = DefinitionFixtures.Serializer();
        Assert.Multiple(() =>
        {
            for (var seed = 0; seed < Definitions; seed++)
            {
                var definition = new DefinitionGenerator(seed).Next();
                var keys = DefinitionKeys.Of(definition);
                var map = DefinitionKeys.NewKeys(keys);
                var renewed = DefinitionKeys.Remap(definition, map, serializer);
                var text = FormDefinitionSerializer.WriteString(renewed);
                var back = DefinitionKeys.Remap(renewed, map.ToDictionary(pair => pair.Value, pair => pair.Key), serializer);

                Assert.That(DefinitionKeys.Of(renewed), Is.EqualTo(keys.Select(key => map[key])), $"seed {seed}: each element has its new key in its place");
                Assert.That(keys.Where(key => text.Contains(key.ToString("D"), StringComparison.Ordinal)), Is.Empty, $"seed {seed}: no old key is left, as a key or a reference");
                Assert.That(FormDefinitionSerializer.WriteString(back), Is.EqualTo(FormDefinitionSerializer.WriteString(definition)), $"seed {seed}: mapped back it is the definition again, so nothing but keys changed");
            }
        });
    }

    [Test]
    public void ADuplicate_RemapsRuleReferences_AndEveryOtherReference_ButNotKeysOfThingsOutsideTheForm()
    {
        var full = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var copy = DefinitionKeys.Renew(full, DefinitionFixtures.Serializer());
        var fields = DefinitionKeys.Fields(copy).ToDictionary(field => field.Alias ?? field.Key.ToString(), field => field.Key);
        var page1 = copy.Pages[0];
        var ticket = page1.Groups[1].Fields[0];
        var sessions = page1.Groups[1].Fields[1];
        var notes = copy.Pages[1].Groups[0].Fields[^3];

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionKeys.Of(copy).Intersect(DefinitionKeys.Of(full)), Is.Empty, "every element has a new key");
            Assert.That(copy.Key, Is.Not.EqualTo(full.Key));
            Assert.That(fields.Values, Has.None.EqualTo(Name).And.None.EqualTo(Email).And.None.EqualTo(Ticket));

            // Rules of fields, groups, pages, validation rules and actions follow the fields they name.
            Assert.That(Condition(page1.Groups[0].Fields[0].Rules[0]).Field, Is.EqualTo(fields["email"]), "a field's rule");
            Assert.That(Condition(ticket.Rules[0]).Field, Is.EqualTo(fields["name"]), "another field's rule");
            Assert.That(Condition(copy.Pages[1].Rules[0]).Field, Is.EqualTo(fields["ticket"]), "a page's rule");
            Assert.That(notes.Alias, Is.EqualTo("notes"));
            var group = (RuleCondition)notes.Rules[0].Conditions[0];
            Assert.That(group.Field, Is.EqualTo(fields["sessions"]), "a field's rule with several conditions");
            Assert.That(((ConditionOperand.List)group.Value!).Items[1], Is.EqualTo(new ConditionOperand.Field(fields["ticket"])), "an answer in a list operand");
            Assert.That(copy.ValidationRules[0].Field, Is.EqualTo(fields["departure"]), "the field a validation rule marks");
            Assert.That(Condition(copy.ValidationRules[0]).Value, Is.EqualTo(new ConditionOperand.Field(fields["arrival"])), "an answer operand");
            Assert.That(Condition(copy.Actions[0].Rules[0]).Field, Is.EqualTo(fields["ticket"]), "an action's rule");

            // Checks, cascading options and the verification field too.
            Assert.That(((MatchesFieldCheck)page1.Groups[0].Fields[2].Validation[0]).Field, Is.EqualTo(fields["email"]), "a matches-field check");
            Assert.That(sessions.Options!.Parent, Is.EqualTo(fields["ticket"]), "the parent of cascading options");
            Assert.That(copy.Settings.Submission.Verification!.EmailField, Is.EqualTo(fields["email"]), "the verification field");

            // What names things outside the form stays.
            Assert.That(copy.Settings.Availability.MemberGroups, Is.EqualTo(full.Settings.Availability.MemberGroups), "member groups");
            Assert.That(sessions.Options.OptionList, Is.EqualTo(full.Pages[0].Groups[1].Fields[1].Options!.OptionList), "a library option list");
            Assert.That(ticket.Options!.Items.Select(item => item.Image), Is.EqualTo(full.Pages[0].Groups[1].Fields[0].Options!.Items.Select(item => item.Image)), "media");
            Assert.That(copy.Outcomes[0].Redirect!.Content, Is.EqualTo(full.Outcomes[0].Redirect!.Content), "a content page");
            Assert.That(fields.Values, Has.None.EqualTo(Arrival).And.None.EqualTo(Departure));
        });
    }

    [Test]
    public void Settings_ThatNameAnElementByKey_FollowItsNewKey_AsAValueOrAPropertyName_InAnyLetterCase()
    {
        var full = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var external = Guid.Parse("74000000-0000-4000-8000-000000000001");
        var key = Email.ToString("D");
        var settings = new JsonObject
        {
            ["replyTo"] = key,
            ["map"] = new JsonObject { [key] = "email", ["other"] = "x" },
            ["upper"] = key.ToUpperInvariant(),
            ["inText"] = "field " + key,
            ["braced"] = "{" + key + "}",
            ["spaceBefore"] = " " + key,
            ["spaceAfter"] = key + " ",
            ["plain"] = Email.ToString("N"),
            ["media"] = external.ToString("D"),
            ["list"] = new JsonArray(key, "x"),
        };
        var definition = full with { Actions = [full.Actions[0] with { Settings = SettingsObject.Parse(settings.ToJsonString()) }] };

        var copy = DefinitionKeys.Renew(definition, DefinitionFixtures.Serializer());
        var email = DefinitionKeys.Fields(copy).Single(field => field.Alias == "email").Key.ToString("D");
        var renewed = JsonNode.Parse(copy.Actions[0].Settings!.ToString())!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(renewed["replyTo"]!.GetValue<string>(), Is.EqualTo(email), "a value");
            Assert.That(renewed["map"]!.AsObject().Select(property => property.Key), Is.EquivalentTo(new[] { email, "other" }), "a property name");
            Assert.That(renewed["map"]![email]!.GetValue<string>(), Is.EqualTo("email"), "its value stays");
            Assert.That(renewed["upper"]!.GetValue<string>(), Is.EqualTo(email), "in another letter case, written as keys are");
            Assert.That(renewed["list"]![0]!.GetValue<string>(), Is.EqualTo(email), "an item of a list");
            Assert.That(renewed["inText"]!.GetValue<string>(), Is.EqualTo("field " + key), "a key inside a longer string is no reference");
            Assert.That(renewed["braced"]!.GetValue<string>(), Is.EqualTo("{" + key + "}"), "nor one in braces");
            Assert.That((renewed["spaceBefore"]!.GetValue<string>(), renewed["spaceAfter"]!.GetValue<string>()), Is.EqualTo((" " + key, key + " ")), "nor one with a space around it");
            Assert.That(renewed["plain"]!.GetValue<string>(), Is.EqualTo(Email.ToString("N")), "nor one without hyphens");
            Assert.That(renewed["media"]!.GetValue<string>(), Is.EqualTo(external.ToString("D")), "a key of something outside the form");
        });
    }

    [Test]
    public void Replace_OffersEveryReference_WithItsPath_AndKeepsWhatItGetsNullFor()
    {
        var kept = Guid.Parse("11000000-0000-4000-8000-000000000001");
        var changed = Guid.Parse("11000000-0000-4000-8000-000000000002");
        var replacement = Guid.Parse("11000000-0000-4000-8000-000000000003");
        var node = new JsonObject
        {
            ["a"] = new JsonArray(new JsonObject { ["b"] = changed.ToString("D") }, 1, true, null),
            [kept.ToString("D")] = changed.ToString("D"),
            ["name with spaces"] = kept.ToString("D"),
            ["c"] = "text",
        };
        var offered = new List<(Guid, string)>();

        DefinitionKeys.Replace(node, "$.x", (key, path) =>
        {
            offered.Add((key, path()));
            return key == changed ? replacement : null;
        });

        Assert.Multiple(() =>
        {
            Assert.That(offered, Is.EqualTo(new[]
            {
                (kept, $"$.x['{kept:D}']"),
                (changed, "$.x.a[0].b"),
                (changed, $"$.x['{kept:D}']"),
                (kept, "$.x['name with spaces']"),
            }));
            Assert.That(node.ToJsonString(), Is.EqualTo($$"""{"a":[{"b":"{{replacement:D}}"},1,true,null],"{{kept:D}}":"{{replacement:D}}","name with spaces":"{{kept:D}}","c":"text"}"""));
        });
    }

    [Test]
    public void ARenamedProperty_KeepsItsPlace_AmongTheOthers()
    {
        var from = Guid.Parse("11000000-0000-4000-8000-000000000001");
        var to = Guid.Parse("11000000-0000-4000-8000-000000000002");
        var node = new JsonObject { ["first"] = 1, [from.ToString("D")] = 2, ["last"] = 3 };

        DefinitionKeys.Replace(node, JsonPaths.Root, (key, _) => key == from ? to : null);

        Assert.That(node.Select(property => property.Key), Is.EqualTo(new[] { "first", to.ToString("D"), "last" }));
    }

    [Test]
    public void TheFields_AreInDocumentOrder_WithTheChildrenOfARepeatableGroupAfterIt()
    {
        static FormField Field(string alias, params FormField[] children) => new() { Key = Guid.NewGuid(), Type = children.Length == 0 ? "text" : "repeater", Alias = alias, Fields = children };
        static FormGroup Group(params FormField[] fields) => new() { Key = Guid.NewGuid(), Fields = fields };
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Pages =
            [
                new FormPage { Key = Guid.NewGuid(), Groups = [Group(Field("a"), Field("guests", Field("guestName"), Field("diet", Field("allergy")))), Group(Field("b"))] },
                new FormPage { Key = Guid.NewGuid(), Groups = [Group(Field("c"))] },
            ],
        };

        Assert.That(DefinitionKeys.Fields(definition).Select(field => field.Alias), Is.EqualTo(new[] { "a", "guests", "guestName", "diet", "allergy", "b", "c" }));
    }

    [Test]
    public void ARemap_ThatGivesTwoElementsOneKey_IsRefused()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var one = Guid.NewGuid();

        Assert.That(
            () => DefinitionKeys.Remap(definition, new Dictionary<Guid, Guid> { [Name] = one, [Email] = one }, DefinitionFixtures.Serializer()),
            Throws.InvalidOperationException.With.Message.Contains(DefinitionErrors.DuplicateKey));
    }

    [Test]
    public void NewKeys_GivesEachKeyOneNewKey_OnceForAKeyNamedTwice()
    {
        var key = Guid.NewGuid();
        var other = Guid.NewGuid();

        var map = DefinitionKeys.NewKeys([key, other, key]);

        Assert.Multiple(() =>
        {
            Assert.That(map.Keys, Is.EquivalentTo(new[] { key, other }));
            Assert.That(map.Values, Is.Unique.And.None.EqualTo(key).And.None.EqualTo(other));
        });
    }

    private static Guid Fixture(string key) => Guid.Parse(key);

    private static RuleCondition Condition(FormRule rule) => (RuleCondition)rule.Conditions[0];

    private static RuleCondition Condition(FormValidationRule rule) => (RuleCondition)rule.Conditions[0];

    // Every Guid property named Key in the model, found by reflection, so an element type added later is listed too. An
    // answer operand's Key names another element, so operands are references, not elements.
    private static List<Guid> ElementKeys(object model)
    {
        var keys = new List<Guid>();
        Walk(model);
        return keys;

        void Walk(object? value)
        {
            switch (value)
            {
                case null or string or LocalizedString or SettingsObject or FieldDefaultValue or ConditionOperand:
                    return;
                case IEnumerable items:
                    foreach (var item in items)
                    {
                        Walk(item);
                    }

                    return;
            }

            var type = value.GetType();
            if (type.IsValueType)
            {
                return;
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
            {
                var item = property.GetValue(value);
                if (property.Name == "Key" && property.PropertyType == typeof(Guid))
                {
                    keys.Add((Guid)item!);
                }
                else
                {
                    Walk(item);
                }
            }
        }
    }
}
