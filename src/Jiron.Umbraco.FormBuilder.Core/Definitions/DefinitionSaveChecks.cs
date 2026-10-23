using System.Buffers;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// What every save refuses on top of the document's shape (ADR-0002, ADR-0034): the element limits of section 4.3.1,
/// the text limits, values out of range, empty keys and two elements with one key. They run on the model, so a
/// definition built in code is held to the same rules as one read from JSON.
/// </summary>
internal sealed class DefinitionSaveChecks
{
    /// <summary>Plain texts that render on one line: at most 500 characters, no line break.</summary>
    public const int LineLength = 500;

    /// <summary>Other plain texts.</summary>
    public const int PlainLength = 2000;

    /// <summary>Rich texts.</summary>
    public const int HtmlLength = 50000;

    /// <summary>An option's value, which entries store.</summary>
    public const int OptionValueLength = 200;

    /// <summary>A field type's or an action type's alias.</summary>
    public const int TypeLength = 128;

    /// <summary>A theme's alias.</summary>
    public const int ThemeLength = 64;

    private const int MaxDays = 36500;

    // CR, LF, NEL, and the line and paragraph separators.
    private static readonly SearchValues<char> LineBreaks = SearchValues.Create([(char)13, (char)10, (char)0x85, (char)0x2028, (char)0x2029]);

    private readonly DefinitionLimits _limits;
    private readonly List<DefinitionProblem> _problems = [];
    private readonly Dictionary<Guid, string> _keys = [];
    private int _groups;
    private int _fields;
    private int _rules;

    private DefinitionSaveChecks(DefinitionLimits limits) => _limits = limits;

    private enum TextKind
    {
        Line,
        Plain,
        Html,
    }

    /// <summary>Every problem the definition has, in document order, at most <see cref="DefinitionShapeReader.MaxProblems"/>.</summary>
    public static IReadOnlyList<DefinitionProblem> Check(FormDefinition definition, DefinitionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(limits);
        var checks = new DefinitionSaveChecks(limits);
        checks.CheckDefinition(definition);
        return checks._problems;
    }

    private static string P(string path, string name) => JsonPaths.Property(path, name);

    private static string I(string path, int index) => JsonPaths.Index(path, index);

    private static bool HasLineBreak(string text) => text.AsSpan().IndexOfAny(LineBreaks) >= 0;

    private void Fail(string code, string path, string message)
    {
        if (_problems.Count < DefinitionShapeReader.MaxProblems)
        {
            _problems.Add(new DefinitionProblem(code, path, message));
        }
    }

    private void CheckDefinition(FormDefinition definition)
    {
        Key(definition.Key, P(JsonPaths.Root, "key"));
        CheckSettings(definition.Settings, P(JsonPaths.Root, "settings"));

        var pagesPath = P(JsonPaths.Root, "pages");
        if (definition.Pages.Count > _limits.MaxPages)
        {
            Fail(DefinitionErrors.LimitExceeded, pagesPath, $"A form has at most {_limits.MaxPages} pages.");
        }

        for (var i = 0; i < definition.Pages.Count; i++)
        {
            CheckPage(definition.Pages[i], I(pagesPath, i));
        }

        var validationPath = P(JsonPaths.Root, "validationRules");
        for (var i = 0; i < definition.ValidationRules.Count; i++)
        {
            var rule = definition.ValidationRules[i];
            var path = I(validationPath, i);
            CountRule(path);
            Key(rule.Key, P(path, "key"));
            Reference(rule.Field, P(path, "field"));
            Text(rule.Message, P(path, "message"), TextKind.Line);
            Conditions(rule.Conditions, P(path, "conditions"));
        }

        var actionsPath = P(JsonPaths.Root, "actions");
        if (definition.Actions.Count > _limits.MaxActions)
        {
            Fail(DefinitionErrors.LimitExceeded, actionsPath, $"A form has at most {_limits.MaxActions} actions.");
        }

        for (var i = 0; i < definition.Actions.Count; i++)
        {
            var action = definition.Actions[i];
            var path = I(actionsPath, i);
            Key(action.Key, P(path, "key"));
            String(action.Type, P(path, "type"), TypeLength, singleLine: true);
            String(action.Name, P(path, "name"), LineLength, singleLine: true);
            Rules(action.Rules, P(path, "rules"));
        }

        var outcomesPath = P(JsonPaths.Root, "outcomes");
        if (definition.Outcomes.Count > _limits.MaxOutcomes)
        {
            Fail(DefinitionErrors.LimitExceeded, outcomesPath, $"A form has at most {_limits.MaxOutcomes} outcomes.");
        }

        for (var i = 0; i < definition.Outcomes.Count; i++)
        {
            var outcome = definition.Outcomes[i];
            var path = I(outcomesPath, i);
            Key(outcome.Key, P(path, "key"));
            Text(outcome.Message, P(path, "message"), TextKind.Html);
            if (outcome.Redirect is { } redirect)
            {
                var redirectPath = P(path, "redirect");
                if (redirect.Content is { } content)
                {
                    Reference(content, P(redirectPath, "content"));
                }

                String(redirect.Url, P(redirectPath, "url"), PlainLength, singleLine: true);
                Range(redirect.DelaySeconds, P(redirectPath, "delaySeconds"), 0, 600);
            }

            Rules(outcome.Rules, P(path, "rules"));
        }
    }

    private void CheckSettings(FormSettings settings, string path)
    {
        var submission = settings.Submission;
        var submissionPath = P(path, "submission");
        Text(submission.SubmitLabel, P(submissionPath, "submitLabel"), TextKind.Line);
        Text(submission.NextLabel, P(submissionPath, "nextLabel"), TextKind.Line);
        Text(submission.BackLabel, P(submissionPath, "backLabel"), TextKind.Line);
        var storeDaysPath = P(submissionPath, "storeDays");
        if (submission.Store == EntryStorage.Days)
        {
            if (submission.StoreDays is { } days)
            {
                Range(days, storeDaysPath, 1, MaxDays);
            }
            else
            {
                Fail(DefinitionErrors.InvalidValue, storeDaysPath, "Storing entries for some days needs the number of days.");
            }
        }
        else if (submission.StoreDays is not null)
        {
            Fail(DefinitionErrors.InvalidValue, storeDaysPath, "The number of days belongs only to storing entries for some days.");
        }

        if (submission.Verification is { } verification)
        {
            var verificationPath = P(submissionPath, "verification");
            Reference(verification.EmailField, P(verificationPath, "emailField"));
            Range(verification.Days, P(verificationPath, "days"), 1, 90);
        }

        if (submission.Resume is { } resume)
        {
            Range(resume.Days, P(P(submissionPath, "resume"), "days"), 1, 365);
        }

        String(submission.EntryTitle, P(submissionPath, "entryTitle"), LineLength, singleLine: true);
        if (submission.EntryReference is { } reference)
        {
            var referencePath = P(submissionPath, "entryReference");
            String(reference.Prefix, P(referencePath, "prefix"), LineLength, singleLine: true);
            Range(reference.Digits, P(referencePath, "digits"), 1, 10);
        }

        var availability = settings.Availability;
        var availabilityPath = P(path, "availability");
        Instant(availability.OpensAt, P(availabilityPath, "opensAt"));
        Instant(availability.ClosesAt, P(availabilityPath, "closesAt"));
        if (availability.MaxEntries is { } maxEntries)
        {
            Range(maxEntries, P(availabilityPath, "maxEntries"), 1, int.MaxValue);
        }

        Text(availability.ClosedMessage, P(availabilityPath, "closedMessage"), TextKind.Html);
        for (var i = 0; i < availability.MemberGroups.Count; i++)
        {
            Reference(availability.MemberGroups[i], I(P(availabilityPath, "memberGroups"), i));
        }

        if (settings.Protection.Captcha is { } captcha)
        {
            String(captcha.Provider, P(P(P(path, "protection"), "captcha"), "provider"), TypeLength, singleLine: true);
        }

        var retention = settings.Privacy.RetentionDays;
        var retentionPath = P(P(path, "privacy"), "retentionDays");
        foreach (var (state, days) in new[] { ("submitted", retention.Submitted), ("approved", retention.Approved), ("rejected", retention.Rejected), ("spam", retention.Spam) })
        {
            if (days is { } value)
            {
                Range(value, P(retentionPath, state), 1, MaxDays);
            }
        }

        var appearancePath = P(path, "appearance");
        String(settings.Appearance.Theme, P(appearancePath, "theme"), ThemeLength, singleLine: true);
        String(settings.Appearance.CssClass, P(appearancePath, "cssClass"), LineLength, singleLine: true);

        var developerPath = P(path, "developer");
        Strings(settings.Developer.QueryValues, P(developerPath, "queryValues"), LineLength);
        Strings(settings.Developer.ContextValues, P(developerPath, "contextValues"), LineLength);
    }

    private void CheckPage(FormPage page, string path)
    {
        Key(page.Key, P(path, "key"));
        Text(page.Title, P(path, "title"), TextKind.Line);
        Text(page.Description, P(path, "description"), TextKind.Html);
        Rules(page.Rules, P(path, "rules"));
        var groupsPath = P(path, "groups");
        for (var i = 0; i < page.Groups.Count; i++)
        {
            var groupPath = I(groupsPath, i);
            if (++_groups == _limits.MaxGroups + 1)
            {
                Fail(DefinitionErrors.LimitExceeded, groupPath, $"A form has at most {_limits.MaxGroups} groups.");
            }

            CheckGroup(page.Groups[i], groupPath);
        }
    }

    private void CheckGroup(FormGroup group, string path)
    {
        Key(group.Key, P(path, "key"));
        Text(group.Title, P(path, "title"), TextKind.Line);
        Text(group.Description, P(path, "description"), TextKind.Html);
        Range(group.Columns, P(path, "columns"), 1, 4);
        Rules(group.Rules, P(path, "rules"));
        Fields(group.Fields, P(path, "fields"), Math.Clamp(group.Columns, 1, 4));
    }

    private void Fields(IReadOnlyList<FormField> list, string path, int columns)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var fieldPath = I(path, i);
            CountField(fieldPath);
            CheckField(list[i], fieldPath, columns);
        }
    }

    private void CountField(string path)
    {
        if (++_fields == _limits.MaxFields + 1)
        {
            Fail(DefinitionErrors.LimitExceeded, path, $"A form has at most {_limits.MaxFields} fields, counting the fields of repeatable groups and the rows of grids.");
        }
    }

    private void CheckField(FormField field, string path, int columns)
    {
        Key(field.Key, P(path, "key"));
        String(field.Type, P(path, "type"), TypeLength, singleLine: true);
        String(field.Alias, P(path, "alias"), LineLength, singleLine: true);
        Strings(field.FormerAliases, P(path, "formerAliases"), LineLength);
        Range(field.Span, P(path, "span"), 1, columns);
        Text(field.Label, P(path, "label"), TextKind.Line);
        Text(field.Help, P(path, "help"), TextKind.Plain);
        Text(field.Placeholder, P(path, "placeholder"), TextKind.Line);
        Messages(field.Messages, P(path, "messages"));

        if (field.DefaultValue is { } defaultValue)
        {
            var defaultPath = P(path, "defaultValue");
            for (var i = 0; i < defaultValue.Values.Count; i++)
            {
                String(defaultValue.Values[i], defaultValue.IsList ? I(defaultPath, i) : defaultPath, PlainLength, singleLine: false);
            }
        }

        if (field.Prefill is { } prefill)
        {
            var prefillPath = P(path, "prefill");
            String(prefill.Query, P(prefillPath, "query"), LineLength, singleLine: true);
            String(prefill.Member, P(prefillPath, "member"), LineLength, singleLine: true);
        }

        String(field.Autocomplete, P(path, "autocomplete"), LineLength, singleLine: true);
        String(field.CssClass, P(path, "cssClass"), LineLength, singleLine: true);

        var validationPath = P(path, "validation");
        for (var i = 0; i < field.Validation.Count; i++)
        {
            var check = field.Validation[i];
            var checkPath = I(validationPath, i);
            switch (check)
            {
                case PatternCheck pattern:
                    String(pattern.Pattern, P(checkPath, "pattern"), PlainLength, singleLine: false);
                    break;
                case MatchesFieldCheck matches:
                    Reference(matches.Field, P(checkPath, "field"));
                    break;
            }

            Text(check.Message, P(checkPath, "message"), TextKind.Line);
        }

        Rules(field.Rules, P(path, "rules"));
        if (field.Options is { } options)
        {
            CheckOptions(options, P(path, "options"));
        }

        var rowsPath = P(path, "rows");
        for (var i = 0; i < field.Rows.Count; i++)
        {
            var row = field.Rows[i];
            var rowPath = I(rowsPath, i);
            CountField(rowPath);
            Key(row.Key, P(rowPath, "key"));
            Text(row.Label, P(rowPath, "label"), TextKind.Line);
        }

        Fields(field.Fields, P(path, "fields"), columns);
    }

    private void Messages(FieldMessages messages, string path)
    {
        var all = new (string Name, LocalizedText? Text)[]
        {
            ("required", messages.Required), ("invalid", messages.Invalid), ("minLength", messages.MinLength),
            ("maxLength", messages.MaxLength), ("minWords", messages.MinWords), ("maxWords", messages.MaxWords),
            ("min", messages.Min), ("max", messages.Max), ("step", messages.Step), ("dateWindow", messages.DateWindow),
            ("selections", messages.Selections), ("fileType", messages.FileType), ("fileSize", messages.FileSize),
            ("fileCount", messages.FileCount), ("full", messages.Full),
        };
        foreach (var (name, text) in all)
        {
            Text(text, P(path, name), TextKind.Line);
        }
    }

    private void CheckOptions(FieldOptions options, string path)
    {
        var itemsPath = P(path, "items");
        if (options.Items.Count > _limits.MaxOptionsPerField)
        {
            Fail(DefinitionErrors.LimitExceeded, itemsPath, $"A field has at most {_limits.MaxOptionsPerField} options of its own.");
        }

        for (var i = 0; i < options.Items.Count; i++)
        {
            var option = options.Items[i];
            var optionPath = I(itemsPath, i);
            OptionValue(option.Value, P(optionPath, "value"));
            Text(option.Label, P(optionPath, "label"), TextKind.Line);
            Text(option.Description, P(optionPath, "description"), TextKind.Plain);
            if (option.Image is { } image)
            {
                Reference(image, P(optionPath, "image"));
            }

            if (option.Places is { } places)
            {
                Range(places, P(optionPath, "places"), 0, 1_000_000);
            }

            var parentValuesPath = P(optionPath, "parentValues");
            for (var j = 0; j < option.ParentValues.Count; j++)
            {
                OptionValue(option.ParentValues[j], I(parentValuesPath, j));
            }
        }

        if (options.List is { } list)
        {
            Reference(list, P(path, "list"));
        }

        if (options.Parent is { } parent)
        {
            Reference(parent, P(path, "parent"));
        }
    }

    private void OptionValue(string value, string path)
    {
        if (value.Length == 0)
        {
            Fail(DefinitionErrors.InvalidValue, path, "An option's value has at least one character.");
            return;
        }

        String(value, path, OptionValueLength, singleLine: true);
    }

    private void Rules(IReadOnlyList<FormRule> list, string path)
    {
        for (var i = 0; i < list.Count; i++)
        {
            var rulePath = I(path, i);
            CountRule(rulePath);
            Key(list[i].Key, P(rulePath, "key"));
            Conditions(list[i].Conditions, P(rulePath, "conditions"));
        }
    }

    private void CountRule(string path)
    {
        if (++_rules == _limits.MaxRules + 1)
        {
            Fail(DefinitionErrors.LimitExceeded, path, $"A form has at most {_limits.MaxRules} rules.");
        }
    }

    private void Conditions(IReadOnlyList<RuleItem> items, string path)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = I(path, i);
            switch (items[i])
            {
                case RuleGroup group:
                    Conditions(group.Conditions, P(itemPath, "conditions"));
                    break;
                case RuleCondition condition:
                    if (condition.Field is { } field)
                    {
                        Reference(field, P(itemPath, "field"));
                    }

                    String(condition.Context, P(itemPath, "context"), LineLength, singleLine: true);
                    String(condition.Op, P(itemPath, "op"), LineLength, singleLine: true);
                    if (condition.Value is { } value)
                    {
                        Operand(value, P(itemPath, "value"));
                    }

                    break;
            }
        }
    }

    private void Operand(ConditionOperand operand, string path)
    {
        switch (operand)
        {
            case ConditionOperand.Text text:
                String(text.Value, path, PlainLength, singleLine: false);
                break;
            case ConditionOperand.Field field:
                Reference(field.Key, P(path, "field"));
                break;
            case ConditionOperand.Today today:
                String(today.Offset, P(path, "today"), LineLength, singleLine: true);
                break;
            case ConditionOperand.List list:
                // A list holds at least one item and no list (its constructor sees to that).
                for (var i = 0; i < list.Items.Count; i++)
                {
                    Operand(list.Items[i], I(path, i));
                }

                break;
        }
    }

    private void Key(Guid key, string path)
    {
        if (key == Guid.Empty)
        {
            Fail(DefinitionErrors.InvalidValue, path, "A key is not the empty GUID.");
            return;
        }

        if (!_keys.TryAdd(key, path))
        {
            Fail(DefinitionErrors.DuplicateKey, path, $"The key {key} is already the key of {_keys[key]}.");
        }
    }

    private void Reference(Guid key, string path)
    {
        if (key == Guid.Empty)
        {
            Fail(DefinitionErrors.InvalidValue, path, "The empty GUID names nothing.");
        }
    }

    private void Range(int value, string path, int min, int max)
    {
        if (value < min || value > max)
        {
            Fail(DefinitionErrors.InvalidValue, path, string.Create(CultureInfo.InvariantCulture, $"The value is {min} to {max}, not {value}."));
        }
    }

    private void Instant(DateTime? value, string path)
    {
        if (value is { } instant && !UtcInstantConverter.IsWritable(instant))
        {
            Fail(DefinitionErrors.InvalidValue, path, "An instant is in UTC and has whole seconds.");
        }
    }

    private void Strings(IReadOnlyList<string> values, string path, int max)
    {
        for (var i = 0; i < values.Count; i++)
        {
            String(values[i], I(path, i), max, singleLine: true);
        }
    }

    private void String(string? value, string path, int max, bool singleLine)
    {
        if (value is null)
        {
            return;
        }

        if (!StrictStringConverter.IsWellFormed(value))
        {
            Fail(DefinitionErrors.InvalidValue, path, "The text holds a lone surrogate.");
        }
        else if (value.Length > max)
        {
            Fail(DefinitionErrors.LimitExceeded, path, $"The text has at most {max} characters.");
        }
        else if (singleLine && HasLineBreak(value))
        {
            Fail(DefinitionErrors.InvalidValue, path, "The text has no line break.");
        }
    }

    private void Text(LocalizedString? text, string path, TextKind kind)
    {
        if (text is null)
        {
            return;
        }

        var max = kind switch
        {
            TextKind.Line => LineLength,
            TextKind.Plain => PlainLength,
            _ => HtmlLength,
        };
        foreach (var (culture, value) in text.Texts)
        {
            String(value, P(path, culture), max, kind == TextKind.Line);
        }
    }
}
