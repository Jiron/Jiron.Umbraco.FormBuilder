namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// What a field's answers are compared as (ADR-0005). The one public part of the shared logic: a field type declares
/// it (ADR-0055), and the logic's engines stay internal until they get public interfaces.
/// </summary>
public enum FieldKind
{
    /// <summary>Any text: compared folded.</summary>
    Text,

    /// <summary>One option key: compared exactly.</summary>
    Choice,

    /// <summary>Several option keys: compared exactly.</summary>
    Choices,

    /// <summary>"true" when checked, no answer otherwise.</summary>
    Checkbox,

    /// <summary>Upload tokens: counted.</summary>
    Files,

    /// <summary>An exact decimal in the wire form.</summary>
    Number,

    /// <summary><c>YYYY-MM-DD</c>.</summary>
    Date,

    /// <summary><c>HH:mm(:ss)</c>.</summary>
    Time,

    /// <summary><c>YYYY-MM-DDTHH:mm(:ss)</c>, local wall-clock time.</summary>
    DateTime,
}

/// <summary>An operand: text, another answer, a date relative to today, a list, or a shape nothing accepts.</summary>
internal abstract record Operand;

/// <summary>A text operand, compared as the condition's kind compares.</summary>
internal sealed record TextOperand(string Value) : Operand;

/// <summary>Another field's answers ("departure after arrival").</summary>
internal sealed record FieldOperand(string Field) : Operand;

/// <summary>A date relative to today, such as "+1m+1d".</summary>
internal sealed record TodayOperand(string Expression) : Operand;

/// <summary>A list of operands, for <c>isOneOf</c>, <c>isNoneOf</c> and <c>between</c>.</summary>
internal sealed record ListOperand(IReadOnlyList<Operand> Items) : Operand;

/// <summary>An operand that could not be read (wrong type or shape); checking refuses it as <c>invalid_operand</c>.</summary>
internal sealed record InvalidOperand : Operand;

/// <summary>A condition or a nested group.</summary>
internal abstract record LogicItem;

/// <summary>Looks at an answer (<see cref="Field"/>) or at the context (<see cref="Context"/>): exactly one of them.</summary>
internal sealed record LogicCondition(string? Field, string? Context, string Operator, Operand? Value) : LogicItem;

/// <summary>Conditions that must all match (<c>all</c>) or of which one must (<c>any</c>).</summary>
internal sealed record LogicGroup(string Match, IReadOnlyList<LogicItem> Conditions) : LogicItem;

/// <summary>A rule: its effect applies while its conditions match; a switched-off rule is kept but ignored.</summary>
internal sealed record LogicRule(string Effect, bool Enabled, string Match, IReadOnlyList<LogicItem> Conditions);

/// <summary>The member a condition can look at.</summary>
internal sealed record Member(bool SignedIn, IReadOnlyList<string> Groups);

/// <summary>The context of an evaluation.</summary>
/// <param name="Now">The instant, ISO 8601 with Z or an offset (the server injects its TimeProvider's).</param>
/// <param name="TimeZone">The site's IANA time zone (the TimeZone setting).</param>
/// <param name="Culture">The visitor's culture.</param>
/// <param name="Member">The signed-in member, if any.</param>
/// <param name="Query">The allowed query values of the page.</param>
/// <param name="Hidden">Aliases of the fields a rule currently hides: they count as empty. The form engine decides this itself.</param>
internal sealed record EvaluationContext(
    string Now,
    string TimeZone,
    string Culture,
    Member Member,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Query,
    IReadOnlyList<string> Hidden);

/// <summary>What a condition is evaluated against: the form's fields, the answers and the context.</summary>
internal sealed record Subject(
    IReadOnlyDictionary<string, FieldKind> Fields,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    EvaluationContext Context)
{
    /// <summary>
    /// Where the form engine finds a field's texts as given (null for a hidden field), in place of
    /// <see cref="Values"/> and the context's hidden list; it gives each row of a repeatable group a lookup of its own
    /// (ADR-0061).
    /// </summary>
    public Func<string, IReadOnlyList<string>?>? Lookup { get; init; }
}

/// <summary>A grid's row: a subject of conditions, shown with its grid.</summary>
internal sealed record LogicGridRow(string Id, FieldKind Kind);

/// <summary>A field; one with child fields is a repeatable group, whose child fields have one answer per row.</summary>
/// <param name="Id">The field's id.</param>
/// <param name="Kind">The kind its answers compare as; null for a field that holds no value.</param>
/// <param name="Required">Whether the field is required of itself, whatever its rules say.</param>
/// <param name="Rules">Its rules.</param>
/// <param name="Fields">A repeatable group's child fields.</param>
/// <param name="Rows">A grid's rows.</param>
internal sealed record LogicField(string Id, FieldKind? Kind, bool Required, IReadOnlyList<LogicRule> Rules, IReadOnlyList<LogicField> Fields, IReadOnlyList<LogicGridRow> Rows);

/// <summary>A group of fields on a page (the conditions' own groups are <see cref="LogicGroup"/>).</summary>
internal sealed record LogicFormGroup(string Id, IReadOnlyList<LogicRule> Rules, IReadOnlyList<LogicField> Fields);

/// <summary>A page.</summary>
internal sealed record LogicPage(string Id, IReadOnlyList<LogicRule> Rules, IReadOnlyList<LogicFormGroup> Groups);

/// <summary>A form-level validation rule: while it matches, its field (a field, child field or grid row) shows its message.</summary>
internal sealed record LogicValidationRule(string Id, string Field, bool Enabled, string Match, IReadOnlyList<LogicItem> Conditions);

/// <summary>An action, which runs unless its <c>run</c> rule does not match.</summary>
internal sealed record LogicAction(string Id, IReadOnlyList<LogicRule> Rules);

/// <summary>An outcome: the first whose <c>use</c> rule matches, or that has none, is used.</summary>
internal sealed record LogicOutcome(string Id, IReadOnlyList<LogicRule> Rules);

/// <summary>A form as its rules see it (ADR-0061), the same in TypeScript.</summary>
internal sealed record LogicForm(IReadOnlyList<LogicPage> Pages, IReadOnlyList<LogicValidationRule> ValidationRules, IReadOnlyList<LogicAction> Actions, IReadOnlyList<LogicOutcome> Outcomes);

/// <summary>The answers: each field's and grid row's texts by id, and each repeatable group's rows of its child fields' texts.</summary>
internal sealed record FormAnswers(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,
    IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>> Rows);

/// <summary>One row of a repeatable group: its child fields that are hidden or required, and its messages.</summary>
internal sealed record RowState(IReadOnlyList<string> Hidden, IReadOnlyList<string> Required, IReadOnlyDictionary<string, IReadOnlyList<string>> Messages);

/// <summary>What a form's rules decide, every list in document order.</summary>
/// <param name="Hidden">The pages, groups and fields (outside repeatable groups) that are hidden.</param>
/// <param name="Required">The shown fields that are required.</param>
/// <param name="Messages">The ids of the validation rules that match, by the shown field that shows them.</param>
/// <param name="Rows">Every repeatable group's rows.</param>
/// <param name="Actions">The actions that run.</param>
/// <param name="Outcome">The outcome used, or null.</param>
internal sealed record FormState(
    IReadOnlyList<string> Hidden,
    IReadOnlyList<string> Required,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Messages,
    IReadOnlyDictionary<string, IReadOnlyList<RowState>> Rows,
    IReadOnlyList<string> Actions,
    string? Outcome);

/// <summary>A calculation: a decimal constant, or an operation over calculations.</summary>
internal abstract record Calculation;

/// <summary>A decimal constant in the wire form.</summary>
internal sealed record Constant(string Text) : Calculation;

/// <summary>An operation; <see cref="Scale"/> is round's number of places, null when absent or not an integer.</summary>
internal sealed record Operation(string Op, IReadOnlyList<Calculation> Args, int? Scale) : Calculation;
