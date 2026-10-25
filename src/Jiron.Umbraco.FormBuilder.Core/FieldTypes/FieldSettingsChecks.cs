using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Holds a field's settings to its type's settings schema (ADR-0056), before the type's own check reads them as its
/// class: every setting of its kind and keeping its rules, and no setting the schema lacks. Each problem has the path
/// of the value it concerns. The designer runs the same checks over the schema it is served (5.1c).
/// </summary>
internal static class FieldSettingsChecks
{
    /// <summary>Checks settings, or none, against a schema; whether nothing was found, so the type's own check may read them.</summary>
    /// <param name="schema">The type's settings schema.</param>
    /// <param name="settings">The settings, or null when the field gives none.</param>
    /// <param name="path">The JSON path of the settings.</param>
    /// <param name="owner">Who has the settings, as the start of a sentence: <c>A text field</c>.</param>
    /// <param name="problems">Where the problems go.</param>
    public static bool Check(FieldSettingsSchema schema, SettingsObject? settings, string path, string owner, DefinitionProblems problems)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(problems);
        var found = problems.Found;
        CheckObject(schema.Settings, settings?.Value, path, owner, problems);
        return problems.Found == found;
    }

    /// <summary>Checks the value a settings class starts a setting with, as a field's own value would be checked.</summary>
    public static void CheckDefault(FieldSettingDescriptor setting, JsonElement value, DefinitionProblems problems) =>
        CheckValue(setting, value, JsonPaths.Root, problems);

    /// <summary>Whether a whole number, or a number in the wire form of ADR-0005, keeps the bounds of the rules.</summary>
    public static bool Within(FieldSettingRules rules, string value)
    {
        if (rules.Minimum is { } lowest && ExactDecimal.CompareText(value, lowest) is { } below && (below < 0 || (below == 0 && rules.MinimumIsExclusive)))
        {
            return false;
        }

        return rules.Maximum is not { } highest || ExactDecimal.CompareText(value, highest) is not { } above || above < 0 || (above == 0 && !rules.MaximumIsExclusive);
    }

    /// <summary>A count in words, in the singular or the plural: <c>1 item</c>, <c>2 items</c>.</summary>
    public static string Count(int count, string word) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {word}{(count == 1 ? string.Empty : "s")}");

    /// <summary>The bounds of the rules in words: <c>3 to 10</c>, <c>more than 0</c>, <c>at least 1 and less than 5</c>.</summary>
    public static string Bounds(FieldSettingRules rules)
    {
        var low = rules.Minimum is { } lowest ? (rules.MinimumIsExclusive ? $"more than {lowest}" : $"at least {lowest}") : null;
        var high = rules.Maximum is { } highest ? (rules.MaximumIsExclusive ? $"less than {highest}" : $"at most {highest}") : null;
        return (low, high) switch
        {
            (not null, not null) when !rules.MinimumIsExclusive && !rules.MaximumIsExclusive => $"{rules.Minimum} to {rules.Maximum}",
            (not null, not null) => $"{low} and {high}",
            _ => low ?? high ?? "any number",
        };
    }

    private static void CheckObject(ImmutableArray<FieldSettingDescriptor> settings, JsonElement? value, string path, string owner, DefinitionProblems problems)
    {
        if (value is { } given)
        {
            foreach (var property in given.EnumerateObject())
            {
                var propertyPath = JsonPaths.Property(path, property.Name);
                if (settings.FirstOrDefault(each => string.Equals(each.Name, property.Name, StringComparison.Ordinal)) is { } setting)
                {
                    CheckValue(setting, property.Value, propertyPath, problems);
                }
                else
                {
                    problems.Add(DefinitionErrors.SettingUnknown, propertyPath, $"{owner} has no setting '{property.Name}'.");
                }
            }
        }

        // A setting the field does not give takes its default, which keeps the rules (the reader made sure of it); only
        // a required one has none. Inside an object, that holds once the field gives the object.
        foreach (var setting in settings)
        {
            if (setting.Rules.Required && (value is not { } present || !present.TryGetProperty(setting.Name, out _)))
            {
                problems.Add(DefinitionErrors.SettingMissing, JsonPaths.Property(path, setting.Name), $"{What(setting)} needs a value.");
            }
        }
    }

    private static void CheckValue(FieldSettingDescriptor setting, JsonElement value, string path, DefinitionProblems problems)
    {
        switch (setting.Kind)
        {
            case FieldSettingKind.List:
                List(setting, value, path, problems);
                break;
            case FieldSettingKind.Nested:
                if (value.ValueKind != JsonValueKind.Object)
                {
                    WrongType(What(setting), "an object of settings", value, path, problems);
                }
                else
                {
                    CheckObject(setting.Settings, value, path, What(setting), problems);
                }

                break;
            case FieldSettingKind.Json:
                // Free JSON is the type's own to check.
                break;
            default:
                Plain(setting, setting.Kind, value, path, What(setting), problems);
                break;
        }
    }

    private static void List(FieldSettingDescriptor setting, JsonElement value, string path, DefinitionProblems problems)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            WrongType(What(setting), "a list", value, path, problems);
            return;
        }

        var rules = setting.Rules;
        var count = value.GetArrayLength();
        if (rules.MaximumCount is { } most && count > most)
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, Invariant($"{What(setting)} has at most {Count(most, "item")}, not {count}."));
        }
        else if (rules.MinimumCount is { } fewest && count < fewest)
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, Invariant($"{What(setting)} has at least {Count(fewest, "item")}, not {count}."));
        }

        // Each value is there once, as the values compare: 1.50 is 1.5, and 09:30:00 is 09:30.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = JsonPaths.Index(path, index++);
            if (Plain(setting, setting.ItemKind!.Value, item, itemPath, $"An item of the setting '{setting.Name}'", problems) is { } key && !seen.Add(key))
            {
                problems.Add(DefinitionErrors.SettingInvalid, itemPath, $"{What(setting)} holds each value once; {item.GetRawText()} is there before.");
            }
        }
    }

    // Checks a value that is neither a list nor an object; the value as items compare, or null when it has a problem.
    private static string? Plain(FieldSettingDescriptor setting, FieldSettingKind kind, JsonElement value, string path, string what, DefinitionProblems problems)
    {
        var found = problems.Found;
        var key = kind switch
        {
            FieldSettingKind.Boolean => Boolean(value, path, what, problems),
            FieldSettingKind.WholeNumber => WholeNumber(setting.Rules, value, path, what, problems),
            FieldSettingKind.Number => Number(setting.Rules, value, path, what, problems),
            FieldSettingKind.Text => Text(setting.Rules, value, path, what, problems),
            FieldSettingKind.Date => Formatted(value, path, what, "a date, YYYY-MM-DD", text => CalendarValues.ParseDate(text)?.ToString(CultureInfo.InvariantCulture), problems),
            FieldSettingKind.Time => Formatted(value, path, what, "a time of day, HH:mm", text => CalendarValues.ParseTime(text)?.ToString(CultureInfo.InvariantCulture), problems),
            FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml => Localized(setting.Rules, value, path, what, problems),
            FieldSettingKind.Choice => Choice(setting.Choices, value, path, what, problems),
            _ => Formatted(value, path, what, "a key, such as 5c0f9b5e-3a4d-4b8e-9b51-6f1d2c3a4b5c", Key, problems),
        };
        return problems.Found == found ? key : null;
    }

    private static string? Boolean(JsonElement value, string path, string what, DefinitionProblems problems)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetRawText();
        }

        WrongType(what, "true or false", value, path, problems);
        return null;
    }

    private static string? WholeNumber(FieldSettingRules rules, JsonElement value, string path, string what, DefinitionProblems problems)
    {
        // Settings hold integers of at most 15 digits only (SettingsObject).
        if (value.ValueKind != JsonValueKind.Number)
        {
            WrongType(what, "a whole number", value, path, problems);
            return null;
        }

        var number = value.GetInt64().ToString(CultureInfo.InvariantCulture);
        return KeepsBounds(rules, number, path, what, problems) ? number : null;
    }

    private static string? Number(FieldSettingRules rules, JsonElement value, string path, string what, DefinitionProblems problems)
    {
        if (String(value, path, what, "a number as text, such as \"2.5\"", problems) is not { } text)
        {
            return null;
        }

        if (ExactDecimal.Parse(text) is not { } number)
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} is a number such as \"-12.5\" (digits, with a minus and a point if need be), not \"{text}\".");
            return null;
        }

        if (!ExactDecimal.Fits(number))
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} has at most 15 digits, at most 15 of them after the point.");
            return null;
        }

        var canonical = number.ToString();
        return KeepsBounds(rules, canonical, path, what, problems) ? canonical : null;
    }

    private static bool KeepsBounds(FieldSettingRules rules, string value, string path, string what, DefinitionProblems problems)
    {
        if (Within(rules, value))
        {
            return true;
        }

        problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} is {Bounds(rules)}, not {value}.");
        return false;
    }

    private static string? Text(FieldSettingRules rules, JsonElement value, string path, string what, DefinitionProblems problems)
    {
        if (String(value, path, what, "a text", problems) is not { } text)
        {
            return null;
        }

        TextRules(rules, text, path, what, problems);
        return text;
    }

    private static void TextRules(FieldSettingRules rules, string text, string path, string what, DefinitionProblems problems)
    {
        if (rules.Required && string.IsNullOrWhiteSpace(text))
        {
            problems.Add(DefinitionErrors.SettingMissing, path, $"{what} needs a text that is not blank.");
        }
        else if (rules.MaximumLength is { } most && text.Length > most)
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, Invariant($"{what} has at most {Count(most, "character")}, not {text.Length}."));
        }
        else if (rules.MinimumLength is { } fewest && text.Length < fewest)
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, Invariant($"{what} has at least {Count(fewest, "character")}, not {text.Length}."));
        }
        else if (rules.AllowedValues.Length > 0 && !rules.AllowedValues.Contains(text, StringComparer.Ordinal))
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} is {Names(rules.AllowedValues)}, not '{text}'.");
        }
    }

    // A localised text (ADR-0002): culture names and texts, with the default text "", each text held to the rules.
    private static string? Localized(FieldSettingRules rules, JsonElement value, string path, string what, DefinitionProblems problems)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            WrongType(what, "a text per culture (an object from culture to text)", value, path, problems);
            return null;
        }

        var cultures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? defaultText = null;
        foreach (var property in value.EnumerateObject())
        {
            var culture = property.Name;
            var textPath = JsonPaths.Property(path, culture);
            var inCulture = culture.Length == 0 ? $"The default text of {Lower(what)}" : $"{what} in '{culture}'";
            if (culture.Length > 0 && !LocalizedString.IsCultureName(culture))
            {
                problems.Add(DefinitionErrors.SettingInvalid, textPath, $"\"{culture}\" is not a culture name; the default text is \"\".");
            }
            else if (!cultures.Add(culture))
            {
                problems.Add(DefinitionErrors.SettingInvalid, textPath, $"The culture \"{culture}\" appears twice in another letter case.");
            }
            else if (String(property.Value, textPath, inCulture, "a text", problems) is { } text)
            {
                defaultText = culture.Length == 0 ? text : defaultText;
                TextRules(rules with { Required = false }, text, textPath, inCulture, problems);
            }
        }

        if (!cultures.Contains(string.Empty))
        {
            problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} needs its default text, the culture \"\".");
        }
        else if (rules.Required && defaultText is { } given && string.IsNullOrWhiteSpace(given))
        {
            problems.Add(DefinitionErrors.SettingMissing, JsonPaths.Property(path, string.Empty), $"The default text of {Lower(what)} needs a text that is not blank.");
        }

        return null;
    }

    private static string? Choice(ImmutableArray<string> choices, JsonElement value, string path, string what, DefinitionProblems problems)
    {
        if (String(value, path, what, "a choice", problems) is not { } choice)
        {
            return null;
        }

        if (choices.Contains(choice, StringComparer.Ordinal))
        {
            return choice;
        }

        problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} is {Names(choices)}, not '{choice}'.");
        return null;
    }

    // A text in a form the value has, read as the key items compare by; null with the problem when it is not.
    private static string? Formatted(JsonElement value, string path, string what, string form, Func<string, string?> read, DefinitionProblems problems)
    {
        if (String(value, path, what, form, problems) is not { } text)
        {
            return null;
        }

        if (read(text) is { } key)
        {
            return key;
        }

        problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} is {form}, not \"{text}\".");
        return null;
    }

    // A key as definitions write keys: the 36 lowercase characters of the "D" form.
    private static string? Key(string text) =>
        Guid.TryParseExact(text, "D", out var key) && string.Equals(key.ToString("D"), text, StringComparison.Ordinal) ? text : null;

    // Settings hold no lone surrogates (SettingsObject), so a string always reads.
    private static string? String(JsonElement value, string path, string what, string form, DefinitionProblems problems)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }

        WrongType(what, form, value, path, problems);
        return null;
    }

    private static void WrongType(string what, string form, JsonElement value, string path, DefinitionProblems problems) =>
        problems.Add(DefinitionErrors.SettingInvalid, path, $"{what} is {form}, not {JsonPaths.Describe(value.ValueKind)}.");

    private static string What(FieldSettingDescriptor setting) => $"The setting '{setting.Name}'";

    // "The setting 'x'" within a sentence, and "An item of the setting 'x'" likewise.
    private static string Lower(string what) => string.Concat(what[..1].ToLowerInvariant(), what[1..]);

    private static string Names(ImmutableArray<string> names) =>
        names.Length == 1 ? $"'{names[0]}'" : $"one of {string.Join(", ", names.Select(name => $"'{name}'"))}";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
