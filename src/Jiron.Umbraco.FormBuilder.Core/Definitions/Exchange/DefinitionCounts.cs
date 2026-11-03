namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;

/// <summary>
/// How much a definition holds, as the import's check and the template gallery say it (ROADMAP 9.2): its pages, its
/// fields with the children of repeatable groups, its rules (those of pages, groups, fields, actions and outcomes, and
/// the form-level validation rules, as the rule limit counts them), its outcomes and its actions.
/// </summary>
/// <param name="Pages">The pages.</param>
/// <param name="Fields">The fields, the children of a repeatable group included.</param>
/// <param name="Rules">The rules.</param>
/// <param name="Outcomes">The outcomes.</param>
/// <param name="Actions">The actions.</param>
internal sealed record DefinitionCounts(int Pages, int Fields, int Rules, int Outcomes, int Actions)
{
    /// <summary>The counts of a definition.</summary>
    public static DefinitionCounts Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var fields = DefinitionKeys.Fields(definition).ToList();
        var rules = definition.Pages.Sum(page => page.Rules.Count + page.Groups.Sum(group => group.Rules.Count))
            + fields.Sum(field => field.Rules.Count)
            + definition.Actions.Sum(action => action.Rules.Count)
            + definition.Outcomes.Sum(outcome => outcome.Rules.Count)
            + definition.ValidationRules.Count;
        return new DefinitionCounts(definition.Pages.Count, fields.Count, rules, definition.Outcomes.Count, definition.Actions.Count);
    }
}
