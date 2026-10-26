using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
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
})
{
    /// <inheritdoc />
    protected override void CheckSettings(DropdownFieldSettings settings, FieldSettingsCheck check)
    {
        if (!settings.Multiple)
        {
            foreach (var (name, value) in new[] { ("minSelections", settings.MinSelections), ("maxSelections", settings.MaxSelections) })
            {
                if (value is not null)
                {
                    check.Invalid(name, $"A dropdown takes one choice unless 'multiple' is on, so it has no '{name}'.");
                }
            }

            return;
        }

        BuiltInChecks.NotAbove(check, "minSelections", settings.MinSelections, "maxSelections", settings.MaxSelections);
        BuiltInChecks.NotMoreThanOptions(check, "minSelections", settings.MinSelections);
    }

    /// <inheritdoc />
    /// <remarks>Each value is one of the options; with <c>multiple</c>, as many as the selections allow.</remarks>
    protected override void Validate(DropdownFieldSettings settings, FieldValidation validation)
    {
        BuiltInValidation.AmongOptions(validation);
        if (settings.Multiple)
        {
            BuiltInValidation.Selections(validation, settings.MinSelections, settings.MaxSelections);
        }
    }
}

/// <summary>The settings of a dropdown (ADR-0002).</summary>
public sealed class DropdownFieldSettings
{
    /// <summary>The first line of a dropdown before anything is chosen; the type's own text by default.</summary>
    [StringLength(200)]
    [FormBuilderSetting("#jfbFieldSettings_dropdownPrompt", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? Prompt { get; init; }

    /// <summary>Whether the visitor may choose several options, in a native list box.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dropdownMultiple", Public = true)]
    public bool Multiple { get; init; }

    /// <summary>The fewest options a visitor chooses when several may be chosen; no limit by default.</summary>
    [Range(1, 1000)]
    [FormBuilderSetting("#jfbFieldSettings_dropdownMinSelections", Group = FieldSettingGroup.Validation, Public = true)]
    public int? MinSelections { get; init; }

    /// <summary>The most options a visitor chooses when several may be chosen; no limit by default.</summary>
    [Range(1, 1000)]
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
})
{
    /// <summary>The part that holds the visitor's own text when the field offers "Other" (ADR-0058).</summary>
    public const string OtherPart = BuiltInAnswers.OtherPart;

    /// <inheritdoc />
    protected override void CheckSettings(RadioFieldSettings settings, FieldSettingsCheck check) =>
        BuiltInChecks.OtherLabelNeedsOther(check, settings.Other, settings.OtherLabel);

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(RadioFieldSettings settings, FormField field) =>
        settings.Other ? new(FieldAnswerLimits.DefaultTextLength, 2) : new(DefinitionSaveChecks.OptionValueLength, 1);

    /// <inheritdoc />
    protected override void Read(RadioFieldSettings settings, FieldReading reading) =>
        BuiltInAnswers.ReadChoices(reading, several: false, settings.Other);

    /// <inheritdoc />
    /// <remarks>The value is one of the options, or the visitor's own "Other" text.</remarks>
    protected override void Validate(RadioFieldSettings settings, FieldValidation validation) => BuiltInValidation.AmongOptions(validation);
}

/// <summary>The settings of radio buttons (ADR-0002).</summary>
public sealed class RadioFieldSettings
{
    /// <summary>How the options are laid out; as the form says by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_radioLayout", Group = FieldSettingGroup.Appearance, Public = true)]
    public FieldChoiceLayout Layout { get; init; }

    /// <summary>Whether the options end with "Other", whose text the visitor types.</summary>
    [FormBuilderSetting("#jfbFieldSettings_radioOther", Public = true)]
    public bool Other { get; init; }

    /// <summary>The label of the "Other" option; the type's own text by default.</summary>
    [StringLength(200)]
    [FormBuilderSetting("#jfbFieldSettings_radioOtherLabel", Order = 1, Public = true)]
    public LocalizedText? OtherLabel { get; init; }
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
})
{
    /// <inheritdoc />
    protected override void CheckSettings(CheckboxesFieldSettings settings, FieldSettingsCheck check)
    {
        BuiltInChecks.NotAbove(check, "minSelections", settings.MinSelections, "maxSelections", settings.MaxSelections);
        BuiltInChecks.NotMoreThanOptions(check, "minSelections", settings.MinSelections, settings.Other ? 1 : 0);
        BuiltInChecks.OtherLabelNeedsOther(check, settings.Other, settings.OtherLabel);
    }

    /// <summary>The part that holds the visitor's own text when the field offers "Other" (ADR-0058).</summary>
    public const string OtherPart = BuiltInAnswers.OtherPart;

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(CheckboxesFieldSettings settings, FormField field) =>
        settings.Other
            ? new(FieldAnswerLimits.DefaultTextLength, FieldAnswerLimits.DefaultManyTexts + 1)
            : new(DefinitionSaveChecks.OptionValueLength, FieldAnswerLimits.DefaultManyTexts);

    /// <inheritdoc />
    protected override void Read(CheckboxesFieldSettings settings, FieldReading reading) =>
        BuiltInAnswers.ReadChoices(reading, several: true, settings.Other);

    /// <inheritdoc />
    /// <remarks>Each value is one of the options or the visitor's own "Other" text, which counts as one selection.</remarks>
    protected override void Validate(CheckboxesFieldSettings settings, FieldValidation validation)
    {
        BuiltInValidation.AmongOptions(validation);
        BuiltInValidation.Selections(validation, settings.MinSelections, settings.MaxSelections);
    }
}

/// <summary>The settings of checkboxes (ADR-0002).</summary>
public sealed class CheckboxesFieldSettings
{
    /// <summary>How the options are laid out; as the form says by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxesLayout", Group = FieldSettingGroup.Appearance, Public = true)]
    public FieldChoiceLayout Layout { get; init; }

    /// <summary>The fewest options a visitor ticks; no limit by default.</summary>
    [Range(1, 1000)]
    [FormBuilderSetting("#jfbFieldSettings_checkboxesMinSelections", Group = FieldSettingGroup.Validation, Public = true)]
    public int? MinSelections { get; init; }

    /// <summary>The most options a visitor ticks; no limit by default.</summary>
    [Range(1, 1000)]
    [FormBuilderSetting("#jfbFieldSettings_checkboxesMaxSelections", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public int? MaxSelections { get; init; }

    /// <summary>Whether the options end with "Other", whose text the visitor types.</summary>
    [FormBuilderSetting("#jfbFieldSettings_checkboxesOther", Public = true)]
    public bool Other { get; init; }

    /// <summary>The label of the "Other" option; the type's own text by default.</summary>
    [StringLength(200)]
    [FormBuilderSetting("#jfbFieldSettings_checkboxesOtherLabel", Order = 1, Public = true)]
    public LocalizedText? OtherLabel { get; init; }
}

/// <summary>How a choice field lays out its options.</summary>
public enum FieldChoiceLayout
{
    /// <summary>As the form's appearance says (<c>inherit</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_layoutInherit")]
    Inherit,

    /// <summary>One option per line (<c>vertical</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_layoutVertical")]
    Vertical,

    /// <summary>Options side by side (<c>horizontal</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_layoutHorizontal")]
    Horizontal,

    /// <summary>A card per option, with the option's image when it has one (<c>cards</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_layoutCards")]
    Cards,
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
})
{
    /// <inheritdoc />
    protected override void CheckSettings(GridFieldSettings settings, FieldSettingsCheck check)
    {
        var hasOptions = check.Field.Options is { } options && (options.Source != OptionSource.Items || options.Items.Count > 0);
        if (settings.Scale is not null && hasOptions)
        {
            check.Invalid("scale", "A grid's columns come from its options or from a scale, not from both.");
        }
        else if (settings.Scale is null && !hasOptions)
        {
            check.Invalid("scale", "A grid's columns come from its options or from a scale; it has neither.");
        }
    }

    /// <summary>The part that holds a row's chosen columns: the row's key in the <c>D</c> format (ADR-0058).</summary>
    /// <param name="row">The row's key.</param>
    public static string RowPart(Guid row) => row.ToString("D", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(GridFieldSettings settings, FormField field) =>
        field.Rows.Count == 0
            ? FieldAnswerLimits.None
            : new(DefinitionSaveChecks.OptionValueLength, field.Rows.Count * (settings.Multiple ? FieldAnswerLimits.DefaultManyTexts : 1));

    /// <inheritdoc />
    /// <remarks>Each row gives its chosen columns as its own values; texts that answer no row are ignored.</remarks>
    protected override void Read(GridFieldSettings settings, FieldReading reading)
    {
        foreach (var row in reading.Field.Rows)
        {
            var chosen = reading.Answers.Part(RowPart(row.Key));
            if ((!settings.Multiple && chosen.Length > 1) || chosen.Distinct(StringComparer.Ordinal).Count() != chosen.Length)
            {
                reading.Invalid();
                return;
            }

            foreach (var value in chosen)
            {
                reading.Add(value, row.Key);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each row's values are among the columns: the scale's whole numbers when the grid has one, otherwise its options.
    /// </remarks>
    protected override void Validate(GridFieldSettings settings, FieldValidation validation)
    {
        Func<string, bool>? column = settings.Scale is { } scale
            ? value => BuiltInValidation.IsWholeNumber(value, scale.From, scale.To)
            : BuiltInValidation.OwnOptions(validation.Field) is { } options ? options.Contains : null;
        if (column is not null && !validation.Values.All(value => column(value.Text)))
        {
            validation.Fail(FieldMessageKinds.Invalid);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each answered row as its statement and its columns, such as <c>Service: Good</c>: the columns' labels, or the
    /// scale's numbers; the columns of one row apart by " / ".
    /// </remarks>
    protected override void Display(GridFieldSettings settings, FieldDisplay display)
    {
        foreach (var row in display.Field.Rows)
        {
            var columns = display.Values.Where(value => value.Row == row.Key)
                .Select(value => settings.Scale is null ? display.OptionLabel(value.Text) : display.Number(value.Text))
                .ToList();
            if (columns.Count > 0)
            {
                var chosen = string.Join(" / ", columns);
                display.Add(display.Text(row.Label) is { Length: > 0 } statement ? statement + ": " + chosen : chosen);
            }
        }
    }
}

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
    [Range(0, 1)]
    [FormBuilderSetting("#jfbFieldSettings_gridScaleFrom")]
    public int From { get; init; } = 1;

    /// <summary>The last number.</summary>
    [Range(2, 10)]
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
})
{
    /// <inheritdoc />
    protected override void CheckSettings(RankingFieldSettings settings, FieldSettingsCheck check) =>
        BuiltInChecks.NotMoreThanOptions(check, "top", settings.Top);

    /// <inheritdoc />
    /// <remarks>
    /// The options come in their order, or, from a form without JavaScript, as parts: each option's value with the
    /// position the visitor gave it, a whole number from 1. Positions given twice, or both forms at once, are unreadable.
    /// </remarks>
    protected override void Read(RankingFieldSettings settings, FieldReading reading)
    {
        if (reading.Answers.Parts.IsEmpty)
        {
            base.Read(settings, reading);
            return;
        }

        var ranked = new List<(int Position, string Value)>();
        foreach (var (value, positions) in reading.Answers.Parts)
        {
            if (!reading.Answers.Values.IsEmpty || positions is not [var text]
                || !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < 1)
            {
                reading.Invalid();
                return;
            }

            ranked.Add((position, value));
        }

        if (ranked.Select(each => each.Position).Distinct().Count() != ranked.Count)
        {
            reading.Invalid();
            return;
        }

        foreach (var (_, value) in ranked.OrderBy(each => each.Position))
        {
            reading.Add(value);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each value is one of the options, which reading takes once each, and there are as many as <c>top</c>, or as all
    /// the options when it is not given or exceeds them.
    /// </remarks>
    protected override void Validate(RankingFieldSettings settings, FieldValidation validation)
    {
        if (BuiltInValidation.OwnOptions(validation.Field) is not { } options)
        {
            return;
        }

        var ranked = settings.Top is { } top ? Math.Min(top, options.Count) : options.Count;
        if (validation.Values.Length != ranked || !validation.Values.All(value => options.Contains(value.Text)))
        {
            validation.Fail(FieldMessageKinds.Invalid);
        }
    }
}

/// <summary>The settings of a ranking (ADR-0002).</summary>
public sealed class RankingFieldSettings
{
    /// <summary>How many options the visitor ranks; all of them by default.</summary>
    [Range(1, 1000)]
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
    [StringLength(20000)]
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
})
{
    /// <summary>The value of yes.</summary>
    public const string Yes = "yes";

    /// <summary>The value of no.</summary>
    public const string No = "no";

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(YesNoFieldSettings settings, FormField field) => new(Yes.Length, 1);

    /// <inheritdoc />
    protected override void Read(YesNoFieldSettings settings, FieldReading reading)
    {
        switch (reading.Answers.Values)
        {
            case []:
                break;
            case [Yes or No]:
                reading.Add(reading.Answers.Values[0]);
                break;
            default:
                reading.Invalid();
                break;
        }
    }

    /// <inheritdoc />
    /// <remarks>The field's label of the answer, or the word for yes or no in the culture.</remarks>
    protected override void Display(YesNoFieldSettings settings, FieldDisplay display)
    {
        foreach (var value in display.Values)
        {
            var yes = value.Text == Yes;
            display.Add(display.Text(yes ? settings.YesLabel : settings.NoLabel) is { Length: > 0 } label ? label : yes ? display.Yes : display.No);
        }
    }
}

/// <summary>The settings of yes or no (ADR-0002).</summary>
public sealed class YesNoFieldSettings
{
    /// <summary>The label of yes; the type's own text by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_yesnoYesLabel", Public = true)]
    public LocalizedText? YesLabel { get; init; }

    /// <summary>The label of no; the type's own text by default.</summary>
    [StringLength(100)]
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
    [FormBuilderChoice("#jfbFieldChoices_yesnoStyleButtons")]
    Buttons,

    /// <summary>A switch (<c>switch</c>).</summary>
    [FormBuilderChoice("#jfbFieldChoices_yesnoStyleSwitch")]
    Switch,
}

/// <summary>
/// A consent (<c>consent</c>, section 4.3.2): the exact text shown is stored (ADR-0009), so its value is the hash of the
/// wording, as text.
/// </summary>
public sealed partial class ConsentFieldType() : FormBuilderFieldTypeBase<ConsentFieldSettings>(new FieldTypeDeclaration
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
})
{
    /// <summary>The part that holds the hash of the wording the visitor was shown (ADR-0009, ADR-0058).</summary>
    public const string WordingPart = "wording";

    /// <summary>The length of a wording's hash: a SHA-256 in lowercase hex.</summary>
    public const int WordingHashLength = 64;

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(ConsentFieldSettings settings, FormField field) => new(WordingHashLength, 2);

    /// <inheritdoc />
    /// <remarks>
    /// A tick reads as <c>true</c>, which rules compare as a ticked box, and stores the wording's hash; a tick without a
    /// wording's hash cannot be read, and anything but a tick leaves the consent not given.
    /// </remarks>
    protected override void Read(ConsentFieldSettings settings, FieldReading reading)
    {
        if (reading.Answers.Values.Length > 1)
        {
            reading.Invalid();
        }
        else if (reading.Answers.Values is [FieldAnswerDefaults.Ticked])
        {
            if (reading.Answers.Part(WordingPart) is [var hash] && WordingHash().IsMatch(hash))
            {
                reading.Add(new FieldValue(FieldAnswerDefaults.Ticked) { StoredText = hash });
            }
            else
            {
                reading.Invalid();
            }
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex WordingHash();
}

/// <summary>The settings of a consent (ADR-0002).</summary>
public sealed class ConsentFieldSettings
{
    /// <summary>The consent text, which may link to the privacy page and use merge tags that are the same for every visitor (ADR-0014).</summary>
    [Required]
    [StringLength(20000)]
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
})
{
    /// <inheritdoc />
    protected override void CheckSettings(CountryFieldSettings settings, FieldSettingsCheck check)
    {
        for (var i = 0; i < settings.Allowed.Count; i++)
        {
            BuiltInChecks.IsCountry(check, BuiltInChecks.At("allowed", i), settings.Allowed[i]);
        }

        for (var i = 0; i < settings.Top.Count; i++)
        {
            var top = settings.Top[i];
            if (BuiltInChecks.IsCountry(check, BuiltInChecks.At("top", i), top) && settings.Allowed.Count > 0 && !settings.Allowed.Contains(top, StringComparer.Ordinal))
            {
                check.Invalid(BuiltInChecks.At("top", i), $"The country {top} is shown first, but 'allowed' leaves it out.");
            }
        }

        if (settings.Default is { } country && BuiltInChecks.IsCountry(check, "default", country) && settings.Allowed.Count > 0
            && !settings.Allowed.Contains(country, StringComparer.Ordinal))
        {
            check.Invalid("default", $"The country {country} is chosen at the start, but 'allowed' leaves it out.");
        }
    }

    /// <inheritdoc />
    protected override FieldAnswerLimits AnswerLimits(CountryFieldSettings settings, FormField field) => new(2, 1);

    /// <inheritdoc />
    /// <remarks>A country is its ISO 3166 alpha-2 code in capitals, such as <c>CH</c>; any other text is unreadable.</remarks>
    protected override void Read(CountryFieldSettings settings, FieldReading reading)
    {
        if (!reading.Answers.Values.All(BuiltInChecks.CountryCodes.Contains))
        {
            reading.Invalid();
            return;
        }

        base.Read(settings, reading);
    }

    /// <inheritdoc />
    /// <remarks>The country is one of <c>allowed</c>, when it lists some.</remarks>
    protected override void Validate(CountryFieldSettings settings, FieldValidation validation)
    {
        if (settings.Allowed.Count > 0 && !validation.Values.All(value => settings.Allowed.Contains(value.Text, StringComparer.Ordinal)))
        {
            validation.Fail(FieldMessageKinds.Invalid);
        }
    }

    /// <inheritdoc />
    /// <remarks>The country's name in the culture's language, such as <c>Schweiz</c> for <c>CH</c> in German.</remarks>
    protected override void Display(CountryFieldSettings settings, FieldDisplay display)
    {
        foreach (var value in display.Values)
        {
            display.Add(display.CountryName(value.Text));
        }
    }
}

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
