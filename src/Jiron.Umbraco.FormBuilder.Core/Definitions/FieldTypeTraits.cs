using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// The common properties of a field (ADR-0002) that a field type supports. A property the type does not support keeps
/// its default; publishing refuses any other value (ADR-0034, ADR-0035).
/// </summary>
[Flags]
internal enum FieldFeatures
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

    /// <summary>The type cannot be answered without options.</summary>
    NeedsOptions = 1 << 13,

    /// <summary>A number of places on its own options (section 4.4).</summary>
    Places = 1 << 14,

    /// <summary>Rows, at least one: the grid.</summary>
    Rows = 1 << 15,

    /// <summary>Child fields, at least one: the repeatable group.</summary>
    ChildFields = 1 << 16,
}

/// <summary>The error kinds of section 4.3.3 a field type raises, each with a custom message in <see cref="FieldMessages"/>.</summary>
[Flags]
internal enum FieldMessageKinds
{
    /// <summary>No messages: the type raises no error a visitor sees.</summary>
    None = 0,

    /// <summary><c>required</c>.</summary>
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
}

/// <summary>One property of <see cref="FieldMessages"/>: its name, the error kind it is the message of, and its reader.</summary>
/// <param name="Name">Its JSON name.</param>
/// <param name="Kind">The error kind it is the message of.</param>
/// <param name="Read">Reads it from a field's messages.</param>
internal sealed record FieldMessageProperty(string Name, FieldMessageKinds Kind, Func<FieldMessages, LocalizedText?> Read);

/// <summary>
/// The properties of <see cref="FieldMessages"/>, listed once for the save checks and the structural checks; a test
/// holds the list to the record and to <see cref="FieldMessageKinds"/>.
/// </summary>
internal static class FieldMessageProperties
{
    /// <summary>Every property, in the record's order.</summary>
    public static readonly ImmutableArray<FieldMessageProperty> All =
    [
        new("required", FieldMessageKinds.Required, messages => messages.Required),
        new("invalid", FieldMessageKinds.Invalid, messages => messages.Invalid),
        new("minLength", FieldMessageKinds.MinLength, messages => messages.MinLength),
        new("maxLength", FieldMessageKinds.MaxLength, messages => messages.MaxLength),
        new("minWords", FieldMessageKinds.MinWords, messages => messages.MinWords),
        new("maxWords", FieldMessageKinds.MaxWords, messages => messages.MaxWords),
        new("min", FieldMessageKinds.Min, messages => messages.Min),
        new("max", FieldMessageKinds.Max, messages => messages.Max),
        new("step", FieldMessageKinds.Step, messages => messages.Step),
        new("dateWindow", FieldMessageKinds.DateWindow, messages => messages.DateWindow),
        new("selections", FieldMessageKinds.Selections, messages => messages.Selections),
        new("fileType", FieldMessageKinds.FileType, messages => messages.FileType),
        new("fileSize", FieldMessageKinds.FileSize, messages => messages.FileSize),
        new("fileCount", FieldMessageKinds.FileCount, messages => messages.FileCount),
        new("full", FieldMessageKinds.Full, messages => messages.Full),
    ];
}

/// <summary>
/// How a field's values are stored, one of the kinds of section 4.3.2, as the field catalogue records it (ADR-0044).
/// Step 5.1 maps each kind to the typed columns of the entry values (ADR-0003).
/// </summary>
internal enum FieldStorageKind
{
    /// <summary>No values of its own: a grid or a repeatable group, whose parts hold them.</summary>
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

/// <summary>
/// What the structural validation, the public projection and the field catalogue need to know about a field type
/// (section 4.3.2, ADR-0035, ADR-0044): whether it holds a value, what rules compare it as, how its values are stored,
/// which common properties and error kinds it has, and which of its settings visitors may see. Step 5.1's registry
/// declares the same for every registered type.
/// </summary>
internal sealed record FieldTypeTraits
{
    /// <summary>The type's alias, for example <c>email</c>.</summary>
    public required string Alias { get; init; }

    /// <summary>
    /// Whether the type holds a value: it then has an alias and former aliases, and may be sensitive, left out of mail
    /// and kept only until the actions have run.
    /// </summary>
    public bool HoldsValue { get; init; }

    /// <summary>What rules compare the type's answers as; null when rules cannot compare them (a name, a repeatable group, a grid itself).</summary>
    public FieldKind? Kind { get; init; }

    /// <summary>
    /// Which column holds the type's values; <see cref="FieldStorageKind.None"/> for a type whose parts hold them (a
    /// grid's rows hold text) or that holds no value.
    /// </summary>
    public FieldStorageKind Storage { get; init; }

    /// <summary>Whether the setting <c>multiple: true</c> turns one choice into several (a dropdown; a grid's rows).</summary>
    public bool MultipleSetting { get; init; }

    /// <summary>The common properties the type supports.</summary>
    public FieldFeatures Features { get; init; }

    /// <summary>The error kinds the type raises.</summary>
    public FieldMessageKinds Messages { get; init; }

    /// <summary>Whether a form has at most one field of the type, never inside a repeatable group (the captcha placement).</summary>
    public bool OnePerForm { get; init; }

    /// <summary>
    /// Whether the default value stays on the server: it is resolved when the entry is submitted and never reaches the
    /// page (the hidden field, which may use member and context values, ADR-0014).
    /// </summary>
    public bool ServerOnlyDefault { get; init; }

    /// <summary>The settings visitors may see; every other setting stays on the server (ADR-0002, the public projection).</summary>
    public FrozenSet<string> PublicSettings { get; init; } = FrozenSet<string>.Empty;

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

/// <summary>The field types a site has, by alias; the built-in ones of section 4.3.2 until step 5.1 builds the registry.</summary>
internal sealed class FieldTypeCatalogue
{
    private const FieldFeatures Input = FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.Required | FieldFeatures.VisibilityRules;
    private const FieldFeatures TextInput = Input | FieldFeatures.Placeholder | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete
        | FieldFeatures.PatternCheck | FieldFeatures.MatchesFieldCheck | FieldFeatures.UniquePerFormCheck;

    private const FieldFeatures Comparable = FieldFeatures.MatchesFieldCheck | FieldFeatures.UniquePerFormCheck;
    private const FieldFeatures Choices = FieldFeatures.Options | FieldFeatures.NeedsOptions;

    private readonly FrozenDictionary<string, FieldTypeTraits> _types;

    /// <summary>A catalogue of the given types.</summary>
    /// <exception cref="ArgumentException">Two types have the same alias.</exception>
    public FieldTypeCatalogue(IEnumerable<FieldTypeTraits> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var byAlias = new Dictionary<string, FieldTypeTraits>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!byAlias.TryAdd(type.Alias, type))
            {
                throw new ArgumentException($"Two field types have the alias '{type.Alias}'.", nameof(types));
            }
        }

        _types = byAlias.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The built-in types of section 4.3.2.</summary>
    public static FieldTypeCatalogue BuiltIn { get; } = new(BuiltInTypes());

    /// <summary>Every type, by alias.</summary>
    public IEnumerable<FieldTypeTraits> All => _types.Values;

    /// <summary>The type with this alias, or null.</summary>
    public FieldTypeTraits? Find(string alias) => _types.GetValueOrDefault(alias);

    // The storage of a type follows what rules compare it as, unless the type says otherwise.
    private static FieldTypeTraits Value(string alias, FieldKind? kind, FieldFeatures features, FieldMessageKinds messages, params string[] settings) => new()
    {
        Alias = alias,
        HoldsValue = true,
        Kind = kind,
        Storage = kind switch
        {
            FieldKind.Number => FieldStorageKind.Number,
            FieldKind.Date or FieldKind.DateTime => FieldStorageKind.Date,
            FieldKind.Checkbox => FieldStorageKind.Bool,
            FieldKind.Files => FieldStorageKind.File,
            null => FieldStorageKind.None,
            _ => FieldStorageKind.Text,
        },
        Features = features,
        Messages = messages,
        PublicSettings = settings.ToFrozenSet(StringComparer.Ordinal),
    };

    private static FieldTypeTraits Content(string alias, params string[] settings) => new()
    {
        Alias = alias,
        Features = FieldFeatures.VisibilityRules,
        PublicSettings = settings.ToFrozenSet(StringComparer.Ordinal),
    };

    // Section 4.3.2 and ADR-0002's table of settings. The settings a visitor's browser needs are public; an email
    // field's allowed or blocked domains are the site's policy and stay on the server, which checks them.
    private static IEnumerable<FieldTypeTraits> BuiltInTypes()
    {
        const FieldMessageKinds Required = FieldMessageKinds.Required;
        const FieldMessageKinds Readable = FieldMessageKinds.Required | FieldMessageKinds.Invalid;
        const FieldMessageKinds Ranged = Readable | FieldMessageKinds.Min | FieldMessageKinds.Max | FieldMessageKinds.Step;

        yield return Value("text", FieldKind.Text, TextInput, Required | FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength, "minLength", "maxLength", "prefix", "suffix", "counter");
        yield return Value("textarea", FieldKind.Text, TextInput, Required | FieldMessageKinds.MinLength | FieldMessageKinds.MaxLength | FieldMessageKinds.MinWords | FieldMessageKinds.MaxWords, "rows", "autoGrow", "minLength", "maxLength", "minWords", "maxWords", "counter") with { Storage = FieldStorageKind.LongText };
        yield return Value("email", FieldKind.Text, TextInput, Readable, "confirm", "confirmLabel");
        yield return Value("phone", FieldKind.Text, TextInput, Readable, "defaultCountry");
        yield return Value("url", FieldKind.Text, TextInput, Readable, "schemes");
        yield return Value("number", FieldKind.Number, Input | FieldFeatures.Placeholder | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | Comparable, Ranged, "min", "max", "step", "decimals", "prefix", "suffix");
        yield return Value("date", FieldKind.Date, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | Comparable, Readable | FieldMessageKinds.DateWindow, "min", "max", "weekdaysOnly", "excluded", "defaultToday");
        yield return Value("time", FieldKind.Time, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | Comparable, Ranged, "min", "max", "stepMinutes");
        yield return Value("datetime", FieldKind.DateTime, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | Comparable, Readable | FieldMessageKinds.DateWindow | FieldMessageKinds.Step, "min", "max", "weekdaysOnly", "excluded", "defaultToday", "stepMinutes");
        yield return Value("rating", FieldKind.Number, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill, Readable, "count", "symbol", "lowLabel", "highLabel");
        yield return Value("scale", FieldKind.Number, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill, Readable, "from", "to", "lowLabel", "highLabel");
        yield return Value("slider", FieldKind.Number, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill, Ranged, "min", "max", "step", "unit", "showValue");
        yield return Value("calculation", FieldKind.Number, FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.VisibilityRules, FieldMessageKinds.None, "formula", "format", "visible");
        yield return Value("dropdown", FieldKind.Choice, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | Choices | FieldFeatures.Places, Readable | FieldMessageKinds.Selections | FieldMessageKinds.Full, "prompt", "multiple", "minSelections", "maxSelections") with { MultipleSetting = true };
        yield return Value("radio", FieldKind.Choice, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | Choices | FieldFeatures.Places, Readable | FieldMessageKinds.Full, "layout", "other");
        yield return Value("checkboxes", FieldKind.Choices, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | Choices | FieldFeatures.Places, Readable | FieldMessageKinds.Selections | FieldMessageKinds.Full, "layout", "minSelections", "maxSelections", "other");
        yield return Value("grid", null, Input | FieldFeatures.Options | FieldFeatures.Rows, Readable, "multiple", "scale") with { MultipleSetting = true };
        yield return Value("ranking", FieldKind.Choices, Input | Choices, Readable, "top");
        yield return Value("checkbox", FieldKind.Checkbox, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill, Required, "text");
        yield return Value("yesno", FieldKind.Choice, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill, Required, "yesLabel", "noLabel", "style");
        // A consent value stores the hash of the wording the visitor agreed to (ADR-0009).
        yield return Value("consent", FieldKind.Checkbox, Input, Required, "text") with { Storage = FieldStorageKind.Text };
        yield return Value("country", FieldKind.Choice, Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete, Readable, "allowed", "top", "default");
        yield return Value("name", null, Input | FieldFeatures.Prefill, Readable, "parts", "requiredParts") with { Storage = FieldStorageKind.Json };
        yield return Value("address", null, Input | FieldFeatures.Prefill, Readable, "parts", "requiredParts") with { Storage = FieldStorageKind.Json };
        yield return Value("file", FieldKind.Files, Input, Required | FieldMessageKinds.FileType | FieldMessageKinds.FileSize | FieldMessageKinds.FileCount, "types", "extensions", "maxBytes", "maxFiles", "dropZone", "thumbnails", "camera");
        yield return Value("signature", FieldKind.Files, Input, Readable, "width", "height");
        yield return Value("repeater", null, FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.VisibilityRules | FieldFeatures.ChildFields, FieldMessageKinds.None, "minRows", "maxRows", "addLabel", "removeLabel");
        yield return Value("hidden", FieldKind.Text, FieldFeatures.Label | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.VisibilityRules, FieldMessageKinds.None) with { ServerOnlyDefault = true };
        yield return Content("heading", "text", "level");
        yield return Content("paragraph", "html");
        yield return Content("divider");
        yield return Content("image", "media", "alt", "size");
        yield return new FieldTypeTraits { Alias = "captcha", OnePerForm = true };
    }
}
