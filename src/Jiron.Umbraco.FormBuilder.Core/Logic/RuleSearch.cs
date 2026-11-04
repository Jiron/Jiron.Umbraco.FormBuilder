using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>What the rule search tries answers with: the fields' kinds, each choice's own options, the site's languages, today.</summary>
/// <param name="Fields">The kinds of the fields and grid rows conditions may look at, by key.</param>
/// <param name="Options">The options a choice offers that the form knows, by the key of the field or grid row; none where it doesn't.</param>
/// <param name="Cultures">The site's languages.</param>
/// <param name="Today">Today, <c>YYYY-MM-DD</c>, from which days counted from today are counted; no visit is on an earlier day.</param>
internal sealed record SearchScope(
    IReadOnlyDictionary<string, FieldKind> Fields,
    Func<string, IReadOnlyList<string>> Options,
    IReadOnlyList<string> Cultures,
    string Today)
{
    /// <summary>
    /// The fields the form hides for a set of answers, which count as unanswered: as the form engine decides
    /// (<see cref="RuleSearch.FormSight"/>), or null where it can't tell. Without it, every field the rules look at counts as shown.
    /// </summary>
    public Func<IReadOnlyDictionary<string, IReadOnlyList<string>>, EvaluationContext, IReadOnlySet<string>?>? Hidden { get; init; }

    /// <summary>Whether a field's answers, read where the rules read them, may be several, as a repeatable group's child's are.</summary>
    public Func<string, bool>? Several { get; init; }
}

/// <summary>One answer of a conflict: what it is about (<c>field:&lt;key&gt;</c> or <c>context:&lt;name&gt;</c>) and the answers given.</summary>
/// <param name="Subject">The field or part of the visit.</param>
/// <param name="Values">The answers given; empty for none, or for an answer the rules don't name (<paramref name="NoneOf"/>).</param>
/// <param name="NoneOf">Set for a text the rules don't name: the texts they do, which any other answer stands for alike.</param>
internal sealed record SearchAnswer(string Subject, IReadOnlyList<string> Values, IReadOnlyList<string>? NoneOf = null);

/// <summary>A require rule and a rule that hides its field holding together, and for which answers.</summary>
/// <param name="Hiding">The rule that hides the field for those answers: the first of those given that does.</param>
/// <param name="Requiring">The require rule.</param>
/// <param name="Answers">The answers that matter, in the order the rules first name them; empty where the rules conflict for any answer.</param>
internal sealed record RuleConflict(LogicRule Hiding, LogicRule Requiring, IReadOnlyList<SearchAnswer> Answers);

/// <summary>Whether an element shows for some answers, for none of all a visitor can give, or can't be told.</summary>
internal enum Showing
{
    /// <summary>Some answers a visitor gives show it.</summary>
    Shows,

    /// <summary>No answer a visitor can give shows it.</summary>
    Never,

    /// <summary>The answers tried don't stand for all, or the engine refuses a rule.</summary>
    Unknown,
}

/// <summary>
/// The rule search (ROADMAP 9.5c, 9.5d; ADR-0178, ADR-0179): what rules do for the answers a visitor can give, worked out by
/// the engine itself (<see cref="Rules.EvaluateRule"/>) over sets of answers, as <c>search.ts</c> of <c>@jfb/logic</c> does
/// in TypeScript; both pass the shared vectors of <c>tests/vectors/search.json</c> (D10). A conflict is said only where
/// the engine finds a require rule holding while a rule hides its field, the answers of the fields the form hides for
/// them counting as none (<see cref="FormSight"/>), so a conflict said is real; an element is said to never show only
/// where the answers tried stand for every answer a visitor gives.
/// </summary>
internal static partial class RuleSearch
{
    /// <summary>How many sets of answers are tried at most, so an element with many rules still answers at once.</summary>
    public const int MostTried = 4096;

    /// <summary>How many answers a choice of several is tried with at most.</summary>
    private const int MostSets = 32;

    /// <summary>How far sets of answers are counted: well past <see cref="MostTried"/>, and never past what a long holds.</summary>
    private const long MostCounted = long.MaxValue / 1024;

    private static readonly FrozenSet<string> Counts = new[] { "countAtLeast", "countAtMost" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The operators for which every answer the rules don't name is alike: none of them looks inside an answer.</summary>
    private static readonly FrozenSet<string> Alike = new[] { "equals", "notEquals", "isOneOf", "isNoneOf", "isEmpty", "isNotEmpty", "countAtLeast", "countAtMost" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The operators that only ask which texts are ticked or given, not how many or whether any is.</summary>
    private static readonly FrozenSet<string> Named = new[] { "equals", "notEquals", "isOneOf", "isNoneOf", "isChecked", "isNotChecked" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The conflict of a require rule with rules that hide its field, or null where none of the answers tried make the
    /// field required while one of them hides it. <paramref name="hidings"/> are show and hide rules: the field's own, its
    /// group's, its page's, in that order.
    /// </summary>
    public static RuleConflict? ConflictOf(LogicRule requiring, IReadOnlyList<LogicRule> hidings, SearchScope scope)
    {
        ArgumentNullException.ThrowIfNull(requiring);
        ArgumentNullException.ThrowIfNull(hidings);
        ArgumentNullException.ThrowIfNull(scope);
        var hiders = hidings.Where(rule => Used(rule) && rule.Effect is "hide" or "show").ToList();
        if (requiring.Effect != "require" || !Used(requiring) || hiders.Count == 0)
        {
            return null;
        }

        // The hiding rules' answers are said first, as the rules overview says a conflict ("when Topic is Press").
        List<LogicRule> all = [.. hiders, requiring];
        var subjects = SubjectsOf(all);
        var tries = subjects.Select(subject => TriesOf(subject, all, scope)).ToList();
        var candidates = tries.Select(one => one.Candidates).ToList();
        if (candidates.Any(list => list.Count == 0))
        {
            return null;
        }

        LogicRule? HiderAt(IReadOnlyList<IReadOnlyList<string>> values)
        {
            var answers = Seen(AnswersOf(subjects, values, scope), scope);
            if (answers is null || Matches(requiring, answers.Value, scope) != true)
            {
                return null;
            }

            return hiders.FirstOrDefault(rule => Hides(rule, Matches(rule, answers.Value, scope)));
        }

        bool Conflicts(IReadOnlyList<IReadOnlyList<string>> values) => HiderAt(values) is not null;

        var found = Search(candidates, Conflicts);
        return found is not null && HiderAt(found) is { } hiding
            ? new RuleConflict(hiding, requiring, Said(subjects, tries, found, Conflicts))
            : null;
    }

    /// <summary>
    /// Whether an element with these show and hide rules shows to a visitor who has not yet answered the fields of
    /// <paramref name="unanswered"/> (their keys), for some of the answers a visitor gives. An element without rules in use shows.
    /// </summary>
    public static Showing ShowsAtAll(IReadOnlyList<LogicRule> rules, SearchScope scope, IReadOnlyCollection<string> unanswered)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(unanswered);
        var own = rules.Where(rule => Used(rule) && rule.Effect is "hide" or "show").ToList();
        if (own.Count == 0)
        {
            return Showing.Shows;
        }

        var subjects = SubjectsOf(own);
        var blank = unanswered.Select(key => $"field:{key}").ToHashSet(StringComparer.Ordinal);
        var tries = subjects.Select(subject => blank.Contains(subject) ? new Tries([[]], null, [], Complete: true) : TriesOf(subject, own, scope)).ToList();
        var candidates = tries.Select(one => one.Candidates).ToList();
        if (candidates.Any(list => list.Count == 0))
        {
            return Showing.Unknown;
        }

        var undecided = false;
        bool Shows(IReadOnlyList<IReadOnlyList<string>> values)
        {
            var answers = AnswersOf(subjects, values, scope);
            if (answers is null)
            {
                return false;
            }

            return own.All(rule =>
            {
                var holds = Matches(rule, answers.Value, scope);

                // A rule the engine refuses says nothing, and the element's showing can't be told.
                if (holds is null && rule.Enabled)
                {
                    undecided = true;
                }

                return !Hides(rule, holds);
            });
        }

        var found = Search(candidates, Shows);
        if (undecided)
        {
            return Showing.Unknown;
        }

        if (found is not null)
        {
            return Showing.Shows;
        }

        return tries.All(one => one.Complete) && TrialsOf(candidates) <= MostTried ? Showing.Never : Showing.Unknown;
    }

    /// <summary>
    /// The fields a form hides for a set of answers, as the form engine decides (<see cref="FormRules.Evaluate"/>), for a
    /// scope's <see cref="SearchScope.Hidden"/>: a field or grid row is hidden where it, its grid or anything holding it is.
    /// A repeatable group's child fields are answered in one row, as a visitor with one row answers them. Null where the
    /// engine refuses the form.
    /// </summary>
    public static Func<IReadOnlyDictionary<string, IReadOnlyList<string>>, EvaluationContext, IReadOnlySet<string>?> FormSight(LogicForm form)
    {
        ArgumentNullException.ThrowIfNull(form);

        // Only what holds what and the show and hide rules decide what is hidden.
        var pages = form with { ValidationRules = [], Actions = [], Outcomes = [] };
        var repeaterOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var gridOf = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(IReadOnlyList<LogicField> fields, string? repeater)
        {
            foreach (var field in fields)
            {
                if (repeater is not null)
                {
                    repeaterOf[field.Id] = repeater;
                }

                foreach (var row in field.Rows)
                {
                    gridOf[row.Id] = field.Id;
                }

                Walk(field.Fields, field.Fields.Count > 0 ? field.Id : repeater);
            }
        }

        foreach (var group in form.Pages.SelectMany(page => page.Groups))
        {
            Walk(group.Fields, null);
        }

        return (values, context) =>
        {
            var outside = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var row = new Dictionary<string, Dictionary<string, IReadOnlyList<string>>>(StringComparer.Ordinal);
            foreach (var (key, given) in values)
            {
                if (repeaterOf.TryGetValue(gridOf.TryGetValue(key, out var grid) ? grid : key, out var repeater))
                {
                    if (!row.TryGetValue(repeater, out var one))
                    {
                        row[repeater] = one = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
                    }

                    one[key] = given;
                }
                else
                {
                    outside[key] = given;
                }
            }

            var rows = row.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<IReadOnlyDictionary<string, IReadOnlyList<string>>>)[pair.Value],
                StringComparer.Ordinal);
            FormState state;
            try
            {
                state = FormRules.Evaluate(pages, new FormAnswers(outside, rows), context);
            }
            catch (LogicException)
            {
                return null;
            }

            var hidden = state.Hidden.ToHashSet(StringComparer.Ordinal);
            foreach (var one in state.Rows.Values.Where(one => one.Count > 0))
            {
                hidden.UnionWith(one[0].Hidden);
            }

            foreach (var id in gridOf.Where(pair => hidden.Contains(pair.Value)).Select(pair => pair.Key).ToList())
            {
                hidden.Add(id);
            }

            return hidden;
        };
    }

    /// <summary>
    /// The answers as the form sees them: those of the fields it hides for them count as unanswered (a hidden field is no
    /// answer). Null where the form can't tell, which then makes no conflict.
    /// </summary>
    private static (IReadOnlyDictionary<string, IReadOnlyList<string>> Values, EvaluationContext Context)? Seen((IReadOnlyDictionary<string, IReadOnlyList<string>> Values, EvaluationContext Context)? answers, SearchScope scope)
    {
        if (answers is not { } given || scope.Hidden is null)
        {
            return answers;
        }

        if (scope.Hidden(given.Values, given.Context) is not { } hidden)
        {
            return null;
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> values = given.Values.ToDictionary(
            pair => pair.Key,
            pair => hidden.Contains(pair.Key) ? (IReadOnlyList<string>)[] : pair.Value,
            StringComparer.Ordinal);
        return (values, given.Context);
    }

    // A rule that is off the engine leaves undecided (null), as one it refuses, so neither ever conflicts or hides.
    private static bool Used(LogicRule rule) => rule.Conditions.Count > 0;

    /// <summary>Whether a rule hides its element, given whether it holds: a hide rule that holds, a show rule that doesn't.</summary>
    private static bool Hides(LogicRule rule, bool? holds) => holds is { } value && (rule.Effect == "hide" ? value : !value);

    /// <summary>
    /// The answers that matter: those that conflict whatever they are, together, are left out, each one left out staying
    /// free while the next is tried, so the answers said hold the conflict whatever the others are.
    /// </summary>
    private static List<SearchAnswer> Said(List<string> subjects, IReadOnlyList<Tries> tries, List<IReadOnlyList<string>> found, Func<IReadOnlyList<IReadOnlyList<string>>, bool> conflicts)
    {
        var candidates = tries.Select(one => one.Candidates).ToList();
        var free = new List<int>();
        for (var at = 0; at < subjects.Count; at++)
        {
            if (FreeTogether([.. free, at], candidates, found, conflicts))
            {
                free.Add(at);
            }
        }

        var answers = new List<SearchAnswer>();
        for (var at = 0; at < subjects.Count; at++)
        {
            if (free.Contains(at))
            {
                continue;
            }

            var values = found[at];
            var (fresh, literals) = (tries[at].Fresh, tries[at].Literals);
            answers.Add(fresh is not null && values.Count > 0 && values[0] == fresh
                ? new SearchAnswer(subjects[at], [], literals)
                : new SearchAnswer(subjects[at], values));
        }

        return answers;
    }

    /// <summary>Whether the rules conflict for every answer of these subjects tried together, the others as found.</summary>
    private static bool FreeTogether(IReadOnlyList<int> free, List<List<IReadOnlyList<string>>> candidates, IReadOnlyList<IReadOnlyList<string>> found, Func<IReadOnlyList<IReadOnlyList<string>>, bool> conflicts)
    {
        var lists = found.Select((one, at) => free.Contains(at) ? candidates[at] : (IReadOnlyList<IReadOnlyList<string>>)[one]).ToList();

        // Too many to try counts as not free, so an answer is said rather than claimed not to matter.
        if (TrialsOf(lists) > MostTried)
        {
            return false;
        }

        return Search(lists, values => !conflicts(values)) is null;
    }

    // What the rules look at, and what to try.

    /// <summary>The field an operand compares with; null for any other operand.</summary>
    private static string? OtherFieldOf(Operand? operand) => operand is FieldOperand field ? field.Field : null;

    /// <summary>The offset of a date relative to today an operand holds (<c>+2d</c>, empty for today); null for any other operand.</summary>
    private static string? RelativeOf(Operand? operand) => operand is TodayOperand today ? today.Expression : null;

    /// <summary>Each field and part of the visit the rules look at or compare with, in the order they first name them.</summary>
    private static List<string> SubjectsOf(IReadOnlyList<LogicRule> rules)
    {
        var subjects = new List<string>();
        void Add(string id)
        {
            if (!subjects.Contains(id))
            {
                subjects.Add(id);
            }
        }

        foreach (var condition in ConditionsIn(rules))
        {
            Add(SubjectOf(condition));
            foreach (var operand in OperandsOf(condition))
            {
                if (OtherFieldOf(operand) is { } other)
                {
                    Add($"field:{other}");
                }

                if (RelativeOf(operand) is not null)
                {
                    Add("context:today");
                }
            }
        }

        return subjects;
    }

    private static List<LogicCondition> ConditionsIn(IReadOnlyList<LogicRule> rules)
    {
        var conditions = new List<LogicCondition>();
        void Walk(IReadOnlyList<LogicItem> items)
        {
            foreach (var item in items)
            {
                if (item is LogicGroup group)
                {
                    Walk(group.Conditions);
                }
                else if (item is LogicCondition condition)
                {
                    conditions.Add(condition);
                }
            }
        }

        foreach (var rule in rules)
        {
            Walk(rule.Conditions);
        }

        return conditions;
    }

    private static IReadOnlyList<Operand> OperandsOf(LogicCondition condition) => condition.Value switch
    {
        ListOperand list => list.Items,
        null => [],
        var one => [one],
    };

    private static FieldKind? KindOf(string subject, SearchScope scope)
    {
        if (subject.StartsWith("field:", StringComparison.Ordinal))
        {
            return scope.Fields.TryGetValue(subject["field:".Length..], out var kind) ? kind : null;
        }

        var name = subject["context:".Length..];
        if (name == "culture" || name.StartsWith("query.", StringComparison.Ordinal))
        {
            return FieldKind.Text;
        }

        return name switch
        {
            "memberSignedIn" => FieldKind.Checkbox,
            "memberGroup" => FieldKind.Choices,
            _ => FieldKind.Date,
        };
    }

    private static string SubjectOf(LogicCondition condition) =>
        condition.Field is { } field ? $"field:{field}" : $"context:{condition.Context}";

    private static List<LogicCondition> ConditionsOn(string subject, IReadOnlyList<LogicRule> rules) =>
        [.. ConditionsIn(rules).Where(condition => SubjectOf(condition) == subject)];

    /// <summary>
    /// The texts the rules compare a subject with: the values of its own conditions (a count only where
    /// <paramref name="counts"/> asks for it, as no option is a number of ticks) and, where two answers are compared, the
    /// other's values too, so the answers tried can stand on either side of each other.
    /// </summary>
    private static List<string> LiteralsOf(string subject, IReadOnlyList<LogicRule> rules, SearchScope scope, bool counts)
    {
        var literals = new List<string>();
        void Add(string text)
        {
            if (text.Length > 0 && !literals.Contains(text))
            {
                literals.Add(text);
            }
        }

        var conditions = ConditionsIn(rules);
        void Own(string of)
        {
            foreach (var condition in conditions)
            {
                if (SubjectOf(condition) != of || (!counts && Counts.Contains(condition.Operator)))
                {
                    continue;
                }

                foreach (var operand in OperandsOf(condition))
                {
                    if (operand is TextOperand text)
                    {
                        Add(text.Value);
                    }

                    if (RelativeOf(operand) is { } offset
                        && CalendarValues.ParseRelative(offset) is { } steps
                        && CalendarValues.ParseDate(scope.Today) is { } baseDay
                        && CalendarValues.ApplyRelative(baseDay, steps) is { } day)
                    {
                        Add(CalendarValues.FormatDate(day));
                    }
                }
            }
        }

        Own(subject);
        foreach (var condition in conditions)
        {
            var others = OperandsOf(condition).Select(OtherFieldOf).OfType<string>().Select(other => $"field:{other}").ToList();
            if (SubjectOf(condition) == subject)
            {
                others.ForEach(Own);
            }
            else if (others.Contains(subject))
            {
                Own(SubjectOf(condition));
            }
        }

        return literals;
    }

    /// <summary>Whether a subject is compared with another answer, or another with it.</summary>
    private static bool Compared(string subject, IReadOnlyList<LogicRule> rules) =>
        ConditionsIn(rules).Any(condition =>
            OperandsOf(condition).Any(operand => OtherFieldOf(operand) is not null)
            && (SubjectOf(condition) == subject || OperandsOf(condition).Any(operand => $"field:{OtherFieldOf(operand)}" == subject)));

    /// <summary>
    /// Whether a subject's answers the rules don't name are all alike to them: each condition on it only tests it for being
    /// equal to texts it names, and no condition compares it with another answer. One such answer then stands for them all.
    /// </summary>
    private static bool OthersAlike(string subject, IReadOnlyList<LogicRule> rules) =>
        !Compared(subject, rules)
        && ConditionsOn(subject, rules).All(condition => Alike.Contains(condition.Operator) && OperandsOf(condition).All(operand => operand is TextOperand));

    /// <summary>A text none of the rules' texts is, compared as the engine compares texts.</summary>
    private static string FreshText(IReadOnlyList<string> literals)
    {
        var taken = literals.Select(TextSemantics.Fold).ToHashSet(StringComparer.Ordinal);
        var text = "zz-other";
        while (taken.Contains(TextSemantics.Fold(text)))
        {
            text += "-";
        }

        return text;
    }

    /// <summary>A plain answer of an ordered kind, tried where the rules name none.</summary>
    private static string? PlainOf(FieldKind kind, string today) => kind switch
    {
        FieldKind.Number => "1",
        FieldKind.Date => today,
        FieldKind.Time => "12:00",
        FieldKind.DateTime => $"{today}T12:00",
        _ => null,
    };

    [GeneratedRegex(@"^-?\d+(\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"^(\d{2}):(\d{2})", RegexOptions.CultureInvariant)]
    private static partial Regex TimeOfDay();

    /// <summary>Next to a number, a date, a time or a date and time: one less and one more, which comparisons tell apart.</summary>
    private static List<string> Neighbours(FieldKind kind, string text)
    {
        if (kind == FieldKind.Number)
        {
            // A plain decimal well inside what both runtimes write without an exponent, so they write its neighbours alike.
            if (!PlainNumber().IsMatch(text) || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || Math.Abs(value) >= 1e15)
            {
                return [];
            }

            // Below a ten-thousandth, C# writes a number with an exponent and JavaScript doesn't, so such a neighbour isn't tried.
            return [.. new[] { value - 1, value + 1 }.Where(one => one == 0 || Math.Abs(one) >= 1e-4).Select(one => one.ToString(CultureInfo.InvariantCulture))];
        }

        if (kind is FieldKind.Date or FieldKind.DateTime)
        {
            if (CalendarValues.ParseDate(text.Length >= 10 ? text[..10] : text) is not { } day)
            {
                return [];
            }

            var rest = text.Length >= 10 ? text[10..] : string.Empty;
            return [CalendarValues.FormatDate(day - 1) + rest, CalendarValues.FormatDate(day + 1) + rest];
        }

        if (kind == FieldKind.Time)
        {
            var match = TimeOfDay().Match(text);
            if (!match.Success)
            {
                return [];
            }

            var minutes = (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60) + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            static string At(int value)
            {
                var wrapped = (value + 1440) % 1440;
                return $"{(wrapped / 60).ToString("00", CultureInfo.InvariantCulture)}:{(wrapped % 60).ToString("00", CultureInfo.InvariantCulture)}";
            }

            return [At(minutes - 1), At(minutes + 1)];
        }

        return [];
    }

    /// <summary>
    /// What a subject is tried with; where one text stands for the answers the rules don't name, that one; and whether the
    /// answers tried stand for every answer a visitor can give.
    /// </summary>
    private sealed record Tries(List<IReadOnlyList<string>> Candidates, string? Fresh, IReadOnlyList<string> Literals, bool Complete);

    /// <summary>
    /// The answers a subject is tried with, each one a visitor can give: none first (a language is always one of the
    /// site's), then what the rules name and what lies next to it. A choice keeps to its options where the form knows
    /// them; member groups are tried with signing in (<see cref="AnswersOf"/>). They stand for every answer where no answer
    /// outside them could make a condition on the subject come out otherwise: ticks, the signing in, the site's
    /// languages, numbers of files, whole days not compared with another answer (between two neighbours lies no other), a
    /// choice whose options the form knows, a text or a number only tested for being equal (one tried being none the
    /// rules name), and several choices where at most two named options matter and counts tell at most two ticked from
    /// fewer, one from more. A field whose answers are several where the rules read them (<see cref="SearchScope.Several"/>),
    /// or a query value, which a link may give twice, never does.
    /// </summary>
    private static Tries TriesOf(string subject, IReadOnlyList<LogicRule> rules, SearchScope scope)
    {
        var kind = KindOf(subject, scope);
        var literals = LiteralsOf(subject, rules, scope, kind == FieldKind.Files);
        var ops = ConditionsOn(subject, rules).Select(condition => condition.Operator).ToList();
        Tries Tried(List<IReadOnlyList<string>> candidates, bool complete) => new(candidates, null, literals, complete);
        var relative = ConditionsIn(rules).Any(condition => OperandsOf(condition).Any(operand => RelativeOf(operand) is not null));
        if (subject == "context:memberSignedIn" || kind == FieldKind.Checkbox)
        {
            return Tried([[], ["true"]], complete: true);
        }

        if (subject == "context:today")
        {
            var days = literals.SelectMany(text => (IEnumerable<string>)[text, .. Neighbours(FieldKind.Date, text)]);

            // No visit is on a day before today; days counted from today move with it, so a today tried at another day stands
            // for no other.
            var coming = days.Where(day => string.CompareOrdinal(day, scope.Today) >= 0);
            return Tried(Unique([[scope.Today], .. coming.Select(day => (IReadOnlyList<string>)[day])]), complete: !relative);
        }

        if (subject == "context:culture")
        {
            var cultures = scope.Cultures.Count > 0 ? scope.Cultures : literals;
            return Tried(Unique(Ordered(literals, cultures).Select(culture => (IReadOnlyList<string>)[culture])), complete: scope.Cultures.Count > 0);
        }

        if (kind == FieldKind.Files)
        {
            var counts = literals.SelectMany(text => PlainNumber().IsMatch(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var count) && count == Math.Floor(count) && !double.IsInfinity(count)
                ? (IEnumerable<double>)[count - 1, count, count + 1]
                : []);
            return Tried(Unique([[], .. counts.Prepend(1d).Where(count => count > 0).Select(count => (IReadOnlyList<string>)Files(count))]), complete: true);
        }

        var known = subject.StartsWith("field:", StringComparison.Ordinal) ? scope.Options(subject["field:".Length..]) : [];
        // At most two options ticked are tried: "at least" two can be told from fewer, "at most" one from more.
        var fewCounts = ConditionsOn(subject, rules).All(condition => condition.Operator switch
        {
            "countAtLeast" => CountOf(condition) <= 2,
            "countAtMost" => CountOf(condition) <= 1,
            _ => true,
        });
        var several = subject.StartsWith("field:", StringComparison.Ordinal) && scope.Several?.Invoke(subject["field:".Length..]) == true;
        if (kind == FieldKind.Choices)
        {
            var values = known.Count > 0 ? Ordered(literals, known) : [.. literals];
            var named = literals.Where(values.Contains).ToList();
            var all = Unique([[], .. values.Select(value => (IReadOnlyList<string>)[value]), named, .. Pairs(values)]);
            var groups = subject == "context:memberGroup";

            // A member may be in any group, so groups stand for the others only where the conditions name them.
            var complete = all.Count <= MostSets && named.Count <= 2 && fewCounts && (groups ? ops.All(Named.Contains) : known.Count > 0);
            return Tried([.. all.Take(MostSets)], complete && !several);
        }

        if (kind == FieldKind.Choice)
        {
            return Tried(Unique([[], .. (known.Count > 0 ? Ordered(literals, known) : [.. literals]).Select(value => (IReadOnlyList<string>)[value])]), complete: known.Count > 0 && !several);
        }

        if (kind == FieldKind.Text)
        {
            var fresh = OthersAlike(subject, rules) ? FreshText(literals) : null;
            IEnumerable<string> tried = fresh is not null ? [.. literals, fresh] : literals;

            // A query value may be given several times, and so may a repeatable group's child read from outside it.
            var complete = fresh is not null && !several && !subject.StartsWith("context:query.", StringComparison.Ordinal);
            return new Tries(Unique([[], .. tried.Select(value => (IReadOnlyList<string>)[value])]), fresh, literals, complete);
        }

        if (kind is not { } ordered)
        {
            return Tried([[], .. literals.Select(value => (IReadOnlyList<string>)[value])], complete: false);
        }

        var near = literals.SelectMany(text => Neighbours(ordered, text)).ToList();
        IEnumerable<string> given = literals.Count > 0 || PlainOf(ordered, scope.Today) is not { } plain ? [] : [plain];

        // Whole days have no answer between two neighbours (times may hold seconds); numbers and moments stand for the others
        // only where tested for equality and one tried is none the rules name. An answer compared with another stands for
        // nothing, as the other moves too.
        var discrete = ordered == FieldKind.Date && !relative && !Compared(subject, rules);
        var other = near.Any(value => !literals.Contains(value)) || literals.Count == 0;
        var whole = !several && (discrete || (OthersAlike(subject, rules) && !relative && other));
        return Tried(Unique([[], .. literals.Concat(near).Concat(given).Select(value => (IReadOnlyList<string>)[value])]), whole);
    }

    /// <summary>The number of options a count names, as both runtimes read it: a plain decimal; NaN for any other operand.</summary>
    private static double CountOf(LogicCondition condition) =>
        condition.Value is TextOperand count && PlainNumber().IsMatch(count.Value)
            ? double.Parse(count.Value, NumberStyles.Float, CultureInfo.InvariantCulture)
            : double.NaN;

    /// <summary>The values a subject may take, those the rules name first.</summary>
    private static List<string> Ordered(IReadOnlyList<string> named, IReadOnlyList<string> all) =>
        [.. named.Where(all.Contains).Concat(all).Distinct(StringComparer.Ordinal)];

    private static List<string> Files(double count) =>
        [.. Enumerable.Range(1, (int)Math.Min(count, 99)).Select(at => $"file{at.ToString(CultureInfo.InvariantCulture)}")];

    private static IEnumerable<IReadOnlyList<string>> Pairs(IReadOnlyList<string> values) =>
        values.SelectMany((one, at) => values.Skip(at + 1).Select(other => (IReadOnlyList<string>)[one, other]));

    private static List<IReadOnlyList<string>> Unique(IEnumerable<IReadOnlyList<string>> lists)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new List<IReadOnlyList<string>>();
        foreach (var list in lists)
        {
            if (seen.Add(string.Join('\u001f', list) + "\u001e" + list.Count.ToString(CultureInfo.InvariantCulture)))
            {
                unique.Add([.. list]);
            }
        }

        return unique;
    }

    // Trying.

    /// <summary>The answers and the visit of one try; null for a visit no visitor makes, member groups without signing in.</summary>
    private static (IReadOnlyDictionary<string, IReadOnlyList<string>> Values, EvaluationContext Context)? AnswersOf(List<string> subjects, IReadOnlyList<IReadOnlyList<string>> values, SearchScope scope)
    {
        var fields = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var culture = scope.Cultures.Count > 0 ? scope.Cultures[0] : string.Empty;
        bool? signedIn = null;
        IReadOnlyList<string> groups = [];
        var today = scope.Today;
        for (var at = 0; at < subjects.Count; at++)
        {
            var subject = subjects[at];
            var given = at < values.Count ? values[at] : [];
            if (subject.StartsWith("field:", StringComparison.Ordinal))
            {
                fields[subject["field:".Length..]] = given;
            }
            else if (subject == "context:culture")
            {
                culture = given.Count > 0 ? given[0] : culture;
            }
            else if (subject == "context:memberSignedIn")
            {
                signedIn = given.Count > 0;
            }
            else if (subject == "context:memberGroup")
            {
                groups = given;
            }
            else if (subject == "context:today")
            {
                today = given.Count > 0 ? given[0] : today;
            }
            else if (subject.StartsWith("context:query.", StringComparison.Ordinal) && given.Count > 0)
            {
                query[subject["context:query.".Length..]] = [given[0]];
            }
        }

        // Only a signed-in member is in groups. The engine reads each part of the visit alone, so where no rule asks
        // whether the visitor signed in, it makes no difference what the visit says.
        if (signedIn == false && groups.Count > 0)
        {
            return null;
        }

        return (fields, new EvaluationContext($"{today}T12:00:00Z", "UTC", culture, new Member(signedIn == true, [.. groups]), query, []));
    }

    /// <summary>Whether a rule matches the answers; null where the engine refuses the rule or it is off.</summary>
    private static bool? Matches(LogicRule rule, (IReadOnlyDictionary<string, IReadOnlyList<string>> Values, EvaluationContext Context) answers, SearchScope scope)
    {
        try
        {
            return Rules.EvaluateRule(rule, new Subject(scope.Fields, answers.Values, answers.Context));
        }
        catch (LogicException)
        {
            return null;
        }
    }

    /// <summary>How many sets of answers the lists make, counted up to <see cref="MostCounted"/>.</summary>
    private static long TrialsOf<T>(IEnumerable<IReadOnlyList<T>> lists) =>
        lists.Aggregate(1L, (total, list) =>
        {
            var count = Math.Max(list.Count, 1);
            return total > MostCounted / count ? MostCounted : total * count;
        });

    /// <summary>The first set of answers, one from each list, for which <paramref name="holds"/> says yes; at most <see cref="MostTried"/> sets are tried.</summary>
    private static List<IReadOnlyList<string>>? Search(IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> lists, Func<IReadOnlyList<IReadOnlyList<string>>, bool> holds)
    {
        // The longest lists lose their last tries first, so every subject keeps its answers that matter most.
        var trimmed = lists.Select(list => list.ToList()).ToList();
        while (TrialsOf(trimmed) > MostTried)
        {
            var longest = 0;
            for (var at = 0; at < trimmed.Count; at++)
            {
                if (trimmed[at].Count > trimmed[longest].Count)
                {
                    longest = at;
                }
            }

            trimmed[longest].RemoveAt(trimmed[longest].Count - 1);
        }

        var place = new int[trimmed.Count];
        while (true)
        {
            var values = trimmed.Select((list, i) => list.Count > place[i] ? list[place[i]] : []).ToList();
            if (holds(values))
            {
                return values;
            }

            var i2 = 0;
            for (; i2 < place.Length; i2++)
            {
                place[i2]++;
                if (place[i2] < trimmed[i2].Count)
                {
                    break;
                }

                place[i2] = 0;
            }

            if (i2 == place.Length)
            {
                return null;
            }
        }
    }
}
