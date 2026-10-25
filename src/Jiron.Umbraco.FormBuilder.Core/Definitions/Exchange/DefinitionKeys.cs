using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;

/// <summary>
/// The element keys of a definition and the references to them (ADR-0046). A reference is a string value or a property
/// name anywhere in the canonical JSON that is exactly a key in the <c>D</c> format, in any letter case, so that the
/// settings of field and action types are covered without knowing them; a key inside a longer string is no reference.
/// </summary>
internal static class DefinitionKeys
{
    private const int KeyLength = 36;

    private static readonly JsonSerializerOptions DeepText = FrozenOptions.Of(new() { MaxDepth = 4 * FormDefinitionSerializer.MaxDepth });

    /// <summary>
    /// Every element key of a definition in document order: the definition's own, its pages, groups and fields (the
    /// children of a repeatable group included) with their grid rows, the rules of each, then the form-level validation
    /// rules, the actions and the outcomes with their rules.
    /// </summary>
    public static IReadOnlyList<Guid> Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var keys = new List<Guid> { definition.Key };
        foreach (var page in definition.Pages)
        {
            keys.Add(page.Key);
            AddRules(keys, page.Rules);
            foreach (var group in page.Groups)
            {
                keys.Add(group.Key);
                AddRules(keys, group.Rules);
                AddFields(keys, group.Fields);
            }
        }

        keys.AddRange(definition.ValidationRules.Select(rule => rule.Key));
        foreach (var action in definition.Actions)
        {
            keys.Add(action.Key);
            AddRules(keys, action.Rules);
        }

        foreach (var outcome in definition.Outcomes)
        {
            keys.Add(outcome.Key);
            AddRules(keys, outcome.Rules);
        }

        return keys;
    }

    /// <summary>Every field of a definition in document order, the children of a repeatable group after it.</summary>
    public static IEnumerable<FormField> Fields(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        static IEnumerable<FormField> All(IReadOnlyList<FormField> fields) => fields.SelectMany(field => All(field.Fields).Prepend(field));
        return definition.Pages.SelectMany(page => page.Groups).SelectMany(group => All(group.Fields));
    }

    /// <summary>A new key for each of these keys.</summary>
    public static Dictionary<Guid, Guid> NewKeys(IEnumerable<Guid> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var map = new Dictionary<Guid, Guid>();
        foreach (var key in keys)
        {
            map.TryAdd(key, Guid.NewGuid());
        }

        return map;
    }

    /// <summary>
    /// The definition with a new key for every element, and every reference to an element naming its new key: rules,
    /// checks, cascading options, the verification field and the settings of types alike.
    /// </summary>
    /// <param name="definition">A definition a save takes.</param>
    /// <param name="serializer">The reader that reads the result back through the model.</param>
    public static FormDefinition Renew(FormDefinition definition, FormDefinitionSerializer serializer) =>
        Remap(definition, NewKeys(Of(definition)), serializer);

    /// <summary>
    /// The definition with every reference to a key of <paramref name="map"/>, element keys included, replaced by the key
    /// it maps to.
    /// </summary>
    /// <param name="definition">A definition a save takes.</param>
    /// <param name="map">The keys to replace and their replacements; no two map to one key.</param>
    /// <param name="serializer">The reader that reads the result back through the model.</param>
    /// <exception cref="InvalidOperationException">The result does not read, which a map that keeps keys apart never causes.</exception>
    public static FormDefinition Remap(FormDefinition definition, IReadOnlyDictionary<Guid, Guid> map, FormDefinitionSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(serializer);
        var document = Document(definition);
        Replace(document, JsonPaths.Root, (key, _) => map.TryGetValue(key, out var replacement) ? replacement : null);
        var read = serializer.ReadStored(Text(document));
        return read.Definition
            ?? throw new InvalidOperationException($"A definition with its keys replaced no longer reads: {read.Problems[0].Code} at {read.Problems[0].Path}.");
    }

    /// <summary>The canonical JSON of a definition as a node to change.</summary>
    public static JsonObject Document(FormDefinition definition) =>
        JsonNode.Parse(FormDefinitionSerializer.Write(definition), documentOptions: new JsonDocumentOptions { MaxDepth = FormDefinitionSerializer.MaxDepth })!.AsObject();

    /// <summary>The JSON text of a node.</summary>
    public static string Text(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.ToJsonString(DeepText);
    }

    /// <summary>
    /// Offers every reference below <paramref name="node"/> to <paramref name="replace"/>, with a function that gives its
    /// JSON path; a key it returns takes the reference's place, and null keeps it.
    /// </summary>
    /// <param name="node">The node to walk; it changes in place.</param>
    /// <param name="path">The node's own path.</param>
    /// <param name="replace">What a reference becomes.</param>
    public static void Replace(JsonNode node, string path, Func<Guid, Func<string>, Guid?> replace)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(replace);
        new Walker(path, replace).Walk(node);
    }

    // The key a text is, when it is one in the D format.
    private static bool IsKey(string text, out Guid key)
    {
        key = default;
        return text.Length == KeyLength && Guid.TryParseExact(text, "D", out key);
    }

    private static void AddRules(List<Guid> keys, IReadOnlyList<FormRule> rules) => keys.AddRange(rules.Select(rule => rule.Key));

    private static void AddFields(List<Guid> keys, IReadOnlyList<FormField> fields)
    {
        foreach (var field in fields)
        {
            keys.Add(field.Key);
            keys.AddRange(field.Rows.Select(row => row.Key));
            AddRules(keys, field.Rules);
            AddFields(keys, field.Fields);
        }
    }

    // A depth-first walk that knows the path of where it is, and makes it text only when asked.
    private sealed class Walker(string root, Func<Guid, Func<string>, Guid?> replace)
    {
        private readonly List<object> _steps = [];

        public void Walk(JsonNode node)
        {
            switch (node)
            {
                case JsonObject value:
                    WalkObject(value);
                    break;
                case JsonArray array:
                    for (var i = 0; i < array.Count; i++)
                    {
                        _steps.Add(i);
                        if (array[i] is { } item && Replaced(item) is { } replacement)
                        {
                            array[i] = replacement;
                        }

                        _steps.RemoveAt(_steps.Count - 1);
                    }

                    break;
            }
        }

        private void WalkObject(JsonObject value)
        {
            // Property names first: a renamed property keeps its place among the others.
            if (value.Any(property => IsKey(property.Key, out _)))
            {
                var properties = value.ToList();
                value.Clear();
                foreach (var (name, item) in properties)
                {
                    var newName = name;
                    if (IsKey(name, out var key))
                    {
                        _steps.Add(name);
                        newName = replace(key, Path) is { } replacement ? KeyText(replacement) : name;
                        _steps.RemoveAt(_steps.Count - 1);
                    }

                    value[newName] = item;
                }
            }

            foreach (var name in value.Select(property => property.Key).ToList())
            {
                _steps.Add(name);
                if (value[name] is { } item && Replaced(item) is { } replacement)
                {
                    value[name] = replacement;
                }

                _steps.RemoveAt(_steps.Count - 1);
            }
        }

        // The node that takes this one's place, or null when it stays (having changed in place, perhaps).
        private JsonValue? Replaced(JsonNode node)
        {
            if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
            {
                return IsKey(value.GetValue<string>(), out var key) && replace(key, Path) is { } replacement ? JsonValue.Create(KeyText(replacement)) : null;
            }

            Walk(node);
            return null;
        }

        private string Path()
        {
            var path = root;
            foreach (var step in _steps)
            {
                path = step is int index ? JsonPaths.Index(path, index) : JsonPaths.Property(path, (string)step);
            }

            return path;
        }

        private static string KeyText(Guid key) => key.ToString("D");
    }
}
