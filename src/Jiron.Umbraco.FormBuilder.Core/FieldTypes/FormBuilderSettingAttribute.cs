namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Makes a property of a settings class a setting of its field type (section 4.9, ADR-0055), with what the designer's
/// generic settings editor needs to show it. Every public property of a settings class carries it, except one marked
/// <c>[JsonIgnore]</c>; its kind follows from the property's type, and its default is the property's initial value.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class FormBuilderSettingAttribute : Attribute
{
    /// <summary>A setting with its label.</summary>
    /// <param name="label">The label: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</param>
    public FormBuilderSettingAttribute(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        Label = label;
    }

    /// <summary>The label: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public string Label { get; }

    /// <summary>A line of help below the setting, as text or a <c>#</c> key; none by default.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// The editor the designer shows, by its alias; by default the one of the setting's kind (its name in camel case, such
    /// as <c>boolean</c> or <c>localizedText</c>). A key or free JSON has no default and names its editor, for example
    /// <c>mediaPicker</c>. A <c>jfbSettingEditor</c> manifest can supply an editor of its own (15.1).
    /// </summary>
    public string? Editor { get; set; }

    /// <summary>
    /// How the setting is stored when its property's type leaves a choice: a text may be <see cref="FieldSettingKind.Number"/>,
    /// <see cref="FieldSettingKind.Date"/> or <see cref="FieldSettingKind.Time"/>, also as a list's items (for a list, the
    /// kind given is its items'). By default it is the kind of the property's type.
    /// </summary>
    public FieldSettingKind Kind { get; set; } = FieldSettingKind.Inferred;

    /// <summary>Where the setting sits among the others of its group: lower first, then in the order the class declares them.</summary>
    public int Order { get; set; }

    /// <summary>The section of the designer's inspector the setting appears in (section 4.13).</summary>
    public FieldSettingGroup Group { get; set; } = FieldSettingGroup.Essentials;

    /// <summary>Whether the setting's text may hold merge tags (ADR-0014), which its editor then offers.</summary>
    public bool SupportsMergeTags { get; set; }

    /// <summary>
    /// Whether visitors see the setting: it is then part of the public projection (ADR-0002), as a browser needs it to show
    /// and check the field. Off by default, so a setting stays on the server unless its type says otherwise.
    /// </summary>
    public bool Public { get; set; }
}

/// <summary>The kinds of value a setting holds (ADR-0055), each with the JSON it is stored as.</summary>
public enum FieldSettingKind
{
    /// <summary>Not given: the kind of the property's type.</summary>
    Inferred,

    /// <summary><c>true</c> or <c>false</c>.</summary>
    Boolean,

    /// <summary>A whole number, a JSON integer of at most 15 digits.</summary>
    WholeNumber,

    /// <summary>A number that answers are compared with, as a string in the wire form of ADR-0005, for example <c>"2.5"</c>.</summary>
    Number,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A date, <c>YYYY-MM-DD</c>, as a string.</summary>
    Date,

    /// <summary>A time of day, <c>HH:mm</c>, as a string.</summary>
    Time,

    /// <summary>A text per culture (<c>LocalizedText</c>).</summary>
    LocalizedText,

    /// <summary>Rich text per culture (<c>LocalizedHtml</c>), sanitised when it is shown.</summary>
    LocalizedHtml,

    /// <summary>One of a fixed set of names, from an enum, as a string in camel case.</summary>
    Choice,

    /// <summary>The key of something else, such as a media item or a content page, as a GUID.</summary>
    Key,

    /// <summary>A list of values of one kind.</summary>
    List,

    /// <summary>An object of settings of its own, nested one level deep.</summary>
    Nested,

    /// <summary>Any JSON, which the type checks itself (for example a date or a date relative to today).</summary>
    Json,
}

/// <summary>The sections of the designer's inspector that settings appear in (section 4.13).</summary>
public enum FieldSettingGroup
{
    /// <summary>What the field is for, shown first.</summary>
    Essentials,

    /// <summary>What answers it takes.</summary>
    Validation,

    /// <summary>How it looks.</summary>
    Appearance,

    /// <summary>Everything else.</summary>
    Advanced,
}
