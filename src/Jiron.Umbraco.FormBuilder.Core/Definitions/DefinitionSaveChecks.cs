using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// What every save refuses on top of the document's shape (ADR-0002, ADR-0034): the element limits of section 4.3.1,
/// the text limits, values out of range, words outside their enum, missing required values, empty keys and two
/// elements with one key. They run on the model, so a definition built in code is held to the same rules as one read
/// from JSON, and they refuse first, at its path, everything the writer refuses.
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

    /// <summary>The most days entries are kept or stored for.</summary>
    public const int MaxDays = 36500;

    /// <summary>The most days a verification link stays valid.</summary>
    public const int MaxVerificationDays = 90;

    /// <summary>The most days a resume link stays valid.</summary>
    public const int MaxResumeDays = 365;

    /// <summary>The most digits an entry reference pads its number to.</summary>
    public const int MaxReferenceDigits = 10;

    // Each field inside a field and each rule group inside a rule group opens two levels of the document, an array and
    // an object, so a definition that nests more than half as deep as a document may is too deep for any reader.
    private const int MaxNesting = FormDefinitionSerializer.MaxDepth / 2;

    // CR, LF, NEL, and the line and paragraph separators.
    private static readonly SearchValues<char> LineBreaks = SearchValues.Create([(char)13, (char)10, (char)0x85, (char)0x2028, (char)0x2029]);

    private readonly DefinitionLimits _limits;
    private readonly DefinitionProblems _problems = new();
    private readonly Dictionary<Guid, string> _keys = [];
    private int _groups;
    private int _fields;
    private int _rules;
    private int _nesting;
    private bool _tooDeep;

    private DefinitionSaveChecks(DefinitionLimits limits) => _limits = limits;

    private enum TextKind
    {
        Line,
        Plain,
        Html,
    }

    /// <summary>
    /// The problems of one text outside a definition, such as a setting that a new form takes into its definition
    /// (ADR-0044): the rules of a label on one line, or of rich text when <paramref name="html"/>.
    /// </summary>
    public static IReadOnlyList<DefinitionProblem> CheckText(LocalizedString text, bool html)
    {
        ArgumentNullException.ThrowIfNull(text);
        var checks = new DefinitionSaveChecks(new DefinitionLimits());
        checks.Text(text, JsonPaths.Root, html ? TextKind.Html : TextKind.Line);
        return checks._problems.ToReadOnlyList();
    }

    /// <summary>
    /// The problems of options outside a definition, such as a library list's items (ADR-0073): the rules of a field's
    /// own options, at most <paramref name="max"/> of them, each problem at its path under <paramref name="path"/>.
    /// </summary>
    public static IReadOnlyList<DefinitionProblem> CheckOptionItems(IReadOnlyList<FieldOption> items, int max, string path)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(path);
        var checks = new DefinitionSaveChecks(new DefinitionLimits());
        checks.OptionItems(items, path, max, $"A list has at most {max} options.");
        return checks._problems.ToReadOnlyList();
    }

    /// <summary>The problem of a definition that nests deeper than a document may: no reader reads it.</summary>
    public static DefinitionProblem NestsTooDeep() =>
        new(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"The definition nests deeper than {FormDefinitionSerializer.MaxDepth} levels, which no reader reads.");

    /// <summary>Every problem the definition has, in document order, at most <see cref="DefinitionProblems.Max"/>.</summary>
    public static IReadOnlyList<DefinitionProblem> Check(FormDefinition definition, DefinitionLimits limits)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(limits);
        var checks = new DefinitionSaveChecks(limits);
        checks.CheckDefinition(definition);
        return checks._problems.ToReadOnlyList();
    }

    private static string P(string path, string name) => JsonPaths.Property(path, name);

    private static string I(string path, int index) => JsonPaths.Index(path, index);

    private static bool HasLineBreak(string text) => text.AsSpan().IndexOfAny(LineBreaks) >= 0;

    private void Fail(string code, string path, string message) => _problems.Add(code, path, message);

    private void CheckDefinition(FormDefinition definition)
    {
        Key(definition.Key, P(JsonPaths.Root, "key"));
        var settingsPath = P(JsonPaths.Root, "settings");
        if (Present(definition.Settings, settingsPath))
        {
            CheckSettings(definition.Settings, settingsPath);
        }

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
            Word(rule.Match, P(path, "match"));
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
            RequiredString(action.Type, P(path, "type"), TypeLength);
            String(action.Name, P(path, "name"), LineLength, singleLine: true);
            Word(action.Trigger, P(path, "trigger"));
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
            Word(outcome.Kind, P(path, "kind"));
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
        var submissionPath = P(path, "submission");
        if (Present(settings.Submission, submissionPath))
        {
            CheckSubmission(settings.Submission, submissionPath);
        }

        var availabilityPath = P(path, "availability");
        if (Present(settings.Availability, availabilityPath))
        {
            CheckAvailability(settings.Availability, availabilityPath);
        }

        var protectionPath = P(path, "protection");
        if (Present(settings.Protection, protectionPath))
        {
            var protection = settings.Protection;
            Word(protection.Level, P(protectionPath, "level"));
            Word(protection.ContentRules, P(protectionPath, "contentRules"));
            if (protection.Captcha is { } captcha)
            {
                RequiredString(captcha.Provider, P(P(protectionPath, "captcha"), "provider"), TypeLength);
            }
        }

        var privacyPath = P(path, "privacy");
        if (Present(settings.Privacy, privacyPath))
        {
            Word(settings.Privacy.IP, P(privacyPath, "ip"));
            var retentionPath = P(privacyPath, "retentionDays");
            if (Present(settings.Privacy.RetentionDays, retentionPath))
            {
                var retention = settings.Privacy.RetentionDays;
                foreach (var (state, days) in new[] { ("submitted", retention.Submitted), ("approved", retention.Approved), ("rejected", retention.Rejected), ("spam", retention.Spam) })
                {
                    if (days is { } value)
                    {
                        Range(value, P(retentionPath, state), 1, MaxDays);
                    }
                }
            }
        }

        var appearancePath = P(path, "appearance");
        if (Present(settings.Appearance, appearancePath))
        {
            var appearance = settings.Appearance;
            Word(appearance.RequiredMarker, P(appearancePath, "requiredMarker"));
            Word(appearance.ChoiceLayout, P(appearancePath, "choiceLayout"));
            String(appearance.Theme, P(appearancePath, "theme"), ThemeLength, singleLine: true);
            String(appearance.CssClass, P(appearancePath, "cssClass"), LineLength, singleLine: true);
        }

        var developerPath = P(path, "developer");
        if (Present(settings.Developer, developerPath))
        {
            Strings(settings.Developer.QueryValues, P(developerPath, "queryValues"), LineLength);
            Strings(settings.Developer.ContextValues, P(developerPath, "contextValues"), LineLength);
        }
    }

    private void CheckSubmission(FormSubmissionSettings submission, string submissionPath)
    {
        Word(submission.Progress, P(submissionPath, "progress"));
        var stored = Word(submission.Store, P(submissionPath, "store"));
        Word(submission.Approval, P(submissionPath, "approval"));
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
        else if (stored && submission.StoreDays is not null)
        {
            Fail(DefinitionErrors.InvalidValue, storeDaysPath, "The number of days belongs only to storing entries for some days.");
        }

        if (submission.Verification is { } verification)
        {
            var verificationPath = P(submissionPath, "verification");
            Reference(verification.EmailField, P(verificationPath, "emailField"));
            Range(verification.Days, P(verificationPath, "days"), 1, MaxVerificationDays);
        }

        if (submission.Resume is { } resume)
        {
            Range(resume.Days, P(P(submissionPath, "resume"), "days"), 1, MaxResumeDays);
        }

        String(submission.EntryTitle, P(submissionPath, "entryTitle"), LineLength, singleLine: true);
        if (submission.EntryReference is { } reference)
        {
            var referencePath = P(submissionPath, "entryReference");
            String(reference.Prefix, P(referencePath, "prefix"), LineLength, singleLine: true);
            Range(reference.Digits, P(referencePath, "digits"), 1, MaxReferenceDigits);
        }
    }

    private void CheckAvailability(FormAvailabilitySettings availability, string availabilityPath)
    {
        Word(availability.Access, P(availabilityPath, "members"));
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
        Word(group.Style, P(path, "style"));
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
        RequiredString(field.Type, P(path, "type"), TypeLength);
        String(field.Alias, P(path, "alias"), LineLength, singleLine: true);
        Strings(field.FormerAliases, P(path, "formerAliases"), LineLength);
        Range(field.Span, P(path, "span"), 1, columns);
        Text(field.Label, P(path, "label"), TextKind.Line);
        Text(field.Help, P(path, "help"), TextKind.Plain);
        Text(field.Placeholder, P(path, "placeholder"), TextKind.Line);
        var messagesPath = P(path, "messages");
        if (Present(field.Messages, messagesPath))
        {
            Messages(field.Messages, messagesPath);
        }

        Word(field.Keep, P(path, "keep"));

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
            String(prefill.Context, P(prefillPath, "context"), LineLength, singleLine: true);
            Word(prefill.MemberMode, P(prefillPath, "memberMode"));
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
                    var patternPath = P(checkPath, "pattern");
                    if (Present(pattern.Pattern, patternPath))
                    {
                        String(pattern.Pattern, patternPath, PlainLength, singleLine: false);
                    }

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

        if (field.Fields.Count > 0 && Deeper())
        {
            Fields(field.Fields, P(path, "fields"), columns);
            _nesting--;
        }
    }

    private void Messages(FieldMessages messages, string path)
    {
        foreach (var message in FieldMessageProperties.All)
        {
            Text(message.Read(messages), P(path, message.Name), TextKind.Line);
        }
    }

    private void CheckOptions(FieldOptions options, string path)
    {
        Word(options.Source, P(path, "source"));
        OptionItems(options.Items, P(path, "items"), _limits.MaxOptionsPerField, $"A field has at most {_limits.MaxOptionsPerField} options of its own.");
        if (options.OptionList is { } list)
        {
            Reference(list, P(path, "list"));
        }

        if (options.Parent is { } parent)
        {
            Reference(parent, P(path, "parent"));
        }
    }

    private void OptionItems(IReadOnlyList<FieldOption> items, string itemsPath, int max, string tooMany)
    {
        if (items.Count > max)
        {
            Fail(DefinitionErrors.LimitExceeded, itemsPath, tooMany);
        }

        for (var i = 0; i < items.Count; i++)
        {
            var option = items[i];
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
    }

    private void OptionValue(string? value, string path)
    {
        if (!Present(value, path))
        {
            return;
        }

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
            Word(list[i].Effect, P(rulePath, "effect"));
            Word(list[i].Match, P(rulePath, "match"));
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
                    Word(group.Match, P(itemPath, "match"));
                    if (Deeper())
                    {
                        Conditions(group.Conditions, P(itemPath, "conditions"));
                        _nesting--;
                    }

                    break;
                case RuleCondition condition:
                    if (condition.Field is { } field)
                    {
                        Reference(field, P(itemPath, "field"));
                    }

                    String(condition.Context, P(itemPath, "context"), LineLength, singleLine: true);
                    RequiredString(condition.Operator, P(itemPath, "op"), LineLength);
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
                if (Present(text.Value, path))
                {
                    String(text.Value, path, PlainLength, singleLine: false);
                }

                break;
            case ConditionOperand.Field field:
                Reference(field.Key, P(path, "field"));
                break;
            case ConditionOperand.Today today:
                RequiredString(today.Offset, P(path, "today"), LineLength);
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

    // One level deeper into fields inside fields or rule groups inside rule groups, or, past the levels a reader reads, the
    // problem once and no deeper: however deep a definition built in code nests, the checks never run out of stack.
    private bool Deeper()
    {
        if (_nesting < MaxNesting)
        {
            _nesting++;
            return true;
        }

        if (!_tooDeep)
        {
            _tooDeep = true;
            _problems.Add(NestsTooDeep());
        }

        return false;
    }

    // Whether a value the model requires is there; a missing one is refused at its path.
    private bool Present([NotNullWhen(true)] object? value, string path)
    {
        if (value is not null)
        {
            return true;
        }

        Fail(DefinitionErrors.InvalidValue, path, "A required value is missing.");
        return false;
    }

    // Whether the value is a word of the definition, one its enum defines, which the writer can write; one that is not
    // is refused at its path, and what depends on it is not checked against it.
    private bool Word<T>(T value, string path)
        where T : struct, Enum
    {
        if (DefinitionWords<T>.Of(value) is not null)
        {
            return true;
        }

        Fail(DefinitionErrors.InvalidValue, path, DefinitionWords<T>.NotAWord(value));
        return false;
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

    // A text on one line that the model requires.
    private void RequiredString(string? value, string path, int max)
    {
        if (Present(value, path))
        {
            String(value, path, max, singleLine: true);
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
