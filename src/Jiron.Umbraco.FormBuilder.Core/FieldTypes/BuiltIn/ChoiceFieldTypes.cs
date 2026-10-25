using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>A dropdown, or a native list box that takes several choices (<c>dropdown</c>, section 4.3.2).</summary>
public sealed class DropdownFieldType() : FormBuilderFieldTypeBase<DropdownFieldSettings>(new FieldTypeDeclaration
{
    Alias = "dropdown",
    Name = "#jfbFieldTypes_dropdown",
    Icon = "icon-list",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    MultipleSetting = true,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | BuiltInFieldTypes.Choices | FieldFeatures.Places,
    Messages = BuiltInFieldTypes.Readable | FieldMessageKinds.Selections | FieldMessageKinds.Full,
});

/// <summary>The settings of a dropdown (ADR-0002).</summary>
public sealed class DropdownFieldSettings
{
    /// <summary>The first line of a dropdown before anything is chosen; the type's own text by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dropdownPrompt", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? Prompt { get; init; }

    /// <summary>Whether the visitor may choose several options, in a native list box.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dropdownMultiple", Public = true)]
    public bool Multiple { get; init; }

    /// <summary>The fewest options a visitor chooses when several may be chosen; no limit by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dropdownMinSelections", Group = FieldSettingGroup.Validation, Public = true)]
    public int? MinSelections { get; init; }

    /// <summary>The most options a visitor chooses when several may be chosen; no limit by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dropdownMaxSelections", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int? MaxSelections { get; init; }
}

/// <summary>Radio buttons (<c>radio</c>, section 4.3.2).</summary>
public sealed class RadioFieldType() : FormBuilderFieldTypeBase<RadioFieldSettings>(new FieldTypeDeclaration
{
    Alias = "radio",
    Name = "#jfbFieldTypes_radio",
    Icon = "icon-circle-dotted-active",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | BuiltInFieldTypes.Choices | FieldFeatures.Places,
    Messages = BuiltInFieldTypes.Readable | FieldMessageKinds.Full,
});

/// <summary>The settings of radio buttons (ADR-0002).</summary>
public sealed class RadioFieldSettings
{
    /// <summary>How the options are laid out; as the form says by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_radioLayout", Group = FieldSettingGroup.Appearance, Public = true)]
    public FieldChoiceLayout Layout { get; init; }

    /// <summary>An "Other" option with a text of its own; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_radioOther", Public = true)]
    public OtherOption? Other { get; init; }
}

/// <summary>Checkboxes, any number of which may be ticked (<c>checkboxes</c>, section 4.3.2).</summary>
public sealed class CheckboxesFieldType() : FormBuilderFieldTypeBase<CheckboxesFieldSettings>(new FieldTypeDeclaration
{
    Alias = "checkboxes",
    Name = "#jfbFieldTypes_checkboxes",
    Icon = "icon-checkbox",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choices,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | BuiltInFieldTypes.Choices | FieldFeatures.Places,
    Messages = BuiltInFieldTypes.Readable | FieldMessageKinds.Selections | FieldMessageKinds.Full,
});

/// <summary>The settings of checkboxes (ADR-0002).</summary>
public sealed class CheckboxesFieldSettings
{
    /// <summary>How the options are laid out; as the form says by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxesLayout", Group = FieldSettingGroup.Appearance, Public = true)]
    public FieldChoiceLayout Layout { get; init; }

    /// <summary>The fewest options a visitor ticks; no limit by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxesMinSelections", Group = FieldSettingGroup.Validation, Public = true)]
    public int? MinSelections { get; init; }

    /// <summary>The most options a visitor ticks; no limit by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxesMaxSelections", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int? MaxSelections { get; init; }

    /// <summary>An "Other" option with a text of its own; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxesOther", Public = true)]
    public OtherOption? Other { get; init; }
}

/// <summary>How a choice field lays out its options.</summary>
public enum FieldChoiceLayout
{
    /// <summary>As the form's appearance says (<c>inherit</c>).</summary>
    Inherit,

    /// <summary>One option per line (<c>vertical</c>).</summary>
    Vertical,

    /// <summary>Options side by side (<c>horizontal</c>).</summary>
    Horizontal,

    /// <summary>A card per option, with the option's image when it has one (<c>cards</c>).</summary>
    Cards,
}

/// <summary>The "Other" option of radio buttons or checkboxes, whose text the visitor types.</summary>
public sealed class OtherOption
{
    /// <summary>Its label; the type's own text by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_otherLabel")]
    public LocalizedText? Label { get; init; }
}

/// <summary>Statements rated on shared columns (<c>grid</c>, section 4.3.2); each row is stored as a field of its own.</summary>
public sealed class GridFieldType() : FormBuilderFieldTypeBase<GridFieldSettings>(new FieldTypeDeclaration
{
    Alias = "grid",
    Name = "#jfbFieldTypes_grid",
    Icon = "icon-grid",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.None,
    MultipleSetting = true,
    Features = BuiltInFieldTypes.Input | FieldFeatures.Options | FieldFeatures.Rows,
    Messages = BuiltInFieldTypes.Readable,
});

/// <summary>
/// The settings of a grid (ADR-0002): its columns come from its options or from a numbered scale, its rows from the
/// field's rows.
/// </summary>
public sealed class GridFieldSettings
{
    /// <summary>Whether each row takes several choices.</summary>
    [FormBuilderSetting("#jfbFieldSettings_gridMultiple", Public = true)]
    public bool Multiple { get; init; }

    /// <summary>A numbered scale as the columns, instead of options.</summary>
    [FormBuilderSetting("#jfbFieldSettings_gridScale", Order = 1, Public = true)]
    public GridScale? Scale { get; init; }
}

/// <summary>The numbered columns of a grid.</summary>
public sealed class GridScale
{
    /// <summary>The first number.</summary>
    [FormBuilderSetting("#jfbFieldSettings_gridScaleFrom")]
    public int From { get; init; } = 1;

    /// <summary>The last number.</summary>
    [FormBuilderSetting("#jfbFieldSettings_gridScaleTo", Order = 1)]
    public int To { get; init; } = 5;
}

/// <summary>Options to put in order (<c>ranking</c>, section 4.3.2).</summary>
public sealed class RankingFieldType() : FormBuilderFieldTypeBase<RankingFieldSettings>(new FieldTypeDeclaration
{
    Alias = "ranking",
    Name = "#jfbFieldTypes_ranking",
    Icon = "icon-ordered-list",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choices,
    Features = BuiltInFieldTypes.Input | BuiltInFieldTypes.Choices,
    Messages = BuiltInFieldTypes.Readable,
});

/// <summary>The settings of a ranking (ADR-0002).</summary>
public sealed class RankingFieldSettings
{
    /// <summary>How many options the visitor ranks; all of them by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_rankingTop", Public = true)]
    public int? Top { get; init; }
}

/// <summary>A single checkbox with a text beside it (<c>checkbox</c>, section 4.3.2); required means it is ticked.</summary>
public sealed class CheckboxFieldType() : FormBuilderFieldTypeBase<CheckboxFieldSettings>(new FieldTypeDeclaration
{
    Alias = "checkbox",
    Name = "#jfbFieldTypes_checkbox",
    Icon = "icon-check",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Bool,
    RuleKind = FieldKind.Checkbox,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill,
    Messages = FieldMessageKinds.Required,
});

/// <summary>The settings of a single checkbox (ADR-0002).</summary>
public sealed class CheckboxFieldSettings
{
    /// <summary>The text beside the box, which may hold links.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxText", Public = true)]
    public LocalizedHtml? Text { get; init; }
}

/// <summary>Yes or no (<c>yesno</c>, section 4.3.2).</summary>
public sealed class YesNoFieldType() : FormBuilderFieldTypeBase<YesNoFieldSettings>(new FieldTypeDeclaration
{
    Alias = "yesno",
    Name = "#jfbFieldTypes_yesno",
    Icon = "icon-thumb-up",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill,
    Messages = FieldMessageKinds.Required,
});

/// <summary>The settings of yes or no (ADR-0002).</summary>
public sealed class YesNoFieldSettings
{
    /// <summary>The label of yes; the type's own text by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_yesnoYesLabel", Public = true)]
    public LocalizedText? YesLabel { get; init; }

    /// <summary>The label of no; the type's own text by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_yesnoNoLabel", Order = 1, Public = true)]
    public LocalizedText? NoLabel { get; init; }

    /// <summary>How the two answers show.</summary>
    [FormBuilderSetting("#jfbFieldSettings_yesnoStyle", Group = FieldSettingGroup.Appearance, Public = true)]
    public YesNoStyle Style { get; init; }
}

/// <summary>How a yes or no field shows its answers.</summary>
public enum YesNoStyle
{
    /// <summary>Two buttons (<c>buttons</c>).</summary>
    Buttons,

    /// <summary>A switch (<c>switch</c>).</summary>
    Switch,
}

/// <summary>
/// A consent (<c>consent</c>, section 4.3.2): the exact text shown is stored (ADR-0009), so its value is the hash of the
/// wording, as text.
/// </summary>
public sealed class ConsentFieldType() : FormBuilderFieldTypeBase<ConsentFieldSettings>(new FieldTypeDeclaration
{
    Alias = "consent",
    Name = "#jfbFieldTypes_consent",
    Icon = "icon-handshake",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Checkbox,
    Features = BuiltInFieldTypes.Input,
    Messages = FieldMessageKinds.Required,
});

/// <summary>The settings of a consent (ADR-0002).</summary>
public sealed class ConsentFieldSettings
{
    /// <summary>The consent text, which may link to the privacy page and use merge tags that are the same for every visitor (ADR-0014).</summary>
    [FormBuilderSetting("#jfbFieldSettings_consentText", SupportsMergeTags = true, Public = true)]
    public LocalizedHtml? Text { get; init; }
}

/// <summary>A country (<c>country</c>, section 4.3.2), named from the culture data of the browser and the server.</summary>
public sealed class CountryFieldType() : FormBuilderFieldTypeBase<CountryFieldSettings>(new FieldTypeDeclaration
{
    Alias = "country",
    Name = "#jfbFieldTypes_country",
    Icon = "icon-globe",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete,
    Messages = BuiltInFieldTypes.Readable,
});

/// <summary>The settings of a country (ADR-0002), each country as its ISO 3166 alpha-2 code.</summary>
public sealed class CountryFieldSettings
{
    /// <summary>The countries a visitor may choose; every country by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_countryAllowed", Public = true)]
    public IReadOnlyList<string> Allowed { get; init; } = [];

    /// <summary>The countries shown first; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_countryTop", Order = 1, Public = true)]
    public IReadOnlyList<string> Top { get; init; } = [];

    /// <summary>The country chosen when the field starts; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_countryDefault", Order = 2, Public = true)]
    public string? Default { get; init; }
}
