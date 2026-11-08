using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// A field of a settings section as its screen draws it (ADR-0218), in the shape the inspector's editors read
/// (ADR-0055, ADR-0170).
/// </summary>
/// <param name="Name">Its name in the section's JSON.</param>
/// <param name="Kind">The kind of value it holds: <c>boolean</c>, <c>wholeNumber</c>, <c>text</c>, <c>choice</c>,
/// <c>key</c>, <c>list</c>, <c>localizedText</c>, <c>localizedHtml</c>, <c>nested</c> or <c>json</c>.</param>
/// <param name="ItemKind">The kind of a list's items; null for any other kind.</param>
/// <param name="Choices">The values a choice, or a list of choices, may take, as the JSON writes them.</param>
/// <param name="Fields">The fields of a nested object, in their order.</param>
/// <param name="Default">The value without one stored, as the JSON writes it; null when there is none.</param>
/// <param name="Label">The label's key, <c>#jfbGlobalSettings_&lt;section&gt;_&lt;field&gt;</c>.</param>
/// <param name="Description">The help's key, the label's with <c>Help</c>.</param>
/// <param name="Editor">The editor that draws it.</param>
/// <param name="Box">The box on the screen; a nested field's is its object's.</param>
/// <param name="Unit">The key of the word after a number; null for none.</param>
/// <param name="Rules">The limits its value keeps.</param>
internal sealed record SettingsFieldDescriptor(
    string Name,
    string Kind,
    string? ItemKind,
    IReadOnlyList<string> Choices,
    IReadOnlyList<SettingsFieldDescriptor> Fields,
    JsonElement? Default,
    string Label,
    string Description,
    string Editor,
    string Box,
    string? Unit,
    SettingsFieldRules Rules);

/// <summary>The limits a field's value keeps, from its <see cref="RangeAttribute"/> and <see cref="MaxLengthAttribute"/>.</summary>
/// <param name="Minimum">The lowest number, in the invariant culture; null for none.</param>
/// <param name="Maximum">The highest number; null for none.</param>
/// <param name="MaximumLength">The most characters of a text; null for no limit here.</param>
/// <param name="MaximumCount">The most items of a list; null for no limit here.</param>
internal sealed record SettingsFieldRules(string? Minimum, string? Maximum, int? MaximumLength, int? MaximumCount);

/// <summary>
/// Reads a settings section's record into the fields its screen draws (ADR-0218). Every public property is a field with
/// <see cref="SettingsFieldAttribute"/> or is <see cref="HostOnlyAttribute"/>; its kind follows from its type, its default
/// from the value a new section holds, as the section's JSON writes it. Objects nest one level deep; a deeper one, or a key,
/// names the editor that draws it.
/// </summary>
internal static class SettingsSchema
{
    /// <summary>
    /// The fields of a section, in the order its screen shows them: by box, as the record first names each, then by order.
    /// Each call reads the record again; a section's screen keeps what it read.
    /// </summary>
    /// <exception cref="InvalidOperationException">A property is no field of a screen, or its type is none a field holds.</exception>
    public static IReadOnlyList<SettingsFieldDescriptor> Of<T>()
        where T : class, ISettingsSection, new() => Read(typeof(T), T.Alias);

    /// <summary>The fields of a section's record.</summary>
    /// <exception cref="InvalidOperationException">A property is no field of a screen, or its type is none a field holds; the message gives every reason.</exception>
    public static IReadOnlyList<SettingsFieldDescriptor> Read(Type type, string section)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        var problems = new List<string>();
        var defaults = JsonSerializer.SerializeToNode(Activator.CreateInstance(type), type, SettingsSectionJson.Options) as JsonObject;
        var fields = Fields(type, section, string.Empty, defaults, nested: false, problems);
        return problems.Count == 0 ? fields : throw new InvalidOperationException(string.Join(" ", problems));
    }

    private static List<SettingsFieldDescriptor> Fields(Type type, string section, string prefix, JsonObject? defaults, bool nested, List<string> problems)
    {
        var found = new List<(SettingsFieldDescriptor Field, int Order, int Index)>();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(property => property.MetadataToken).ToList();
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (property.GetCustomAttribute<HostOnlyAttribute>() is not null)
            {
                continue;
            }

            if (property.GetCustomAttribute<SettingsFieldAttribute>() is not { } attribute)
            {
                problems.Add($"{type.Name}.{property.Name} is no field of a settings screen: give it [SettingsField] or [HostOnly].");
                continue;
            }

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (Describe(property, attribute, section, name, prefix + name, defaults?[name], nested, problems) is { } field)
            {
                found.Add((field, attribute.Order, index));
            }
        }

        // Boxes in the order the record first names them, then by order and by the record's order within a box.
        var boxes = found.Select(each => each.Field.Box).Distinct(StringComparer.Ordinal).ToList();
        return [.. found.OrderBy(each => boxes.IndexOf(each.Field.Box)).ThenBy(each => each.Order).ThenBy(each => each.Index).Select(each => each.Field)];
    }

    private static SettingsFieldDescriptor? Describe(PropertyInfo property, SettingsFieldAttribute attribute, string section, string name, string path, JsonNode? defaultValue, bool nested, List<string> problems)
    {
        var where = $"{property.DeclaringType?.Name}.{property.Name}";
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var key = $"#jfbGlobalSettings_{section}_{path.Replace('.', '_')}";
        string kind;
        string? itemKind = null;
        IReadOnlyList<string> choices = [];
        IReadOnlyList<SettingsFieldDescriptor> fields = [];
        if (Plain(type, attribute) is { } plain)
        {
            (kind, choices) = plain;
        }
        else if (ListItem(type) is { } item && Plain(item, attribute) is { } listed && listed.Kind is "text" or "choice" or "wholeNumber" or "key")
        {
            kind = "list";
            (itemKind, choices) = listed;
        }
        else if (type == typeof(IReadOnlyDictionary<string, string>))
        {
            // Texts per culture, the culture "" holding the default, as a definition's (ADR-0002).
            kind = attribute.Editor == "localizedHtml" ? "localizedHtml" : "localizedText";
        }
        else if (type.IsClass && type != typeof(string) && !nested && attribute.Editor is null)
        {
            kind = "nested";
            fields = Fields(type, section, path + ".", defaultValue as JsonObject, nested: true, problems);
        }
        else if (attribute.Editor is not null)
        {
            // Anything else is JSON its own editor draws and the section's validator checks.
            kind = "json";
        }
        else
        {
            problems.Add($"{where} is of the type {type.Name}, which no editor of a settings screen draws without one named: give it Editor.");
            return null;
        }

        if (kind is "key" or "json" && attribute.Editor is null)
        {
            problems.Add($"{where} holds a {kind}, which has no editor of its own: name one with Editor.");
            return null;
        }

        var range = property.GetCustomAttribute<RangeAttribute>();
        var length = property.GetCustomAttribute<MaxLengthAttribute>()?.Length;
        var rules = new SettingsFieldRules(Bound(range?.Minimum), Bound(range?.Maximum), kind == "text" ? length : null, kind == "list" ? length : null);
        var editor = attribute.Editor ?? (kind == "list" && itemKind == "text" ? "list" : kind);
        return new SettingsFieldDescriptor(
            name,
            kind,
            itemKind,
            choices,
            fields,
            defaultValue is null ? null : JsonSerializer.SerializeToElement(defaultValue),
            key,
            key + "Help",
            editor,
            nested ? string.Empty : attribute.Box,
            attribute.Unit ? key + "Unit" : null,
            rules);
    }

    // A plain value's kind, with an enum's values but those the field leaves out; null for anything else.
    private static (string Kind, IReadOnlyList<string> Choices)? Plain(Type type, SettingsFieldAttribute attribute)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
        {
            var names = Enum.GetNames(type).Select(JsonNamingPolicy.CamelCase.ConvertName).Where(name => !attribute.Except.Contains(name, StringComparer.Ordinal)).ToList();
            return ("choice", names);
        }

        return type switch
        {
            _ when type == typeof(bool) => ("boolean", []),
            _ when type == typeof(int) || type == typeof(long) => ("wholeNumber", []),
            _ when type == typeof(string) => attribute.Choices.Length > 0 ? ("choice", attribute.Choices) : ("text", []),
            _ when type == typeof(Guid) => ("key", []),
            _ => null,
        };
    }

    // The item type of a read-only list, or null for any other type.
    private static Type? ListItem(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;

    private static string? Bound(object? bound) => bound is null ? null : Convert.ToString(bound, CultureInfo.InvariantCulture);
}
