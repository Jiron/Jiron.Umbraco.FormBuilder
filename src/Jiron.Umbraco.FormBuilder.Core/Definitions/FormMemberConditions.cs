namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// What a form's rules and validation rules ask of the member (section 4.3.3): whether any looks at the member at all, and which groups they
/// name. The challenge answers with no more than this, because cached markup holds nothing per visitor (ADR-0012).
/// </summary>
/// <param name="Any">Whether a rule looks at the member.</param>
/// <param name="Groups">The groups the rules name, as they name them.</param>
internal sealed record FormMemberConditions(bool Any, IReadOnlySet<string> Groups)
{
    private const string SignedIn = "memberSignedIn";
    private const string Group = "memberGroup";

    /// <summary>What the rules of a form's pages, groups, fields and their children ask.</summary>
    /// <param name="definition">The form.</param>
    public static FormMemberConditions Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var any = false;
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Rules(definition))
        {
            Walk(rule.Conditions, ref any, groups);
        }

        foreach (var rule in definition.ValidationRules)
        {
            Walk(rule.Conditions, ref any, groups);
        }

        return new FormMemberConditions(any, groups);
    }

    private static IEnumerable<FormRule> Rules(FormDefinition definition)
    {
        foreach (var page in definition.Pages)
        {
            foreach (var rule in page.Rules)
            {
                yield return rule;
            }

            foreach (var group in page.Groups)
            {
                foreach (var rule in group.Rules.Concat(group.Fields.SelectMany(field => field.Fields.Prepend(field)).SelectMany(field => field.Rules)))
                {
                    yield return rule;
                }
            }
        }
    }

    private static void Walk(IEnumerable<RuleItem> items, ref bool any, HashSet<string> groups)
    {
        foreach (var item in items)
        {
            switch (item)
            {
                case RuleGroup group:
                    Walk(group.Conditions, ref any, groups);
                    break;
                case RuleCondition { Context: SignedIn }:
                    any = true;
                    break;
                case RuleCondition { Context: Group } condition:
                    any = true;
                    Texts(condition.Value, groups);
                    break;
            }
        }
    }

    private static void Texts(ConditionOperand? operand, HashSet<string> groups)
    {
        switch (operand)
        {
            case ConditionOperand.Text text:
                groups.Add(text.Value);
                break;
            case ConditionOperand.List list:
                foreach (var item in list.Items)
                {
                    Texts(item, groups);
                }

                break;
        }
    }
}
