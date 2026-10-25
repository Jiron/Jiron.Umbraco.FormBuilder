using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;

/// <summary>
/// Compares two definitions element by element (ADR-0002, ADR-0045). It reads their canonical forms (ADR-0034), so a
/// default that one version leaves out and the other writes compares alike, matches elements by key (settings groups by
/// name), and reports each element that was added, removed, moved or changed, with every property that differs.
/// </summary>
internal static class DefinitionDiff
{
    private static readonly ImmutableArray<string> SettingsGroups = ["submission", "availability", "protection", "privacy", "appearance", "developer"];

    /// <summary>What changed from <paramref name="from"/> to <paramref name="to"/>, in the order of ADR-0045.</summary>
    public static IReadOnlyList<DefinitionChange> Compare(FormDefinition from, FormDefinition to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        var older = Elements(from);
        var newer = Elements(to);
        var olderById = new Dictionary<(DefinitionElementKind, string), Element>();
        foreach (var element in older)
        {
            olderById.TryAdd((element.Kind, element.Id), element);
        }

        var moved = Moved(newer, olderById);
        var seen = new HashSet<(DefinitionElementKind, string)>();
        var changes = new List<DefinitionChange>();
        foreach (var element in newer)
        {
            if (!seen.Add((element.Kind, element.Id)))
            {
                continue;
            }

            if (!olderById.TryGetValue((element.Kind, element.Id), out var before))
            {
                changes.Add(Change(element, null, element, DefinitionChangeKind.Added, false, []));
                continue;
            }

            var properties = new List<DefinitionPropertyChange>();
            CompareValues(before.Own, element.Own, [], properties, element.Kind == DefinitionElementKind.Field);
            var parentChanged = before.Parent != element.Parent;
            var isMoved = parentChanged || moved.Contains(element);
            if (properties.Count > 0 || isMoved)
            {
                changes.Add(Change(element, before, element, DefinitionChangeKind.Changed, isMoved, properties) with
                {
                    PreviousParentKey = parentChanged ? before.Parent : null,
                });
            }
        }

        var newerIds = newer.Select(element => (element.Kind, element.Id)).ToHashSet();
        var removed = new HashSet<(DefinitionElementKind, string)>();
        foreach (var element in older)
        {
            if (!newerIds.Contains((element.Kind, element.Id)) && removed.Add((element.Kind, element.Id)))
            {
                changes.Add(Change(element, element, null, DefinitionChangeKind.Removed, false, []));
            }
        }

        return changes;
    }

    /// <summary>
    /// Whether <paramref name="to"/> adds, removes, changes or moves an action of <paramref name="from"/>, or a rule of
    /// one (ADR-0051): what a save without <c>manageActions</c> may not do. A <paramref name="from"/> that could not be
    /// read counts as one without actions.
    /// </summary>
    public static bool ChangesActions(FormDefinition? from, FormDefinition to)
    {
        ArgumentNullException.ThrowIfNull(to);
        var older = from ?? new FormDefinition { Key = to.Key };
        var actions = older.Actions.Concat(to.Actions).Select(action => (Guid?)action.Key).ToHashSet();
        return Compare(older, to).Any(change =>
            change.ElementKind == DefinitionElementKind.Action
            || (change.ElementKind == DefinitionElementKind.Rule && (actions.Contains(change.ParentKey) || actions.Contains(change.PreviousParentKey))));
    }

    private static DefinitionChange Change(Element element, Element? before, Element? after, DefinitionChangeKind kind, bool moved, IReadOnlyList<DefinitionPropertyChange> properties) => new(
        element.Kind,
        element.Key,
        element.Kind == DefinitionElementKind.Settings ? element.Id : null,
        element.Parent,
        null,
        kind,
        moved,
        after?.Label ?? before?.Label,
        after?.Alias ?? before?.Alias,
        after?.Type ?? before?.Type,
        before?.Own,
        after?.Own,
        properties);

    // The elements of a definition in document order, each element's rules after it.
    private static List<Element> Elements(FormDefinition definition)
    {
        var root = JsonNode.Parse(FormDefinitionSerializer.Write(definition))!.AsObject();
        var elements = new List<Element>();
        // The canonical form writes every settings group, defaults included.
        var settings = root["settings"]!.AsObject();
        foreach (var name in SettingsGroups)
        {
            var group = settings[name]!.AsObject();
            elements.Add(new Element(DefinitionElementKind.Settings, name, null, null, "settings", 0, false, (JsonObject)group.DeepClone(), null, null, null));
        }

        var pages = Items(root, "pages");
        for (var p = 0; p < pages.Count; p++)
        {
            var page = pages[p];
            var pageKey = Add(elements, DefinitionElementKind.Page, page, null, "pages", p, true, Text(page, "title"), null, null, "groups");
            var groups = Items(page, "groups");
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g];
                var groupKey = Add(elements, DefinitionElementKind.Group, group, pageKey, "groups:" + pageKey, g, true, Text(group, "title"), null, null, "fields");
                AddFields(elements, Items(group, "fields"), groupKey);
            }
        }

        var validation = Items(root, "validationRules");
        for (var i = 0; i < validation.Count; i++)
        {
            Add(elements, DefinitionElementKind.ValidationRule, validation[i], null, "validationRules", i, true, null, null, null);
        }

        var actions = Items(root, "actions");
        for (var i = 0; i < actions.Count; i++)
        {
            Add(elements, DefinitionElementKind.Action, actions[i], null, "actions", i, true, StringOf(actions[i], "name"), null, StringOf(actions[i], "type"));
        }

        var outcomes = Items(root, "outcomes");
        for (var i = 0; i < outcomes.Count; i++)
        {
            Add(elements, DefinitionElementKind.Outcome, outcomes[i], null, "outcomes", i, true, null, null, null);
        }

        return elements;
    }

    private static void AddFields(List<Element> elements, List<JsonObject> fields, Guid? parent)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            var key = Add(elements, DefinitionElementKind.Field, field, parent, "fields:" + parent, f, true, Text(field, "label"), StringOf(field, "alias"), StringOf(field, "type"), "fields");
            AddFields(elements, Items(field, "fields"), key);
        }
    }

    // Adds an element and then its rules; the element's own properties leave out its key, its rules and what it holds.
    private static Guid? Add(List<Element> elements, DefinitionElementKind kind, JsonObject node, Guid? parent, string container, int index, bool ordered, string? label, string? alias, string? type, string? holds = null)
    {
        var own = (JsonObject)node.DeepClone();
        own.Remove("key");
        own.Remove("rules");
        if (holds is not null)
        {
            own.Remove(holds);
        }

        var key = Key(node);
        elements.Add(new Element(kind, key?.ToString() ?? $"{container}#{index}", key, parent, container, index, ordered, own, label, alias, type));
        var rules = Items(node, "rules");
        for (var r = 0; r < rules.Count; r++)
        {
            Add(elements, DefinitionElementKind.Rule, rules[r], key, "rules:" + key, r, false, null, null, null);
        }

        return key;
    }

    private static Guid? Key(JsonObject node) => Guid.TryParse(StringOf(node, "key"), out var key) ? key : null;

    private static string? StringOf(JsonObject node, string property) =>
        node[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static List<JsonObject> Items(JsonObject node, string property) =>
        node[property] is JsonArray array ? array.OfType<JsonObject>().ToList() : [];

    // The default-culture text of a localised text property, or null.
    private static string? Text(JsonObject node, string property) =>
        node[property] is JsonObject text ? StringOf(text, string.Empty) : null;

    /// <summary>
    /// The elements that moved among the siblings both versions share: in each ordered container of the newer version,
    /// those outside the longest run that kept the older order, the fewest moves that explain the new order.
    /// </summary>
    private static HashSet<Element> Moved(List<Element> newer, Dictionary<(DefinitionElementKind, string), Element> olderById)
    {
        var moved = new HashSet<Element>();
        foreach (var siblings in newer.Where(element => element.Ordered).GroupBy(element => element.Container))
        {
            var shared = siblings
                .Select(element => (Element: element, Before: olderById.GetValueOrDefault((element.Kind, element.Id))))
                .Where(pair => pair.Before is not null && pair.Before.Container == pair.Element.Container)
                .ToList();
            var kept = LongestIncreasing(shared.Select(pair => pair.Before!.Index).ToList());
            for (var i = 0; i < shared.Count; i++)
            {
                if (!kept.Contains(i))
                {
                    moved.Add(shared[i].Element);
                }
            }
        }

        return moved;
    }

    /// <summary>The positions in <paramref name="values"/> of one longest strictly increasing subsequence.</summary>
    internal static HashSet<int> LongestIncreasing(IReadOnlyList<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var tails = new List<int>();
        var previous = new int[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            int low = 0, high = tails.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (values[tails[middle]] < values[i])
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            previous[i] = low > 0 ? tails[low - 1] : -1;
            if (low == tails.Count)
            {
                tails.Add(i);
            }
            else
            {
                tails[low] = i;
            }
        }

        var result = new HashSet<int>();
        for (var i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = previous[i])
        {
            result.Add(i);
        }

        return result;
    }

    // Compares two values: objects property by property (a missing object counts as empty), a field's option items and
    // grid rows item by item, and anything else as a whole.
    private static void CompareValues(JsonNode? from, JsonNode? to, List<string> path, List<DefinitionPropertyChange> changes, bool isField)
    {
        if (isField && KeyedBy(path) is { } id && TryKeyed(from, id, out var olderItems) && TryKeyed(to, id, out var newerItems))
        {
            CompareKeyed(olderItems, newerItems, path, changes, isField);
            return;
        }

        if ((from is JsonObject || to is JsonObject) && from is JsonObject or null && to is JsonObject or null)
        {
            var older = from as JsonObject ?? [];
            var newer = to as JsonObject ?? [];
            foreach (var name in newer.Select(property => property.Key).Concat(older.Select(property => property.Key).Where(name => !newer.ContainsKey(name))).ToList())
            {
                CompareValues(older[name], newer[name], [.. path, name], changes, isField);
            }

            return;
        }

        if (!JsonNode.DeepEquals(from, to))
        {
            changes.Add(new DefinitionPropertyChange(path, from?.DeepClone(), to?.DeepClone()));
        }
    }

    // The property that names the items of a field's list at this path: an option item's value, a grid row's key.
    private static string? KeyedBy(List<string> path) => path switch
    {
        ["options", "items"] => "value",
        ["rows"] => "key",
        _ => null,
    };

    // The items of a list by their names, when each has a distinct one; a missing list has none. Otherwise the list is
    // compared as a whole.
    private static bool TryKeyed(JsonNode? node, string id, out List<(string Id, JsonObject Item)> items)
    {
        items = [];
        if (node is null)
        {
            return true;
        }

        if (node is not JsonArray array)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in array)
        {
            if (element is not JsonObject item || item[id] is not JsonValue value || !value.TryGetValue<string>(out var name) || !names.Add(name))
            {
                return false;
            }

            items.Add((name, item));
        }

        return true;
    }

    private static void CompareKeyed(List<(string Id, JsonObject Item)> older, List<(string Id, JsonObject Item)> newer, List<string> path, List<DefinitionPropertyChange> changes, bool isField)
    {
        var olderById = older.ToDictionary(item => item.Id, item => item.Item, StringComparer.Ordinal);
        var newerIds = newer.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        // The order, unless the newer list is the older one's kept items in their order with the new ones after them.
        var plain = older.Where(item => newerIds.Contains(item.Id)).Concat(newer.Where(item => !olderById.ContainsKey(item.Id))).Select(item => item.Id);
        if (!plain.SequenceEqual(newer.Select(item => item.Id), StringComparer.Ordinal))
        {
            changes.Add(new DefinitionPropertyChange(path, Names(older), Names(newer)));
        }

        // An item both lists have is compared property by property; one only either has comes as a whole.
        foreach (var (id, item) in newer)
        {
            if (olderById.TryGetValue(id, out var before))
            {
                CompareValues(Without(before, path), Without(item, path), [.. path, id], changes, isField);
            }
            else
            {
                changes.Add(new DefinitionPropertyChange([.. path, id], null, Without(item, path)));
            }
        }

        foreach (var (id, item) in older.Where(item => !newerIds.Contains(item.Id)))
        {
            changes.Add(new DefinitionPropertyChange([.. path, id], Without(item, path), null));
        }
    }

    private static JsonArray Names(List<(string Id, JsonObject Item)> items) => [.. items.Select(item => (JsonNode)item.Id)];

    // An item without the property that names it, which its path holds already.
    private static JsonObject Without(JsonObject item, List<string> path)
    {
        var copy = (JsonObject)item.DeepClone();
        copy.Remove(KeyedBy(path)!);
        return copy;
    }

    /// <summary>An element of a definition, found in its canonical form.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Id">What matches it across versions: its key, or a settings group's name.</param>
    /// <param name="Key">Its key, or null.</param>
    /// <param name="Parent">The element that holds it, or null.</param>
    /// <param name="Container">Which list holds it, so siblings share one.</param>
    /// <param name="Index">Its place in that list.</param>
    /// <param name="Ordered">Whether the order of that list means something.</param>
    /// <param name="Own">Its own properties.</param>
    /// <param name="Label">Its default-culture title, label or name.</param>
    /// <param name="Alias">A field's alias.</param>
    /// <param name="Type">A field's or an action's type.</param>
    private sealed record Element(
        DefinitionElementKind Kind,
        string Id,
        Guid? Key,
        Guid? Parent,
        string Container,
        int Index,
        bool Ordered,
        JsonObject Own,
        string? Label,
        string? Alias,
        string? Type);
}
