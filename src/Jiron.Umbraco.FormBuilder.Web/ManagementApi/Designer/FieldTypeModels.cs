using System.Collections.Immutable;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The site's field types for the designer (ADR-0057): the groups of the palette and of the inspector in the order they
/// are shown, and every type in the registry's order with the host's locks of its settings (ADR-0060). Names and enum
/// values are in camel case, as definition documents write them; a text is plain text or a <c>#</c> key of the
/// backoffice's localisation.
/// </summary>
internal sealed record FieldTypesResponseModel
{
    /// <summary>The groups of the palette, in its order.</summary>
    public required IReadOnlyList<FieldGroupResponseModel> PaletteGroups { get; init; }

    /// <summary>The sections of the inspector that settings sit in, in its order.</summary>
    public required IReadOnlyList<FieldGroupResponseModel> SettingGroups { get; init; }

    /// <summary>The field types: the built-in ones in palette order, then the site's.</summary>
    public required IReadOnlyList<FieldTypeResponseModel> Types { get; init; }

    /// <summary>The model of the registry's types, with the availability of each in the same order and the host's locks.</summary>
    public static FieldTypesResponseModel Of(IReadOnlyList<IFormBuilderFieldType> types, IReadOnlyList<FieldTypeAvailability> availability, FieldSettingLocks locks)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(locks);
        if (types.Count != availability.Count)
        {
            throw new ArgumentException("Every type needs its availability, in the same order.", nameof(availability));
        }

        return new FieldTypesResponseModel
        {
            PaletteGroups = [.. Enum.GetValues<FieldPaletteGroup>().Select(group => new FieldGroupResponseModel(FieldTypeNames.Of(group), $"#jfbFieldPalette_{FieldTypeNames.Of(group)}"))],
            SettingGroups = [.. Enum.GetValues<FieldSettingGroup>().Select(group => new FieldGroupResponseModel(FieldTypeNames.Of(group), $"#jfbFieldSettingGroups_{FieldTypeNames.Of(group)}"))],
            Types = [.. types.Select((type, i) => FieldTypeResponseModel.Of(type, availability[i], locks.Of(type.Declaration.Alias), types.Any(to => FieldTypeChanges.CanChange(type.Declaration, to.Declaration))))],
        };
    }
}

/// <summary>A group of the palette or of the inspector.</summary>
/// <param name="Alias">Its alias, as a type or a setting names it.</param>
/// <param name="Name">Its name: a <c>#</c> key of the backoffice's localisation.</param>
internal sealed record FieldGroupResponseModel(string Alias, string Name);

/// <summary>A field type (section 4.3.2, ADR-0055): what it is, what it supports, its settings and whether it can be used.</summary>
internal sealed record FieldTypeResponseModel
{
    /// <summary>The alias forms name it by, such as <c>email</c>.</summary>
    public required string Alias { get; init; }

    /// <summary>The name the palette shows.</summary>
    public required string Name { get; init; }

    /// <summary>The Umbraco icon the palette shows.</summary>
    public required string Icon { get; init; }

    /// <summary>The alias of its group in the palette.</summary>
    public required string PaletteGroup { get; init; }

    /// <summary>Whether it holds a value, and so has an alias and a label.</summary>
    public required bool HoldsValue { get; init; }

    /// <summary>How its values are stored: <c>text</c>, <c>longText</c>, <c>number</c>, <c>date</c>, <c>bool</c>, <c>file</c>, <c>json</c> or <c>none</c>.</summary>
    public required string Storage { get; init; }

    /// <summary>What rules compare its answers as; null when rules cannot compare them.</summary>
    public string? RuleKind { get; init; }

    /// <summary>Whether its setting <c>multiple</c> turns one choice into several.</summary>
    public required bool MultipleSetting { get; init; }

    /// <summary>Whether a form has at most one field of it, never inside a repeatable group.</summary>
    public required bool OnePerForm { get; init; }

    /// <summary>Whether its default value stays on the server and never reaches the page.</summary>
    public required bool ServerOnlyDefault { get; init; }

    /// <summary>The common properties it supports, such as <c>label</c>, <c>placeholder</c> or <c>places</c>.</summary>
    public required IReadOnlyList<string> Features { get; init; }

    /// <summary>The error kinds it raises, each the name of a field's message, such as <c>required</c>.</summary>
    public required IReadOnlyList<string> Messages { get; init; }

    /// <summary>Its settings, in the order the inspector shows them, each with the host's lock of it.</summary>
    public required IReadOnlyList<FieldSettingResponseModel> Settings { get; init; }

    /// <summary>Whether the site has what it needs.</summary>
    public required FieldTypeAvailabilityResponseModel Availability { get; init; }

    /// <summary>
    /// Whether a field of the type may become another of the site's types (ADR-0059), whose changes
    /// <c>field-type/{alias}/changes</c> serves; content, a grid and a repeatable group keep their type.
    /// </summary>
    public required bool MayChangeType { get; init; }

    /// <summary>The model of a type, with the host's locks of its settings and whether its fields may change type.</summary>
    public static FieldTypeResponseModel Of(IFormBuilderFieldType type, FieldTypeAvailability availability, IReadOnlyList<FieldSettingLock> locks, bool mayChangeType)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(locks);
        var declaration = type.Declaration;
        return new FieldTypeResponseModel
        {
            Alias = declaration.Alias,
            Name = declaration.Name,
            Icon = declaration.Icon,
            PaletteGroup = FieldTypeNames.Of(declaration.PaletteGroup),
            HoldsValue = declaration.HoldsValue,
            Storage = FieldTypeNames.Of(declaration.Storage),
            RuleKind = declaration.RuleKind is { } kind ? FieldTypeNames.Of(kind) : null,
            MultipleSetting = declaration.MultipleSetting,
            OnePerForm = declaration.OnePerForm,
            ServerOnlyDefault = declaration.ServerOnlyDefault,
            Features = FieldTypeNames.OfFeatures(declaration.Features),
            Messages = [.. FieldMessageProperties.All.Where(message => declaration.Messages.HasFlag(message.Kind)).Select(message => message.Name)],
            Settings = FieldSettingResponseModel.Of(type.SettingsSchema.Settings, locks),
            Availability = new FieldTypeAvailabilityResponseModel(availability.IsAvailable, availability.Reason),
            MayChangeType = mayChangeType,
        };
    }
}

/// <summary>
/// The changes of type a field of one type may make (ADR-0059), for the designer's Change type: served apart from the
/// list, as every type's changes would make the list four times its size, and the designer asks only for the field it
/// changes.
/// </summary>
internal sealed record FieldTypeChangesResponseModel
{
    /// <summary>The alias of the type the field has.</summary>
    public required string From { get; init; }

    /// <summary>The changes, in the palette's order; none for a type whose fields keep their type.</summary>
    public required IReadOnlyList<FieldTypeChangeResponseModel> Changes { get; init; }

    /// <summary>The changes a field of <paramref name="from"/> may make among the site's types, each with the host's locks of its target applied.</summary>
    public static FieldTypeChangesResponseModel Of(IFormBuilderFieldType from, IReadOnlyList<IFormBuilderFieldType> types, FieldSettingLocks locks)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(locks);
        return new FieldTypeChangesResponseModel
        {
            From = from.Declaration.Alias,
            Changes =
            [
                .. types
                    .Where(to => FieldTypeChanges.CanChange(from.Declaration, to.Declaration))
                    .Select(to => FieldTypeChangeResponseModel.Of(FieldTypeChanges.UnderLocks(
                        FieldTypeChanges.Between(from, to),
                        locks.Of(to.Declaration.Alias).Select(each => each.Setting.Name)))),
            ],
        };
    }
}

/// <summary>
/// A change of type a field may make (ADR-0059): what it keeps, narrows, loses and needs, worked out from the two types'
/// declarations, with the host's locks of the target applied (ADR-0060). The designer applies it to the field in the draft.
/// </summary>
internal sealed record FieldTypeChangeResponseModel
{
    /// <summary>The alias of the type the field becomes.</summary>
    public required string To { get; init; }

    /// <summary>The settings carried over as they are: the target's rules take every value the source's take.</summary>
    public required IReadOnlyList<string> KeptSettings { get; init; }

    /// <summary>The settings whose rules in the target take fewer values: a value outside them is not carried over.</summary>
    public required IReadOnlyList<string> NarrowedSettings { get; init; }

    /// <summary>The source's settings the target does not have, or has with another kind: their values go.</summary>
    public required IReadOnlyList<string> LostSettings { get; init; }

    /// <summary>The target's required settings the change does not carry over, which the field needs before it publishes.</summary>
    public required IReadOnlyList<string> NeededSettings { get; init; }

    /// <summary>The settings it would carry over that the host locks on the target: the lock sets them.</summary>
    public required IReadOnlyList<string> LockedSettings { get; init; }

    /// <summary>The common properties the target does not support, such as <c>placeholder</c>: the field's values for them go.</summary>
    public required IReadOnlyList<string> LostProperties { get; init; }

    /// <summary>Whether the target cannot be answered without options and the source has none.</summary>
    public required bool NeedsOptions { get; init; }

    /// <summary>The error kinds the target does not raise, each a message's name: the field's own messages for them go.</summary>
    public required IReadOnlyList<string> LostMessages { get; init; }

    /// <summary>The operators the target's rule kind does not take: a condition on the field with one of them no longer publishes.</summary>
    public required IReadOnlyList<string> LostOperators { get; init; }

    /// <summary>Whether both have a rule kind and they differ, so the conditions that stay compare answers as the target's kind.</summary>
    public required bool ComparesAnew { get; init; }

    /// <summary>Whether both store in the same columns, so the entries screen filters and sorts older answers as before.</summary>
    public required bool KeepsColumns { get; init; }

    /// <summary>The model of a change.</summary>
    public static FieldTypeChangeResponseModel Of(FieldTypeChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return new FieldTypeChangeResponseModel
        {
            To = change.To,
            KeptSettings = change.KeptSettings,
            NarrowedSettings = change.NarrowedSettings,
            LostSettings = change.LostSettings,
            NeededSettings = change.NeededSettings,
            LockedSettings = change.LockedSettings,
            LostProperties = FieldTypeNames.OfFeatures(change.LostProperties),
            NeedsOptions = change.NeedsOptions,
            LostMessages = [.. FieldMessageProperties.All.Where(message => change.LostMessages.HasFlag(message.Kind)).Select(message => message.Name)],
            LostOperators = change.LostOperators,
            ComparesAnew = change.ComparesAnew,
            KeepsColumns = change.KeepsColumns,
        };
    }
}

/// <summary>One setting of a field type (ADR-0055), with the rules its value keeps (ADR-0056).</summary>
internal sealed record FieldSettingResponseModel
{
    /// <summary>Its name in the settings' JSON.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The kind of value it holds: <c>boolean</c>, <c>wholeNumber</c>, <c>number</c>, <c>text</c>, <c>date</c>, <c>time</c>,
    /// <c>localizedText</c>, <c>localizedHtml</c>, <c>choice</c>, <c>key</c>, <c>list</c>, <c>nested</c> or <c>json</c>.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>The kind of a list's items; null for any other kind.</summary>
    public string? ItemKind { get; init; }

    /// <summary>The values a choice, or a list of choices, may take, with their labels; empty for any other kind.</summary>
    public required IReadOnlyList<FieldSettingChoiceResponseModel> Choices { get; init; }

    /// <summary>The settings of an object, in the order the inspector shows them; empty for any other kind.</summary>
    public required IReadOnlyList<FieldSettingResponseModel> Settings { get; init; }

    /// <summary>The value a field has without one of its own, as settings store it; null when it has none.</summary>
    public JsonElement? Default { get; init; }

    /// <summary>The label.</summary>
    public required string Label { get; init; }

    /// <summary>A line of help, or null.</summary>
    public string? Description { get; init; }

    /// <summary>The alias of the editor the designer shows.</summary>
    public required string Editor { get; init; }

    /// <summary>The alias of the inspector's section it sits in.</summary>
    public required string Group { get; init; }

    /// <summary>Whether its text may hold merge tags.</summary>
    public required bool SupportsMergeTags { get; init; }

    /// <summary>Whether visitors see it; read for the settings at the top only.</summary>
    public required bool IsPublic { get; init; }

    /// <summary>The setting whose editor draws this one as well, by its name; null when it has its own place.</summary>
    public string? Within { get; init; }

    /// <summary>
    /// For a field picker, the fields it offers: <c>any</c> (that holds a value), <c>email</c>, <c>choice</c> (with options),
    /// <c>file</c> (that stores files) or <c>sensitive</c>; null for any other editor.
    /// </summary>
    public string? Picks { get; init; }

    /// <summary>The rules its value keeps, which publishing checks.</summary>
    public required FieldSettingRulesResponseModel Rules { get; init; }

    /// <summary>The host's lock of it, for the settings at the top only; null when it is not locked.</summary>
    public FieldSettingLockResponseModel? Lock { get; init; }

    /// <summary>The key of the word after a number, such as days; null for none (a settings screen's, ADR-0218).</summary>
    public string? Unit { get; init; }

    /// <summary>The models of settings, in their order, with the host's locks of those at the top.</summary>
    public static IReadOnlyList<FieldSettingResponseModel> Of(ImmutableArray<FieldSettingDescriptor> settings, IReadOnlyList<FieldSettingLock> locks) =>
        [.. settings.Select(setting => Of(setting, locks.FirstOrDefault(each => string.Equals(each.Setting.Name, setting.Name, StringComparison.Ordinal))))];

    private static FieldSettingResponseModel Of(FieldSettingDescriptor setting, FieldSettingLock? settingLock) => new()
    {
        Name = setting.Name,
        Kind = FieldTypeNames.Of(setting.Kind),
        ItemKind = setting.ItemKind is { } item ? FieldTypeNames.Of(item) : null,
        Choices = [.. setting.Choices.Select(choice => new FieldSettingChoiceResponseModel(choice.Value, choice.Label))],
        Settings = Of(setting.Settings, []),
        Default = setting.Default,
        Label = setting.Label,
        Description = setting.Description,
        Editor = setting.Editor,
        Group = FieldTypeNames.Of(setting.Group),
        SupportsMergeTags = setting.SupportsMergeTags,
        IsPublic = setting.IsPublic,
        Within = setting.Within,
        Picks = setting.Editor == "fieldPicker" ? FieldTypeNames.Of(setting.Picks) : null,
        Rules = FieldSettingRulesResponseModel.Of(setting.Rules),
        Lock = settingLock is null ? null : new FieldSettingLockResponseModel(settingLock.Hidden, settingLock.ConfigurationKey, settingLock.Hidden ? null : settingLock.Value),
    };
}

/// <summary>
/// A host's lock of a setting (ADR-0060): every field of the type has the locked value, which the designer shows
/// read-only, or leaves out when the lock hides the setting.
/// </summary>
/// <param name="Hidden">Whether the designer leaves the setting out.</param>
/// <param name="ConfigurationKey">The key that locks it, such as <c>JironFormBuilder:Designer:SettingLocks:0</c>, which the designer names.</param>
/// <param name="Value">
/// The value every field has, as settings store it; null when the setting keeps its default, and always for a hidden lock.
/// </param>
internal sealed record FieldSettingLockResponseModel(bool Hidden, string ConfigurationKey, JsonElement? Value);

/// <summary>A value a choice setting may take.</summary>
/// <param name="Value">The value as the settings' JSON writes it.</param>
/// <param name="Label">The label the designer shows.</param>
internal sealed record FieldSettingChoiceResponseModel(string Value, string Label);

/// <summary>
/// The rules a setting's value keeps (ADR-0056). A bound is a number in the wire form of ADR-0005; a length counts UTF-16
/// code units, of each culture's text for a localised text, and of each item for a list of texts; a count counts a list's
/// items.
/// </summary>
internal sealed record FieldSettingRulesResponseModel
{
    /// <summary>Whether the field must give the setting a value of its own.</summary>
    public required bool Required { get; init; }

    /// <summary>The lowest value, or null.</summary>
    public string? Minimum { get; init; }

    /// <summary>Whether the lowest value itself is refused.</summary>
    public required bool MinimumIsExclusive { get; init; }

    /// <summary>The highest value, or null.</summary>
    public string? Maximum { get; init; }

    /// <summary>Whether the highest value itself is refused.</summary>
    public required bool MaximumIsExclusive { get; init; }

    /// <summary>The fewest characters, or null.</summary>
    public int? MinimumLength { get; init; }

    /// <summary>The most characters, or null.</summary>
    public int? MaximumLength { get; init; }

    /// <summary>The fewest items, or null.</summary>
    public int? MinimumCount { get; init; }

    /// <summary>The most items, or null.</summary>
    public int? MaximumCount { get; init; }

    /// <summary>The texts the value may be, compared exactly; empty for any text.</summary>
    public required IReadOnlyList<string> AllowedValues { get; init; }

    /// <summary>The model of a setting's rules.</summary>
    public static FieldSettingRulesResponseModel Of(FieldSettingRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return new FieldSettingRulesResponseModel
        {
            Required = rules.Required,
            Minimum = rules.Minimum,
            MinimumIsExclusive = rules.MinimumIsExclusive,
            Maximum = rules.Maximum,
            MaximumIsExclusive = rules.MaximumIsExclusive,
            MinimumLength = rules.MinimumLength,
            MaximumLength = rules.MaximumLength,
            MinimumCount = rules.MinimumCount,
            MaximumCount = rules.MaximumCount,
            AllowedValues = rules.AllowedValues,
        };
    }
}

/// <summary>Whether a field type can be used on the site (ADR-0057).</summary>
/// <param name="IsAvailable">Whether it can.</param>
/// <param name="Reason">What is missing, a sentence for editors that the palette shows with "Not configured"; null when it can.</param>
internal sealed record FieldTypeAvailabilityResponseModel(bool IsAvailable, string? Reason);

/// <summary>The names the field types' endpoint writes for enum values: camel case, as definition documents write them.</summary>
internal static class FieldTypeNames
{
    /// <summary>The name of an enum value.</summary>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>The names of the common properties a type supports, in the enum's order.</summary>
    public static IReadOnlyList<string> OfFeatures(FieldFeatures features) =>
        [.. Enum.GetValues<FieldFeatures>().Where(feature => feature != FieldFeatures.None && features.HasFlag(feature)).Select(feature => Of(feature))];
}
