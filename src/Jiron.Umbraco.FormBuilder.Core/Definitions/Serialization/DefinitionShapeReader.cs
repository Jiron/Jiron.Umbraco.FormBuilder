using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// Validates a document's shape against the model's source-generated metadata before System.Text.Json reads it
/// (ADR-0002, ADR-0034), and reports every problem with its code and JSON path: unknown and missing properties,
/// properties named twice, wrong JSON types and values that are not allowed. Because it walks the metadata, a property
/// added to the model is checked the moment it exists.
/// </summary>
/// <remarks>
/// It also returns the document as the model reads it: a property that is <c>null</c>, an empty array or an object
/// with nothing in it is left out, so it takes its default, exactly as the canonical form leaves it out.
/// </remarks>
internal sealed partial class DefinitionShapeReader
{
    /// <summary>A save reports at most this many problems; the rest would only repeat them.</summary>
    public const int MaxProblems = 100;

    // A default instance of each model type, for FillDefaults: immutable records, the same on every server.
    private static readonly ConcurrentDictionary<Type, object> Defaults = new();

    private readonly JsonSerializerOptions _options;
    private readonly List<DefinitionProblem> _problems = [];

    public DefinitionShapeReader(JsonSerializerOptions options) => _options = options;

    public IReadOnlyList<DefinitionProblem> Problems => _problems;

    /// <summary>
    /// Reads the document's root. <paramref name="skipped"/> names root properties another step has checked, such as
    /// <c>schemaVersion</c>. Null when the document has problems.
    /// </summary>
    public JsonObject? ReadRoot(JsonElement root, Type type, IReadOnlySet<string> skipped)
    {
        var ok = TryReadObject(root, _options.GetTypeInfo(type), JsonPaths.Root, skipped, out var node);
        return ok && _problems.Count == 0 ? (JsonObject?)node ?? new JsonObject() : null;
    }

    private bool Fail(string code, string path, string message)
    {
        if (_problems.Count < MaxProblems)
        {
            _problems.Add(new DefinitionProblem(code, path, message));
        }

        return false;
    }

    private bool WrongType(JsonElement value, string path, string expected) =>
        Fail(DefinitionErrors.InvalidType, path, $"Expected {expected}, found {JsonPaths.Describe(value.ValueKind)}.");

    /// <summary>
    /// Reads one value of <paramref name="type"/>. True with a null node when the value is left out (absent, null or
    /// empty); false when it was refused, with the problem recorded.
    /// </summary>
    private bool TryRead(JsonElement value, Type type, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        var target = Nullable.GetUnderlyingType(type) ?? type;
        if (target == typeof(string))
        {
            return TryReadString(value, path, out node);
        }

        if (target == typeof(int))
        {
            return TryReadInteger(value, path, out node);
        }

        if (target == typeof(bool))
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return WrongType(value, path, "true or false");
            }

            node = JsonValue.Create(value.GetBoolean());
            return true;
        }

        if (target == typeof(Guid))
        {
            return TryReadKey(value, path, out node);
        }

        if (target == typeof(DateTime))
        {
            if (!TryReadString(value, path, out node))
            {
                return false;
            }

            return UtcInstantConverter.TryParse(node!.GetValue<string>(), out _)
                || Fail(DefinitionErrors.InvalidValue, path, "An instant is written in UTC as YYYY-MM-DDTHH:mm:ssZ.");
        }

        if (target.IsEnum)
        {
            return TryReadWord(value, target, path, out node);
        }

        if (target == typeof(LocalizedText) || target == typeof(LocalizedHtml))
        {
            return TryReadLocalized(value, path, out node);
        }

        if (target == typeof(FieldDefaultValue))
        {
            return TryReadDefaultValue(value, path, out node);
        }

        if (target == typeof(ConditionOperand))
        {
            return TryReadOperand(value, path, list: true, out node);
        }

        if (target == typeof(SettingsObject))
        {
            return TryReadSettings(value, path, out node);
        }

        if (target == typeof(RuleItem))
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return WrongType(value, path, "a condition or a group (an object)");
            }

            var item = RuleItemConverter.IsGroup(value) ? typeof(RuleGroup) : typeof(RuleCondition);
            return TryReadObject(value, _options.GetTypeInfo(item), path, null, out node);
        }

        var info = _options.GetTypeInfo(target);
        return info.Kind switch
        {
            JsonTypeInfoKind.Object => TryReadObject(value, info, path, null, out node),
            JsonTypeInfoKind.Enumerable => TryReadArray(value, info, path, out node),
            _ => throw new InvalidOperationException($"The definition model has no shape rule for {target}."),
        };
    }

    private bool TryReadString(JsonElement value, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind != JsonValueKind.String)
        {
            return WrongType(value, path, "a string");
        }

        var text = JsonPaths.TryGetString(value);
        if (text is null)
        {
            return Fail(DefinitionErrors.InvalidValue, path, "The string holds a lone surrogate.");
        }

        node = JsonValue.Create(text);
        return true;
    }

    private bool TryReadInteger(JsonElement value, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind != JsonValueKind.Number || !SettingsJson.IsCanonicalInteger(value.GetRawText(), int.MaxValue))
        {
            return WrongType(value, path, "an integer without a fraction, an exponent or leading zeros");
        }

        if (!value.TryGetInt32(out var number))
        {
            return Fail(DefinitionErrors.InvalidValue, path, "The number is out of range.");
        }

        node = JsonValue.Create(number);
        return true;
    }

    private bool TryReadKey(JsonElement value, string path, out JsonNode? node)
    {
        if (!TryReadString(value, path, out node))
        {
            return false;
        }

        return Key().IsMatch(node!.GetValue<string>())
            || Fail(DefinitionErrors.InvalidValue, path, "A key is a GUID in lowercase, such as 3f2c1b9e-0d4a-4c51-9a8e-6b1f0c2d7e45.");
    }

    private bool TryReadWord(JsonElement value, Type type, string path, out JsonNode? node)
    {
        if (!TryReadString(value, path, out node))
        {
            return false;
        }

        var words = Enum.GetNames(type).Select(JsonNamingPolicy.CamelCase.ConvertName).ToList();
        var word = node!.GetValue<string>();
        return words.Contains(word, StringComparer.Ordinal)
            || Fail(DefinitionErrors.InvalidValue, path, $"\"{word}\" is not one of {string.Join(", ", words)}.");
    }

    private bool TryReadArray(JsonElement value, JsonTypeInfo info, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind != JsonValueKind.Array)
        {
            return WrongType(value, path, "an array");
        }

        var items = new JsonArray();
        var ok = true;
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var child = JsonPaths.Index(path, index++);
            if (item.ValueKind == JsonValueKind.Null)
            {
                ok = WrongType(item, child, "a value") && ok;
                continue;
            }

            if (!TryRead(item, info.ElementType!, child, out var read))
            {
                ok = false;
                continue;
            }

            if (read is null)
            {
                ok = Fail(DefinitionErrors.InvalidValue, child, "An item of a list cannot be empty.") && ok;
                continue;
            }

            items.Add(read);
        }

        if (ok && items.Count > 0)
        {
            node = items;
        }

        return ok;
    }

    private bool TryReadObject(JsonElement value, JsonTypeInfo info, string path, IReadOnlySet<string>? skipped, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return WrongType(value, path, "an object");
        }

        var result = new JsonObject();
        string? discriminatorName = null;
        if (info.PolymorphismOptions is { } polymorphism)
        {
            discriminatorName = polymorphism.TypeDiscriminatorPropertyName;
            var derived = ChooseDerivedType(value, polymorphism, path);
            if (derived is null)
            {
                return false;
            }

            info = _options.GetTypeInfo(derived.Value.DerivedType);
            result[discriminatorName] = (string)derived.Value.TypeDiscriminator!;
        }

        var properties = info.Properties.ToDictionary(property => property.Name, StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var refused = new HashSet<string>(StringComparer.Ordinal);
        var ok = true;
        foreach (var property in value.EnumerateObject())
        {
            var child = JsonPaths.Property(path, property.Name);
            if (!names.Add(property.Name))
            {
                ok = Fail(DefinitionErrors.DuplicateProperty, child, $"The property \"{property.Name}\" appears twice.");
                continue;
            }

            if (property.Name == discriminatorName || (skipped?.Contains(property.Name) ?? false))
            {
                continue;
            }

            if (!properties.TryGetValue(property.Name, out var metadata) || metadata.Set is null)
            {
                ok = Fail(DefinitionErrors.UnknownProperty, child, $"\"{property.Name}\" is not a property here.");
                continue;
            }

            if (!TryRead(property.Value, metadata.PropertyType, child, out var read))
            {
                refused.Add(property.Name);
                ok = false;
                continue;
            }

            if (read is not null)
            {
                result[property.Name] = read;
            }
        }

        foreach (var metadata in info.Properties)
        {
            if (metadata.IsRequired && !result.ContainsKey(metadata.Name) && !refused.Contains(metadata.Name))
            {
                ok = Fail(DefinitionErrors.MissingProperty, JsonPaths.Property(path, metadata.Name), $"\"{metadata.Name}\" is required.");
            }
        }

        // An object with nothing in it is left out, as the canonical form leaves it out; its kind alone is something.
        if (!ok || result.Count == 0)
        {
            return ok;
        }

        FillDefaults(result, info);
        node = result;
        return true;
    }

    /// <summary>
    /// Writes each missing property's default into the object. The source generator builds a record with init-only
    /// properties through an object initializer that sets every property, so a property missing from the JSON would
    /// become null or zero instead of keeping its initializer (an empty list, <c>enabled: true</c>, one column).
    /// </summary>
    private void FillDefaults(JsonObject result, JsonTypeInfo info)
    {
        var defaults = Defaults.GetOrAdd(info.Type, type => Activator.CreateInstance(type)!);
        foreach (var metadata in info.Properties)
        {
            if (metadata.Set is null || metadata.IsRequired || result.ContainsKey(metadata.Name) || metadata.Get?.Invoke(defaults) is not { } value)
            {
                continue;
            }

            result[metadata.Name] = JsonSerializer.SerializeToNode(value, _options.GetTypeInfo(metadata.PropertyType));
        }
    }

    private JsonDerivedType? ChooseDerivedType(JsonElement value, JsonPolymorphismOptions polymorphism, string path)
    {
        var name = polymorphism.TypeDiscriminatorPropertyName;
        var child = JsonPaths.Property(path, name);
        if (!value.TryGetProperty(name, out var discriminator))
        {
            Fail(DefinitionErrors.MissingProperty, child, $"\"{name}\" is required.");
            return null;
        }

        if (discriminator.ValueKind != JsonValueKind.String)
        {
            WrongType(discriminator, child, "a string");
            return null;
        }

        var word = JsonPaths.TryGetString(discriminator);
        foreach (var derived in polymorphism.DerivedTypes)
        {
            if (derived.TypeDiscriminator is string known && string.Equals(known, word, StringComparison.Ordinal))
            {
                return derived;
            }
        }

        var words = polymorphism.DerivedTypes.Select(derived => derived.TypeDiscriminator);
        Fail(DefinitionErrors.InvalidValue, child, $"\"{word}\" is not one of {string.Join(", ", words)}.");
        return null;
    }

    private bool TryReadLocalized(JsonElement value, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return WrongType(value, path, "a localised text (an object from culture to text)");
        }

        var result = new JsonObject();
        var exact = new HashSet<string>(StringComparer.Ordinal);
        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ok = true;
        foreach (var property in value.EnumerateObject())
        {
            var child = JsonPaths.Property(path, property.Name);
            if (!exact.Add(property.Name))
            {
                ok = Fail(DefinitionErrors.DuplicateProperty, child, $"The culture \"{property.Name}\" appears twice.");
                continue;
            }

            if (!cultures.Add(property.Name))
            {
                ok = Fail(DefinitionErrors.InvalidValue, child, $"The culture \"{property.Name}\" appears twice in another letter case.");
                continue;
            }

            if (property.Name.Length > 0 && !LocalizedString.IsCultureName(property.Name))
            {
                ok = Fail(DefinitionErrors.InvalidValue, child, $"\"{property.Name}\" is not a culture name; the default text is \"\".");
                continue;
            }

            if (!TryRead(property.Value, typeof(string), child, out var text))
            {
                ok = false;
                continue;
            }

            if (text is not null)
            {
                result[property.Name] = text;
            }
        }

        if (!ok || result.Count == 0)
        {
            return ok;
        }

        if (!result.ContainsKey(string.Empty))
        {
            return Fail(DefinitionErrors.InvalidValue, path, "A localised text needs its default text, the culture \"\".");
        }

        node = result;
        return true;
    }

    private bool TryReadDefaultValue(JsonElement value, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind == JsonValueKind.String)
        {
            return TryReadString(value, path, out node);
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return WrongType(value, path, "a string or an array of strings");
        }

        var items = new JsonArray();
        var ok = true;
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (!TryReadString(item, JsonPaths.Index(path, index++), out var text))
            {
                ok = false;
                continue;
            }

            items.Add(text);
        }

        if (ok && items.Count > 0)
        {
            node = items;
        }

        return ok;
    }

    private bool TryReadOperand(JsonElement value, string path, bool list, out JsonNode? node)
    {
        node = null;
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return TryReadString(value, path, out node);
            case JsonValueKind.Array when list:
                var items = new JsonArray();
                var ok = true;
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (!TryReadOperand(item, JsonPaths.Index(path, index++), list: false, out var read))
                    {
                        ok = false;
                        continue;
                    }

                    items.Add(read);
                }

                if (ok && items.Count == 0)
                {
                    return Fail(DefinitionErrors.InvalidValue, path, "A list operand has at least one item.");
                }

                node = ok ? items : null;
                return ok;
            case JsonValueKind.Object:
                return TryReadOperandObject(value, path, out node);
            default:
                return WrongType(value, path, list ? "a string, an answer, a relative date or a list of those" : "a string, an answer or a relative date");
        }
    }

    private bool TryReadOperandObject(JsonElement value, string path, out JsonNode? node)
    {
        node = null;
        var result = new JsonObject();
        var ok = true;
        foreach (var property in value.EnumerateObject())
        {
            var child = JsonPaths.Property(path, property.Name);
            if (result.ContainsKey(property.Name))
            {
                ok = Fail(DefinitionErrors.DuplicateProperty, child, $"The property \"{property.Name}\" appears twice.");
                continue;
            }

            JsonNode? read;
            switch (property.Name)
            {
                case "field":
                    ok = TryReadKey(property.Value, child, out read) && ok;
                    break;
                case "today":
                    ok = TryReadString(property.Value, child, out read) && ok;
                    break;
                default:
                    ok = Fail(DefinitionErrors.UnknownProperty, child, $"\"{property.Name}\" is not a property of an operand; it names \"field\" or \"today\".");
                    continue;
            }

            if (read is not null)
            {
                result[property.Name] = read;
            }
        }

        if (!ok)
        {
            return false;
        }

        if (result.Count != 1)
        {
            return Fail(DefinitionErrors.InvalidValue, path, "An operand object names either \"field\" or \"today\".");
        }

        node = result;
        return true;
    }

    private bool TryReadSettings(JsonElement value, string path, out JsonNode? node)
    {
        node = null;
        if (value.ValueKind == JsonValueKind.Object && CanonicalJson.IsLeftOut(value) && !HasDuplicate(value))
        {
            return true;
        }

        if (!SettingsJson.TryCanonicalize(value, path, out var canonical, out var problem))
        {
            return Fail(problem.Code, problem.Path, problem.Message);
        }

        node = JsonNode.Parse(canonical);
        return true;
    }

    private static bool HasDuplicate(JsonElement value)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        return value.EnumerateObject().Any(property => !names.Add(property.Name) || (property.Value.ValueKind == JsonValueKind.Object && HasDuplicate(property.Value)));
    }

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex Key();
}
