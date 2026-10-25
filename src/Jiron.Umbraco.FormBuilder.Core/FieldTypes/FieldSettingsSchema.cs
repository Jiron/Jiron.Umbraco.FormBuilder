using System.Collections.Immutable;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// A field type's settings as the designer's generic settings editor and the checks read them (section 4.9, ADR-0055):
/// each setting of the type's settings class, in the order the inspector shows them.
/// </summary>
public sealed class FieldSettingsSchema
{
    /// <summary>A schema of no settings, for a type without any.</summary>
    public static FieldSettingsSchema Empty { get; } = new([]);

    /// <summary>A schema of the given settings.</summary>
    /// <param name="settings">The settings, in the order the inspector shows them.</param>
    public FieldSettingsSchema(ImmutableArray<FieldSettingDescriptor> settings)
    {
        Settings = settings.IsDefault ? [] : settings;
    }

    /// <summary>The settings, in the order the inspector shows them: by group, then by order, then as the class declares them.</summary>
    public ImmutableArray<FieldSettingDescriptor> Settings { get; }

    /// <summary>The setting with this name at the top of the settings, or null.</summary>
    public FieldSettingDescriptor? Find(string name) => Settings.FirstOrDefault(setting => string.Equals(setting.Name, name, StringComparison.Ordinal));
}

/// <summary>One setting of a field type (ADR-0055).</summary>
public sealed record FieldSettingDescriptor
{
    /// <summary>Its name in the settings' JSON: a lowercase letter, then letters and digits, at most 64 characters.</summary>
    public required string Name { get; init; }

    /// <summary>The kind of value it holds.</summary>
    public required FieldSettingKind Kind { get; init; }

    /// <summary>The kind of a list's items; null for any other kind.</summary>
    public FieldSettingKind? ItemKind { get; init; }

    /// <summary>The names a choice, or a list of choices, may take, in the enum's order; empty for any other kind.</summary>
    public ImmutableArray<string> Choices { get; init; } = [];

    /// <summary>The settings of an object, in the order the inspector shows them; empty for any other kind.</summary>
    public ImmutableArray<FieldSettingDescriptor> Settings { get; init; } = [];

    /// <summary>
    /// The value a field has without one of its own, as the settings class starts; null when it has none (an absent
    /// setting, an empty list or an empty object, which settings never store, ADR-0002).
    /// </summary>
    public JsonElement? Default { get; init; }

    /// <summary>The label: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public required string Label { get; init; }

    /// <summary>A line of help, or null.</summary>
    public string? Description { get; init; }

    /// <summary>The alias of the editor the designer shows.</summary>
    public required string Editor { get; init; }

    /// <summary>The section of the inspector it appears in.</summary>
    public FieldSettingGroup Group { get; init; }

    /// <summary>Its place among the settings of its group: lower first.</summary>
    public int Order { get; init; }

    /// <summary>Whether its text may hold merge tags (ADR-0014).</summary>
    public bool SupportsMergeTags { get; init; }

    /// <summary>Whether visitors see it in the public projection (ADR-0002); read for the settings at the top only.</summary>
    public bool IsPublic { get; init; }
}
