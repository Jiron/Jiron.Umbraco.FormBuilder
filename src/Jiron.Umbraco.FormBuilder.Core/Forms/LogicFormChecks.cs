using System.Collections.Frozen;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// What the rule search tries a form's answers with on this site (ROADMAP 9.5d, ADR-0179): the kinds of its fields and
/// grid rows as its rules see them, each choice's own options (<see cref="OwnOptions"/>), the
/// site's languages, today in the site's time zone, the fields the form hides for the answers tried, as the form engine
/// decides, and the repeatable groups' child fields, whose answers are several where rules outside them read them.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="languages">The site's languages.</param>
/// <param name="settings">The settings, whose general section holds the site's time zone.</param>
/// <param name="time">The clock.</param>
/// <param name="links">The server's links of older time zone names.</param>
internal sealed class RuleSearchScopes(FieldTypeRegistry types, ILanguageService languages, IFormBuilderSettingsService settings, TimeProvider time, SystemTimeZoneLinks links)
{
    /// <summary>The form as its rules see it, and what the search tries its answers with.</summary>
    public async Task<(LogicForm Logic, SearchScope Scope)> OfAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var logic = DefinitionLogic.From(definition, types);
        var kinds = new Dictionary<string, FieldKind>(StringComparer.Ordinal);
        var children = new HashSet<string>(StringComparer.Ordinal);
        void Kinds(IReadOnlyList<LogicField> fields, bool inRows)
        {
            foreach (var field in fields)
            {
                if (field.Kind is { } kind)
                {
                    kinds[field.Id] = kind;
                }

                foreach (var row in field.Rows)
                {
                    kinds[row.Id] = row.Kind;
                }

                if (inRows)
                {
                    children.Add(field.Id);
                    children.UnionWith(field.Rows.Select(row => row.Id));
                }

                Kinds(field.Fields, inRows: true);
            }
        }

        foreach (var group in logic.Pages.SelectMany(page => page.Groups))
        {
            Kinds(group.Fields, inRows: false);
        }

        var options = OwnOptions(definition);
        var general = await settings.GetAsync<GeneralSettings>(null, cancellationToken);
        var zone = CalendarValues.FindTimeZone(general.Value.TimeZone, links)?.Id ?? "UTC";
        var now = time.GetUtcNow().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        var cultures = (await languages.GetAllAsync()).Select(language => language.IsoCode).ToList();
        var scope = new SearchScope(
            kinds,
            key => options.TryGetValue(key, out var known) ? known : [],
            cultures,
            CalendarValues.FormatDate(CalendarValues.Today(now, zone)))
        {
            Hidden = RuleSearch.FormSight(logic),
            Several = children.Contains,
        };
        return (logic, scope);
    }

    /// <summary>
    /// The values of each choice whose options are its own, by its logic id and its grid rows' ids: the options listed on
    /// the field, and a yes or no's two (16.6's review). Options from a library list or a data source, a cascading list's,
    /// which hang on its parent's answer, and a choice whose "Other" takes any text have none: they are not the form's alone.
    /// </summary>
    /// <param name="definition">The form.</param>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> OwnOptions(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var options = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var field in DefinitionKeys.Fields(definition))
        {
            var other = field.Settings?.TryGetProperty("other", out var setting) == true && setting.ValueKind == JsonValueKind.True;
            IReadOnlyList<string>? values = field.Type == YesNo
                ? [YesNoFieldType.Yes, YesNoFieldType.No]
                : field.Options is { Source: OptionSource.Items, Parent: null } own && !other
                    ? [.. own.Items.Select(item => item.Value).Where(value => value.Length > 0)]
                    : null;
            if (values is null)
            {
                continue;
            }

            options[DefinitionLogic.Id(field.Key)] = values;
            foreach (var row in field.Rows)
            {
                options[DefinitionLogic.Id(row.Key)] = values;
            }
        }

        return options;
    }

    // The yes or no type's alias, whose two values are its options.
    private const string YesNo = "yesno";
}

/// <summary>
/// A required field that a rule can hide (ROADMAP 9.5d, ADR-0179): a warning on a field's require rule that holds for
/// answers a visitor gives while a rule hides the field, its own show or hide rule, its repeatable group's, its group's or
/// its page's. A hidden field is never required, so visitors can send without it. The rule search finds the answers
/// (the rules overview's own, ADR-0178), so a warning said is real. A field required of itself and shown by a rule is
/// required while shown, which is ordinary design and no issue.
/// </summary>
/// <param name="scopes">What the rule search tries answers with.</param>
internal sealed class RequiredHiddenFormCheck(RuleSearchScopes scopes) : IFormCheck
{
    /// <summary>A require rule that holds while a rule hides its field.</summary>
    public const string RequiredCanHide = "required_can_hide";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (logic, scope) = await scopes.OfAsync(definition, cancellationToken);
        var issues = new List<FormCheckIssue>();
        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var groups = JsonPaths.Property(JsonPaths.Index(pages, p), "groups");
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                // Those that hide a field nearest it come first: its own, its repeatable group's, its group's, its page's.
                var around = new List<(LogicRule Rule, string Whose)>();
                around.AddRange(logic.Pages[p].Groups[g].Rules.Select(rule => (rule, "its group's rule")));
                around.AddRange(logic.Pages[p].Rules.Select(rule => (rule, "its page's rule")));
                Fields(issues, definition.Pages[p].Groups[g].Fields, logic.Pages[p].Groups[g].Fields, JsonPaths.Property(JsonPaths.Index(groups, g), "fields"), around, scope);
            }
        }

        return issues;
    }

    private static void Fields(List<FormCheckIssue> issues, IReadOnlyList<FormField> fields, IReadOnlyList<LogicField> logic, string path, IReadOnlyList<(LogicRule Rule, string Whose)> around, SearchScope scope)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            var fieldPath = JsonPaths.Index(path, f);
            var own = logic[f].Rules.Select(rule => (rule, "its own rule")).ToList();
            List<(LogicRule Rule, string Whose)> hidings = [.. own, .. around];
            for (var r = 0; r < logic[f].Rules.Count; r++)
            {
                if (RuleSearch.ConflictOf(logic[f].Rules[r], [.. hidings.Select(one => one.Rule)], scope) is { } conflict)
                {
                    var whose = hidings.First(one => ReferenceEquals(one.Rule, conflict.Hiding)).Whose;
                    var name = fields[f].Alias ?? DefinitionLogic.Id(fields[f].Key);
                    issues.Add(new FormCheckIssue(
                        FormCheckSeverity.Warning,
                        RequiredCanHide,
                        JsonPaths.Index(JsonPaths.Property(fieldPath, "rules"), r),
                        $"This rule makes {name} required for answers for which {whose} hides it. A hidden field is never required, so visitors can send without it."));
                }
            }

            // A repeatable group's own rules hide its fields too.
            var inner = own.Select(one => (one.rule, "its repeatable group's rule")).Concat(around).ToList();
            Fields(issues, fields[f].Fields, logic[f].Fields, JsonPaths.Property(fieldPath, "fields"), inner, scope);
        }
    }
}

/// <summary>
/// An unreachable page (ROADMAP 9.5d, ADR-0179): a warning on a page whose show and hide rules let it show for none of
/// the answers a visitor can have given when it is reached, going forward: those of the earlier pages and of the visit,
/// the fields of the page itself and of the later pages not yet answered (but for those a default value, a prefill or a
/// calculation answers). It is said only where the rule search tried answers that stand for every answer a visitor
/// gives, so a page said to be unreachable is.
/// </summary>
/// <param name="scopes">What the rule search tries answers with.</param>
internal sealed class UnreachablePageFormCheck(RuleSearchScopes scopes) : IFormCheck
{
    /// <summary>A page no visitor reaches.</summary>
    public const string PageUnreachable = "page_unreachable";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var (logic, scope) = await scopes.OfAsync(definition, cancellationToken);
        var issues = new List<FormCheckIssue>();
        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < logic.Pages.Count; p++)
        {
            var rules = logic.Pages[p].Rules;
            if (!rules.Any(rule => rule.Effect is "show" or "hide"))
            {
                continue;
            }

            var unanswered = Enumerable.Range(p, logic.Pages.Count - p)
                .SelectMany(later => definition.Pages[later].Groups.Zip(logic.Pages[later].Groups))
                .SelectMany(group => Blank(group.First.Fields, group.Second.Fields))
                .ToList();
            if (RuleSearch.ShowsAtAll(rules, scope, unanswered) == Showing.Never)
            {
                var at = rules.ToList().FindIndex(rule => rule.Effect is "show" or "hide");
                issues.Add(new FormCheckIssue(
                    FormCheckSeverity.Warning,
                    PageUnreachable,
                    JsonPaths.Index(JsonPaths.Property(JsonPaths.Index(pages, p), "rules"), at),
                    $"No answers a visitor can give before page {p + 1} make it show, so a visitor going forward never reaches it."));
            }
        }

        return issues;
    }

    /// <summary>
    /// The keys of the fields a visitor going forward has not answered yet, of their grid rows and of their child fields:
    /// all but those with a default value, a prefill or a calculation, which hold an answer before the visitor gives one.
    /// </summary>
    private static IEnumerable<string> Blank(IReadOnlyList<FormField> fields, IReadOnlyList<LogicField> logic)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            if (fields[f].DefaultValue is null && fields[f].Prefill is null && logic[f].Calculation is null)
            {
                yield return logic[f].Id;
                foreach (var row in logic[f].Rows)
                {
                    yield return row.Id;
                }
            }

            foreach (var key in Blank(fields[f].Fields, logic[f].Fields))
            {
                yield return key;
            }
        }
    }
}

/// <summary>
/// A rule's value that is none of its choice's own options (16.6's review): a warning at each text a condition compares
/// a choice to with "is", "is not", "is one of" or "is none of" that the choice does not offer, such as "Yes" for a yes or
/// no question, whose answer is <c>yes</c>. A visitor never gives it, so the condition never holds as written, or always for
/// "is not". Only a choice whose options are the form's own is held to them (<see cref="RuleSearchScopes.OwnOptions"/>);
/// a value from a library list, a data source or another answer may come later.
/// </summary>
internal sealed class RuleValueOptionFormCheck : IFormCheck
{
    /// <summary>A condition's value that its choice does not offer.</summary>
    public const string RuleValueNotOption = "rule_value_not_option";

    private static readonly FrozenSet<string> Compared = new[] { "equals", "notEquals", "isOneOf", "isNoneOf" }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var options = RuleSearchScopes.OwnOptions(definition);
        var issues = new List<FormCheckIssue>();
        if (options.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
        }

        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = definition.Pages[p];
            var pagePath = JsonPaths.Index(pages, p);
            Rules(page.Rules, JsonPaths.Property(pagePath, "rules"));
            for (var g = 0; g < page.Groups.Count; g++)
            {
                var groupPath = JsonPaths.Index(JsonPaths.Property(pagePath, "groups"), g);
                Rules(page.Groups[g].Rules, JsonPaths.Property(groupPath, "rules"));
                Fields(page.Groups[g].Fields, JsonPaths.Property(groupPath, "fields"));
            }
        }

        var validationRules = JsonPaths.Property(JsonPaths.Root, "validationRules");
        for (var v = 0; v < definition.ValidationRules.Count; v++)
        {
            Conditions(definition.ValidationRules[v].Conditions, JsonPaths.Property(JsonPaths.Index(validationRules, v), "conditions"));
        }

        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var a = 0; a < definition.Actions.Count; a++)
        {
            Rules(definition.Actions[a].Rules, JsonPaths.Property(JsonPaths.Index(actions, a), "rules"));
        }

        var outcomes = JsonPaths.Property(JsonPaths.Root, "outcomes");
        for (var o = 0; o < definition.Outcomes.Count; o++)
        {
            Rules(definition.Outcomes[o].Rules, JsonPaths.Property(JsonPaths.Index(outcomes, o), "rules"));
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);

        void Fields(IReadOnlyList<FormField> fields, string path)
        {
            for (var f = 0; f < fields.Count; f++)
            {
                var fieldPath = JsonPaths.Index(path, f);
                Rules(fields[f].Rules, JsonPaths.Property(fieldPath, "rules"));
                Fields(fields[f].Fields, JsonPaths.Property(fieldPath, "fields"));
            }
        }

        void Rules(IReadOnlyList<FormRule> rules, string path)
        {
            for (var r = 0; r < rules.Count; r++)
            {
                Conditions(rules[r].Conditions, JsonPaths.Property(JsonPaths.Index(path, r), "conditions"));
            }
        }

        void Conditions(IReadOnlyList<RuleItem> items, string path)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var at = JsonPaths.Index(path, i);
                switch (items[i])
                {
                    case RuleGroup group:
                        Conditions(group.Conditions, JsonPaths.Property(at, "conditions"));
                        break;
                    case RuleCondition { Field: { } key } condition when Compared.Contains(condition.Operator)
                        && options.TryGetValue(DefinitionLogic.Id(key), out var values):
                        foreach (var (text, textPath) in Texts(condition.Value, JsonPaths.Property(at, "value")))
                        {
                            if (!values.Contains(text, StringComparer.Ordinal))
                            {
                                issues.Add(new FormCheckIssue(
                                    FormCheckSeverity.Warning,
                                    RuleValueNotOption,
                                    textPath,
                                    $"The condition compares the answer to '{text}', which is none of its options, so a visitor never gives it."));
                            }
                        }

                        break;
                }
            }
        }
    }

    // The texts an operand compares to, each with its path: one text, or the texts of a list; another answer or a date is none.
    private static IEnumerable<(string Text, string Path)> Texts(ConditionOperand? operand, string path) => operand switch
    {
        ConditionOperand.Text text => [(text.Value, path)],
        ConditionOperand.List list => list.Items
            .Select((item, index) => (Item: item, Path: JsonPaths.Index(path, index)))
            .Where(each => each.Item is ConditionOperand.Text)
            .Select(each => (((ConditionOperand.Text)each.Item).Value, each.Path)),
        _ => [],
    };
}
