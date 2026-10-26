using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type is (section 4.3.2, ADR-0055): its alias, how the designer shows it, how its values are stored and
/// compared, and which common properties and error kinds it has. The registry checks every declaration when the site
/// starts and refuses one that contradicts itself.
/// </summary>
public sealed record FieldTypeDeclaration
{
    /// <summary>
    /// The alias forms name the type by, for example <c>email</c>: a letter, then letters, digits, '.', '-' and '_', at
    /// most 64 characters. A type of a site or an add-on starts with its own prefix, such as <c>acme.colour</c>, so that it
    /// never meets a later built-in type.
    /// </summary>
    public required string Alias { get; init; }

    /// <summary>The name the palette shows: plain text, or a key of the backoffice's localisation that starts with <c>#</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The icon the palette shows, one of Umbraco's backoffice icons, for example <c>icon-message</c>.</summary>
    public required string Icon { get; init; }

    /// <summary>The group of the palette the type is in.</summary>
    public required FieldPaletteGroup PaletteGroup { get; init; }

    /// <summary>
    /// Whether the type holds a value: it then has an alias and former aliases, a label, and may be sensitive, left out of
    /// mail and kept only until the actions have run. A type that holds none (a heading, the captcha placement) has no
    /// storage, no rule kind and no error kinds, and supports nothing but <see cref="FieldFeatures.VisibilityRules"/>.
    /// </summary>
    public bool HoldsValue { get; init; }

    /// <summary>
    /// Which column holds the type's values (ADR-0003, ADR-0044); <see cref="FieldStorageKind.None"/> for a type whose
    /// parts hold them (a grid's rows, a repeatable group's fields) or that holds no value.
    /// </summary>
    public FieldStorageKind Storage { get; init; }

    /// <summary>What rules compare the type's answers as (ADR-0005); null when rules cannot compare them (a name, a repeatable group, a grid itself).</summary>
    public FieldKind? RuleKind { get; init; }

    /// <summary>
    /// Whether the type's boolean setting <c>multiple</c> turns one choice into several (a dropdown; a grid's rows). The
    /// type then has that setting, and compares as a choice or has rows.
    /// </summary>
    public bool MultipleSetting { get; init; }

    /// <summary>The common properties the type supports; a field keeps every other one at its default (ADR-0034).</summary>
    public FieldFeatures Features { get; init; }

    /// <summary>The error kinds the type raises, each of which a field may give its own message for.</summary>
    public FieldMessageKinds Messages { get; init; }

    /// <summary>Whether a form has at most one field of the type, never inside a repeatable group (the captcha placement).</summary>
    public bool OnePerForm { get; init; }

    /// <summary>
    /// Whether the default value stays on the server: it is resolved when the entry is submitted and never reaches the
    /// page (the hidden field, which may use member and context values, ADR-0014). The type then supports a default value.
    /// </summary>
    public bool ServerOnlyDefault { get; init; }
}

/// <summary>The groups of the designer's palette (section 4.3.2), in the order the palette shows them.</summary>
public enum FieldPaletteGroup
{
    /// <summary>Text, long text, email, phone and web address.</summary>
    Text,

    /// <summary>Numbers, dates, times, ratings, scales, sliders and calculations.</summary>
    NumbersAndDates,

    /// <summary>Dropdowns, radio buttons, checkboxes, grids, rankings, single checkboxes, yes or no, consent and countries.</summary>
    Choices,

    /// <summary>Names and addresses.</summary>
    People,

    /// <summary>File uploads and signatures.</summary>
    Files,

    /// <summary>Repeatable groups and hidden fields.</summary>
    Structure,

    /// <summary>Headings, text blocks, dividers and images, which hold no value.</summary>
    Content,

    /// <summary>The captcha placement.</summary>
    Protection,
}

/// <summary>
/// The common properties of a field (ADR-0002) that a field type supports. A property the type does not support keeps
/// its default; publishing refuses any other value (ADR-0034, ADR-0035).
/// </summary>
[Flags]
public enum FieldFeatures
{
    /// <summary>Only the key, the type, the span, the CSS class and the type's settings.</summary>
    None = 0,

    /// <summary>A label.</summary>
    Label = 1 << 0,

    /// <summary>A label hidden from sight but kept for screen readers.</summary>
    HideLabel = 1 << 1,

    /// <summary>A help text.</summary>
    Help = 1 << 2,

    /// <summary>A placeholder.</summary>
    Placeholder = 1 << 3,

    /// <summary>Being required, always or under a <c>require</c> rule.</summary>
    Required = 1 << 4,

    /// <summary>A default value.</summary>
    DefaultValue = 1 << 5,

    /// <summary>A value prefilled from the query or the member.</summary>
    Prefill = 1 << 6,

    /// <summary>An autocomplete token.</summary>
    Autocomplete = 1 << 7,

    /// <summary>A <c>pattern</c> check.</summary>
    PatternCheck = 1 << 8,

    /// <summary>A <c>matchesField</c> check.</summary>
    MatchesFieldCheck = 1 << 9,

    /// <summary>A <c>uniquePerForm</c> check.</summary>
    UniquePerFormCheck = 1 << 10,

    /// <summary><c>show</c> and <c>hide</c> rules.</summary>
    VisibilityRules = 1 << 11,

    /// <summary>Options: the type's choices, or a grid's columns.</summary>
    Options = 1 << 12,

    /// <summary>The type cannot be answered without options; it supports <see cref="Options"/>.</summary>
    NeedsOptions = 1 << 13,

    /// <summary>A number of places on its own options (section 4.4); it supports <see cref="Options"/>.</summary>
    Places = 1 << 14,

    /// <summary>Rows, at least one: the grid.</summary>
    Rows = 1 << 15,

    /// <summary>Child fields, at least one: the repeatable group.</summary>
    ChildFields = 1 << 16,
}

/// <summary>The error kinds of section 4.3.3 a field type raises, each with a custom message in a field's messages.</summary>
[Flags]
public enum FieldMessageKinds
{
    /// <summary>No messages: the type raises no error a visitor sees.</summary>
    None = 0,

    /// <summary><c>required</c>: raised exactly by the types that support <see cref="FieldFeatures.Required"/>.</summary>
    Required = 1 << 0,

    /// <summary><c>invalid</c>.</summary>
    Invalid = 1 << 1,

    /// <summary><c>minLength</c>.</summary>
    MinLength = 1 << 2,

    /// <summary><c>maxLength</c>.</summary>
    MaxLength = 1 << 3,

    /// <summary><c>minWords</c>.</summary>
    MinWords = 1 << 4,

    /// <summary><c>maxWords</c>.</summary>
    MaxWords = 1 << 5,

    /// <summary><c>min</c>.</summary>
    Min = 1 << 6,

    /// <summary><c>max</c>.</summary>
    Max = 1 << 7,

    /// <summary><c>step</c>.</summary>
    Step = 1 << 8,

    /// <summary><c>dateWindow</c>.</summary>
    DateWindow = 1 << 9,

    /// <summary><c>selections</c>.</summary>
    Selections = 1 << 10,

    /// <summary><c>fileType</c>.</summary>
    FileType = 1 << 11,

    /// <summary><c>fileSize</c>.</summary>
    FileSize = 1 << 12,

    /// <summary><c>fileCount</c>.</summary>
    FileCount = 1 << 13,

    /// <summary><c>full</c>.</summary>
    Full = 1 << 14,

    /// <summary><c>confirmation</c>: the answer typed again is missing or another one, such as an email address (ADR-0062).</summary>
    Confirmation = 1 << 15,

    /// <summary>
    /// <c>digits</c>: a number with more digits than a stored number keeps exactly, more than 15 in all or after the
    /// point (ADR-0003, ADR-0069).
    /// </summary>
    Digits = 1 << 16,
}

/// <summary>
/// How a field's values are stored, one of the kinds of section 4.3.2, as the field catalogue records it (ADR-0044).
/// Step 5.1d maps each kind to the typed columns of the entry values (ADR-0003).
/// </summary>
public enum FieldStorageKind
{
    /// <summary>No values of its own: a grid or a repeatable group, whose parts hold them, or a type that holds no value.</summary>
    None,

    /// <summary>Text, as typed or chosen.</summary>
    Text,

    /// <summary>Text that may run long, a long answer.</summary>
    LongText,

    /// <summary>A number.</summary>
    Number,

    /// <summary>A date, or a date with a time.</summary>
    Date,

    /// <summary>Checked or not.</summary>
    Bool,

    /// <summary>Uploaded files.</summary>
    File,

    /// <summary>A value of several parts, such as a name or an address.</summary>
    Json,
}
