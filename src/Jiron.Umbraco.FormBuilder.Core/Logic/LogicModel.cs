namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>What a field's answers are compared as (ADR-0005).</summary>
internal enum FieldKind
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
internal abstract record RuleItem;

/// <summary>Looks at an answer (<see cref="Field"/>) or at the context (<see cref="Context"/>): exactly one of them.</summary>
internal sealed record Condition(string? Field, string? Context, string Op, Operand? Value) : RuleItem;

/// <summary>Conditions that must all match (<c>all</c>) or of which one must (<c>any</c>).</summary>
internal sealed record Group(string Match, IReadOnlyList<RuleItem> Conditions) : RuleItem;

/// <summary>A rule: its effect applies while its conditions match; a switched-off rule is kept but ignored.</summary>
internal sealed record Rule(string Effect, bool Enabled, string Match, IReadOnlyList<RuleItem> Conditions);

/// <summary>The member a condition can look at.</summary>
internal sealed record Member(bool SignedIn, IReadOnlyList<string> Groups);

/// <summary>The context of an evaluation.</summary>
/// <param name="Now">The instant, ISO 8601 with Z or an offset (the server injects its TimeProvider's).</param>
/// <param name="TimeZone">The site's IANA time zone (the TimeZone setting).</param>
/// <param name="Culture">The visitor's culture.</param>
/// <param name="Member">The signed-in member, if any.</param>
/// <param name="Query">The allowed query values of the page.</param>
/// <param name="Hidden">Aliases of the fields a rule currently hides: they count as empty.</param>
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
    EvaluationContext Context);

/// <summary>A calculation: a decimal constant, or an operation over calculations.</summary>
internal abstract record Calculation;

/// <summary>A decimal constant in the wire form.</summary>
internal sealed record Constant(string Text) : Calculation;

/// <summary>An operation; <see cref="Scale"/> is round's number of places, null when absent or not an integer.</summary>
internal sealed record Operation(string Op, IReadOnlyList<Calculation> Args, int? Scale) : Calculation;
