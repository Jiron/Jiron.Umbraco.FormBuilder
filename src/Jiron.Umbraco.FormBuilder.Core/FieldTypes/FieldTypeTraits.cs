using System.Collections.Frozen;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What the structural validation, the public projection and the field catalogue need to know about a field type
/// (section 4.3.2, ADR-0035, ADR-0044), as the registry reads it from the type's declaration and settings schema
/// (ADR-0055): whether it holds a value, what rules compare it as, how its values are stored, which common properties
/// and error kinds it has, and which of its settings visitors may see.
/// </summary>
internal sealed class FieldTypeTraits
{
    /// <summary>The traits of a type: its declaration's, and the public settings of its schema.</summary>
    public FieldTypeTraits(IFormBuilderFieldType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
        var declaration = type.Declaration;
        Alias = declaration.Alias;
        HoldsValue = declaration.HoldsValue;
        Kind = declaration.RuleKind;
        Storage = declaration.Storage;
        MultipleSetting = declaration.MultipleSetting;
        Features = declaration.Features;
        Messages = declaration.Messages;
        OnePerForm = declaration.OnePerForm;
        ServerOnlyDefault = declaration.ServerOnlyDefault;
        PublicSettings = type.SettingsSchema.Settings.Where(setting => setting.IsPublic).Select(setting => setting.Name).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The type itself, whose settings schema and own check publishing holds a field's settings to (ADR-0056).</summary>
    public IFormBuilderFieldType Type { get; }

    /// <summary>The type's alias, for example <c>email</c>.</summary>
    public string Alias { get; }

    /// <summary>
    /// Whether the type holds a value: it then has an alias and former aliases, and may be sensitive, left out of mail
    /// and kept only until the actions have run.
    /// </summary>
    public bool HoldsValue { get; }

    /// <summary>What rules compare the type's answers as; null when rules cannot compare them (a name, a repeatable group, a grid itself).</summary>
    public FieldKind? Kind { get; }

    /// <summary>
    /// Which column holds the type's values; <see cref="FieldStorageKind.None"/> for a type whose parts hold them (a
    /// grid's rows hold text) or that holds no value.
    /// </summary>
    public FieldStorageKind Storage { get; }

    /// <summary>Whether the setting <c>multiple: true</c> turns one choice into several (a dropdown; a grid's rows).</summary>
    public bool MultipleSetting { get; }

    /// <summary>The common properties the type supports.</summary>
    public FieldFeatures Features { get; }

    /// <summary>The error kinds the type raises.</summary>
    public FieldMessageKinds Messages { get; }

    /// <summary>Whether a form has at most one field of the type, never inside a repeatable group (the captcha placement).</summary>
    public bool OnePerForm { get; }

    /// <summary>
    /// Whether the default value stays on the server: it is resolved when the entry is submitted and never reaches the
    /// page (the hidden field, which may use member and context values, ADR-0014).
    /// </summary>
    public bool ServerOnlyDefault { get; }

    /// <summary>The settings visitors may see; every other setting stays on the server (ADR-0002, the public projection).</summary>
    public FrozenSet<string> PublicSettings { get; }

    /// <summary>Whether the type supports every feature given.</summary>
    public bool Supports(FieldFeatures features) => (Features & features) == features;

    /// <summary>What rules compare a field of the type as, its settings considered.</summary>
    public FieldKind? KindOf(FormField field) =>
        Kind == FieldKind.Choice && MultipleSetting && IsMultiple(field) ? FieldKind.Choices : Kind;

    /// <summary>What rules compare a grid row's answers as.</summary>
    public FieldKind RowKindOf(FormField grid) => MultipleSetting && IsMultiple(grid) ? FieldKind.Choices : FieldKind.Choice;

    private static bool IsMultiple(FormField field) =>
        field.Settings is { } settings && settings.TryGetProperty("multiple", out var value) && value.ValueKind == JsonValueKind.True;
}
