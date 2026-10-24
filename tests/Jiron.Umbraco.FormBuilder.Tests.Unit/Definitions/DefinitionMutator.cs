using System.Globalization;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Changes a definition's canonical JSON the way editors do, keeping its keys, for the property-style tests of the
/// comparison (ADR-0045): texts and flags, a field removed, moved or copied, options and actions reordered, a rule removed,
/// a settings flag, pages swapped. It also finds elements by key and rebuilds an element from its property changes.
/// </summary>
internal static class DefinitionMutator
{
    /// <summary>Changes <paramref name="root"/> in place and returns it; every key stays unique.</summary>
    public static JsonObject Mutate(JsonObject root, Random random)
    {
        var groups = Groups(root);
        foreach (var (_, field) in AllFields(root))
        {
            if (random.Next(5) == 0)
            {
                field["label"] = new JsonObject { [string.Empty] = "Changed " + random.Next(1000).ToString(CultureInfo.InvariantCulture) };
            }

            if (random.Next(8) == 0 && field["required"] is JsonValue required)
            {
                field["required"] = !required.GetValue<bool>();
            }
        }

        var fields = AllFields(root);
        if (fields.Count > 1 && random.Next(3) == 0)
        {
            var (container, field) = fields[random.Next(fields.Count)];
            container.Remove(field);
        }

        var top = groups.SelectMany(group => Array(group, "fields").OfType<JsonObject>().Select(field => (Group: group, Field: field))).ToList();
        if (top.Count > 0 && random.Next(3) > 0)
        {
            var (from, field) = top[random.Next(top.Count)];
            Array(from, "fields").Remove(field);
            var to = groups[random.Next(groups.Count)];
            var list = Attached(to, "fields");
            list.Insert(random.Next(list.Count + 1), Fit(field, to));
        }

        if (top.Count > 0 && random.Next(2) == 0)
        {
            var copy = (JsonObject)top[random.Next(top.Count)].Field.DeepClone();
            ReKey(copy);
            if (copy.ContainsKey("alias"))
            {
                copy["alias"] = "added" + random.Next(100000).ToString(CultureInfo.InvariantCulture);
            }

            var to = groups[random.Next(groups.Count)];
            var list = Attached(to, "fields");
            list.Insert(random.Next(list.Count + 1), Fit(copy, to));
        }

        foreach (var (_, field) in AllFields(root))
        {
            if (field["options"]?["items"] is JsonArray items && items.Count > 1 && random.Next(3) == 0)
            {
                Reverse(items);
            }
        }

        if (random.Next(2) == 0 && root["settings"]?["appearance"] is JsonObject appearance && appearance["validationSummary"] is JsonValue summary)
        {
            appearance["validationSummary"] = !summary.GetValue<bool>();
        }

        if (root["actions"] is JsonArray actions && actions.Count > 1 && random.Next(2) == 0)
        {
            Reverse(actions);
        }

        var withRules = Elements(root).Where(element => element["rules"] is JsonArray { Count: > 0 }).ToList();
        if (withRules.Count > 0 && random.Next(3) == 0)
        {
            var rules = withRules[random.Next(withRules.Count)]["rules"]!.AsArray();
            rules.RemoveAt(random.Next(rules.Count));
        }

        if (root["pages"] is JsonArray { Count: > 1 } pages && random.Next(3) == 0)
        {
            Reverse(pages);
        }

        return root;
    }

    /// <summary>The keys of every element: pages, groups, fields with a repeater's children, rules, validation rules, actions, outcomes.</summary>
    public static HashSet<Guid> Keys(JsonObject root) =>
        Elements(root).Concat(Elements(root).SelectMany(element => Array(element, "rules").OfType<JsonObject>()))
            .Select(element => Guid.Parse(element["key"]!.GetValue<string>()))
            .ToHashSet();

    /// <summary>The element with this key, or null.</summary>
    public static JsonObject? FindByKey(JsonObject root, Guid key)
    {
        var text = key.ToString();
        return Elements(root).Concat(Elements(root).SelectMany(element => Array(element, "rules").OfType<JsonObject>()))
            .FirstOrDefault(element => element["key"]?.GetValue<string>() == text);
    }

    /// <summary>
    /// <paramref name="before"/> with every property change applied: what the newer version's element must be. A field's
    /// option items and grid rows are found by value and key, added at the end and then put in order by an order change.
    /// </summary>
    public static JsonObject Apply(JsonObject before, IReadOnlyList<DefinitionPropertyChange> changes, bool isField)
    {
        var result = (JsonObject)before.DeepClone();
        var orders = new List<DefinitionPropertyChange>();
        foreach (var change in changes)
        {
            if (isField && KeyedBy(change.Path) is not null && change.To is JsonArray to && to.All(name => name is JsonValue value && value.TryGetValue<string>(out _)) && Lookup(result, change.Path) is JsonArray)
            {
                orders.Add(change);
                continue;
            }

            Set(result, change.Path, change.To, isField);
        }

        foreach (var order in orders)
        {
            if (Lookup(result, order.Path) is JsonArray list)
            {
                var id = KeyedBy(order.Path)!;
                var names = order.To!.AsArray().Select(name => name!.GetValue<string>()).ToList();
                var items = list.OfType<JsonObject>().ToList();
                list.Clear();
                foreach (var item in items.OrderBy(item => names.IndexOf(item[id]!.GetValue<string>())))
                {
                    list.Add(item);
                }
            }
        }

        Prune(result);
        return result;
    }

    // Sets the value at a path, or removes it for null, creating objects on the way; inside a field's keyed list a segment
    // names an item.
    private static void Set(JsonObject root, IReadOnlyList<string> path, JsonNode? value, bool isField)
    {
        JsonNode current = root;
        for (var i = 0; i < path.Count; i++)
        {
            var segment = path[i];
            var last = i == path.Count - 1;
            var keyed = isField ? KeyedBy(path.Take(i).ToList()) : null;
            if (keyed is not null)
            {
                var list = (JsonArray)current;
                var item = list.OfType<JsonObject>().FirstOrDefault(entry => entry[keyed]?.GetValue<string>() == segment);
                if (last)
                {
                    if (item is not null)
                    {
                        list.Remove(item);
                    }

                    if (value is JsonObject whole)
                    {
                        var added = new JsonObject { [keyed] = segment };
                        foreach (var (name, node) in whole)
                        {
                            added[name] = node?.DeepClone();
                        }

                        list.Add(added);
                    }

                    return;
                }

                current = item ?? throw new InvalidOperationException($"No item {segment} at {string.Join("/", path)}");
                continue;
            }

            var target = (JsonObject)current;
            if (last)
            {
                if (value is null)
                {
                    target.Remove(segment);
                }
                else
                {
                    target[segment] = value.DeepClone();
                }

                return;
            }

            if (target[segment] is not JsonNode next)
            {
                next = isField && KeyedBy([.. path.Take(i + 1)]) is not null ? new JsonArray() : new JsonObject();
                target[segment] = next;
            }

            current = next;
        }
    }

    private static JsonNode? Lookup(JsonObject root, IReadOnlyList<string> path)
    {
        JsonNode? current = root;
        foreach (var segment in path)
        {
            current = (current as JsonObject)?[segment];
        }

        return current;
    }

    // Leaves out empty objects and lists, as the canonical form does.
    private static void Prune(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in obj.Select(property => property.Key).ToList())
            {
                if (obj[name] is { } child)
                {
                    Prune(child);
                    if (child is JsonObject { Count: 0 } or JsonArray { Count: 0 })
                    {
                        obj.Remove(name);
                    }
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array.OfType<JsonNode>())
            {
                Prune(child);
            }
        }
    }

    private static string? KeyedBy(IReadOnlyList<string> path) => path switch
    {
        ["options", "items"] => "value",
        ["rows"] => "key",
        _ => null,
    };

    private static void Reverse(JsonArray array)
    {
        var items = array.Select(item => item?.DeepClone()).Reverse().ToList();
        array.Clear();
        foreach (var item in items)
        {
            array.Add(item);
        }
    }

    private static void ReKey(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj.ContainsKey("key"))
            {
                obj["key"] = Guid.NewGuid().ToString();
            }

            foreach (var (_, child) in obj)
            {
                ReKey(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                ReKey(child);
            }
        }
    }

    // A field narrowed to the columns of the group it goes to, as an editor's designer does.
    private static JsonObject Fit(JsonObject field, JsonObject group)
    {
        var columns = group["columns"]?.GetValue<int>() ?? 1;
        if (field["span"]?.GetValue<int>() > columns)
        {
            field["span"] = columns;
        }

        foreach (var child in Array(field, "fields").OfType<JsonObject>())
        {
            Fit(child, group);
        }

        return field;
    }

    // A list of the node, or an empty one that is not part of it.
    private static JsonArray Array(JsonObject node, string property) => node[property] as JsonArray ?? [];

    // A list of the node, added to it when it has none.
    private static JsonArray Attached(JsonObject node, string property)
    {
        if (node[property] is not JsonArray array)
        {
            array = [];
            node[property] = array;
        }

        return array;
    }

    private static List<JsonObject> Groups(JsonObject root) =>
        Array(root, "pages").OfType<JsonObject>().SelectMany(page => Array(page, "groups").OfType<JsonObject>()).ToList();

    // Every field with the list that holds it, a repeater's children included.
    private static List<(JsonArray Container, JsonObject Field)> AllFields(JsonObject root)
    {
        var fields = new List<(JsonArray, JsonObject)>();
        void Walk(JsonArray list)
        {
            foreach (var field in list.OfType<JsonObject>().ToList())
            {
                fields.Add((list, field));
                if (field["fields"] is JsonArray children)
                {
                    Walk(children);
                }
            }
        }

        foreach (var group in Groups(root))
        {
            Walk(Array(group, "fields"));
        }

        return fields;
    }

    // Every element with a key except rules.
    private static IEnumerable<JsonObject> Elements(JsonObject root) =>
        Array(root, "pages").OfType<JsonObject>()
            .Concat(Groups(root))
            .Concat(AllFields(root).Select(entry => entry.Field))
            .Concat(Array(root, "validationRules").OfType<JsonObject>())
            .Concat(Array(root, "actions").OfType<JsonObject>())
            .Concat(Array(root, "outcomes").OfType<JsonObject>());
}
