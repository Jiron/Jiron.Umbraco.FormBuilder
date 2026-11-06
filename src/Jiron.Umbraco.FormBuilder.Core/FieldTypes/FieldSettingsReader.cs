using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Reads a settings class into its schema (ADR-0055): every public property is a setting with
/// <see cref="FormBuilderSettingAttribute"/>, or is left out with <c>[JsonIgnore]</c>; its kind follows from its type, its
/// default from the value a new instance holds. Objects of settings nest one level deep, and lists hold plain values.
/// </summary>
internal static class FieldSettingsReader
{
    private static readonly string IntMinimum = int.MinValue.ToString(CultureInfo.InvariantCulture);
    private static readonly string IntMaximum = int.MaxValue.ToString(CultureInfo.InvariantCulture);

    /// <summary>The schema of a settings class.</summary>
    /// <exception cref="InvalidOperationException">The class cannot be a settings class; the message gives every reason.</exception>
    public static FieldSettingsSchema Read(Type settingsType)
    {
        ArgumentNullException.ThrowIfNull(settingsType);
        var problems = new List<string>();
        var settings = Settings(settingsType, nested: false, problems);
        return problems.Count == 0 ? new FieldSettingsSchema(settings) : throw new InvalidOperationException(string.Join(" ", problems));
    }

    private static ImmutableArray<FieldSettingDescriptor> Settings(Type type, bool nested, List<string> problems)
    {
        if (type.IsAbstract || type.IsValueType || type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) is null)
        {
            problems.Add($"{NameOf(type)} is a settings class only as a class with a public constructor without parameters, whose new instance holds the defaults.");
            return [];
        }

        JsonTypeInfo info;
        object instance;
        JsonElement defaults;
        try
        {
            // System.Text.Json refuses two properties of one name, and a constructor may throw.
            info = FieldSettingsJson.Options.GetTypeInfo(type);
            instance = Activator.CreateInstance(type)!;
            defaults = JsonSerializer.SerializeToElement(instance, info);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or JsonException or TargetInvocationException)
        {
            problems.Add($"{NameOf(type)} cannot be written as JSON: {(exception as TargetInvocationException)?.InnerException?.Message ?? exception.Message}");
            return [];
        }

        var read = info.Properties.Where(property => property.Set is not null).ToDictionary(property => property.Name, StringComparer.Ordinal);
        var found = new List<(FieldSettingDescriptor Setting, int Index)>();
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(property => Depth(property.DeclaringType!))
            .ThenBy(property => property.MetadataToken)
            .ToList();
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            var where = $"{NameOf(type)}.{property.Name}";
            var attribute = property.GetCustomAttribute<FormBuilderSettingAttribute>();
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
            {
                if (attribute is not null)
                {
                    problems.Add($"{where} is a setting and ignored by JSON at once; keep one of the two.");
                }

                continue;
            }

            if (attribute is null)
            {
                problems.Add($"{where} is not a setting: give it [FormBuilderSetting] or [JsonIgnore].");
                continue;
            }

            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!FormBuilderOptionsValidator.IsSettingName(name))
            {
                problems.Add($"{where} is named '{name}' in JSON; a setting's name is a lowercase letter, then letters and digits, at most 64 characters.");
                continue;
            }

            if (property.GetIndexParameters().Length > 0 || property.SetMethod is not { IsPublic: true } || !read.TryGetValue(name, out var json))
            {
                problems.Add($"{where} cannot be read from JSON: a setting is a public property with a public setter or init.");
                continue;
            }

            // Settings leave nulls out, so such a value would stay null after every read.
            if (!json.IsGetNullable && property.GetValue(instance) is null)
            {
                problems.Add($"{where} starts null, though the class says it cannot be null: give it an initial value, or make it nullable.");
                continue;
            }

            if (Describe(property, attribute, name, defaults, nested, where, problems) is { } setting)
            {
                found.Add((setting, index));
            }
        }

        CheckWithin(found.Select(each => (each.Setting, $"{NameOf(type)}.{properties[each.Index].Name}")).ToList(), nested, problems);
        return [.. found.OrderBy(each => each.Setting.Group).ThenBy(each => each.Setting.Order).ThenBy(each => each.Index).Select(each => each.Setting)];
    }

    // The editors that draw settings within them, each with the kinds of setting it draws and how many of each: the email's
    // recipients (ADR-0189), and the webhook's address and body (ADR-0191).
    private static readonly ImmutableSortedDictionary<string, BoxEditor> Boxes = new Dictionary<string, BoxEditor>(StringComparer.Ordinal)
    {
        ["recipients"] = new(
            setting => setting switch
            {
                { Editor: "fieldPicker", Picks: FieldPick.Email } => ("an email field's answer", 1),
                { Editor: "boolean" } => ("the person who created the form", 1),
                { Editor: "recipientMap" } => ("the addresses an answer chooses", 1),
                _ => null,
            },
            "an email field's answer (a fieldPicker of email fields), the person who created the form (a boolean) and the addresses an answer chooses (a recipientMap)"),
        ["webhookAddress"] = new(
            setting => setting is { Kind: FieldSettingKind.Text, Editor: "text" } ? ("an address", 1) : null,
            "an address (a text)"),
        ["webhookBody"] = new(
            setting => setting switch
            {
                { Kind: FieldSettingKind.Choice } => ("a format", 1),
                { Kind: FieldSettingKind.Text, Editor: "text" } => ("texts", 2),
                { Kind: FieldSettingKind.Boolean } => ("switches", 2),
                _ => null,
            },
            "a format (a choice), a body and a message (texts) and two switches (booleans)"),
    }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    // A setting within another names one of its class at the top, which is itself within none, and whose editor draws it
    // (ADR-0189, ADR-0191): only the editors of Boxes draw settings within them, as many of each kind as each knows.
    private static void CheckWithin(List<(FieldSettingDescriptor Setting, string Where)> settings, bool nested, List<string> problems)
    {
        var held = new Dictionary<(string Box, string Kind), int>();
        foreach (var (setting, where) in settings.Where(each => each.Setting.Within is not null))
        {
            var box = settings.FirstOrDefault(each => !ReferenceEquals(each.Setting, setting) && string.Equals(each.Setting.Name, setting.Within, StringComparison.Ordinal)).Setting;
            if (nested)
            {
                problems.Add($"{where} is within '{setting.Within}' inside an object, whose editor draws all of its settings.");
            }
            else if (box is null)
            {
                problems.Add($"{where} is within '{setting.Within}', which is no other setting of its class.");
            }
            else if (box.Within is { } outer)
            {
                problems.Add($"{where} is within '{box.Name}', which is itself within '{outer}'; a setting is within one that has its own place.");
            }
            else if (!Boxes.TryGetValue(box.Editor, out var editor))
            {
                problems.Add($"{where} is within '{box.Name}', whose editor draws no setting within it; only the editors {string.Join(", ", Boxes.Keys)} do.");
            }
            else if (editor.KindOf(setting) is not { } found)
            {
                problems.Add($"{where} is within '{box.Name}', whose editor {box.Editor} draws only {editor.Draws}.");
            }
            else
            {
                var count = held.GetValueOrDefault((box.Name, found.Kind)) + 1;
                held[(box.Name, found.Kind)] = count;
                if (count > found.Most)
                {
                    problems.Add(found.Most == 1
                        ? $"{where} is within '{box.Name}', which already holds {found.Kind}; the editor {box.Editor} draws one of each."
                        : string.Create(CultureInfo.InvariantCulture, $"{where} is within '{box.Name}', which already holds {found.Most} {found.Kind}; the editor {box.Editor} draws at most {found.Most}."));
                }
            }
        }
    }

    /// <summary>An editor that draws settings within it: the kind of each it draws with how many, and what it draws in words.</summary>
    /// <param name="KindOf">
    /// The kind a setting is to the editor and how many of it the editor draws (the kind in the plural where it draws more than
    /// one); null for one it does not draw.
    /// </param>
    /// <param name="Draws">What it draws, for the problem of a setting it does not.</param>
    private sealed record BoxEditor(Func<FieldSettingDescriptor, (string Kind, int Most)?> KindOf, string Draws);

    private static FieldSettingDescriptor? Describe(
        PropertyInfo property,
        FormBuilderSettingAttribute attribute,
        string name,
        JsonElement defaults,
        bool nested,
        string where,
        List<string> problems)
    {
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        FieldSettingKind kind;
        FieldSettingKind? itemKind = null;
        ImmutableArray<FieldSettingChoice> choices = [];
        ImmutableArray<FieldSettingDescriptor> settings = [];
        if (ListItem(type) is { } item)
        {
            // A list's kind, if given, is the kind of its items; List itself says no more than the type does. An item
            // that is no plain value (an object, a list, a double) gets the list's reason; an enum that cannot be a
            // choice, or a kind its type cannot be stored as, gets its own.
            kind = FieldSettingKind.List;
            var reasons = item.IsEnum || Plain(item, FieldSettingKind.Inferred, where, problems: null, out _) is not null ? problems : null;
            itemKind = Plain(item, attribute.Kind is FieldSettingKind.List ? FieldSettingKind.Inferred : attribute.Kind, where, reasons, out choices);
            if (itemKind is not (FieldSettingKind.WholeNumber or FieldSettingKind.Text or FieldSettingKind.Number or FieldSettingKind.Date
                or FieldSettingKind.Time or FieldSettingKind.Choice or FieldSettingKind.Key))
            {
                if (itemKind is not null || reasons is null)
                {
                    problems.Add($"{where} is a list of {NameOf(item)}; a list holds whole numbers, texts, numbers, dates, times, choices or keys.");
                }

                return null;
            }
        }
        else if (Plain(type, attribute.Kind, where, problems: null, out choices) is { } plain)
        {
            kind = plain;
        }
        else if (type == typeof(string) || !type.IsClass)
        {
            // Plain again, this time naming the reason.
            Plain(type, attribute.Kind, where, problems, out _);
            return null;
        }
        else if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            problems.Add($"{where} is of the type {NameOf(type)}; a list of settings is an IReadOnlyList<T>.");
            return null;
        }
        else if (nested)
        {
            problems.Add($"{where} is an object inside an object; settings nest one level deep.");
            return null;
        }
        else if (attribute.Kind is not (FieldSettingKind.Inferred or FieldSettingKind.Nested))
        {
            problems.Add($"{where} is an object of settings, which cannot be stored as {Describe(attribute.Kind, null)}.");
            return null;
        }
        else
        {
            kind = FieldSettingKind.Nested;
            settings = Settings(type, nested: true, problems);
        }

        var editor = attribute.Editor ?? (kind is FieldSettingKind.Key or FieldSettingKind.Json || itemKind is FieldSettingKind.Key
            ? null
            : JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()));
        if (string.IsNullOrWhiteSpace(editor))
        {
            problems.Add($"{where} holds {Describe(kind, itemKind)}, which has no editor of its own: name one with Editor.");
            return null;
        }

        if (attribute.Picks != FieldPick.Any && editor != "fieldPicker")
        {
            problems.Add($"{where} picks fields of a kind, which only the editor fieldPicker offers.");
            return null;
        }

        var setting = new FieldSettingDescriptor
        {
            Name = name,
            Kind = kind,
            ItemKind = itemKind,
            Choices = choices,
            Settings = settings,
            Default = defaults.TryGetProperty(name, out var value) && !CanonicalJson.IsLeftOut(value) ? Canonical(value) : null,
            Label = attribute.Label,
            Description = attribute.Description,
            Editor = editor,
            Group = attribute.Group,
            Order = attribute.Order,
            SupportsMergeTags = attribute.SupportsMergeTags,
            IsPublic = attribute.Public && !nested,
            Within = attribute.Within,
            Picks = attribute.Picks,
        };
        if (Rules(property, setting, ListItem(type) ?? type, where, problems) is not { } rules)
        {
            return null;
        }

        setting = setting with { Rules = rules };
        return setting.Default is not { } start || KeepsRules(setting, start, where, problems) ? setting : null;
    }

    // The rules of a setting from its validation attributes, with the limits of a setting without them (ADR-0056); null,
    // with every reason, when an attribute does not fit the setting.
    private static FieldSettingRules? Rules(PropertyInfo property, FieldSettingDescriptor setting, Type valueType, string where, List<string> problems)
    {
        // Bounds, lengths and allowed values apply to a list's items; the count to the list.
        var kind = setting.ItemKind ?? setting.Kind;
        var isList = setting.Kind == FieldSettingKind.List;
        var count = problems.Count;
        var rules = FieldSettingRules.None;
        var (ranged, measured, counted) = (false, false, false);
        foreach (var attribute in property.GetCustomAttributes<ValidationAttribute>().OrderBy(each => each.GetType().Name, StringComparer.Ordinal))
        {
            var name = $"[{AttributeName(attribute.GetType())}]";
            switch (attribute)
            {
                case RequiredAttribute required when attribute.GetType() == typeof(RequiredAttribute):
                    if (required.AllowEmptyStrings)
                    {
                        problems.Add($"{where} has {name} with AllowEmptyStrings; a required text is never blank.");
                    }
                    else if (setting.Default is not null)
                    {
                        problems.Add($"{where} has {name} and starts with a value; {name} asks each field for a value of its own, so the class starts it null or empty.");
                    }
                    else
                    {
                        rules = rules with { Required = true };
                    }

                    break;
                case RangeAttribute range when attribute.GetType() == typeof(RangeAttribute):
                    ranged = true;
                    rules = Range(range, kind, valueType, rules, $"{where} has {name}", problems);
                    break;
                case StringLengthAttribute length when attribute.GetType() == typeof(StringLengthAttribute):
                    measured = true;
                    if (kind is not (FieldSettingKind.Text or FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml))
                    {
                        problems.Add($"{where} has {name}, which measures texts, not {Describe(kind, null)}.");
                    }
                    else if (length.MaximumLength < 1 || length.MinimumLength < 0 || length.MinimumLength > length.MaximumLength)
                    {
                        problems.Add(Invariant($"{where} has {name} of {length.MinimumLength} to {length.MaximumLength} characters, which no text keeps."));
                    }
                    else
                    {
                        rules = rules with { MinimumLength = length.MinimumLength > 0 ? length.MinimumLength : null, MaximumLength = length.MaximumLength };
                    }

                    break;
                case LengthAttribute length when attribute.GetType() == typeof(LengthAttribute):
                    counted = true;
                    if (!isList)
                    {
                        problems.Add($"{where} has {name}, which counts the items of a list; a text's length is [StringLength].");
                    }
                    else if (length.MaximumLength < 1 || length.MinimumLength < 0 || length.MinimumLength > length.MaximumLength)
                    {
                        problems.Add(Invariant($"{where} has {name} of {length.MinimumLength} to {length.MaximumLength} items, which no list keeps."));
                    }
                    else
                    {
                        rules = rules with { MinimumCount = length.MinimumLength > 0 ? length.MinimumLength : null, MaximumCount = length.MaximumLength };
                    }

                    break;
                case AllowedValuesAttribute allowed when attribute.GetType() == typeof(AllowedValuesAttribute):
                    if (kind != FieldSettingKind.Text)
                    {
                        problems.Add($"{where} has {name}, which lists the texts a text may be; a setting of a few choices is an enum.");
                    }
                    else if (allowed.Values.Length == 0 || allowed.Values.Any(each => each is not string))
                    {
                        problems.Add($"{where} has {name} with no values, or with one that is not a text.");
                    }
                    else
                    {
                        rules = rules with { AllowedValues = [.. allowed.Values.Cast<string>().Distinct(StringComparer.Ordinal)] };
                    }

                    break;
                default:
                    problems.Add($"{where} has {name}, which settings do not read: their rules are [Required], [Range], [StringLength], [Length] and [AllowedValues].");
                    break;
            }
        }

        // Without an attribute, the limits every setting keeps: those of the save checks for texts, and what an int holds.
        if (!ranged && valueType == typeof(int))
        {
            rules = rules with { Minimum = IntMinimum, Maximum = IntMaximum };
        }

        if (!measured && kind is FieldSettingKind.Text or FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml)
        {
            rules = rules with { MaximumLength = kind == FieldSettingKind.LocalizedHtml ? FieldSettingRules.HtmlLength : FieldSettingRules.TextLength };
        }

        if (!counted && isList)
        {
            rules = rules with { MaximumCount = FieldSettingRules.ListCount };
        }

        if (rules.MinimumCount is { } fewest && !rules.Required && setting.Default is null)
        {
            problems.Add($"{where} has at least {FieldSettingsChecks.Count(fewest, "item")} and starts with none; give it [Required], or a default of {FieldSettingsChecks.Count(fewest, "item")}.");
        }

        foreach (var allowed in rules.AllowedValues)
        {
            if (allowed.Length > rules.MaximumLength || allowed.Length < rules.MinimumLength)
            {
                problems.Add($"{where} allows '{allowed}', which is longer or shorter than its [StringLength] allows.");
            }
        }

        return problems.Count == count ? rules : null;
    }

    // [Range] for whole numbers and numbers, its bounds as the wire form writes them (ADR-0005).
    private static FieldSettingRules Range(RangeAttribute range, FieldSettingKind kind, Type valueType, FieldSettingRules rules, string what, List<string> problems)
    {
        if (kind is not (FieldSettingKind.WholeNumber or FieldSettingKind.Number))
        {
            problems.Add($"{what}, which bounds whole numbers and numbers, not {Describe(kind, null)}.");
            return rules;
        }

        var whole = kind == FieldSettingKind.WholeNumber;
        if (Bound(range.Minimum, whole) is not { } lowest || Bound(range.Maximum, whole) is not { } highest)
        {
            problems.Add($"{what} with a bound that is not {(whole ? "a whole number" : "a number")} of at most 15 digits.");
            return rules;
        }

        if (valueType == typeof(int) && (ExactDecimal.CompareText(lowest, IntMinimum) < 0 || ExactDecimal.CompareText(highest, IntMaximum) > 0))
        {
            problems.Add($"{what} beyond what an int holds, {IntMinimum} to {IntMaximum}.");
            return rules;
        }

        var order = ExactDecimal.CompareText(lowest, highest);
        if (order > 0 || (order == 0 && (range.MinimumIsExclusive || range.MaximumIsExclusive)))
        {
            problems.Add($"{what} from {lowest} to {highest}, which no value keeps.");
            return rules;
        }

        return rules with { Minimum = lowest, MinimumIsExclusive = range.MinimumIsExclusive, Maximum = highest, MaximumIsExclusive = range.MaximumIsExclusive };
    }

    // A bound of [Range] (an int, a double, or a text given with its type) in the wire form, or null when it is none.
    private static string? Bound(object? bound, bool whole)
    {
        var text = bound switch
        {
            int number => number.ToString(CultureInfo.InvariantCulture),
            double number when double.IsFinite(number) && Math.Abs(number) < 1e15 => ((decimal)number).ToString(CultureInfo.InvariantCulture),
            string number => number,
            _ => null,
        };
        return text is not null && ExactDecimal.Parse(text) is { } parsed && ExactDecimal.Fits(parsed) && (!whole || parsed.S == 0) ? parsed.ToString() : null;
    }

    // A default the field takes without a value of its own keeps the setting's rules, so every field does.
    private static bool KeepsRules(FieldSettingDescriptor setting, JsonElement start, string where, List<string> problems)
    {
        var found = new DefinitionProblems();
        FieldSettingsChecks.CheckDefault(setting, start, found);
        if (found.Found == 0)
        {
            return true;
        }

        problems.Add($"{where} starts as {start.GetRawText()}, which breaks its rules: {string.Join(" ", found.ToReadOnlyList().Select(problem => problem.Message))}");
        return false;
    }

    private static string AttributeName(Type type) => type.Name.EndsWith("Attribute", StringComparison.Ordinal) ? type.Name[..^"Attribute".Length] : type.Name;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // The kind of a value that is neither a list nor an object, or null (with the reason when problems are asked for).
    private static FieldSettingKind? Plain(Type type, FieldSettingKind given, string where, List<string>? problems, out ImmutableArray<FieldSettingChoice> choices)
    {
        choices = [];
        FieldSettingKind? inferred = null;
        if (type == typeof(bool))
        {
            inferred = FieldSettingKind.Boolean;
        }
        else if (type == typeof(int) || type == typeof(long))
        {
            inferred = FieldSettingKind.WholeNumber;
        }
        else if (type == typeof(string))
        {
            // A text may be stored as a number, a date or a time instead.
            if (given is FieldSettingKind.Number or FieldSettingKind.Date or FieldSettingKind.Time)
            {
                return given;
            }

            inferred = FieldSettingKind.Text;
        }
        else if (type == typeof(LocalizedText))
        {
            inferred = FieldSettingKind.LocalizedText;
        }
        else if (type == typeof(LocalizedHtml))
        {
            inferred = FieldSettingKind.LocalizedHtml;
        }
        else if (type == typeof(Guid))
        {
            inferred = FieldSettingKind.Key;
        }
        else if (type == typeof(JsonElement))
        {
            inferred = FieldSettingKind.Json;
        }
        else if (type.IsEnum)
        {
            if (Choices(type) is not { } values)
            {
                problems?.Add($"{where} is the enum {NameOf(type)}; a choice is an enum without [Flags] whose values each have one name.");
                return null;
            }

            choices = values;
            inferred = FieldSettingKind.Choice;
        }

        if (inferred is null)
        {
            problems?.Add($"{where} is of the type {NameOf(type)}, which no setting holds: use bool, int, long, string, LocalizedText, LocalizedHtml, an enum, Guid, JsonElement, IReadOnlyList<T> of a plain value, or a class of settings.");
            return null;
        }

        if (given != FieldSettingKind.Inferred && given != inferred)
        {
            problems?.Add($"{where} holds {Describe(inferred.Value, null)}, which cannot be stored as {Describe(given, null)}; only a text can be a number, a date or a time.");
            return null;
        }

        return inferred;
    }

    // A default as settings store it (ADR-0002): without what is left out, with sorted properties.
    private static JsonElement Canonical(JsonElement value)
    {
        using var document = JsonDocument.Parse(CanonicalJson.Write(value, sortProperties: true));
        return document.RootElement.Clone();
    }

    private static Type? ListItem(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>) ? type.GetGenericArguments()[0] : null;

    // The values of an enum as its JSON writes them (camel case, or the name [JsonStringEnumMemberName] gives) with their
    // labels ([FormBuilderChoice], else the C# name), in the order it declares them; null for flags or a value with two names.
    private static ImmutableArray<FieldSettingChoice>? Choices(Type type)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(field => field.MetadataToken).ToList();
        if (type.IsDefined(typeof(FlagsAttribute), inherit: false) || fields.Select(field => field.GetRawConstantValue()).Distinct().Count() != fields.Count)
        {
            return null;
        }

        return [.. fields.Select(field => new FieldSettingChoice(
            field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(field.Name),
            field.GetCustomAttribute<FormBuilderChoiceAttribute>()?.Label ?? field.Name))];
    }

    // A kind in words, for the reasons the reader gives.
    private static string Describe(FieldSettingKind kind, FieldSettingKind? item) => (kind, item) switch
    {
        (FieldSettingKind.List, FieldSettingKind.Key) => "a list of keys",
        (FieldSettingKind.List, _) => "a list",
        (FieldSettingKind.Boolean, _) => "a yes or no",
        (FieldSettingKind.WholeNumber, _) => "a whole number",
        (FieldSettingKind.Number, _) => "a number",
        (FieldSettingKind.Text, _) => "a text",
        (FieldSettingKind.Date, _) => "a date",
        (FieldSettingKind.Time, _) => "a time",
        (FieldSettingKind.LocalizedText, _) => "a text per culture",
        (FieldSettingKind.LocalizedHtml, _) => "rich text per culture",
        (FieldSettingKind.Choice, _) => "a choice",
        (FieldSettingKind.Key, _) => "a key",
        (FieldSettingKind.Nested, _) => "an object of settings",
        _ => "free JSON",
    };

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static string NameOf(Type type) => type.FullName ?? type.Name;
}
