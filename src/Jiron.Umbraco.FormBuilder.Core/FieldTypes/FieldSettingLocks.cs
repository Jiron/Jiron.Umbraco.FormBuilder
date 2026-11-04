using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>A host's lock of a field type's setting (ADR-0060): the value every field of the type has.</summary>
/// <param name="Type">The field type's alias.</param>
/// <param name="Setting">The setting, at the top of the type's settings.</param>
/// <param name="ConfigurationKey">The lock's key, such as <c>JironFormBuilder:Designer:SettingLocks:0</c>.</param>
/// <param name="Value">The value every field has, as settings store it; null when the setting keeps its default.</param>
/// <param name="Hidden">Whether the designer leaves the setting out instead of showing it read-only.</param>
internal sealed record FieldSettingLock(string Type, FieldSettingDescriptor Setting, string ConfigurationKey, JsonElement? Value, bool Hidden);

/// <summary>
/// The host's designer locks of field settings (<c>JironFormBuilder:Designer:SettingLocks</c> of kind <c>field</c>,
/// ADR-0060), resolved against the site's field types. The site runs a form with the locks applied, and a draft save
/// refuses a locked setting that an editor gives another value. Start-up refuses locks that do not fit the field types
/// (<see cref="FieldSettingLockStartupCheck"/>), so a running site always resolves them; locks of kind <c>action</c> are
/// <see cref="Actions.ActionSettingLocks"/>'s.
/// </summary>
internal sealed class FieldSettingLocks
{
    private const string Root = FormBuilderOptions.SectionName + ":Designer:SettingLocks";

    private readonly FrozenDictionary<string, ImmutableArray<FieldSettingLock>> _byType;

    /// <summary>The site's locks of field settings.</summary>
    /// <exception cref="InvalidOperationException">A lock does not fit the field types; the message lists every problem.</exception>
    public FieldSettingLocks(IOptions<FormBuilderOptions> options, FieldTypeRegistry registry)
        : this(Build((options ?? throw new ArgumentNullException(nameof(options))).Value.Designer.SettingLocks, registry))
    {
    }

    private FieldSettingLocks(FrozenDictionary<string, ImmutableArray<FieldSettingLock>> byType)
    {
        _byType = byType;
    }

    /// <summary>Whether the host locks no field setting.</summary>
    public bool IsEmpty => _byType.Count == 0;

    /// <summary>
    /// What keeps the locks from fitting the site's field types, each naming the lock's key and never its value: a type or
    /// a setting the site lacks, a value the setting refuses, or a required setting kept without a value. Locks of another
    /// shape are the options' own validation's (ADR-0042), and those of kind <c>action</c> have a check of their own.
    /// </summary>
    public static IReadOnlyList<string> Problems(IList<DesignerSettingLock> locks, FieldTypeRegistry registry)
    {
        var problems = new List<string>();
        Resolve(locks, registry, problems);
        return problems;
    }

    /// <summary>The locks of a field type, in the configuration's order; none for a type without any.</summary>
    public ImmutableArray<FieldSettingLock> Of(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _byType.TryGetValue(type, out var locks) ? locks : [];
    }

    /// <summary>
    /// The definition as the site runs it: every field of a locked type, child fields included, with the locked values,
    /// and without a setting whose lock keeps the default. The same instance when no lock changes a field.
    /// </summary>
    public FormDefinition Apply(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (IsEmpty)
        {
            return definition;
        }

        FormPage[]? pages = null;
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = definition.Pages[p];
            FormGroup[]? groups = null;
            for (var g = 0; g < page.Groups.Count; g++)
            {
                if (Apply(page.Groups[g].Fields) is { } fields)
                {
                    groups ??= [.. page.Groups];
                    groups[g] = page.Groups[g] with { Fields = fields };
                }
            }

            if (groups is not null)
            {
                pages ??= [.. definition.Pages];
                pages[p] = page with { Groups = groups };
            }
        }

        return pages is null ? definition : definition with { Pages = pages };
    }

    /// <summary>
    /// Refuses a draft that gives a locked setting a value of its own: one that is neither the lock's (the setting's
    /// default, for a lock without a value) nor the one the stored draft holds for the field with the same key, which a
    /// form keeps from before the lock came, whatever type the field had there: an Undo after a save may bring back a
    /// change of type (ADR-0174). A setting the field leaves out is always fine. The first such setting in document
    /// order is named.
    /// </summary>
    /// <param name="definition">The draft to save.</param>
    /// <param name="stored">The stored draft the save replaces, or null when there is none to read.</param>
    /// <exception cref="FormBuilderSettingLockedException">A locked setting has another value (<c>setting_locked</c>).</exception>
    public void CheckSave(FormDefinition definition, FormDefinition? stored)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (IsEmpty)
        {
            return;
        }

        var before = new Dictionary<Guid, FormField>();
        if (stored is not null)
        {
            foreach (var (field, _) in FieldsOf(stored))
            {
                before.TryAdd(field.Key, field);
            }
        }

        foreach (var (field, path) in FieldsOf(definition))
        {
            var kept = before.TryGetValue(field.Key, out var earlier) ? earlier.Settings : null;
            if (FirstRefused(field.Settings, kept, Of(field.Type)) is { } settingLock)
            {
                throw new FormBuilderSettingLockedException(JsonPaths.Property(JsonPaths.Property(path, "settings"), settingLock.Setting.Name), settingLock.ConfigurationKey);
            }
        }
    }

    /// <summary>
    /// The first lock whose setting has a value of its own: neither the lock's (the setting's default, for a lock without
    /// a value) nor the one kept from before the lock came; null when there is none. A setting left out is always fine.
    /// </summary>
    /// <param name="settings">The settings to save.</param>
    /// <param name="kept">The settings the stored draft holds for the same element, or null.</param>
    /// <param name="locks">The locks of the element's type.</param>
    internal static FieldSettingLock? FirstRefused(SettingsObject? settings, SettingsObject? kept, ImmutableArray<FieldSettingLock> locks)
    {
        foreach (var settingLock in locks)
        {
            var name = settingLock.Setting.Name;
            if (settings is null || !settings.TryGetProperty(name, out var given)
                || ((settingLock.Value ?? settingLock.Setting.Default) is { } allowed && Same(settingLock.Setting, given, allowed))
                || (kept is not null && kept.TryGetProperty(name, out var keptValue) && Same(settingLock.Setting, given, keptValue)))
            {
                continue;
            }

            return settingLock;
        }

        return null;
    }

    /// <summary>Whether a lock is of the kind, with a type, a setting and a value of the right shape.</summary>
    internal static bool IsWellFormed(DesignerSettingLock settingLock, string kind) =>
        string.Equals(settingLock.Kind, kind, StringComparison.OrdinalIgnoreCase)
        && FormBuilderOptionsValidator.IsTypeAlias(settingLock.Type)
        && FormBuilderOptionsValidator.IsSettingName(settingLock.Setting)
        && settingLock.Value is not { Length: > FormBuilderOptionsValidator.MaxLockValueLength };

    private static FrozenDictionary<string, ImmutableArray<FieldSettingLock>> Build(IList<DesignerSettingLock> locks, FieldTypeRegistry registry)
    {
        var problems = new List<string>();
        var resolved = Resolve(locks, registry, problems);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }

        return resolved.GroupBy(each => each.Type, StringComparer.Ordinal).ToFrozenDictionary(group => group.Key, group => group.ToImmutableArray(), StringComparer.Ordinal);
    }

    private static List<FieldSettingLock> Resolve(IList<DesignerSettingLock> locks, FieldTypeRegistry registry, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(registry);
        var resolved = new List<FieldSettingLock>();
        var seen = new HashSet<(string Type, string Setting)>();
        for (var i = 0; i < locks.Count; i++)
        {
            // The options' own validation refuses a lock of another shape and a second lock of one setting (ADR-0042).
            if (locks[i] is { } settingLock && IsWellFormed(settingLock, DesignerSettingLock.FieldKind) && seen.Add((settingLock.Type!, settingLock.Setting!))
                && Resolve(settingLock, string.Create(CultureInfo.InvariantCulture, $"{Root}:{i}"), registry, problems) is { } found)
            {
                resolved.Add(found);
            }
        }

        return resolved;
    }

    private static FieldSettingLock? Resolve(DesignerSettingLock settingLock, string key, FieldTypeRegistry registry, List<string> problems)
    {
        var alias = settingLock.Type!;
        if (registry.FindType(alias) is not { } type)
        {
            var aliases = registry.Types.Select(each => each.Declaration.Alias).ToList();
            problems.Add(aliases.Find(each => string.Equals(each, alias, StringComparison.OrdinalIgnoreCase)) is { } spelt
                ? $"{key}:Type ('{alias}') is not a field type of the site; aliases are compared exactly, and the site has '{spelt}'."
                : $"{key}:Type ('{alias}') is not a field type of the site; its field types are {Names(aliases)}.");
            return null;
        }

        return ResolveSetting(settingLock, key, "field type", "a field", type.SettingsSchema, problems);
    }

    /// <summary>A lock resolved against its type's settings; null, with what is wrong, when it does not fit.</summary>
    /// <param name="settingLock">The lock, of the right shape.</param>
    /// <param name="key">The lock's key.</param>
    /// <param name="kind">What the type is, in words: <c>field type</c>.</param>
    /// <param name="element">What has the settings, in words: <c>a field</c>.</param>
    /// <param name="schema">The type's settings.</param>
    /// <param name="problems">Where the problems go.</param>
    internal static FieldSettingLock? ResolveSetting(DesignerSettingLock settingLock, string key, string kind, string element, FieldSettingsSchema schema, List<string> problems)
    {
        var alias = settingLock.Type!;
        var name = settingLock.Setting!;
        var settings = schema.Settings;
        if (schema.Find(name) is not { } setting)
        {
            problems.Add(settings.IsEmpty
                ? $"{key}:Setting ('{name}') is not a setting of the {kind} '{alias}', which has no settings."
                : settings.FirstOrDefault(each => string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase)) is { } spelt
                    ? $"{key}:Setting ('{name}') is not a setting of the {kind} '{alias}'; names are compared exactly, and it has '{spelt.Name}'."
                    : $"{key}:Setting ('{name}') is not a setting of the {kind} '{alias}'; its settings are {Names(settings.Select(each => each.Name))}.");
            return null;
        }

        JsonElement? value = null;
        if (settingLock.Value is { } text && !TryRead(setting, text, out value))
        {
            problems.Add($"{key}:Value is not a value of the setting '{name}' of the {kind} '{alias}', which takes {Takes(setting)}.");
            return null;
        }

        if (value is null && setting.Rules.Required)
        {
            problems.Add($"{key} locks the required setting '{name}' of the {kind} '{alias}' without a value; {element} cannot do without one, so give the lock its Value.");
            return null;
        }

        return new FieldSettingLock(alias, setting, key, value, settingLock.Hidden);
    }

    // Reads a lock's text as the setting's kind needs it and holds it to the setting as a field's own value would be held:
    // false when it does not fit; true without a value when settings leave it out (an empty list or object, or null).
    private static bool TryRead(FieldSettingDescriptor setting, string text, out JsonElement? value)
    {
        value = null;
        if (!StrictStringConverter.IsWellFormed(text) || Parse(setting.Kind, text) is not { } parsed)
        {
            return false;
        }

        using (parsed)
        {
            if (CanonicalJson.IsLeftOut(parsed.RootElement))
            {
                return true;
            }

            // As settings store it: integers of at most 15 digits, no null in a list, no property given twice.
            using var wrapped = JsonDocument.Parse(Write(writer =>
            {
                writer.WriteStartObject();
                writer.WritePropertyName(setting.Name);
                parsed.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }));
            if (!SettingsJson.TryCanonicalize(wrapped.RootElement, JsonPaths.Root, out var canonical, out _))
            {
                return false;
            }

            using var stored = JsonDocument.Parse(canonical);
            var read = stored.RootElement.GetProperty(setting.Name).Clone();
            var problems = new DefinitionProblems();
            FieldSettingsChecks.CheckDefault(setting, read, problems);
            if (problems.Found > 0)
            {
                return false;
            }

            value = read;
            return true;
        }
    }

    // The JSON of a lock's text for the setting's kind (ADR-0060's table), or null when the text cannot be one.
    private static JsonDocument? Parse(FieldSettingKind kind, string text)
    {
        switch (kind)
        {
            case FieldSettingKind.Boolean:
                return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ? JsonDocument.Parse("true")
                    : string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ? JsonDocument.Parse("false")
                    : null;
            case FieldSettingKind.WholeNumber:
                return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    ? JsonDocument.Parse(number.ToString(CultureInfo.InvariantCulture))
                    : null;
            case FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml:
                return JsonDocument.Parse(Write(writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString(string.Empty, text);
                    writer.WriteEndObject();
                }));
            case FieldSettingKind.List or FieldSettingKind.Nested or FieldSettingKind.Json:
                try
                {
                    return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = FormDefinitionSerializer.ModelDepth });
                }
                catch (JsonException)
                {
                    return null;
                }

            default:
                return JsonDocument.Parse(Write(writer => writer.WriteStringValue(text)));
        }
    }

    private static byte[] Write(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        return buffer.ToArray();
    }

    // The fields with their locks applied, or null when no lock changes one.
    private FormField[]? Apply(IReadOnlyList<FormField> fields)
    {
        FormField[]? applied = null;
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var children = Apply(field.Fields);
            var settings = SettingsOf(field);
            if (children is not null || !ReferenceEquals(settings, field.Settings))
            {
                applied ??= [.. fields];
                applied[i] = field with { Settings = settings, Fields = children ?? field.Fields };
            }
        }

        return applied;
    }

    // A field's settings with its type's locks applied; the same instance when they change nothing.
    private SettingsObject? SettingsOf(FormField field) => Applied(field.Settings, Of(field.Type));

    /// <summary>Settings with their type's locks applied; the same instance when they change nothing.</summary>
    internal static SettingsObject? Applied(SettingsObject? given, ImmutableArray<FieldSettingLock> locks)
    {
        if (locks.IsEmpty)
        {
            return given;
        }

        var locked = locks.Select(each => each.Setting.Name).ToHashSet(StringComparer.Ordinal);
        var json = Write(writer =>
        {
            writer.WriteStartObject();
            if (given is { } settings)
            {
                foreach (var property in settings.Value.EnumerateObject().Where(property => !locked.Contains(property.Name)))
                {
                    property.WriteTo(writer);
                }
            }

            foreach (var settingLock in locks)
            {
                if (settingLock.Value is { } value)
                {
                    writer.WritePropertyName(settingLock.Setting.Name);
                    value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        });
        using var document = JsonDocument.Parse(json);
        var applied = CanonicalJson.IsLeftOut(document.RootElement) ? null : SettingsObject.From(document.RootElement);
        return Equals(applied, given) ? given : applied;
    }

    // Every field with its path, in document order: a group's fields, each followed by its child fields.
    private static IEnumerable<(FormField Field, string Path)> FieldsOf(FormDefinition definition)
    {
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "pages"), p);
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                foreach (var field in FieldsOf(definition.Pages[p].Groups[g].Fields, JsonPaths.Index(JsonPaths.Property(page, "groups"), g)))
                {
                    yield return field;
                }
            }
        }
    }

    private static IEnumerable<(FormField Field, string Path)> FieldsOf(IReadOnlyList<FormField> fields, string parent)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var path = JsonPaths.Index(JsonPaths.Property(parent, "fields"), i);
            yield return (fields[i], path);
            foreach (var child in FieldsOf(fields[i].Fields, path))
            {
                yield return child;
            }
        }
    }

    // Whether two values of a setting are one: numbers, dates and times as their values ("2.50" is 2.5), a list item by
    // item, anything else as JSON.
    private static bool Same(FieldSettingDescriptor setting, JsonElement a, JsonElement b) => Same(setting.Kind, setting.ItemKind, a, b);

    private static bool Same(FieldSettingKind kind, FieldSettingKind? itemKind, JsonElement a, JsonElement b)
    {
        if (kind == FieldSettingKind.List && itemKind is { } item && a.ValueKind == JsonValueKind.Array && b.ValueKind == JsonValueKind.Array)
        {
            return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(pair => Same(item, null, pair.First, pair.Second));
        }

        if (a.ValueKind == JsonValueKind.String && b.ValueKind == JsonValueKind.String && ValueOf(kind, a.GetString()!) is { } first)
        {
            return string.Equals(first, ValueOf(kind, b.GetString()!), StringComparison.Ordinal);
        }

        return JsonElement.DeepEquals(a, b);
    }

    // The value a number, a date or a time compares as; null for another kind, or a text that is not one.
    private static string? ValueOf(FieldSettingKind kind, string text) => kind switch
    {
        FieldSettingKind.Number => ExactDecimal.Parse(text)?.ToString(),
        FieldSettingKind.Date => CalendarValues.ParseDate(text)?.ToString(CultureInfo.InvariantCulture),
        FieldSettingKind.Time => CalendarValues.ParseTime(text)?.ToString(CultureInfo.InvariantCulture),
        _ => null,
    };

    // What a setting takes, in words, such as "a whole number, 1 to 2000", for a refusal that never repeats the value.
    private static string Takes(FieldSettingDescriptor setting) => setting.Kind switch
    {
        FieldSettingKind.List => TakesList(setting),
        FieldSettingKind.Nested => $"an object written as JSON, with the settings {Names(setting.Settings.Select(each => each.Name))}",
        FieldSettingKind.Json => "JSON, which the field type checks when a form is published",
        _ => TakesPlain(setting.Kind, setting.Rules, setting.Choices),
    };

    private static string TakesList(FieldSettingDescriptor setting)
    {
        // The settings reader gives every list its most items (ADR-0056); a lock that keeps one empty has no value.
        var rules = setting.Rules;
        var most = FieldSettingsChecks.Count(rules.MaximumCount ?? FieldSettingRules.ListCount, "item");
        var count = rules.MinimumCount is { } fewest ? $", with {fewest} to {most}" : $", with at most {most}";
        return setting.ItemKind is { } item
            ? $"a list written as JSON{count}, each item {TakesPlain(item, rules, setting.Choices)}"
            : $"a list written as JSON{count}";
    }

    private static string TakesPlain(FieldSettingKind kind, FieldSettingRules rules, ImmutableArray<FieldSettingChoice> choices) => kind switch
    {
        FieldSettingKind.Boolean => "true or false",
        FieldSettingKind.WholeNumber => Bounded("a whole number", rules),
        FieldSettingKind.Number => Bounded("a number such as 2.5", rules),
        FieldSettingKind.Text or FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml => TakesText(rules),
        FieldSettingKind.Date => "a date, YYYY-MM-DD",
        FieldSettingKind.Time => "a time of day, HH:mm",
        FieldSettingKind.Choice => OneOf(choices.Select(choice => choice.Value)),
        _ => "a key, such as 5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c",
    };

    private static string Bounded(string what, FieldSettingRules rules) =>
        rules.Minimum is null && rules.Maximum is null ? what : $"{what}, {FieldSettingsChecks.Bounds(rules)}";

    private static string TakesText(FieldSettingRules rules)
    {
        if (!rules.AllowedValues.IsEmpty)
        {
            return OneOf(rules.AllowedValues);
        }

        // The settings reader gives every text its most characters (ADR-0056).
        var most = FieldSettingsChecks.Count(rules.MaximumLength ?? FieldSettingRules.TextLength, "character");
        var length = rules.MinimumLength is { } fewest ? $"{fewest} to {most}" : $"at most {most}";
        return rules.Required ? $"a text that is not blank, of {length}" : $"a text of {length}";
    }

    private static string OneOf(IEnumerable<string> values)
    {
        var list = values.ToList();
        return list.Count == 1 ? $"'{list[0]}'" : $"one of {string.Join(", ", list.Select(value => $"'{value}'"))}";
    }

    private static string Names(IEnumerable<string> names)
    {
        var list = names.Select(name => $"'{name}'").ToList();
        return list.Count == 1 ? list[0] : $"{string.Join(", ", list.Take(list.Count - 1))} and {list[^1]}";
    }
}
