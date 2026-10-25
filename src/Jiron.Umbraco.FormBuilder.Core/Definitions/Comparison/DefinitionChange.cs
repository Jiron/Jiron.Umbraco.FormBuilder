using System.Text.Json.Nodes;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Comparison;

/// <summary>What an element of a definition is, for a comparison (ADR-0045).</summary>
internal enum DefinitionElementKind
{
    /// <summary>A group of the form settings, named by <see cref="DefinitionChange.SettingsGroup"/>.</summary>
    Settings,

    /// <summary>A page.</summary>
    Page,

    /// <summary>A group of fields on a page.</summary>
    Group,

    /// <summary>A field, a repeatable group's children included.</summary>
    Field,

    /// <summary>A rule of a page, a group, a field, an action or an outcome.</summary>
    Rule,

    /// <summary>A form-level validation rule.</summary>
    ValidationRule,

    /// <summary>An action.</summary>
    Action,

    /// <summary>An outcome.</summary>
    Outcome,
}

/// <summary>What happened to an element between two versions.</summary>
internal enum DefinitionChangeKind
{
    /// <summary>Only the newer version has it.</summary>
    Added,

    /// <summary>Only the older version has it.</summary>
    Removed,

    /// <summary>Both have it, and its properties or its place differ.</summary>
    Changed,
}

/// <summary>A property of an element that differs between two versions.</summary>
/// <param name="Path">
/// The property's path from the element, one segment per step: a property name, a culture of a text (<c>""</c> for the
/// default), or the value or key of a field's option item or grid row, as in <c>["options", "items", "phone", "label", ""]</c>.
/// </param>
/// <param name="From">The older value as JSON, or null where the older version has none.</param>
/// <param name="To">The newer value as JSON, or null where the newer version has none.</param>
internal sealed record DefinitionPropertyChange(IReadOnlyList<string> Path, JsonNode? From, JsonNode? To);

/// <summary>An element of a definition that differs between two versions (ADR-0045).</summary>
/// <param name="ElementKind">What the element is.</param>
/// <param name="Key">Its key, or null for a settings group.</param>
/// <param name="SettingsGroup">The settings group's name, such as <c>appearance</c>, or null for other elements.</param>
/// <param name="ParentKey">
/// The element that holds it in the newer version (in the older one when removed): a group's page, a field's group or
/// repeatable group, a rule's element; null for pages and for elements of the form itself.
/// </param>
/// <param name="PreviousParentKey">The element that held it in the older version, when it moved to another one; otherwise null.</param>
/// <param name="ChangeKind">Whether it was added, removed or changed.</param>
/// <param name="Moved">Whether its place changed: another parent, or another order among the siblings both versions share.</param>
/// <param name="Label">Its default-culture title, label or name, from the newer version when it has one, or null.</param>
/// <param name="Alias">A field's alias, or null.</param>
/// <param name="Type">A field's or an action's type, or null.</param>
/// <param name="Before">Its own properties in the older version, without its key and the elements it holds; null when added.</param>
/// <param name="After">Its own properties in the newer version, without its key and the elements it holds; null when removed.</param>
/// <param name="Properties">The properties that differ, in the newer version's order; empty when added, removed or only moved.</param>
internal sealed record DefinitionChange(
    DefinitionElementKind ElementKind,
    Guid? Key,
    string? SettingsGroup,
    Guid? ParentKey,
    Guid? PreviousParentKey,
    DefinitionChangeKind ChangeKind,
    bool Moved,
    string? Label,
    string? Alias,
    string? Type,
    JsonObject? Before,
    JsonObject? After,
    IReadOnlyList<DefinitionPropertyChange> Properties)
{
    /// <summary>The change without its properties, for a version list.</summary>
    public DefinitionChangeSummary Summarize() => new(ElementKind, Key, SettingsGroup, ParentKey, ChangeKind, Moved, Label, Alias);
}

/// <summary>An element that differs between two versions, without its properties: what a version list shows (ADR-0045).</summary>
/// <param name="ElementKind">What the element is.</param>
/// <param name="Key">Its key, or null for a settings group.</param>
/// <param name="SettingsGroup">The settings group's name, or null for other elements.</param>
/// <param name="ParentKey">The element that holds it, as in <see cref="DefinitionChange.ParentKey"/>.</param>
/// <param name="ChangeKind">Whether it was added, removed or changed.</param>
/// <param name="Moved">Whether its place changed.</param>
/// <param name="Label">Its default-culture title, label or name, or null.</param>
/// <param name="Alias">A field's alias, or null.</param>
internal sealed record DefinitionChangeSummary(
    DefinitionElementKind ElementKind,
    Guid? Key,
    string? SettingsGroup,
    Guid? ParentKey,
    DefinitionChangeKind ChangeKind,
    bool Moved,
    string? Label,
    string? Alias);
