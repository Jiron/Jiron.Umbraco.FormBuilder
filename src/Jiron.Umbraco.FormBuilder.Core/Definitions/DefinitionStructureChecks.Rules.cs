using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>Rules, their conditions, actions, outcomes and cycles.</summary>
internal sealed partial class DefinitionStructureChecks
{
    private const string QueryContext = "query.";

    private static readonly ImmutableArray<RuleEffect> Visibility = [RuleEffect.Show, RuleEffect.Hide];
    private static readonly ImmutableArray<RuleEffect> AnyFieldEffect = [RuleEffect.Show, RuleEffect.Hide, RuleEffect.Require];

    private static string Word(RuleEffect effect) => DefinitionLogic.Effect(effect);

    /// <summary>
    /// An element's rules: effects it takes, one rule per effect and not both show and hide, and every condition. A
    /// switched-on show or hide rule makes the element's being shown depend on the fields it looks at.
    /// </summary>
    private void Rules(IReadOnlyList<FormRule> rules, string path, ImmutableArray<RuleEffect> effects, string element, int? node)
    {
        var seen = new HashSet<RuleEffect>();
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var rulePath = I(path, i);
            if (!effects.Contains(rule.Effect))
            {
                var allowed = effects.Length == 0 ? "no rules" : "only " + string.Join(" and ", effects.Select(Word)) + " rules";
                Fail(DefinitionErrors.RuleEffectInvalid, P(rulePath, "effect"), $"{element} takes {allowed}, not a {Word(rule.Effect)} rule.");
            }
            else if (!seen.Add(rule.Effect))
            {
                Fail(DefinitionErrors.RuleDuplicate, rulePath, $"{element} has at most one {Word(rule.Effect)} rule.");
            }
            else if (rule.Effect is RuleEffect.Show or RuleEffect.Hide && seen.Contains(rule.Effect == RuleEffect.Show ? RuleEffect.Hide : RuleEffect.Show))
            {
                Fail(DefinitionErrors.RuleDuplicate, rulePath, $"{element} has a show rule or a hide rule, not both: one rule decides whether it is shown.");
            }

            // The rules of pages, groups and fields run in the visitor's browser too; those of actions and outcomes only on the server.
            var kinds = node is not null ? _visitorKinds : _kinds;
            var visibility = node is not null && rule.Enabled && rule.Effect is RuleEffect.Show or RuleEffect.Hide;
            var dependencies = visibility ? new List<Guid>() : null;
            Conditions(rule.Conditions, P(rulePath, "conditions"), kinds, dependencies, depth: 1);
            if (dependencies is null)
            {
                continue;
            }

            var from = _nodes[node!.Value];
            from.RulePath ??= rulePath;
            foreach (var key in dependencies)
            {
                if (_fields.TryGetValue(key, out var entry))
                {
                    from.Edges.Add(entry.Node);
                }
            }
        }
    }

    // A rule's own list of conditions is the first level; a group nested deeper than the engine reads is refused as the
    // engine refuses it (ADR-0061), once, at the group.
    private void Conditions(IReadOnlyList<RuleItem> items, string path, IReadOnlyDictionary<string, FieldKind> kinds, List<Guid>? dependencies, int depth)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = I(path, i);
            switch (items[i])
            {
                case RuleGroup when depth == Logic.Rules.MaxDepth:
                    Fail(DefinitionErrors.TooDeep, itemPath, $"Groups of conditions nest at most {Logic.Rules.MaxDepth} levels deep, the rule's own conditions being the first.");
                    break;
                case RuleGroup group:
                    Conditions(group.Conditions, P(itemPath, "conditions"), kinds, dependencies, depth + 1);
                    break;
                case RuleCondition condition:
                    Condition(condition, itemPath, kinds, dependencies);
                    break;
            }
        }
    }

    // References first, each with its own path; then the shared check for the operator, the kinds and the operand.
    private void Condition(RuleCondition condition, string path, IReadOnlyDictionary<string, FieldKind> kinds, List<Guid>? dependencies)
    {
        var checkable = true;
        if (condition.Field is { } field)
        {
            dependencies?.Add(field);
            checkable &= Subject(field, P(path, "field"), kinds);
        }

        if (condition.Context is { } context && context.StartsWith(QueryContext, StringComparison.Ordinal) && !_queryValues.Contains(context[QueryContext.Length..]))
        {
            Fail(DefinitionErrors.UnknownField, P(path, "context"), $"The form does not allow the query value '{context[QueryContext.Length..]}' (settings.developer.queryValues).");
            checkable = false;
        }

        if (condition.Value is { } value)
        {
            checkable &= OperandFields(value, P(path, "value"), kinds, dependencies);
        }

        if (!checkable || Logic.Rules.Check(DefinitionLogic.Condition(condition), kinds) is not { } code)
        {
            return;
        }

        var (at, message) = code switch
        {
            DefinitionErrors.UnknownOperator => (P(path, "op"), $"There is no operator '{condition.Operator}'."),
            DefinitionErrors.OperatorNotForKind => (P(path, "op"), $"The operator '{condition.Operator}' does not take the kind of answer the condition looks at."),
            DefinitionErrors.InvalidOperand => (P(path, "value"), $"The operand does not fit the operator '{condition.Operator}'."),
            DefinitionErrors.UnsafePattern => (P(path, "value"), "The pattern uses syntax outside the safe subset or exceeds its limits (ADR-0005)."),

            // The fields were checked above, so what the shared check does not know is the context.
            DefinitionErrors.UnknownField => (P(path, "context"), $"There is no context '{condition.Context}'."),
            _ => (path, "A condition looks at a field or at the context: exactly one of them."),
        };
        Fail(code, at, message);
    }

    private bool OperandFields(ConditionOperand operand, string path, IReadOnlyDictionary<string, FieldKind> kinds, List<Guid>? dependencies)
    {
        switch (operand)
        {
            case ConditionOperand.Field field:
                dependencies?.Add(field.Key);
                return Subject(field.Key, P(path, "field"), kinds);
            case ConditionOperand.List list:
                var all = true;
                for (var i = 0; i < list.Items.Count; i++)
                {
                    all &= OperandFields(list.Items[i], I(path, i), kinds, dependencies);
                }

                return all;
            default:
                return true;
        }
    }

    // Whether a condition may look at the field: one whose answers rules compare, and which the runtime that evaluates
    // the condition has. A field whose type the site lacks is not reported again here; its condition waits for the type.
    private bool Subject(Guid key, string path, IReadOnlyDictionary<string, FieldKind> kinds)
    {
        if (kinds.ContainsKey(KeyText(key)))
        {
            return true;
        }

        if (_kinds.ContainsKey(KeyText(key)))
        {
            Fail(DefinitionErrors.InvalidReference, path, ServerValue(_fields[key]));
        }
        else if (!_fields.TryGetValue(key, out var entry) || !entry.Resolves)
        {
            Fail(DefinitionErrors.UnknownField, path, "The condition names no field of the form that holds a value.");
        }
        else if (!entry.Unknown)
        {
            Fail(DefinitionErrors.UnknownField, path, $"Rules cannot compare the answers of {entry.Describe}.");
        }

        return false;
    }

    private void CheckValidationRules(IReadOnlyList<FormValidationRule> rules)
    {
        var rulesPath = P(JsonPaths.Root, "validationRules");
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var path = I(rulesPath, i);
            if (!_fields.TryGetValue(rule.Field, out var entry) || !entry.Resolves)
            {
                Fail(DefinitionErrors.UnknownField, P(path, "field"), "The validation rule shows its message on no field of the form that holds a value.");
            }
            else if (LeftOutForMembers(rule.Field))
            {
                Fail(DefinitionErrors.InvalidReference, P(path, "field"), $"The validation rule shows its message on {entry.Describe}, which a signed-in member's page leaves out (prefill.memberMode hide).");
            }

            Conditions(rule.Conditions, P(path, "conditions"), _visitorKinds, dependencies: null, depth: 1);
        }
    }

    private void CheckActions(IReadOnlyList<FormAction> actions)
    {
        var actionsPath = P(JsonPaths.Root, "actions");
        for (var i = 0; i < actions.Count; i++)
        {
            Rules(actions[i].Rules, P(I(actionsPath, i), "rules"), [RuleEffect.Run], "An action", node: null);
        }
    }

    private void CheckOutcomes(IReadOnlyList<FormOutcome> outcomes)
    {
        var outcomesPath = P(JsonPaths.Root, "outcomes");
        for (var i = 0; i < outcomes.Count; i++)
        {
            var outcome = outcomes[i];
            var path = I(outcomesPath, i);
            Rules(outcome.Rules, P(path, "rules"), [RuleEffect.Use], "An outcome", node: null);
            var redirectPath = P(path, "redirect");
            if (outcome.Kind == OutcomeKind.Redirect)
            {
                // A redirect outcome leaves at once; a message outcome may redirect after its message.
                if (outcome.Redirect is null)
                {
                    Fail(DefinitionErrors.PartMissing, redirectPath, "A redirect outcome says where it leads.");
                }

                if (outcome.Message is not null)
                {
                    Fail(DefinitionErrors.PropertyNotSupported, P(path, "message"), "A redirect outcome shows no message; a message outcome can redirect after its message.");
                }

                if (outcome.ShowAnswers)
                {
                    Fail(DefinitionErrors.PropertyNotSupported, P(path, "showAnswers"), "A redirect outcome shows no answers.");
                }

                if (outcome.Redirect is { DelaySeconds: > 0 })
                {
                    Fail(DefinitionErrors.PropertyNotSupported, P(redirectPath, "delaySeconds"), "A redirect outcome leaves at once; a delay belongs to a message outcome.");
                }
            }

            if (outcome.Redirect is not { } redirect)
            {
                continue;
            }

            if (redirect.Content is not null && redirect.Url is not null)
            {
                Fail(DefinitionErrors.InvalidRedirect, redirectPath, "A redirect leads to a content page or to a URL, not both.");
            }
            else if (redirect.Content is null && redirect.Url is null)
            {
                Fail(DefinitionErrors.PartMissing, redirectPath, "A redirect names a content page or a URL.");
            }
            else if (redirect.Url is { } url && RedirectUrls.Problem(url) is { } problem)
            {
                Fail(DefinitionErrors.InvalidRedirect, P(redirectPath, "url"), problem);
            }
        }
    }

    // A page, group or field whose being shown, or a calculation whose result, depends on itself has no answer: the
    // shared evaluators would go round. Each strongly connected part of the graph is reported once, at its first element
    // that has a show or hide rule or a formula.
    private void CheckCycles()
    {
        foreach (var component in StronglyConnected().OrderBy(component => component.Min()))
        {
            if (component.Count == 1 && !_nodes[component[0]].Edges.Contains(component[0]))
            {
                continue;
            }

            component.Sort();
            var nodes = component.ConvertAll(node => _nodes[node]);
            var at = nodes.Find(node => node.RulePath is not null || node.FormulaPath is not null) ?? nodes[0];
            var elements = string.Join(", ", nodes.Select(node => node.Description));
            var message = nodes.Exists(node => node.FormulaPath is not null)
                ? $"Whether these are shown, and what they work out, depends on itself, through rules, formulas and what contains them: {elements}."
                : $"Whether these are shown depends on itself, through rules and what contains them: {elements}.";
            Fail(DefinitionErrors.Cycle, at.RulePath ?? at.FormulaPath ?? at.Path, message);
        }

        // Option parents: a field whose options its own answer filters, however many fields lie between. Each field has
        // one parent at most, so one walk from each link that no earlier walk reached finds every circle once.
        var links = new Dictionary<Guid, int>(_parents.Count);
        for (var i = 0; i < _parents.Count; i++)
        {
            links.TryAdd(_parents[i].Field, i);
        }

        var walked = new int[_parents.Count];
        for (var start = 0; start < _parents.Count; start++)
        {
            var walk = new List<int>();
            for (var link = start; walked[link] == 0;)
            {
                walked[link] = start + 1;
                walk.Add(link);
                if (!links.TryGetValue(_parents[link].Parent, out link))
                {
                    break;
                }
            }

            // A walk that comes back to a link of its own has closed a circle; one that meets an earlier walk has not.
            if (walk.Count > 0 && links.TryGetValue(_parents[walk[^1]].Parent, out var end) && walked[end] == start + 1)
            {
                var first = walk.Skip(walk.IndexOf(end)).Min();
                Fail(DefinitionErrors.Cycle, _parents[first].Path, "These fields' options are filtered by each other's answers, round in a circle.");
            }
        }
    }

    // Tarjan's algorithm without recursion, so a large form cannot exhaust the stack.
    private List<List<int>> StronglyConnected()
    {
        var count = _nodes.Count;
        var index = new int[count];
        var low = new int[count];
        var onStack = new bool[count];
        Array.Fill(index, -1);
        var stack = new Stack<int>();
        var work = new Stack<(int Node, int Edge)>();
        var components = new List<List<int>>();
        var next = 0;
        for (var start = 0; start < count; start++)
        {
            if (index[start] >= 0)
            {
                continue;
            }

            work.Push((start, 0));
            while (work.Count > 0)
            {
                var (node, edge) = work.Pop();
                var edges = _nodes[node].Edges;
                if (edge == 0)
                {
                    index[node] = low[node] = next++;
                    stack.Push(node);
                    onStack[node] = true;
                }
                else
                {
                    // Back from the edge followed last.
                    low[node] = Math.Min(low[node], low[edges[edge - 1]]);
                }

                var descended = false;
                for (; edge < edges.Count; edge++)
                {
                    var target = edges[edge];
                    if (index[target] < 0)
                    {
                        work.Push((node, edge + 1));
                        work.Push((target, 0));
                        descended = true;
                        break;
                    }

                    if (onStack[target])
                    {
                        low[node] = Math.Min(low[node], index[target]);
                    }
                }

                if (descended || low[node] != index[node])
                {
                    continue;
                }

                var component = new List<int>();
                int member;
                do
                {
                    member = stack.Pop();
                    onStack[member] = false;
                    component.Add(member);
                }
                while (member != node);
                components.Add(component);
            }
        }

        return components;
    }
}
