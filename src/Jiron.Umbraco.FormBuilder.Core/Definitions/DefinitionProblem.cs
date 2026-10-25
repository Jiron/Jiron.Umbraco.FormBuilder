using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// A problem with a definition: a stable code (<see cref="DefinitionErrors"/>), the JSON path of the part it concerns
/// (<c>$.pages[0].groups[1].fields[2].label['de']</c>) and a message for developers. The backoffice shows its own text
/// for each code.
/// </summary>
/// <param name="Code">The stable code.</param>
/// <param name="Path">The JSON path, <c>$</c> for the whole document.</param>
/// <param name="Message">What is wrong, in English, for logs and developers.</param>
internal sealed record DefinitionProblem(string Code, string Path, string Message);

/// <summary>
/// The problems one read or check collects, in the order found, at most <see cref="Max"/>: past that they would only
/// repeat themselves (ADR-0034). The shape reader, the save checks, the structural checks and the export file's reader
/// all collect through it, so every one of them stops at the same limit.
/// </summary>
internal sealed class DefinitionProblems
{
    /// <summary>The most problems one read or check reports.</summary>
    public const int Max = 100;

    private readonly List<DefinitionProblem> _kept = [];

    /// <summary>How many problems were found, those past the limit included; a step compares it before and after.</summary>
    public int Found { get; private set; }

    /// <summary>Whether the limit is reached, so a loop over many items can stop.</summary>
    public bool IsFull => _kept.Count == Max;

    /// <summary>Records a problem. One past the limit is counted in <see cref="Found"/> but not kept.</summary>
    public void Add(DefinitionProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        Found++;
        if (!IsFull)
        {
            _kept.Add(problem);
        }
    }

    /// <summary>Records a problem with its code, path and message.</summary>
    public void Add(string code, string path, string message) => Add(new DefinitionProblem(code, path, message));

    /// <summary>The problems kept, as a list nobody can change.</summary>
    public IReadOnlyList<DefinitionProblem> ToList() => DefinitionList.Of(_kept);
}

/// <summary>
/// The codes with which a save refuses a definition it cannot read (ADR-0002, ADR-0034), and those with which
/// publishing refuses one whose parts do not fit together (the structural validation, ADR-0035).
/// </summary>
internal static class DefinitionErrors
{
    /// <summary>The text is not JSON, or not valid UTF-8.</summary>
    public const string InvalidJson = "invalid_json";

    /// <summary>An object has a property the model does not have.</summary>
    public const string UnknownProperty = "unknown_property";

    /// <summary>A required property is missing.</summary>
    public const string MissingProperty = "missing_property";

    /// <summary>An object names a property twice.</summary>
    public const string DuplicateProperty = "duplicate_property";

    /// <summary>A value has the wrong JSON type, for example a number where a string belongs.</summary>
    public const string InvalidType = "invalid_type";

    /// <summary>A value has the right type but is not allowed: an unknown word, a malformed key, a number out of range.</summary>
    public const string InvalidValue = "invalid_value";

    /// <summary>Two elements of the form have the same key.</summary>
    public const string DuplicateKey = "duplicate_key";

    /// <summary>The document's schema version is newer than this package reads, or older than any upgrader.</summary>
    public const string UnsupportedSchemaVersion = "unsupported_schema_version";

    /// <summary>An export file is of a format this package does not read, such as one a newer version wrote (ADR-0046).</summary>
    public const string UnsupportedFormat = "unsupported_format";

    /// <summary>The document, a text or a list is larger than its limit.</summary>
    public const string LimitExceeded = "limit_exceeded";

    // Publishing (ADR-0035). A draft keeps all of these, and the form check lists them.

    /// <summary>A field's type is not one the site has.</summary>
    public const string UnknownType = "unknown_type";

    /// <summary>A property its field type (or its outcome's kind) does not support holds a value other than its default.</summary>
    public const string PropertyNotSupported = "property_not_supported";

    /// <summary>A part the element cannot do without is missing: a page, a group, a choice field's options, a grid's rows.</summary>
    public const string PartMissing = "part_missing";

    /// <summary>A field type where it may not be: a repeatable group in a repeatable group, a second captcha placement.</summary>
    public const string FieldNotAllowed = "field_not_allowed";

    /// <summary>A field that holds a value has no alias, or an alias that is not <c>^[a-z][a-zA-Z0-9]{0,63}$</c>.</summary>
    public const string AliasInvalid = "alias_invalid";

    /// <summary>An alias is one of the reserved names of section 4.3.1, in any letter case.</summary>
    public const string AliasReserved = "alias_reserved";

    /// <summary>An alias or former alias is already used in the form, in any letter case.</summary>
    public const string AliasDuplicate = "alias_duplicate";

    /// <summary>An allowed query value or a context value has a name that merge tags cannot write.</summary>
    public const string NameInvalid = "name_invalid";

    /// <summary>An allowed query value or a context value is named twice, in any letter case.</summary>
    public const string NameDuplicate = "name_duplicate";

    /// <summary>An option's value changes when it is cleaned as answers are (ADR-0005), so no answer could ever match it.</summary>
    public const string OptionValueInvalid = "option_value_invalid";

    /// <summary>Two options of a field have values that are equal once cleaned and folded as rules compare text (ADR-0005).</summary>
    public const string OptionValueDuplicate = "option_value_duplicate";

    /// <summary>A field's options hold what their source does not use: own items with a library list, and so on.</summary>
    public const string OptionsInvalid = "options_invalid";

    /// <summary>A reference names no field that holds a value, or a condition names a context the form does not know.</summary>
    public const string UnknownField = LogicErrors.UnknownField;

    /// <summary>A reference names the wrong kind of thing: the field itself, a field of another type, an option the parent lacks.</summary>
    public const string InvalidReference = "invalid_reference";

    /// <summary>A condition names both a field and a context, or neither.</summary>
    public const string InvalidCondition = LogicErrors.InvalidCondition;

    /// <summary>A condition's operator does not exist.</summary>
    public const string UnknownOperator = LogicErrors.UnknownOperator;

    /// <summary>A condition's operator does not take the kind of the field or context it looks at.</summary>
    public const string OperatorNotForKind = LogicErrors.OperatorNotForKind;

    /// <summary>A condition's operand is missing, superfluous or of the wrong shape.</summary>
    public const string InvalidOperand = LogicErrors.InvalidOperand;

    /// <summary>A pattern uses syntax outside the safe subset or exceeds its limits (ADR-0005).</summary>
    public const string UnsafePattern = LogicErrors.UnsafePattern;

    /// <summary>A rule's effect is not one its element takes.</summary>
    public const string RuleEffectInvalid = "rule_effect_invalid";

    /// <summary>An element has two rules with one effect, or both a <c>show</c> and a <c>hide</c> rule.</summary>
    public const string RuleDuplicate = "rule_duplicate";

    /// <summary>Whether an element is shown depends, through rules and what contains it, on itself; or option parents go round.</summary>
    public const string Cycle = "cycle";

    /// <summary>An outcome's redirect is not a path on the site or an https address the editor chose.</summary>
    public const string InvalidRedirect = "invalid_redirect";
}
