using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// Conditions and rules (ADR-0005), the same in TypeScript: checked as a whole first (unknown fields and operators,
/// operands, patterns, groups nested too deeply), then evaluated. Checking first means both runtimes report the same
/// error whatever order they evaluate in. The form engine (<see cref="FormRules"/>, ADR-0061) evaluates a whole form's
/// rules on these.
/// </summary>
internal static class Rules
{
    /// <summary>How deeply groups of conditions nest, the rule's own list being the first level (ADR-0061).</summary>
    public const int MaxDepth = 10;

    private static readonly ImmutableArray<FieldKind> All = [.. Enum.GetValues<FieldKind>()];
    private static readonly ImmutableArray<FieldKind> TextKinds = [FieldKind.Text];
    private static readonly ImmutableArray<FieldKind> Ordered = [FieldKind.Number, FieldKind.Date, FieldKind.Time, FieldKind.DateTime];
    private static readonly ImmutableArray<FieldKind> Checkable = [FieldKind.Checkbox, FieldKind.Choice, FieldKind.Choices];

    private static readonly FrozenDictionary<string, (ImmutableArray<FieldKind> Kinds, OperandShape Operand)> Operators = new Dictionary<string, (ImmutableArray<FieldKind> Kinds, OperandShape Operand)>
    {
        ["equals"] = (All, OperandShape.One),
        ["notEquals"] = (All, OperandShape.One),
        ["isOneOf"] = (All, OperandShape.List),
        ["isNoneOf"] = (All, OperandShape.List),
        ["isEmpty"] = (All, OperandShape.None),
        ["isNotEmpty"] = (All, OperandShape.None),
        ["countAtLeast"] = (All, OperandShape.Count),
        ["countAtMost"] = (All, OperandShape.Count),
        ["contains"] = (TextKinds, OperandShape.One),
        ["notContains"] = (TextKinds, OperandShape.One),
        ["startsWith"] = (TextKinds, OperandShape.One),
        ["endsWith"] = (TextKinds, OperandShape.One),
        ["matches"] = (TextKinds, OperandShape.Pattern),
        ["greaterThan"] = (Ordered, OperandShape.One),
        ["lessThan"] = (Ordered, OperandShape.One),
        ["between"] = (Ordered, OperandShape.Pair),
        ["isChecked"] = (Checkable, OperandShape.Checked),
        ["isNotChecked"] = (Checkable, OperandShape.Checked),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private enum OperandShape
    {
        None,
        One,
        List,
        Pair,
        Count,
        Pattern,
        Checked,
    }

    /// <summary>The operators a condition on a field of the kind takes, in ordinal order (ADR-0059).</summary>
    public static ImmutableArray<string> OperatorsFor(FieldKind kind) =>
        [.. Operators.Where(op => op.Value.Kinds.Contains(kind)).Select(op => op.Key).Order(StringComparer.Ordinal)];

    /// <summary>Checks the condition, then evaluates it.</summary>
    public static bool EvaluateCondition(LogicCondition condition, Subject subject)
    {
        var patterns = new Dictionary<string, SafePattern>(StringComparer.Ordinal);
        CheckCondition(condition, subject.Fields, patterns);
        return Evaluate(condition, subject, patterns);
    }

    /// <summary>Checks the whole rule, then evaluates it: null when the rule is switched off.</summary>
    public static bool? EvaluateRule(LogicRule rule, Subject subject)
    {
        var group = new LogicGroup(rule.Match, rule.Conditions);
        var patterns = new Dictionary<string, SafePattern>(StringComparer.Ordinal);
        CheckGroup(group, subject.Fields, patterns);
        return rule.Enabled ? MatchesGroup(group, subject, patterns) : null;
    }

    /// <summary>
    /// The check both runtimes run before they evaluate a condition, on the form's fields alone: null when the
    /// condition can be evaluated, otherwise the code of the first problem (publishing refuses it, ADR-0035).
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="fields">The fields that hold a value rules can compare, with the kind each compares as.</param>
    public static string? Check(LogicCondition condition, IReadOnlyDictionary<string, FieldKind> fields)
    {
        try
        {
            CheckCondition(condition, fields, patterns: null);
            return null;
        }
        catch (LogicException exception)
        {
            return exception.Code;
        }
    }

    /// <summary>Context subjects: what a condition can look at besides answers, each with the kind it compares as.</summary>
    private static FieldKind ContextKind(string name) => name switch
    {
        "culture" => FieldKind.Text,
        "memberSignedIn" => FieldKind.Checkbox,
        "memberGroup" => FieldKind.Choices,
        "today" => FieldKind.Date,
        _ when name.StartsWith("query.", StringComparison.Ordinal) => FieldKind.Text,
        _ => throw new LogicException(LogicErrors.UnknownField),
    };

    private static FieldKind SubjectKind(LogicCondition condition, IReadOnlyDictionary<string, FieldKind> fields)
    {
        if ((condition.Field is null) == (condition.Context is null))
        {
            throw new LogicException(LogicErrors.InvalidCondition);
        }

        if (condition.Context is { } context)
        {
            return ContextKind(context);
        }

        return fields.TryGetValue(condition.Field!, out var kind)
            ? kind
            : throw new LogicException(LogicErrors.UnknownField);
    }

    /// <summary>A single operand: text, another answer, or a date relative to today.</summary>
    private static void CheckOne(Operand? operand, IReadOnlyDictionary<string, FieldKind> fields)
    {
        switch (operand)
        {
            case TextOperand:
                return;
            case FieldOperand field:
                if (!fields.ContainsKey(field.Field))
                {
                    throw new LogicException(LogicErrors.UnknownField);
                }

                return;
            case TodayOperand today when CalendarValues.ParseRelative(today.Expression) is not null:
                return;
            default:
                throw new LogicException(LogicErrors.InvalidOperand);
        }
    }

    // Checks a condition; a pattern it compiles goes into patterns, when given, for the evaluation that follows.
    private static void CheckCondition(LogicCondition condition, IReadOnlyDictionary<string, FieldKind> fields, Dictionary<string, SafePattern>? patterns)
    {
        var kind = SubjectKind(condition, fields);
        if (!Operators.TryGetValue(condition.Operator, out var op))
        {
            throw new LogicException(LogicErrors.UnknownOperator);
        }

        if (!op.Kinds.Contains(kind))
        {
            throw new LogicException(LogicErrors.OperatorNotForKind);
        }

        var value = condition.Value;
        switch (op.Operand)
        {
            case OperandShape.None:
                if (value is not null)
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                return;
            case OperandShape.One:
                CheckOne(value, fields);
                return;
            case OperandShape.List:
                if (value is ListOperand list)
                {
                    foreach (var item in list.Items)
                    {
                        CheckOne(item, fields);
                    }
                }
                else if (value is FieldOperand)
                {
                    CheckOne(value, fields);
                }
                else
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                return;
            case OperandShape.Pair:
                if (value is not ListOperand { Items.Count: 2 } pair)
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                foreach (var item in pair.Items)
                {
                    CheckOne(item, fields);
                }

                return;
            case OperandShape.Count:
                if (value is not TextOperand { Value: var count } || !IsCount(count))
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                return;
            case OperandShape.Pattern:
                if (value is not TextOperand { Value: var pattern })
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                // A pattern the evaluation already holds is not compiled again.
                if (patterns is null)
                {
                    SafePattern.Compile(pattern);
                }
                else if (!patterns.ContainsKey(pattern))
                {
                    patterns[pattern] = SafePattern.Compile(pattern);
                }

                return;
            case OperandShape.Checked:
                // A checkbox is checked or not; a choice field names the option.
                if (kind == FieldKind.Checkbox ? value is not null : value is not TextOperand)
                {
                    throw new LogicException(LogicErrors.InvalidOperand);
                }

                return;
        }
    }

    /// <summary><c>0</c>, or 1 to 6 digits without a leading zero.</summary>
    private static bool IsCount(string text) =>
        text.Length is >= 1 and <= 6 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0');

    /// <summary>
    /// Checks a group of conditions and every group nested in it, at its depth (the rule's own list is the first level);
    /// the patterns it compiles go into patterns, for the evaluations that follow.
    /// </summary>
    public static void CheckGroup(LogicGroup group, IReadOnlyDictionary<string, FieldKind> fields, Dictionary<string, SafePattern> patterns, int depth = 1)
    {
        if (depth > MaxDepth)
        {
            throw new LogicException(LogicErrors.TooDeep);
        }

        if (group.Match is not ("all" or "any"))
        {
            throw new LogicException(LogicErrors.InvalidCondition);
        }

        foreach (var item in group.Conditions)
        {
            if (item is LogicGroup nested)
            {
                CheckGroup(nested, fields, patterns, depth + 1);
            }
            else
            {
                CheckCondition((LogicCondition)item, fields, patterns);
            }
        }
    }

    /// <summary>Cleaned answers that are not empty. A hidden field has none.</summary>
    private static List<string> Answers(string alias, Subject subject)
    {
        if (subject.Lookup is { } lookup)
        {
            return lookup(alias) is { } given ? NonEmpty(given) : [];
        }

        if (subject.Context.Hidden.Contains(alias, StringComparer.Ordinal))
        {
            return [];
        }

        return subject.Values.TryGetValue(alias, out var values) ? NonEmpty(values) : [];
    }

    private static List<string> NonEmpty(IEnumerable<string> values) =>
        values.Select(TextSemantics.Clean).Where(value => value.Length > 0).ToList();

    private static List<string> ContextValues(string name, EvaluationContext context)
    {
        IEnumerable<string> raw = name switch
        {
            "culture" => [context.Culture],
            "memberSignedIn" => context.Member.SignedIn ? ["true"] : [],
            "memberGroup" => context.Member.Groups,
            "today" => [CalendarValues.FormatDate(CalendarValues.Today(context.Now, context.TimeZone))],
            _ => context.Query.TryGetValue(name["query.".Length..], out var values) ? values : [],
        };
        return NonEmpty(raw);
    }

    private static List<string> SubjectValues(LogicCondition condition, Subject subject) =>
        condition.Context is { } context ? ContextValues(context, subject.Context) : Answers(condition.Field!, subject);

    /// <summary>An operand's values, cleaned. A relative date outside years 1 to 9999 has none.</summary>
    private static List<string> Resolve(Operand operand, Subject subject)
    {
        switch (operand)
        {
            case TextOperand text:
                return [TextSemantics.Clean(text.Value)];
            case ListOperand list:
                return list.Items.SelectMany(item => Resolve(item, subject)).ToList();
            case FieldOperand field:
                return Answers(field.Field, subject);
            case TodayOperand today:
                var day = CalendarValues.ApplyRelative(
                    CalendarValues.Today(subject.Context.Now, subject.Context.TimeZone),
                    CalendarValues.ParseRelative(today.Expression)!);
                return day is { } d ? [CalendarValues.FormatDate(d)] : [];
            default:
                throw new LogicException(LogicErrors.InvalidOperand);
        }
    }

    /// <summary>The key two values other than numbers are equal by: folded text, an exact option key, or the parsed value; null when invalid.</summary>
    private static string? Key(FieldKind kind, string value) => kind switch
    {
        FieldKind.Text => TextSemantics.Fold(value),
        FieldKind.Date => CalendarValues.ParseDate(value)?.ToString(CultureInfo.InvariantCulture),
        FieldKind.Time => CalendarValues.ParseTime(value)?.ToString(CultureInfo.InvariantCulture),
        FieldKind.DateTime => CalendarValues.ParseDateTime(value)?.ToString(CultureInfo.InvariantCulture),
        _ => value,
    };

    /// <summary>Orders two values of an ordered kind; null when either is not a valid value of that kind.</summary>
    private static int? Order(FieldKind kind, string a, string b)
    {
        if (kind == FieldKind.Number)
        {
            return ExactDecimal.CompareText(a, b);
        }

        Func<string, long?> read = kind switch
        {
            FieldKind.Date => text => CalendarValues.ParseDate(text),
            FieldKind.Time => text => CalendarValues.ParseTime(text),
            _ => CalendarValues.ParseDateTime,
        };
        return read(a) is { } p && read(b) is { } q ? Math.Sign(p - q) : null;
    }

    private static bool CompareFirst(FieldKind kind, List<string> values, List<string> operands, Func<int, bool> test) =>
        values.Count > 0 && operands.Count > 0 && Order(kind, values[0], operands[0]) is { } result && test(result);

    private static bool IsChecked(LogicCondition condition, FieldKind kind, List<string> values)
    {
        var expected = kind == FieldKind.Checkbox ? "true" : TextSemantics.Clean(((TextOperand)condition.Value!).Value);
        return values.Contains(expected, StringComparer.Ordinal);
    }

    // Evaluates a checked condition, with the patterns its check compiled.
    private static bool Evaluate(LogicCondition condition, Subject subject, Dictionary<string, SafePattern> patterns)
    {
        var kind = SubjectKind(condition, subject.Fields);
        var values = SubjectValues(condition, subject);
        var value = condition.Value;
        List<string> Operands() => Resolve(value!, subject);

        // Each value and each operand becomes its key once, however many of the others it meets. Numbers compare as
        // the values they are, digit by digit, without a number or a key being built (ADR-0005).
        bool AnyEqual()
        {
            var list = Operands();
            if (kind == FieldKind.Number)
            {
                return values.Exists(v => list.Exists(o => ExactDecimal.CompareText(v, o) == 0));
            }

            var keys = list.ConvertAll(o => Key(kind, o));
            return values.Exists(v => Key(kind, v) is { } key && keys.Contains(key, StringComparer.Ordinal));
        }

        // Each value and each operand is folded once.
        bool AnyText(Func<string, string, bool> test)
        {
            var list = Operands().ConvertAll(TextSemantics.Fold);
            return values.ConvertAll(TextSemantics.Fold).Exists(v => list.Exists(o => test(v, o)));
        }

        switch (condition.Operator)
        {
            case "equals" or "isOneOf":
                return AnyEqual();
            case "notEquals" or "isNoneOf":
                return !AnyEqual();
            case "isEmpty":
                return values.Count == 0;
            case "isNotEmpty":
                return values.Count > 0;
            case "countAtLeast":
                return values.Count >= Count(value);
            case "countAtMost":
                return values.Count <= Count(value);
            case "contains":
                return AnyText((v, o) => v.Contains(o, StringComparison.Ordinal));
            case "notContains":
                return !AnyText((v, o) => v.Contains(o, StringComparison.Ordinal));
            case "startsWith":
                return AnyText((v, o) => v.StartsWith(o, StringComparison.Ordinal));
            case "endsWith":
                return AnyText((v, o) => v.EndsWith(o, StringComparison.Ordinal));
            case "matches":
                var pattern = patterns[((TextOperand)value!).Value];
                return values.Exists(pattern.IsMatch);
            case "greaterThan":
                return CompareFirst(kind, values, Operands(), n => n > 0);
            case "lessThan":
                return CompareFirst(kind, values, Operands(), n => n < 0);
            case "between":
                var pair = ((ListOperand)value!).Items;
                return CompareFirst(kind, values, Resolve(pair[0], subject), n => n >= 0)
                    && CompareFirst(kind, values, Resolve(pair[1], subject), n => n <= 0);
            case "isChecked":
                return IsChecked(condition, kind, values);
            case "isNotChecked":
                return !IsChecked(condition, kind, values);
            default:
                throw new LogicException(LogicErrors.UnknownOperator);
        }
    }

    private static int Count(Operand? value) =>
        int.Parse(((TextOperand)value!).Value, NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>Evaluates a checked group of conditions: all of them, or one, with the patterns its check compiled.</summary>
    public static bool MatchesGroup(LogicGroup group, Subject subject, Dictionary<string, SafePattern> patterns)
    {
        bool Test(LogicItem item) => item is LogicGroup nested ? MatchesGroup(nested, subject, patterns) : Evaluate((LogicCondition)item, subject, patterns);
        return group.Match == "all" ? group.Conditions.All(Test) : group.Conditions.Any(Test);
    }
}
