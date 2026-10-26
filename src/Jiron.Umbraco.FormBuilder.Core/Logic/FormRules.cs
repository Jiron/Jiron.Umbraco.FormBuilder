using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// The form engine (section 4.3.3, ADR-0061), the same in TypeScript (<c>packages/logic/src/form.ts</c>): every rule of a
/// form at once. The whole form is checked first, so the order of evaluation never changes an outcome; then visibility
/// is decided in dependency order (an element after what holds it and after every field its show or hide rule looks
/// at), a hidden field's answers are gone for every condition, and a repeatable group's child fields are decided row by
/// row. What is required, the validation rules' messages, the actions that run and the outcome follow from the final
/// visibility.
/// </summary>
internal static class FormRules
{
    private static readonly FrozenDictionary<string, IReadOnlyList<string>> NoValues = FrozenDictionary<string, IReadOnlyList<string>>.Empty;

    private enum ElementType
    {
        Page,
        Group,
        Field,
    }

    /// <summary>Checks the whole form, then decides what its rules decide.</summary>
    /// <exception cref="LogicException">The form fails the check; nothing was evaluated.</exception>
    public static FormState Evaluate(LogicForm form, FormAnswers answers, EvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(context);
        var (nodes, subjects) = Index(form);
        var kinds = subjects.ToDictionary(pair => pair.Key, pair => pair.Value.Kind, StringComparer.Ordinal);

        // The check, before anything is evaluated.
        var patterns = new Dictionary<string, SafePattern>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            CheckRules(node.Rules, node.Type == ElementType.Field ? ["show", "hide", "require"] : ["show", "hide"], node.Kind is not null, kinds, patterns);
        }

        foreach (var rule in form.ValidationRules)
        {
            if (!subjects.ContainsKey(rule.Field))
            {
                throw new LogicException(LogicErrors.UnknownField);
            }

            Rules.CheckGroup(new LogicGroup(rule.Match, rule.Conditions), kinds, patterns);
        }

        foreach (var action in form.Actions)
        {
            CheckRules(action.Rules, ["run"], holdsValue: true, kinds, patterns);
        }

        foreach (var outcome in form.Outcomes)
        {
            CheckRules(outcome.Rules, ["use"], holdsValue: true, kinds, patterns);
        }

        var sequence = Order(nodes, subjects);

        // Visibility, in dependency order.
        IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>> RowsOf(Node repeater) =>
            answers.Rows.TryGetValue(repeater.Id, out var rows) ? rows : [];
        IReadOnlyList<string>? AnswersOf(string id, Node? repeater, int row)
        {
            // Every field a condition names is a subject: the check saw to it.
            var owner = subjects[id].Owner;
            if (owner.Repeater is null)
            {
                return owner.Hidden ? null : answers.Values.GetValueOrDefault(id);
            }

            var rows = RowsOf(owner.Repeater);
            if (owner.Repeater == repeater)
            {
                return owner.HiddenIn[row] ? null : rows[row].GetValueOrDefault(id);
            }

            var all = new List<string>();
            for (var r = 0; r < rows.Count; r++)
            {
                if (!owner.HiddenIn[r] && rows[r].TryGetValue(id, out var given))
                {
                    all.AddRange(given);
                }
            }

            return all;
        }

        var global = new Subject(kinds, NoValues, context) { Lookup = id => AnswersOf(id, null, -1) };
        Subject ScopeOf(Node? repeater, int row) =>
            repeater is null ? global : new Subject(kinds, NoValues, context) { Lookup = id => AnswersOf(id, repeater, row) };
        bool Matches(LogicRule rule, Subject scope) => Rules.MatchesGroup(new LogicGroup(rule.Match, rule.Conditions), scope, patterns);
        bool Shown(Node element, Subject scope) =>
            RuleOf(element.Rules, "show", "hide") is not { } rule || Matches(rule, scope) == (rule.Effect == "show");
        foreach (var element in sequence)
        {
            if (element.Repeater is { } repeater)
            {
                var count = RowsOf(repeater).Count;
                var hiddenIn = new bool[count];
                for (var row = 0; row < count; row++)
                {
                    hiddenIn[row] = repeater.Hidden || !Shown(element, ScopeOf(repeater, row));
                }

                element.HiddenIn = hiddenIn;
            }
            else
            {
                element.Hidden = element.Container?.Hidden == true || !Shown(element, global);
            }
        }

        // What follows from the final visibility.
        bool Requires(Node element, Subject scope) =>
            element.Required || (RuleOf(element.Rules, "require") is { } rule && Matches(rule, scope));
        var hidden = new List<string>();
        var required = new List<string>();
        var messages = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var element in nodes.Where(element => element.Repeats))
        {
            element.RowBuilders = [.. RowsOf(element).Select(_ => new RowBuilder())];
        }

        foreach (var element in nodes)
        {
            if (element.Repeater is { } repeater)
            {
                for (var row = 0; row < repeater.RowBuilders.Count; row++)
                {
                    if (element.HiddenIn[row])
                    {
                        repeater.RowBuilders[row].Hidden.Add(element.Id);
                    }
                    else if (element.Kind is not null && Requires(element, ScopeOf(repeater, row)))
                    {
                        repeater.RowBuilders[row].Required.Add(element.Id);
                    }
                }
            }
            else if (element.Hidden)
            {
                hidden.Add(element.Id);
            }
            else if (element.Kind is not null && Requires(element, global))
            {
                required.Add(element.Id);
            }
        }

        foreach (var rule in form.ValidationRules.Where(rule => rule.Enabled))
        {
            var owner = subjects[rule.Field].Owner;
            var conditions = new LogicGroup(rule.Match, rule.Conditions);
            if (owner.Repeater is { } repeater)
            {
                for (var row = 0; row < repeater.RowBuilders.Count; row++)
                {
                    if (!owner.HiddenIn[row] && Rules.MatchesGroup(conditions, ScopeOf(repeater, row), patterns))
                    {
                        Push(repeater.RowBuilders[row].Messages, rule.Field, rule.Id);
                    }
                }
            }
            else if (!owner.Hidden && Rules.MatchesGroup(conditions, global, patterns))
            {
                Push(messages, rule.Field, rule.Id);
            }
        }

        bool Applies(IReadOnlyList<LogicRule> rules, string effect) => RuleOf(rules, effect) is not { } rule || Matches(rule, global);

        return new FormState(
            hidden,
            required,
            Frozen(messages),
            nodes.Where(element => element.Repeats).ToDictionary(
                element => element.Id,
                element => (IReadOnlyList<RowState>)[.. element.RowBuilders.Select(row => new RowState(row.Hidden, row.Required, Frozen(row.Messages)))],
                StringComparer.Ordinal),
            [.. form.Actions.Where(action => Applies(action.Rules, "run")).Select(action => action.Id)],
            form.Outcomes.FirstOrDefault(outcome => Applies(outcome.Rules, "use"))?.Id);
    }

    private static Dictionary<string, IReadOnlyList<string>> Frozen(Dictionary<string, List<string>> messages) =>
        messages.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);

    private static void Push(Dictionary<string, List<string>> map, string key, string value)
    {
        if (map.TryGetValue(key, out var list))
        {
            list.Add(value);
        }
        else
        {
            map[key] = [value];
        }
    }

    private static LogicRule? RuleOf(IReadOnlyList<LogicRule> rules, params ReadOnlySpan<string> effects)
    {
        foreach (var rule in rules)
        {
            if (rule.Enabled && effects.Contains(rule.Effect))
            {
                return rule;
            }
        }

        return null;
    }

    // The form's elements in document order and its subjects by id; an id given twice is invalid_form.
    private static (List<Node> Nodes, Dictionary<string, SubjectOf> Subjects) Index(LogicForm form)
    {
        var nodes = new List<Node>();
        var subjects = new Dictionary<string, SubjectOf>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string Claim(string? id) => id is not null && ids.Add(id) ? id : throw new LogicException(LogicErrors.InvalidForm);
        Node Add(Node node)
        {
            nodes.Add(node);
            return node;
        }

        void Fields(IReadOnlyList<LogicField> list, Node container, Node? repeater)
        {
            foreach (var field in list)
            {
                var element = Add(new Node(Claim(field.Id), ElementType.Field, container, repeater, field.Kind, field.Required, field.Rules, field.Fields.Count > 0));

                // A repeatable group holds no repeatable group.
                if (repeater is not null && element.Repeats)
                {
                    throw new LogicException(LogicErrors.InvalidForm);
                }

                if (element.Kind is { } kind)
                {
                    subjects[element.Id] = new SubjectOf(kind, element);
                }

                foreach (var row in field.Rows)
                {
                    subjects[Claim(row.Id)] = new SubjectOf(row.Kind, element);
                }

                Fields(field.Fields, element, element.Repeats ? element : repeater);
            }
        }

        foreach (var page in form.Pages)
        {
            var p = Add(new Node(Claim(page.Id), ElementType.Page, null, null, null, false, page.Rules, false));
            foreach (var group in page.Groups)
            {
                Fields(group.Fields, Add(new Node(Claim(group.Id), ElementType.Group, p, null, null, false, group.Rules, false)), null);
            }
        }

        foreach (var id in form.ValidationRules.Select(rule => rule.Id).Concat(form.Actions.Select(action => action.Id)).Concat(form.Outcomes.Select(outcome => outcome.Id)))
        {
            Claim(id);
        }

        return (nodes, subjects);
    }

    // An element's rules: effects it takes, one rule per effect and not both show and hide (invalid_rule), then each
    // rule's conditions.
    private static void CheckRules(IReadOnlyList<LogicRule> rules, ReadOnlySpan<string> effects, bool holdsValue, IReadOnlyDictionary<string, FieldKind> kinds, Dictionary<string, SafePattern> patterns)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            var effect = rule.Effect;
            if (!effects.Contains(effect) || (effect == "require" && !holdsValue) || seen.Contains(effect)
                || (effect == "show" && seen.Contains("hide")) || (effect == "hide" && seen.Contains("show")))
            {
                throw new LogicException(LogicErrors.InvalidRule);
            }

            seen.Add(effect);
            Rules.CheckGroup(new LogicGroup(rule.Match, rule.Conditions), kinds, patterns);
        }
    }

    // The ids of the fields a group of conditions looks at, through its conditions and their operands.
    private static void LooksAt(IReadOnlyList<LogicItem> items, HashSet<string> into)
    {
        void Operand(Operand? value)
        {
            switch (value)
            {
                case FieldOperand field:
                    into.Add(field.Field);
                    break;
                case ListOperand list:
                    foreach (var item in list.Items)
                    {
                        Operand(item);
                    }

                    break;
            }
        }

        foreach (var item in items)
        {
            if (item is LogicGroup group)
            {
                LooksAt(group.Conditions, into);
            }
            else if (item is LogicCondition condition)
            {
                if (condition.Field is { } field)
                {
                    into.Add(field);
                }

                Operand(condition.Value);
            }
        }
    }

    // The elements in dependency order, the ready ones in document order; a cycle is cycle.
    private static List<Node> Order(List<Node> nodes, Dictionary<string, SubjectOf> subjects)
    {
        foreach (var element in nodes)
        {
            var on = new HashSet<Node>();
            if (element.Container is { } container)
            {
                on.Add(container);
            }

            if (RuleOf(element.Rules, "show", "hide") is { } visibility)
            {
                var named = new HashSet<string>(StringComparer.Ordinal);
                LooksAt(visibility.Conditions, named);
                foreach (var id in named)
                {
                    if (subjects.TryGetValue(id, out var subject))
                    {
                        on.Add(subject.Owner);
                    }
                }
            }

            foreach (var dependency in on)
            {
                dependency.Dependents.Add(element);
                element.Waiting++;
            }
        }

        var ready = nodes.Where(element => element.Waiting == 0).ToList();

        // The loop also reaches the elements it makes ready.
        for (var next = 0; next < ready.Count; next++)
        {
            foreach (var dependent in ready[next].Dependents)
            {
                if (--dependent.Waiting == 0)
                {
                    ready.Add(dependent);
                }
            }
        }

        return ready.Count < nodes.Count ? throw new LogicException(LogicErrors.Cycle) : ready;
    }

    /// <summary>A subject of conditions and the element whose being shown decides its: the field, or a grid row's grid.</summary>
    private sealed record SubjectOf(FieldKind Kind, Node Owner);

    private sealed class RowBuilder
    {
        public List<string> Hidden { get; } = [];

        public List<string> Required { get; } = [];

        public Dictionary<string, List<string>> Messages { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>A page, group or field, with what the evaluation decides about it.</summary>
    private sealed class Node(string id, ElementType type, Node? container, Node? repeater, FieldKind? kind, bool required, IReadOnlyList<LogicRule> rules, bool repeats)
    {
        public string Id { get; } = id;

        public ElementType Type { get; } = type;

        /// <summary>The element that holds it; none for a page.</summary>
        public Node? Container { get; } = container;

        /// <summary>The repeatable group whose rows hold it; none outside one.</summary>
        public Node? Repeater { get; } = repeater;

        public FieldKind? Kind { get; } = kind;

        public bool Required { get; } = required;

        public IReadOnlyList<LogicRule> Rules { get; } = rules;

        public bool Repeats { get; } = repeats;

        public List<Node> Dependents { get; } = [];

        public int Waiting { get; set; }

        public bool Hidden { get; set; }

        /// <summary>A child field's being hidden, row by row.</summary>
        public bool[] HiddenIn { get; set; } = [];

        /// <summary>A repeatable group's rows.</summary>
        public IReadOnlyList<RowBuilder> RowBuilders { get; set; } = [];
    }
}
