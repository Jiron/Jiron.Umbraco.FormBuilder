using System.Globalization;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The comparison of two definitions (ADR-0002, ADR-0045): elements matched by key, added, removed, moved (the fewest
/// moves) and changed, with every differing property by its path and both values; option items and grid rows matched
/// by their value or key. Property-style tests over generated definitions show that a comparison of a definition with
/// itself is empty, that swapping the two mirrors it, and that its property changes rebuild the newer element.
/// </summary>
[TestFixture]
public sealed class DefinitionDiffTests
{
    private const int Seeds = 100;

    [Test]
    public void ADefinition_ComparedWithItself_HasNoChanges()
    {
        var full = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var generator = new DefinitionGenerator(4501);

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionDiff.Compare(full, full), Is.Empty);
            for (var i = 0; i < Seeds; i++)
            {
                var definition = generator.Next();
                Assert.That(DefinitionDiff.Compare(definition, definition), Is.Empty, $"definition {i}");
            }
        });
    }

    [Test]
    public void AChangedTextAndFlag_AreReportedPerCultureAndProperty()
    {
        var before = Base();
        var after = Base();
        Field(after, 0, 0, 0)["label"] = new JsonObject { [string.Empty] = "Full name", ["de"] = "Name" };
        Field(after, 0, 0, 1)["required"] = true;

        var changes = Diff(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => (change.ElementKind, change.Key, change.ChangeKind, change.Moved)), Is.EqualTo(new[]
            {
                (DefinitionElementKind.Field, (Guid?)K(11), DefinitionChangeKind.Changed, false),
                (DefinitionElementKind.Field, (Guid?)K(12), DefinitionChangeKind.Changed, false),
            }));
            Assert.That(Paths(changes[0]), Is.EqualTo(new[] { "label/ : \"Name\" -> \"Full name\"", "label/de : null -> \"Name\"" }));
            Assert.That(Paths(changes[1]), Is.EqualTo(new[] { "required : false -> true" }));
            Assert.That((changes[0].Label, changes[0].Alias, changes[0].Type, changes[0].ParentKey), Is.EqualTo(("Full name", "name", "text", (Guid?)K(2))));
            Assert.That(changes[1].Before?["required"]?.GetValue<bool>(), Is.False);
            Assert.That(changes[1].After?["required"]?.GetValue<bool>(), Is.True);
            Assert.That(changes[1].After?.ContainsKey("key"), Is.False, "the key names the element already");
        });
    }

    [Test]
    public void AChangedType_IsTheNewerOne()
    {
        var before = Base();
        var after = Base();
        Field(after, 0, 0, 0)["type"] = "textarea";

        var change = Diff(before, after).Single();

        Assert.Multiple(() =>
        {
            Assert.That(change.Type, Is.EqualTo("textarea"));
            Assert.That(Paths(change), Is.EqualTo(new[] { "type : \"text\" -> \"textarea\"" }));
        });
    }

    [Test]
    public void APageTitle_IsItsLabel()
    {
        var before = Base();
        var after = Base();
        after["pages"]![0]!["title"] = new JsonObject { [string.Empty] = "Contact details" };

        var change = Diff(before, after).Single();

        Assert.That((change.ElementKind, change.Key, change.Label), Is.EqualTo((DefinitionElementKind.Page, (Guid?)K(9), "Contact details")));
    }

    [Test]
    public void ValidationRules_AreElementsByKey()
    {
        var before = Base();
        var after = Base();
        before["validationRules"] = new JsonArray(ValidationRule(41, "Choose a topic."));
        after["validationRules"] = new JsonArray(ValidationRule(41, "Choose one topic."), ValidationRule(42, "Give an email."));

        var changes = Diff(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => (change.ElementKind, change.Key, change.ChangeKind, change.ParentKey)), Is.EqualTo(new[]
            {
                (DefinitionElementKind.ValidationRule, (Guid?)K(41), DefinitionChangeKind.Changed, (Guid?)null),
                (DefinitionElementKind.ValidationRule, (Guid?)K(42), DefinitionChangeKind.Added, (Guid?)null),
            }));
            Assert.That(Paths(changes[0]), Is.EqualTo(new[] { "message/ : \"Choose a topic.\" -> \"Choose one topic.\"" }));
        });
    }

    [Test]
    public void AKeyTwice_IsReportedOnce_InEitherVersion()
    {
        // Documents a save refuses, as a stored row changed by hand could hold them.
        var relabelled = Base();
        Field(relabelled, 0, 0, 1)["label"] = new JsonObject { [string.Empty] = "Your email" };
        var withoutEmail = Base();
        Fields(withoutEmail, 0, 0).RemoveAt(1);
        var plain = DefinitionFixtures.Read(Base().ToJsonString());

        Assert.Multiple(() =>
        {
            Assert.That(DefinitionDiff.Compare(plain, EmailTwice(DefinitionFixtures.Read(relabelled.ToJsonString()))).Select(change => (change.Key, change.ChangeKind)), Is.EqualTo(new[] { ((Guid?)K(12), DefinitionChangeKind.Changed) }));
            Assert.That(DefinitionDiff.Compare(EmailTwice(plain), DefinitionFixtures.Read(withoutEmail.ToJsonString())).Select(change => (change.Key, change.ChangeKind)), Is.EqualTo(new[] { ((Guid?)K(12), DefinitionChangeKind.Removed) }));
        });
    }

    [Test]
    public void AFieldsTypeSettings_AreComparedOneByOne()
    {
        var before = Base();
        var after = Base();
        Field(before, 0, 0, 0)["settings"] = new JsonObject { ["maxLength"] = 100, ["counter"] = true };
        Field(after, 0, 0, 0)["settings"] = new JsonObject { ["maxLength"] = 80, ["prefix"] = new JsonObject { [string.Empty] = "Mr" }, ["counter"] = true };

        var changes = Diff(before, after);

        Assert.That(Paths(changes.Single()), Is.EqualTo(new[] { "settings/maxLength : 100 -> 80", "settings/prefix/ : null -> \"Mr\"" }));
    }

    [Test]
    public void OptionItems_AreMatchedByValue_AddedRemovedChangedAndReordered()
    {
        var before = Base();
        var after = Base();
        Items(after).Clear();
        Items(after).Add(Option("support", "Support"));
        Items(after).Add(Option("press", "Press office"));
        Items(after).Add(Option("delivery", "Delivery"));

        var change = Diff(before, after).Single();

        Assert.Multiple(() =>
        {
            Assert.That(change.Key, Is.EqualTo(K(13)));
            Assert.That(Paths(change), Is.EqualTo(new[]
            {
                "options/items : [\"sales\",\"press\",\"support\"] -> [\"support\",\"press\",\"delivery\"]",
                "options/items/press/label/ : \"Press\" -> \"Press office\"",
                "options/items/delivery : null -> {\"label\":{\"\":\"Delivery\"}}",
                "options/items/sales : {\"label\":{\"\":\"Sales\"}} -> null",
            }));
        });
    }

    [Test]
    public void AnOptionAddedAtTheEnd_NeedsNoOrder_ButOneAddedBetween_Does()
    {
        var before = Base();
        var appended = Base();
        Items(appended).Add(Option("delivery", "Delivery"));
        var inserted = Base();
        Items(inserted).Insert(1, Option("delivery", "Delivery"));

        Assert.Multiple(() =>
        {
            Assert.That(Paths(Diff(before, appended).Single()), Is.EqualTo(new[] { "options/items/delivery : null -> {\"label\":{\"\":\"Delivery\"}}" }));
            Assert.That(Paths(Diff(before, inserted).Single()).First(), Does.StartWith("options/items : "), "where it went");
        });
    }

    [Test]
    public void AnOptionRemoved_NeedsNoOrder()
    {
        var before = Base();
        var after = Base();
        Items(after).RemoveAt(1);

        Assert.That(Paths(Diff(before, after).Single()), Is.EqualTo(new[] { """options/items/press : {"label":{"":"Press"}} -> null""" }));
    }

    [Test]
    public void OptionsGivenToAFieldWithoutAny_AreAddedOneByOne()
    {
        var before = Base();
        var after = Base();
        Field(before, 0, 0, 2).Remove("options");

        var items = Paths(Diff(before, after).Single()).Where(path => path.StartsWith("options/items", StringComparison.Ordinal));

        Assert.That(items, Is.EqualTo(new[]
        {
            """options/items/sales : null -> {"label":{"":"Sales"}}""",
            """options/items/press : null -> {"label":{"":"Press"}}""",
            """options/items/support : null -> {"label":{"":"Support"}}""",
        }));
    }

    [Test]
    public void OptionItemsWithoutDistinctValues_AreComparedAsAWhole()
    {
        var before = Base();
        var after = Base();
        Items(after).Add(Option("sales", "Sales again"));

        Assert.That(Paths(Diff(before, after).Single()).Single(), Does.StartWith("options/items : [{"), "a draft may hold a value twice until it is published");
    }

    [Test]
    public void GridRows_AreMatchedByKey()
    {
        var before = Base();
        var after = Base();
        Group(before, 0, 1)["fields"] = new JsonArray(Grid(new JsonObject { ["key"] = K(51).ToString(), ["label"] = new JsonObject { [string.Empty] = "Venue" }, ["required"] = false }));
        Group(after, 0, 1)["fields"] = new JsonArray(Grid(new JsonObject { ["key"] = K(51).ToString(), ["label"] = new JsonObject { [string.Empty] = "Location" }, ["required"] = true }));

        var change = Diff(before, after).Single();

        Assert.That(Paths(change), Is.EqualTo(new[]
        {
            $"rows/{K(51)}/label/ : \"Venue\" -> \"Location\"",
            $"rows/{K(51)}/required : false -> true",
        }));
    }

    [Test]
    public void AFieldMovedToAnotherGroup_IsMoved_WithBothParents()
    {
        var before = Base();
        var after = Base();
        var email = Field(after, 0, 0, 1);
        Fields(after, 0, 0).Remove(email);
        Fields(after, 0, 1).Add(email);

        var change = Diff(before, after).Single();

        Assert.Multiple(() =>
        {
            Assert.That((change.ElementKind, change.Key, change.ChangeKind, change.Moved), Is.EqualTo((DefinitionElementKind.Field, (Guid?)K(12), DefinitionChangeKind.Changed, true)));
            Assert.That((change.ParentKey, change.PreviousParentKey), Is.EqualTo(((Guid?)K(3), (Guid?)K(2))));
            Assert.That(change.Properties, Is.Empty);
        });
    }

    [Test]
    public void AFieldMovedWithinItsGroup_IsTheOnlyOneMoved()
    {
        var before = Base();
        var after = Base();
        var topic = Field(after, 0, 0, 2);
        Fields(after, 0, 0).Remove(topic);
        Fields(after, 0, 0).Insert(0, topic);

        var changes = Diff(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => (change.Key, change.Moved)), Is.EqualTo(new[] { ((Guid?)K(13), true) }), "not the two it passed");
            Assert.That(changes.Single().PreviousParentKey, Is.Null, "the same group");
        });
    }

    [Test]
    public void SwappedPages_AndAReversedActionList_MoveTheFewest()
    {
        var before = Base();
        var after = Base();
        var second = new JsonObject { ["key"] = K(4).ToString(), ["title"] = new JsonObject { [string.Empty] = "Second" }, ["groups"] = new JsonArray(new JsonObject { ["key"] = K(5).ToString() }) };
        before["pages"]!.AsArray().Add(second.DeepClone());
        after["pages"]!.AsArray().Insert(0, second);
        before["actions"] = new JsonArray(Action(71, "One"), Action(72, "Two"), Action(73, "Three"));
        after["actions"] = new JsonArray(Action(73, "Three"), Action(72, "Two"), Action(71, "One"));

        var changes = Diff(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Where(change => change.ElementKind == DefinitionElementKind.Page).Select(change => change.Key), Has.Exactly(1).Items, "one of two pages explains a swap");
            Assert.That(changes.Count(change => change.ElementKind == DefinitionElementKind.Action && change.Moved), Is.EqualTo(2), "two of three actions explain a reversal");
            Assert.That(changes.Where(change => change.ElementKind == DefinitionElementKind.Action).Select(change => change.Label), Is.All.Not.Null);
            Assert.That(changes.Where(change => change.ElementKind == DefinitionElementKind.Action).Select(change => change.Type), Is.All.EqualTo("email"));
        });
    }

    [Test]
    public void Rules_AreElementsOfTheirOwn_WhoseOrderMeansNothing()
    {
        var before = Base();
        var after = Base();
        var show = Rule(81, "show", K(13));
        var require = Rule(82, "require", K(13));
        Field(before, 0, 0, 0)["rules"] = new JsonArray(show.DeepClone(), require.DeepClone());
        Field(after, 0, 0, 0)["rules"] = new JsonArray(require.DeepClone(), show.DeepClone(), Rule(83, "hide", K(12)));
        Field(after, 0, 0, 0)["rules"]![0]!["conditions"]![0]!["value"] = "press";

        var changes = Diff(before, after);

        Assert.Multiple(() =>
        {
            Assert.That(changes.Select(change => (change.ElementKind, change.Key, change.ChangeKind, change.Moved, change.ParentKey)), Is.EqualTo(new[]
            {
                (DefinitionElementKind.Rule, (Guid?)K(82), DefinitionChangeKind.Changed, false, (Guid?)K(11)),
                (DefinitionElementKind.Rule, (Guid?)K(83), DefinitionChangeKind.Added, false, (Guid?)K(11)),
            }));
            Assert.That(changes[0].Properties.Single().Path, Is.EqualTo(new[] { "conditions" }), "conditions compare as a whole");
            Assert.That(changes[1].After?["effect"]?.GetValue<string>(), Is.EqualTo("hide"));
        });
    }

    [Test]
    public void ARepeatableGroupsChildren_AreFieldsUnderIt()
    {
        var before = Base();
        var after = Base();
        Fields(before, 0, 1).Add(Repeater(Child(92, "guestName")));
        Fields(after, 0, 1).Add(Repeater(Child(92, "guestName"), Child(93, "guestEmail")));

        var change = Diff(before, after).Single();

        Assert.Multiple(() =>
        {
            Assert.That((change.ElementKind, change.Key, change.ChangeKind, change.ParentKey, change.Alias), Is.EqualTo((DefinitionElementKind.Field, (Guid?)K(93), DefinitionChangeKind.Added, (Guid?)K(91), "guestEmail")));
            Assert.That(change.Before, Is.Null);
            Assert.That(change.After?["type"]?.GetValue<string>(), Is.EqualTo("text"));
        });
    }

    [Test]
    public void SettingsGroups_AreElementsByName()
    {
        var before = Base();
        var after = Base();
        after["settings"] = new JsonObject { ["appearance"] = new JsonObject { ["cssClass"] = "wide", ["validationSummary"] = false } };

        var change = Diff(before, after).Single();

        Assert.Multiple(() =>
        {
            Assert.That((change.ElementKind, change.Key, change.SettingsGroup, change.ChangeKind), Is.EqualTo((DefinitionElementKind.Settings, (Guid?)null, "appearance", DefinitionChangeKind.Changed)));
            Assert.That(Paths(change), Is.EqualTo(new[] { "validationSummary : true -> false", "cssClass : null -> \"wide\"" }));
        });
    }

    [Test]
    public void TheChanges_ComeInTheNewerOrder_ThenWhatWasRemoved_InTheOlderOrder()
    {
        var before = Base();
        var after = Base();
        before["outcomes"]!.AsArray().Add(new JsonObject { ["key"] = K(62).ToString(), ["kind"] = "message" });
        Fields(after, 0, 0).RemoveAt(2);
        Fields(after, 0, 0).RemoveAt(0);
        Fields(after, 0, 1).Add(Child(94, "phone"));
        after["actions"] = new JsonArray(Action(74, "Notify"));
        after["settings"] = new JsonObject { ["submission"] = new JsonObject { ["summaryPage"] = true } };

        var changes = Diff(before, after);

        Assert.That(changes.Select(change => $"{change.ElementKind} {change.Key?.ToString() ?? change.SettingsGroup} {change.ChangeKind}"), Is.EqualTo(new[]
        {
            "Settings submission Changed",
            $"Field {K(94)} Added",
            $"Action {K(74)} Added",
            $"Field {K(11)} Removed",
            $"Field {K(13)} Removed",
            $"Outcome {K(62)} Removed",
        }));
    }

    [Test]
    public void ASummary_KeepsWhatAListShows()
    {
        var before = Base();
        var after = Base();
        Field(after, 0, 0, 0)["label"] = new JsonObject { [string.Empty] = "Full name" };

        var email = Field(after, 0, 0, 1);
        Fields(after, 0, 0).Remove(email);
        Fields(after, 0, 1).Add(email);

        var summaries = Diff(before, after).Select(change => change.Summarize());

        Assert.That(summaries, Is.EqualTo(new[]
        {
            new DefinitionChangeSummary(DefinitionElementKind.Field, K(11), null, K(2), DefinitionChangeKind.Changed, false, "Full name", "name"),
            new DefinitionChangeSummary(DefinitionElementKind.Field, K(12), null, K(3), DefinitionChangeKind.Changed, true, "Email", "email"),
        }));
    }

    [Test]
    public void ARemovedElement_KeepsItsOlderLabelAndParent()
    {
        var before = Base();
        var after = Base();
        Fields(after, 0, 0).RemoveAt(1);

        var change = Diff(before, after).Single();

        Assert.That((change.ChangeKind, change.Label, change.Alias, change.Type, change.ParentKey, change.After), Is.EqualTo((DefinitionChangeKind.Removed, "Email", "email", "email", (Guid?)K(2), (JsonObject?)null)));
    }

    [TestCase(new int[0], new int[0])]
    [TestCase(new[] { 0, 1, 2 }, new[] { 0, 1, 2 })]
    [TestCase(new[] { 2, 0, 1 }, new[] { 1, 2 })]
    [TestCase(new[] { 1, 0 }, new[] { 1 })]
    [TestCase(new[] { 3, 1, 2, 0, 4 }, new[] { 1, 2, 4 })]
    public void TheLongestRunInOrder_IsFound(int[] values, int[] positions)
    {
        Assert.That(DefinitionDiff.LongestIncreasing(values), Is.EquivalentTo(positions));
    }

    [Test]
    public void TheLongestRun_IsStrictlyIncreasing()
    {
        Assert.That(DefinitionDiff.LongestIncreasing([1, 1, 1]), Has.Count.EqualTo(1));
    }

    [Test]
    public void AComparison_NeedsBothDefinitions()
    {
        var definition = DefinitionFixtures.Read(Base().ToJsonString());

        Assert.Multiple(() =>
        {
            Assert.That(() => DefinitionDiff.Compare(null!, definition), Throws.ArgumentNullException);
            Assert.That(() => DefinitionDiff.Compare(definition, null!), Throws.ArgumentNullException);
            Assert.That(() => DefinitionDiff.LongestIncreasing(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void GeneratedChanges_AreMirroredBySwapping_RebuildTheNewerElement_AndNameEveryKeyOnce()
    {
        var generator = new DefinitionGenerator(4502);
        var random = new Random(4503);
        var moved = 0;

        Assert.Multiple(() =>
        {
            for (var i = 0; i < Seeds; i++)
            {
                var older = generator.Next();
                var olderJson = Canonical(older);
                var newerJson = DefinitionMutator.Mutate((JsonObject)olderJson.DeepClone(), random);
                var newer = DefinitionFixtures.Read(newerJson.ToJsonString());
                var changes = DefinitionDiff.Compare(older, newer);
                var mirrored = DefinitionDiff.Compare(newer, older);
                moved += changes.Count(change => change.Moved);

                // Two directions may keep different siblings of one length in place, and an item put between others needs
                // its order only in one direction; everything else mirrors.
                Assert.That(Mirror(Comparable(changes)), Is.EquivalentTo(Comparable(mirrored).Select(Describe)), $"definition {i}: swapped");
                Assert.That(MovesInPlace(changes), Is.EquivalentTo(MovesInPlace(mirrored)), $"definition {i}: as many moves either way");
                Assert.That(changes.Select(Id), Is.Unique, $"definition {i}: each element once");
                AssertRebuilt(changes, $"definition {i}");
                AssertRebuilt(mirrored, $"definition {i} swapped");

                var olderKeys = DefinitionMutator.Keys(olderJson);
                var newerKeys = DefinitionMutator.Keys(newerJson);
                Assert.That(changes.Where(change => change.ChangeKind == DefinitionChangeKind.Added).Select(change => change.Key!.Value), Is.EquivalentTo(newerKeys.Except(olderKeys)), $"definition {i}: added");
                Assert.That(changes.Where(change => change.ChangeKind == DefinitionChangeKind.Removed).Select(change => change.Key!.Value), Is.EquivalentTo(olderKeys.Except(newerKeys)), $"definition {i}: removed");

                foreach (var change in changes)
                {
                    Assert.That(change.Before, Is.EqualTo(Own(olderJson, change)).Using<JsonNode?>(SameJson), $"definition {i}: {Id(change)} before");
                    Assert.That(change.After, Is.EqualTo(Own(newerJson, change)).Using<JsonNode?>(SameJson), $"definition {i}: {Id(change)} after");
                }
            }

            Assert.That(moved, Is.GreaterThan(Seeds / 4), "the mutations move elements often enough to test it");
        });
    }

    // Each changed element's property changes rebuild its newer properties from its older ones.
    [Test]
    public void ChangesActions_HoldsForAnActionOrARuleOfOne_AndForNothingElse()
    {
        var before = Base();
        before["actions"] = new JsonArray(Action(71, "Notify"));
        var withRule = With(before, root => root["actions"]![0]!["rules"] = new JsonArray(Rule(81, "run", K(13))));

        Assert.Multiple(() =>
        {
            Assert.That(Changes(before, before), Is.False, "nothing");
            Assert.That(Changes(before, With(before, root => Field(root, 0, 0, 0)["required"] = true)), Is.False, "a field");
            Assert.That(Changes(before, With(before, root => Field(root, 0, 0, 0)["rules"] = new JsonArray(Rule(82, "hide", K(12))))), Is.False, "a rule of a field");
            Assert.That(Changes(before, With(before, root => root["actions"]![0]!["name"] = "Tell sales")), Is.True, "an action's name");
            Assert.That(Changes(before, With(before, root => root["actions"]!.AsArray().Add(Action(72, "Thank")))), Is.True, "an added action");
            Assert.That(Changes(before, With(before, root => root["actions"] = new JsonArray())), Is.True, "a removed action");
            Assert.That(Changes(before, withRule), Is.True, "a rule added to an action");
            Assert.That(Changes(withRule, With(withRule, root => root["actions"]![0]!["rules"]![0]!["conditions"]![0]!["value"] = "press")), Is.True, "a rule of an action changed");
            Assert.That(Changes(withRule, With(before, root => Field(root, 0, 0, 0)["rules"] = new JsonArray(Rule(81, "show", K(13))))), Is.True, "a rule moved from an action to a field");
            Assert.That(Changes(With(before, root => Field(root, 0, 0, 0)["rules"] = new JsonArray(Rule(81, "run", K(13)))), withRule), Is.True, "a rule moved from a field to an action");
            Assert.That(DefinitionDiff.ChangesActions(null, DefinitionFixtures.Read(before.ToJsonString())), Is.True, "a draft that could not be read had no actions");
            Assert.That(DefinitionDiff.ChangesActions(null, DefinitionFixtures.Read(Base().ToJsonString())), Is.False);
        });

        static bool Changes(JsonObject from, JsonObject to) => DefinitionDiff.ChangesActions(DefinitionFixtures.Read(from.ToJsonString()), DefinitionFixtures.Read(to.ToJsonString()));

        static JsonObject With(JsonObject root, System.Action<JsonObject> change)
        {
            var copy = root.DeepClone().AsObject();
            change(copy);
            return copy;
        }
    }

    private static void AssertRebuilt(IReadOnlyList<DefinitionChange> changes, string what)
    {
        foreach (var change in changes.Where(change => change.ChangeKind == DefinitionChangeKind.Changed))
        {
            Assert.That(change.Properties.Count > 0 || change.Moved, Is.True, $"{what}: {Id(change)} differs");
            var rebuilt = DefinitionMutator.Apply(change.Before!, change.Properties, change.ElementKind == DefinitionElementKind.Field);
            Assert.That(JsonNode.DeepEquals(rebuilt, change.After), Is.True, $"{what}: {Id(change)} rebuilt as {rebuilt.ToJsonString()}, not {change.After?.ToJsonString()}");
        }
    }

    // The changes that must mirror: without order entries of keyed lists, and without elements that only moved among
    // their siblings.
    private static List<DefinitionChange> Comparable(IReadOnlyList<DefinitionChange> changes) => changes
        .Select(change => change with { Properties = change.Properties.Where(property => !IsOrder(property)).ToList() })
        .Where(change => change.ChangeKind != DefinitionChangeKind.Changed || change.Properties.Count > 0 || change.PreviousParentKey is not null)
        .ToList();

    private static bool IsOrder(DefinitionPropertyChange property) =>
        property.Path is ["options", "items"] or ["rows"] && property.To is JsonArray to && to.All(name => name is JsonValue value && value.TryGetValue<string>(out _));

    // How many elements moved among their siblings, per list.
    private static IEnumerable<string> MovesInPlace(IReadOnlyList<DefinitionChange> changes) => changes
        .Where(change => change.Moved && change.PreviousParentKey is null)
        .GroupBy(change => $"{change.ElementKind} under {change.ParentKey}")
        .Select(group => $"{group.Key}: {group.Count()}");

    // The element's own properties in a canonical document: what Before and After must be.
    private static JsonObject? Own(JsonObject root, DefinitionChange change)
    {
        if (change.ElementKind == DefinitionElementKind.Settings)
        {
            return root["settings"]?[change.SettingsGroup!]?.DeepClone() as JsonObject ?? [];
        }

        if (DefinitionMutator.FindByKey(root, change.Key!.Value) is not { } node)
        {
            return null;
        }

        var own = (JsonObject)node.DeepClone();
        own.Remove("key");
        own.Remove("rules");
        own.Remove(change.ElementKind switch
        {
            DefinitionElementKind.Page => "groups",
            DefinitionElementKind.Group or DefinitionElementKind.Field => "fields",
            _ => string.Empty,
        });
        return own;
    }

    private static bool SameJson(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

    private static string Id(DefinitionChange change) => $"{change.ElementKind}:{change.Key?.ToString() ?? change.SettingsGroup}";

    // A change as text, with its properties, to compare one comparison with another.
    private static string Describe(DefinitionChange change) =>
        $"{Id(change)} {change.ChangeKind} parent={change.ParentKey} previous={change.PreviousParentKey} label={change.Label} {string.Join(" | ", Paths(change).Order(StringComparer.Ordinal))}";

    // What the swapped comparison must say: added and removed trade places, values swap, and a move keeps its parents swapped.
    private static IEnumerable<string> Mirror(List<DefinitionChange> changes) => changes.Select(change => Describe(change with
    {
        ChangeKind = change.ChangeKind switch
        {
            DefinitionChangeKind.Added => DefinitionChangeKind.Removed,
            DefinitionChangeKind.Removed => DefinitionChangeKind.Added,
            _ => DefinitionChangeKind.Changed,
        },
        ParentKey = change.PreviousParentKey ?? change.ParentKey,
        PreviousParentKey = change.PreviousParentKey is null ? null : change.ParentKey,
        Label = change.ChangeKind == DefinitionChangeKind.Changed ? Label(change.Before, change) : change.Label,
        Properties = Swapped(change.Properties),
    }));

    // The label the swapped comparison names: the older version's, once it is the newer one.
    private static string? Label(JsonObject? own, DefinitionChange change) => change.ElementKind switch
    {
        DefinitionElementKind.Page or DefinitionElementKind.Group => (own?["title"] as JsonObject)?[string.Empty]?.GetValue<string>() ?? change.Label,
        DefinitionElementKind.Field => (own?["label"] as JsonObject)?[string.Empty]?.GetValue<string>() ?? change.Label,
        DefinitionElementKind.Action => own?["name"]?.GetValue<string>() ?? change.Label,
        _ => change.Label,
    };

    private static List<DefinitionPropertyChange> Swapped(IReadOnlyList<DefinitionPropertyChange> properties) =>
        properties.Select(property => property with { From = property.To, To = property.From }).ToList();

    private static IEnumerable<string> Paths(DefinitionChange change) =>
        change.Properties.Select(property => $"{string.Join("/", property.Path)} : {property.From?.ToJsonString() ?? "null"} -> {property.To?.ToJsonString() ?? "null"}");

    private static List<DefinitionChange> Diff(JsonObject before, JsonObject after) =>
        DefinitionDiff.Compare(DefinitionFixtures.Read(before.ToJsonString()), DefinitionFixtures.Read(after.ToJsonString())).ToList();

    private static JsonObject Canonical(FormDefinition definition) => JsonNode.Parse(FormDefinitionSerializer.Write(definition))!.AsObject();

    // A key by number, so the expectations read like the documents.
    private static Guid K(int n) => Guid.Parse("00000000-0000-4000-8000-" + n.ToString("D12", CultureInfo.InvariantCulture));

    // One page with two groups: a name, an email and a topic with two options in the first, nothing in the second.
    private static JsonObject Base() => new()
    {
        ["schemaVersion"] = 1,
        ["key"] = K(1).ToString(),
        ["pages"] = new JsonArray(new JsonObject
        {
            ["key"] = K(9).ToString(),
            ["groups"] = new JsonArray(
                new JsonObject
                {
                    ["key"] = K(2).ToString(),
                    ["fields"] = new JsonArray(
                        new JsonObject { ["key"] = K(11).ToString(), ["type"] = "text", ["alias"] = "name", ["label"] = new JsonObject { [string.Empty] = "Name" } },
                        new JsonObject { ["key"] = K(12).ToString(), ["type"] = "email", ["alias"] = "email", ["label"] = new JsonObject { [string.Empty] = "Email" } },
                        new JsonObject
                        {
                            ["key"] = K(13).ToString(),
                            ["type"] = "radio",
                            ["alias"] = "topic",
                            ["label"] = new JsonObject { [string.Empty] = "Topic" },
                            ["options"] = new JsonObject { ["source"] = "items", ["items"] = new JsonArray(Option("sales", "Sales"), Option("press", "Press"), Option("support", "Support")) },
                        }),
                },
                new JsonObject { ["key"] = K(3).ToString() }),
        }),
        ["outcomes"] = new JsonArray(new JsonObject { ["key"] = K(61).ToString(), ["kind"] = "message" }),
    };

    private static JsonObject Group(JsonObject root, int page, int group) => root["pages"]![page]!["groups"]![group]!.AsObject();

    private static JsonArray Fields(JsonObject root, int page, int group)
    {
        var node = Group(root, page, group);
        if (node["fields"] is not JsonArray fields)
        {
            fields = [];
            node["fields"] = fields;
        }

        return fields;
    }

    private static JsonObject Field(JsonObject root, int page, int group, int index) => Fields(root, page, group)[index]!.AsObject();

    private static JsonArray Items(JsonObject root) => Field(root, 0, 0, 2)["options"]!["items"]!.AsArray();

    private static JsonObject Option(string value, string label) => new() { ["value"] = value, ["label"] = new JsonObject { [string.Empty] = label } };

    private static JsonObject Grid(JsonObject row) => new()
    {
        ["key"] = K(50).ToString(),
        ["type"] = "grid",
        ["alias"] = "rating",
        ["label"] = new JsonObject { [string.Empty] = "Rating" },
        ["options"] = new JsonObject { ["source"] = "items", ["items"] = new JsonArray(Option("good", "Good")) },
        ["rows"] = new JsonArray(row),
    };

    // The definition with its email field in the second group too, under the same key.
    private static FormDefinition EmailTwice(FormDefinition definition)
    {
        var page = definition.Pages[0];
        return definition with { Pages = [page with { Groups = [page.Groups[0], page.Groups[1] with { Fields = [page.Groups[0].Fields[1]] }] }] };
    }

    private static JsonObject ValidationRule(int key, string message) => new()
    {
        ["key"] = K(key).ToString(),
        ["field"] = K(13).ToString(),
        ["message"] = new JsonObject { [string.Empty] = message },
        ["conditions"] = new JsonArray(new JsonObject { ["field"] = K(13).ToString(), ["op"] = "isEmpty" }),
    };

    private static JsonObject Action(int key, string name) => new() { ["key"] = K(key).ToString(), ["type"] = "email", ["name"] = name };

    private static JsonObject Rule(int key, string effect, Guid field) => new()
    {
        ["key"] = K(key).ToString(),
        ["effect"] = effect,
        ["conditions"] = new JsonArray(new JsonObject { ["field"] = field.ToString(), ["op"] = "equals", ["value"] = "sales" }),
    };

    private static JsonObject Repeater(params JsonObject[] children) => new()
    {
        ["key"] = K(91).ToString(),
        ["type"] = "repeater",
        ["alias"] = "guests",
        ["label"] = new JsonObject { [string.Empty] = "Guests" },
        ["fields"] = new JsonArray(children),
    };

    private static JsonObject Child(int key, string alias) => new() { ["key"] = K(key).ToString(), ["type"] = "text", ["alias"] = alias, ["label"] = new JsonObject { [string.Empty] = alias } };
}
