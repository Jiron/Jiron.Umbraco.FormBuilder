using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Reads a settings class into its schema (ADR-0055): every public property is a setting with
/// <see cref="FormBuilderSettingAttribute"/>, or is left out with <c>[JsonIgnore]</c>; its kind follows from its type, its
/// default from the value a new instance holds. Objects of settings nest one level deep, and lists hold plain values.
/// </summary>
internal static class FieldSettingsReader
{
    /// <summary>The schema of a settings class.</summary>
    /// <exception cref="InvalidOperationException">The class cannot be a settings class; the message gives every reason.</exception>
    public static FieldSettingsSchema Read(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        var problems = new List<string>();
        var settings = Settings(settingsType, nested: false, problems);
        return problems.Count == 0 ? new FieldSettingsSchema(settings) : throw new InvalidOperationException(string.Join(" ", problems));
    }

    private static ImmutableArray<FieldSettingDescriptor> Settings(Type type, bool nested, List<string> problems)
    {
        if (type.IsAbstract || type.IsValueType || type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) is null)
        {
            problems.Add($"{NameOf(type)} is a settings class only as a class with a public constructor without parameters, whose new instance holds the defaults.");
            return [];
        }

        JsonTypeInfo info;
        object instance;
        JsonElement defaults;
        try
        {
            // System.Text.Json refuses two properties of one name, and a constructor may throw.
            info = FieldSettingsJson.Options.GetTypeInfo(type);
            instance = Activator.CreateInstance(type)!;
            defaults = JsonSerializer.SerializeToElement(instance, info);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or JsonException or TargetInvocationException)
        {
            problems.Add($"{NameOf(type)} cannot be written as JSON: {(exception as TargetInvocationException)?.InnerException?.Message ?? exception.Message}");
            return [];
        }

        var read = info.Properties.Where(property => property.Set is not null).ToDictionary(property => property.Name, StringComparer.Ordinal);
        var found = new List<(FieldSettingDescriptor Setting, int Index)>();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => Depth(property.DeclaringType!))
            .ThenBy(property => property.MetadataToken)
            .ToList();
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var where = $"{NameOf(type)}.{property.Name}";
            var attribute = property.GetCustomAttribute<FormBuilderSettingAttribute>();
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                if (attribute is not null)
                {
                    problems.Add($"{where} is a setting and ignored by JSON at once; keep one of the two.");
                }

                continue;
            }

            if (attribute is null)
            {
                problems.Add($"{where} is not a setting: give it [FormBuilderSetting] or [JsonIgnore].");
                continue;
            }

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!FormBuilderOptionsValidator.IsSettingName(name))
            {
                problems.Add($"{where} is named '{name}' in JSON; a setting's name is a lowercase letter, then letters and digits, at most 64 characters.");
                continue;
            }

            if (property.GetIndexParameters().Length > 0 || property.SetMethod is not { IsPublic: true } || !read.TryGetValue(name, out var json))
            {
                problems.Add($"{where} cannot be read from JSON: a setting is a public property with a public setter or init.");
                continue;
            }

            // Settings leave nulls out, so such a value would stay null after every read.
            if (!json.IsGetNullable && property.GetValue(instance) is null)
            {
                problems.Add($"{where} starts null, though the class says it cannot be null: give it an initial value, or make it nullable.");
                continue;
            }

            if (Describe(property, attribute, name, defaults, nested, where, problems) is { } setting)
            {
                found.Add((setting, index));
            }
        }

        return [.. found.OrderBy(each => each.Setting.Group).ThenBy(each => each.Setting.Order).ThenBy(each => each.Index).Select(each => each.Setting)];
    }

    private static FieldSettingDescriptor? Describe(
        PropertyInfo property,
        FormBuilderSettingAttribute attribute,
        string name,
        JsonElement defaults,
        bool nested,
        string where,
        List<string> problems)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        FieldSettingKind kind;
        FieldSettingKind? itemKind = null;
        ImmutableArray<string> choices = [];
        ImmutableArray<FieldSettingDescriptor> settings = [];
        if (ListItem(type) is { } item)
        {
            // A list's kind, if given, is the kind of its items; List itself says no more than the type does. An item
            // that is no plain value (an object, a list, a double) gets the list's reason; an enum that cannot be a
            // choice, or a kind its type cannot be stored as, gets its own.
            kind = FieldSettingKind.List;
            var reasons = item.IsEnum || Plain(item, FieldSettingKind.Inferred, where, problems: null, out _) is not null ? problems : null;
            itemKind = Plain(item, attribute.Kind is FieldSettingKind.List ? FieldSettingKind.Inferred : attribute.Kind, where, reasons, out choices);
            if (itemKind is not (FieldSettingKind.WholeNumber or FieldSettingKind.Text or FieldSettingKind.Number or FieldSettingKind.Date
                or FieldSettingKind.Time or FieldSettingKind.Choice or FieldSettingKind.Key))
            {
                if (itemKind is not null || reasons is null)
                {
                    problems.Add($"{where} is a list of {NameOf(item)}; a list holds whole numbers, texts, numbers, dates, times, choices or keys.");
                }

                return null;
            }
        }
        else if (Plain(type, attribute.Kind, where, problems: null, out choices) is { } plain)
        {
            kind = plain;
        }
        else if (type == typeof(string) || !type.IsClass)
        {
            // Plain again, this time naming the reason.
            Plain(type, attribute.Kind, where, problems, out _);
            return null;
        }
        else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            problems.Add($"{where} is of the type {NameOf(type)}; a list of settings is an IReadOnlyList<T>.");
            return null;
        }
        else if (nested)
        {
            problems.Add($"{where} is an object inside an object; settings nest one level deep.");
            return null;
        }
        else if (attribute.Kind is not (FieldSettingKind.Inferred or FieldSettingKind.Nested))
        {
            problems.Add($"{where} is an object of settings, which cannot be stored as {Describe(attribute.Kind, null)}.");
            return null;
        }
        else
        {
            kind = FieldSettingKind.Nested;
            settings = Settings(type, nested: true, problems);
        }

        var editor = attribute.Editor ?? (kind is FieldSettingKind.Key or FieldSettingKind.Json || itemKind is FieldSettingKind.Key
            ? null
            : JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));
        if (string.IsNullOrWhiteSpace(editor))
        {
            problems.Add($"{where} holds {Describe(kind, itemKind)}, which has no editor of its own: name one with Editor.");
            return null;
        }

        return new FieldSettingDescriptor
        {
            Name = name,
            Kind = kind,
            ItemKind = itemKind,
            Choices = choices,
            Settings = settings,
            Default = defaults.TryGetProperty(name, out var value) && !CanonicalJson.IsLeftOut(value) ? Canonical(value) : null,
            Label = attribute.Label,
            Description = attribute.Description,
            Editor = editor,
            Group = attribute.Group,
            Order = attribute.Order,
            SupportsMergeTags = attribute.SupportsMergeTags,
            IsPublic = attribute.Public && !nested,
        };
    }

    // The kind of a value that is neither a list nor an object, or null (with the reason when problems are asked for).
    private static FieldSettingKind? Plain(Type type, FieldSettingKind given, string where, List<string>? problems, out ImmutableArray<string> choices)
    {
        choices = [];
        FieldSettingKind? inferred = null;
        if (type == typeof(bool))
        {
            inferred = FieldSettingKind.Boolean;
        }
        else if (type == typeof(int) || type == typeof(long))
        {
            inferred = FieldSettingKind.WholeNumber;
        }
        else if (type == typeof(string))
        {
            // A text may be stored as a number, a date or a time instead.
            if (given is FieldSettingKind.Number or FieldSettingKind.Date or FieldSettingKind.Time)
            {
                return given;
            }

            inferred = FieldSettingKind.Text;
        }
        else if (type == typeof(LocalizedText))
        {
            inferred = FieldSettingKind.LocalizedText;
        }
        else if (type == typeof(LocalizedHtml))
        {
            inferred = FieldSettingKind.LocalizedHtml;
        }
        else if (type == typeof(Guid))
        {
            inferred = FieldSettingKind.Key;
        }
        else if (type == typeof(JsonElement))
        {
            inferred = FieldSettingKind.Json;
        }
        else if (type.IsEnum)
        {
            if (Choices(type) is not { } names)
            {
                problems?.Add($"{where} is the enum {NameOf(type)}; a choice is an enum without [Flags] whose values each have one name.");
                return null;
            }

            choices = names;
            inferred = FieldSettingKind.Choice;
        }

        if (inferred is null)
        {
            problems?.Add($"{where} is of the type {NameOf(type)}, which no setting holds: use bool, int, long, string, LocalizedText, LocalizedHtml, an enum, Guid, JsonElement, IReadOnlyList<T> of a plain value, or a class of settings.");
            return null;
        }

        if (given != FieldSettingKind.Inferred && given != inferred)
        {
            problems?.Add($"{where} holds {Describe(inferred.Value, null)}, which cannot be stored as {Describe(given, null)}; only a text can be a number, a date or a time.");
            return null;
        }

        return inferred;
    }

    // A default as settings store it (ADR-0002): without what is left out, with sorted properties.
    private static JsonElement Canonical(JsonElement value)
    {
        using var document = JsonDocument.Parse(CanonicalJson.Write(value, sortProperties: true));
        return document.RootElement.Clone();
    }

    private static Type? ListItem(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;

    // The names of an enum's values as its JSON writes them (camel case, or the name [JsonStringEnumMemberName] gives), in
    // the order it declares them; null for flags or a value with two names.
    private static ImmutableArray<string>? Choices(Type type)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(field => field.MetadataToken).ToList();
        if (type.IsDefined(typeof(FlagsAttribute), inherit: false) || fields.Select(field => field.GetRawConstantValue()).Distinct().Count() != fields.Count)
        {
            return null;
        }

        return [.. fields.Select(field => field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(field.Name))];
    }

    // A kind in words, for the reasons the reader gives.
    private static string Describe(FieldSettingKind kind, FieldSettingKind? item) => (kind, item) switch
    {
        (FieldSettingKind.List, FieldSettingKind.Key) => "a list of keys",
        (FieldSettingKind.List, _) => "a list",
        (FieldSettingKind.Boolean, _) => "a yes or no",
        (FieldSettingKind.WholeNumber, _) => "a whole number",
        (FieldSettingKind.Number, _) => "a number",
        (FieldSettingKind.Text, _) => "a text",
        (FieldSettingKind.Date, _) => "a date",
        (FieldSettingKind.Time, _) => "a time",
        (FieldSettingKind.LocalizedText, _) => "a text per culture",
        (FieldSettingKind.LocalizedHtml, _) => "rich text per culture",
        (FieldSettingKind.Choice, _) => "a choice",
        (FieldSettingKind.Key, _) => "a key",
        (FieldSettingKind.Nested, _) => "an object of settings",
        _ => "free JSON",
    };

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static string NameOf(Type type) => type.FullName ?? type.Name;
}
