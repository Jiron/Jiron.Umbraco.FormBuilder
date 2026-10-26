using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// A definition in the shared logic's terms (ADR-0061): the form engine's model, and the conditions that the publish
/// checks hand to the shared check, so publishing checks what the engine evaluates.
/// </summary>
internal static class DefinitionLogic
{
    /// <summary>
    /// The form as its rules see it: the ids are the keys in the <c>D</c> format, a field's kind is its type's
    /// (ADR-0055), a grid's rows are subjects of conditions, and a switched-off action is left out, as it never runs.
    /// A field whose type the site lacks holds no value here. Every field keeps its alias, and a calculation carries its
    /// formula and places (ADR-0063).
    /// </summary>
    public static LogicForm From(FormDefinition definition, FieldTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        LogicField Field(FormField field)
        {
            var traits = registry.Find(field.Type);
            var rows = traits is not null && traits.Supports(FieldFeatures.Rows)
                ? field.Rows.Select(row => new LogicGridRow(Id(row.Key), traits.RowKindOf(field))).ToList()
                : [];
            return new LogicField(Id(field.Key), traits?.KindOf(field), field.Required, Rules(field.Rules), [.. field.Fields.Select(Field)], rows, field.Alias, Calculation(field, traits));
        }

        return new LogicForm(
            [.. definition.Pages.Select(page => new LogicPage(
                Id(page.Key),
                Rules(page.Rules),
                [.. page.Groups.Select(group => new LogicFormGroup(Id(group.Key), Rules(group.Rules), [.. group.Fields.Select(Field)]))]))],
            [.. definition.ValidationRules.Select(rule => new LogicValidationRule(Id(rule.Key), Id(rule.Field), rule.Enabled, Match(rule.Match), Items(rule.Conditions)))],
            [.. definition.Actions.Where(action => action.Enabled).Select(action => new LogicAction(Id(action.Key), Rules(action.Rules)))],
            [.. definition.Outcomes.Select(outcome => new LogicOutcome(Id(outcome.Key), Rules(outcome.Rules)))]);
    }

    /// <summary>
    /// A calculation field's formula and the places of its result; null for a field of another type. Settings that do
    /// not fit their class, which publishing refuses, give an empty formula, which the form engine refuses too.
    /// </summary>
    public static LogicCalculation? Calculation(FormField field, FieldTypeTraits? traits)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (traits?.Type is not CalculationFieldType type)
        {
            return null;
        }

        try
        {
            var settings = type.ReadSettings(field.Settings);
            return new LogicCalculation(settings.Formula ?? string.Empty, settings.Format?.Decimals);
        }
        catch (JsonException)
        {
            return new LogicCalculation(string.Empty, null);
        }
    }

    /// <summary>A key as the shared logic names it.</summary>
    public static string Id(Guid key) => key.ToString("D");

    /// <summary>An effect as the shared logic names it.</summary>
    public static string Effect(RuleEffect effect) => effect switch
    {
        RuleEffect.Show => "show",
        RuleEffect.Hide => "hide",
        RuleEffect.Require => "require",
        RuleEffect.Run => "run",
        _ => "use",
    };

    /// <summary>A condition as the shared logic reads it.</summary>
    public static LogicCondition Condition(RuleCondition condition) =>
        new(condition.Field is { } field ? Id(field) : null, condition.Context, condition.Operator, condition.Value is { } value ? Operand(value) : null);

    private static string Match(RuleMatch match) => match == RuleMatch.Any ? "any" : "all";

    private static List<LogicRule> Rules(IReadOnlyList<FormRule> rules) =>
        [.. rules.Select(rule => new LogicRule(Effect(rule.Effect), rule.Enabled, Match(rule.Match), Items(rule.Conditions)))];

    private static List<LogicItem> Items(IReadOnlyList<RuleItem> items) =>
        [.. items.Select(item => item switch
        {
            RuleGroup group => new LogicGroup(Match(group.Match), Items(group.Conditions)),
            RuleCondition condition => (LogicItem)Condition(condition),
            _ => throw new ArgumentOutOfRangeException(nameof(items), item, "A rule item is a condition or a group of conditions."),
        })];

    private static Operand Operand(ConditionOperand operand) => operand switch
    {
        ConditionOperand.Text text => new TextOperand(text.Value),
        ConditionOperand.Field field => new FieldOperand(Id(field.Key)),
        ConditionOperand.Today today => new TodayOperand(today.Offset),
        ConditionOperand.List list => new ListOperand([.. list.Items.Select(Operand)]),
        _ => new InvalidOperand(),
    };
}
