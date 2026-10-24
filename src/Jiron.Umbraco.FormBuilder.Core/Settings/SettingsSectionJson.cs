using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// Moves a section's stored JSON from one schema version to the next (ADR-0008). Registered in DI; the reader applies
/// every step from the stored version up to the section's current one, in order.
/// </summary>
internal interface ISettingsSectionMigration
{
    /// <summary>The section's alias.</summary>
    string Section { get; }

    /// <summary>The version this step reads; it writes <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    /// <summary>Changes <paramref name="json"/> in place.</summary>
    void Migrate(JsonObject json);
}

/// <summary>
/// How sections are stored and combined (ADR-0008, ADR-0043): camelCase JSON, enums as camelCase text, nulls left out,
/// unknown properties dropped. A global row holds every field; a site row the top-level fields it overrides, where
/// <c>null</c> means the default.
/// </summary>
internal static class SettingsSectionJson
{
    /// <summary>The serializer options of stored sections.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>
    /// A row's JSON, migrated from <paramref name="storedVersion"/> to the section's schema version; a row from a newer
    /// version is taken as it is.
    /// </summary>
    /// <exception cref="JsonException">The JSON is not an object.</exception>
    /// <exception cref="InvalidOperationException">A migration step is missing or there twice.</exception>
    public static JsonObject Migrate<T>(string json, int storedVersion, IEnumerable<ISettingsSectionMigration> migrations)
        where T : ISettingsSection
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(migrations);
        var node = JsonNode.Parse(json) as JsonObject ?? throw new JsonException($"The settings section '{T.Alias}' is not a JSON object.");
        var steps = migrations.Where(m => m.Section == T.Alias).ToList();
        for (var version = storedVersion; version < T.SchemaVersion; version++)
        {
            var step = steps.Where(m => m.FromVersion == version).ToList() switch
            {
                [var only] => only,
                [] => throw new InvalidOperationException($"The settings section '{T.Alias}' has no migration from schema version {version} to {version + 1}."),
                _ => throw new InvalidOperationException($"The settings section '{T.Alias}' has more than one migration from schema version {version}."),
            };
            step.Migrate(node);
        }

        return node;
    }

    /// <summary>The section a row's JSON holds, on the defaults: missing and null properties keep them.</summary>
    /// <exception cref="JsonException">A value does not fit its field, such as a number for an enum.</exception>
    public static T Read<T>(JsonObject row)
        where T : ISettingsSection, new()
    {
        ArgumentNullException.ThrowIfNull(row);
        var node = row.DeepClone().AsObject();
        RemoveNulls(node);
        return node.Deserialize<T>(Options) ?? new T();
    }

    /// <summary>
    /// The section's values for a scope: the code defaults, then the global row's values, then the fields a site row
    /// overrides, where a null takes the default back. Both rows must read on their own (<see cref="Read{T}"/>).
    /// </summary>
    public static T Combine<T>(JsonObject? global, JsonObject? site)
        where T : ISettingsSection, new()
    {
        var node = global?.DeepClone().AsObject() ?? [];
        foreach (var (name, value) in SiteFields<T>(site))
        {
            Remove(node, name);
            node[name] = value?.DeepClone();
        }

        return Read<T>(node);
    }

    /// <summary>The fields a site row overrides, by their stored names; empty for a section every site shares.</summary>
    public static IReadOnlySet<string> Overrides<T>(JsonObject? site)
        where T : ISettingsSection =>
        SiteFields<T>(site).Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>The top-level fields of a section, by their stored names.</summary>
    public static IReadOnlySet<string> FieldNames<T>()
        where T : ISettingsSection => Fields<T>.All;

    /// <summary>The top-level fields a site can override, by their stored names; none for a section every site shares.</summary>
    public static IReadOnlySet<string> SiteFieldNames<T>()
        where T : ISettingsSection => Fields<T>.Overridable;

    /// <summary>The JSON of a global row: every field, nulls left out.</summary>
    public static string GlobalJson<T>(T section)
        where T : ISettingsSection => JsonSerializer.Serialize(section, Options);

    /// <summary>The JSON of a site row: the <paramref name="fields"/> it overrides, with a null where the value is the default.</summary>
    public static JsonObject SiteRow<T>(T section, IEnumerable<string> fields)
        where T : ISettingsSection
    {
        ArgumentNullException.ThrowIfNull(fields);
        var values = ToNode(section);
        var row = new JsonObject();
        foreach (var field in fields.Order(StringComparer.Ordinal))
        {
            row[field] = values[field]?.DeepClone();
        }

        return row;
    }

    /// <summary>The section as a JSON tree, nulls left out.</summary>
    public static JsonObject ToNode<T>(T section)
        where T : ISettingsSection => JsonSerializer.SerializeToNode(section, Options)!.AsObject();

    /// <summary>The value at a dotted path such as <c>rules.maxLinks</c>, or null.</summary>
    public static JsonNode? At(JsonObject root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(path);
        JsonNode? node = root;
        foreach (var part in path.Split('.'))
        {
            node = node is JsonObject obj && obj.TryGetPropertyValue(part, out var child) ? child : null;
            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>The top-level field of a dotted path: <c>rules</c> for <c>rules.maxLinks</c>.</summary>
    public static string TopField(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? path : path[..dot];
    }

    /// <summary>
    /// The dotted paths that differ between two trees: a leaf whose value changed, and a property that one tree has and
    /// the other has not, even when its value is null. Lists compare whole.
    /// </summary>
    public static IReadOnlyList<string> ChangedFields(JsonObject before, JsonObject after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        var changed = new List<string>();
        Compare(before, after, string.Empty, changed);
        return changed;
    }

    private static void Compare(JsonObject before, JsonObject after, string prefix, List<string> changed)
    {
        foreach (var name in before.Select(p => p.Key).Union(after.Select(p => p.Key), StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var path = prefix + name;
            var inBefore = before.TryGetPropertyValue(name, out var a);
            var inAfter = after.TryGetPropertyValue(name, out var b);
            if (inBefore && inAfter && a is JsonObject objectBefore && b is JsonObject objectAfter)
            {
                Compare(objectBefore, objectAfter, path + ".", changed);
            }
            else if (inBefore != inAfter || !JsonNode.DeepEquals(a, b))
            {
                changed.Add(path);
            }
        }
    }

    // The site row's fields a site can override, each by its stored name; any other is ignored (ADR-0043).
    private static IEnumerable<(string Name, JsonNode? Value)> SiteFields<T>(JsonObject? site)
        where T : ISettingsSection
    {
        if (site is null)
        {
            yield break;
        }

        foreach (var (name, value) in site)
        {
            if (Fields<T>.ByName.TryGetValue(name, out var canonical) && Fields<T>.Overridable.Contains(canonical))
            {
                yield return (canonical, value);
            }
        }
    }

    // Removes a property in any letter case, as the reader matches names ignoring case.
    private static void Remove(JsonObject node, string name)
    {
        foreach (var key in node.Select(p => p.Key).Where(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            node.Remove(key);
        }
    }

    private static void RemoveNulls(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var name in obj.Where(p => p.Value is null).Select(p => p.Key).ToList())
                {
                    obj.Remove(name);
                }

                foreach (var (_, child) in obj)
                {
                    RemoveNulls(child);
                }

                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    RemoveNulls(child);
                }

                break;
        }
    }

    // A section's top-level fields by stored name, found once per section type; it never changes.
    private static class Fields<T>
        where T : ISettingsSection
    {
        public static readonly IReadOnlyDictionary<string, string> ByName = Names().ToDictionary(n => n.Name, n => n.Name, StringComparer.OrdinalIgnoreCase);

        public static readonly IReadOnlySet<string> All = Names().Select(n => n.Name).ToHashSet(StringComparer.Ordinal);

        public static readonly IReadOnlySet<string> Overridable = T.PerSite
            ? Names().Where(n => !n.GlobalOnly).Select(n => n.Name).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        private static IEnumerable<(string Name, bool GlobalOnly)> Names() =>
            typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && p.GetCustomAttribute<JsonIgnoreAttribute>() is null)
                .Select(p => (
                    p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? Options.PropertyNamingPolicy!.ConvertName(p.Name),
                    p.GetCustomAttribute<GlobalOnlyAttribute>() is not null));
    }
}
