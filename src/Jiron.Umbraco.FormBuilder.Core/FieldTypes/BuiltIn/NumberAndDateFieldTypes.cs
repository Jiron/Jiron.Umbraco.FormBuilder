using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>A number (<c>number</c>, section 4.3.2).</summary>
public sealed class NumberFieldType() : FormBuilderFieldTypeBase<NumberFieldSettings>(new FieldTypeDeclaration
{
    Alias = "number",
    Name = "#jfbFieldTypes_number",
    Icon = "icon-binarycode",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Number,
    RuleKind = FieldKind.Number,
    Features = BuiltInFieldTypes.Input | FieldFeatures.Placeholder | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | BuiltInFieldTypes.Comparable,
    Messages = BuiltInFieldTypes.Ranged,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(NumberFieldSettings settings, FieldSettingsCheck check)
    {
        BuiltInChecks.NotAbove(check, "min", settings.Min, "max", settings.Max);
        if (settings.Step is { } step && ExactDecimal.CompareText(step, "0") <= 0)
        {
            check.Invalid("step", $"The setting 'step' is more than 0, not {step}.");
        }

        foreach (var (name, value) in new[] { ("min", settings.Min), ("max", settings.Max), ("step", settings.Step) })
        {
            if (value is not null && BuiltInChecks.Places(value) > settings.Decimals)
            {
                check.Invalid(name, string.Create(CultureInfo.InvariantCulture, $"The setting '{name}', {value}, has more places after the point than 'decimals', {settings.Decimals}, allows."));
            }
        }
    }
}

/// <summary>The settings of a number (ADR-0002). The limits are decimals in the wire form of ADR-0005, as answers are.</summary>
public sealed class NumberFieldSettings
{
    /// <summary>The lowest number an answer may be; no limit by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_numberMin", Kind = FieldSettingKind.Number, Group = FieldSettingGroup.Validation, Public = true)]
    public string? Min { get; init; }

    /// <summary>The highest number an answer may be; no limit by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_numberMax", Kind = FieldSettingKind.Number, Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public string? Max { get; init; }

    /// <summary>The step an answer keeps from the minimum, or from zero without one; any number by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_numberStep", Kind = FieldSettingKind.Number, Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public string? Step { get; init; }

    /// <summary>How many places after the point an answer may have.</summary>
    [Range(0, 15)]
    [FormBuilderSetting("#jfbFieldSettings_numberDecimals", Group = FieldSettingGroup.Validation, Order = 3, Public = true)]
    public int Decimals { get; init; }

    /// <summary>A text shown before the input, such as a currency.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_numberPrefix", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? Prefix { get; init; }

    /// <summary>A text shown after the input, such as a unit.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_numberSuffix", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public LocalizedText? Suffix { get; init; }
}

/// <summary>A date (<c>date</c>, section 4.3.2), with a native date input.</summary>
public sealed class DateFieldType() : FormBuilderFieldTypeBase<DateFieldSettings>(new FieldTypeDeclaration
{
    Alias = "date",
    Name = "#jfbFieldTypes_date",
    Icon = "icon-calendar",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Date,
    RuleKind = FieldKind.Date,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete | BuiltInFieldTypes.Comparable,
    Messages = BuiltInFieldTypes.Readable | FieldMessageKinds.DateWindow,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(DateFieldSettings settings, FieldSettingsCheck check) =>
        BuiltInChecks.DateBounds(check, settings.Min, settings.Max);
}

/// <summary>
/// The settings of a date (ADR-0002). A limit is a date (<c>"2026-12-31"</c>) or a date relative to today in the operand
/// form of the rules (<c>{ "today": "+2d" }</c>), which the type checks itself.
/// </summary>
public sealed class DateFieldSettings
{
    /// <summary>The earliest date an answer may be; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dateMin", Editor = "dateBound", Group = FieldSettingGroup.Validation, Public = true)]
    public JsonElement? Min { get; init; }

    /// <summary>The latest date an answer may be; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dateMax", Editor = "dateBound", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public JsonElement? Max { get; init; }

    /// <summary>Whether an answer is a weekday, Monday to Friday.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dateWeekdaysOnly", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public bool WeekdaysOnly { get; init; }

    /// <summary>Dates an answer may not be, such as holidays.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dateExcluded", Kind = FieldSettingKind.Date, Group = FieldSettingGroup.Validation, Order = 3, Public = true)]
    public IReadOnlyList<string> Excluded { get; init; } = [];

    /// <summary>Whether the input starts at today's date.</summary>
    [FormBuilderSetting("#jfbFieldSettings_dateDefaultToday", Public = true)]
    public bool DefaultToday { get; init; }
}

/// <summary>A time of day (<c>time</c>, section 4.3.2).</summary>
public sealed class TimeFieldType() : FormBuilderFieldTypeBase<TimeFieldSettings>(new FieldTypeDeclaration
{
    Alias = "time",
    Name = "#jfbFieldTypes_time",
    Icon = "icon-time",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Time,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | BuiltInFieldTypes.Comparable,
    Messages = BuiltInFieldTypes.Ranged,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(TimeFieldSettings settings, FieldSettingsCheck check)
    {
        if (settings.Min is { } min && settings.Max is { } max && CalendarValues.ParseTime(min) > CalendarValues.ParseTime(max))
        {
            check.Invalid("min", $"The setting 'min' is {min}, later than 'max', {max}.");
        }
    }
}

/// <summary>The settings of a time of day (ADR-0002).</summary>
public sealed class TimeFieldSettings
{
    /// <summary>The earliest time an answer may be, <c>HH:mm</c>; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_timeMin", Kind = FieldSettingKind.Time, Group = FieldSettingGroup.Validation, Public = true)]
    public string? Min { get; init; }

    /// <summary>The latest time an answer may be, <c>HH:mm</c>; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_timeMax", Kind = FieldSettingKind.Time, Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public string? Max { get; init; }

    /// <summary>The minutes an answer keeps from the earliest time, or from midnight; any minute by default.</summary>
    [Range(1, 720)]
    [FormBuilderSetting("#jfbFieldSettings_timeStepMinutes", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public int? StepMinutes { get; init; }
}

/// <summary>A date and a time (<c>datetime</c>, section 4.3.2).</summary>
public sealed class DateTimeFieldType() : FormBuilderFieldTypeBase<DateTimeFieldSettings>(new FieldTypeDeclaration
{
    Alias = "datetime",
    Name = "#jfbFieldTypes_datetime",
    Icon = "icon-calendar-alt",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Date,
    RuleKind = FieldKind.DateTime,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill | BuiltInFieldTypes.Comparable,
    Messages = BuiltInFieldTypes.Readable | FieldMessageKinds.DateWindow | FieldMessageKinds.Step,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(DateTimeFieldSettings settings, FieldSettingsCheck check) =>
        BuiltInChecks.DateBounds(check, settings.Min, settings.Max);
}

/// <summary>The settings of a date and a time (ADR-0002): those of a date and of a time, combined.</summary>
public sealed class DateTimeFieldSettings
{
    /// <summary>The earliest moment an answer may be, as a date's limit is given; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_datetimeMin", Editor = "dateBound", Group = FieldSettingGroup.Validation, Public = true)]
    public JsonElement? Min { get; init; }

    /// <summary>The latest moment an answer may be, as a date's limit is given; none by default.</summary>
    [FormBuilderSetting("#jfbFieldSettings_datetimeMax", Editor = "dateBound", Group = FieldSettingGroup.Validation, Order = 1, Public = true)]
    public JsonElement? Max { get; init; }

    /// <summary>Whether an answer falls on a weekday, Monday to Friday.</summary>
    [FormBuilderSetting("#jfbFieldSettings_datetimeWeekdaysOnly", Group = FieldSettingGroup.Validation, Order = 2, Public = true)]
    public bool WeekdaysOnly { get; init; }

    /// <summary>Dates an answer may not fall on, such as holidays.</summary>
    [FormBuilderSetting("#jfbFieldSettings_datetimeExcluded", Kind = FieldSettingKind.Date, Group = FieldSettingGroup.Validation, Order = 3, Public = true)]
    public IReadOnlyList<string> Excluded { get; init; } = [];

    /// <summary>The minutes an answer's time keeps from midnight; any minute by default.</summary>
    [Range(1, 720)]
    [FormBuilderSetting("#jfbFieldSettings_datetimeStepMinutes", Group = FieldSettingGroup.Validation, Order = 4, Public = true)]
    public int? StepMinutes { get; init; }

    /// <summary>Whether the input starts at today's date.</summary>
    [FormBuilderSetting("#jfbFieldSettings_datetimeDefaultToday", Public = true)]
    public bool DefaultToday { get; init; }
}

/// <summary>A rating with symbols (<c>rating</c>, section 4.3.2).</summary>
public sealed class RatingFieldType() : FormBuilderFieldTypeBase<RatingFieldSettings>(new FieldTypeDeclaration
{
    Alias = "rating",
    Name = "#jfbFieldTypes_rating",
    Icon = "icon-star",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Number,
    RuleKind = FieldKind.Number,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill,
    Messages = BuiltInFieldTypes.Readable,
});

/// <summary>The settings of a rating (ADR-0002).</summary>
public sealed class RatingFieldSettings
{
    /// <summary>How many symbols the rating has, 3 to 10.</summary>
    [Range(3, 10)]
    [FormBuilderSetting("#jfbFieldSettings_ratingCount", Public = true)]
    public int Count { get; init; } = 5;

    /// <summary>The symbol it shows.</summary>
    [FormBuilderSetting("#jfbFieldSettings_ratingSymbol", Group = FieldSettingGroup.Appearance, Public = true)]
    public RatingSymbol Symbol { get; init; }

    /// <summary>The label of the lowest rating; none by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_ratingLowLabel", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public LocalizedText? LowLabel { get; init; }

    /// <summary>The label of the highest rating; none by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_ratingHighLabel", Group = FieldSettingGroup.Appearance, Order = 2, Public = true)]
    public LocalizedText? HighLabel { get; init; }
}

/// <summary>The symbol of a rating.</summary>
public enum RatingSymbol
{
    /// <summary>Stars (<c>star</c>).</summary>
    Star,

    /// <summary>Hearts (<c>heart</c>).</summary>
    Heart,

    /// <summary>Circles (<c>circle</c>).</summary>
    Circle,
}

/// <summary>A numbered scale, such as 0 to 10 (<c>scale</c>, section 4.3.2).</summary>
public sealed class ScaleFieldType() : FormBuilderFieldTypeBase<ScaleFieldSettings>(new FieldTypeDeclaration
{
    Alias = "scale",
    Name = "#jfbFieldTypes_scale",
    Icon = "icon-bar-chart",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Number,
    RuleKind = FieldKind.Number,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill,
    Messages = BuiltInFieldTypes.Readable,
});

/// <summary>The settings of a scale (ADR-0002).</summary>
public sealed class ScaleFieldSettings
{
    /// <summary>The first number, 0 or 1.</summary>
    [Range(0, 1)]
    [FormBuilderSetting("#jfbFieldSettings_scaleFrom", Public = true)]
    public int From { get; init; }

    /// <summary>The last number, 5 to 10.</summary>
    [Range(5, 10)]
    [FormBuilderSetting("#jfbFieldSettings_scaleTo", Order = 1, Public = true)]
    public int To { get; init; } = 10;

    /// <summary>The label of the first number; none by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_scaleLowLabel", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? LowLabel { get; init; }

    /// <summary>The label of the last number; none by default.</summary>
    [StringLength(100)]
    [FormBuilderSetting("#jfbFieldSettings_scaleHighLabel", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public LocalizedText? HighLabel { get; init; }
}

/// <summary>A slider (<c>slider</c>, section 4.3.2).</summary>
public sealed class SliderFieldType() : FormBuilderFieldTypeBase<SliderFieldSettings>(new FieldTypeDeclaration
{
    Alias = "slider",
    Name = "#jfbFieldTypes_slider",
    Icon = "icon-ruler",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Number,
    RuleKind = FieldKind.Number,
    Features = BuiltInFieldTypes.Input | FieldFeatures.DefaultValue | FieldFeatures.Prefill,
    Messages = BuiltInFieldTypes.Ranged,
})
{
    /// <inheritdoc />
    protected override void CheckSettings(SliderFieldSettings settings, FieldSettingsCheck check)
    {
        var ordered = ExactDecimal.CompareText(settings.Min, settings.Max) < 0;
        if (!ordered)
        {
            check.Invalid("min", $"The setting 'min' is {settings.Min}, not less than 'max', {settings.Max}.");
        }

        if (ExactDecimal.CompareText(settings.Step, "0") <= 0)
        {
            check.Invalid("step", $"The setting 'step' is more than 0, not {settings.Step}.");
        }
        else if (ordered)
        {
            var span = ExactDecimal.Subtract(ExactDecimal.Parse(settings.Max)!.Value, ExactDecimal.Parse(settings.Min)!.Value).ToString();
            if (ExactDecimal.CompareText(settings.Step, span) > 0)
            {
                check.Invalid("step", $"The setting 'step' is at most the distance from 'min' to 'max', {span}, not {settings.Step}.");
            }
        }
    }
}

/// <summary>The settings of a slider (ADR-0002), its limits as decimals in the wire form of ADR-0005.</summary>
public sealed class SliderFieldSettings
{
    /// <summary>The lowest number.</summary>
    [FormBuilderSetting("#jfbFieldSettings_sliderMin", Kind = FieldSettingKind.Number, Public = true)]
    public string Min { get; init; } = "0";

    /// <summary>The highest number.</summary>
    [FormBuilderSetting("#jfbFieldSettings_sliderMax", Kind = FieldSettingKind.Number, Order = 1, Public = true)]
    public string Max { get; init; } = "100";

    /// <summary>The step between two positions.</summary>
    [FormBuilderSetting("#jfbFieldSettings_sliderStep", Kind = FieldSettingKind.Number, Order = 2, Public = true)]
    public string Step { get; init; } = "1";

    /// <summary>The unit shown with the value; none by default.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_sliderUnit", Group = FieldSettingGroup.Appearance, Public = true)]
    public LocalizedText? Unit { get; init; }

    /// <summary>Whether the value shows beside the slider.</summary>
    [FormBuilderSetting("#jfbFieldSettings_sliderShowValue", Group = FieldSettingGroup.Appearance, Order = 1, Public = true)]
    public bool ShowValue { get; init; } = true;
}

/// <summary>A calculation over other fields (<c>calculation</c>, section 4.3.2), which the server recomputes.</summary>
public sealed class CalculationFieldType() : FormBuilderFieldTypeBase<CalculationFieldSettings>(new FieldTypeDeclaration
{
    Alias = "calculation",
    Name = "#jfbFieldTypes_calculation",
    Icon = "icon-calculator",
    PaletteGroup = FieldPaletteGroup.NumbersAndDates,
    HoldsValue = true,
    Storage = FieldStorageKind.Number,
    RuleKind = FieldKind.Number,
    Features = BuiltInFieldTypes.Shown,
});

/// <summary>The settings of a calculation (ADR-0002).</summary>
public sealed class CalculationFieldSettings
{
    /// <summary>The formula (section 4.3.3), over number, choice, rating and date fields.</summary>
    [Required]
    [FormBuilderSetting("#jfbFieldSettings_calculationFormula", Editor = "formula", Public = true)]
    public string? Formula { get; init; }

    /// <summary>How the result shows; two places after the point without one.</summary>
    [FormBuilderSetting("#jfbFieldSettings_calculationFormat", Group = FieldSettingGroup.Appearance, Public = true)]
    public CalculationFormat? Format { get; init; }

    /// <summary>Whether visitors see the result.</summary>
    [FormBuilderSetting("#jfbFieldSettings_calculationVisible", Order = 1, Public = true)]
    public bool Visible { get; init; } = true;
}

/// <summary>How a calculation's result shows.</summary>
public sealed class CalculationFormat
{
    /// <summary>The places after the point, 0 to 15; 2 when unset.</summary>
    [Range(0, 15)]
    [FormBuilderSetting("#jfbFieldSettings_calculationFormatDecimals")]
    public int? Decimals { get; init; }

    /// <summary>A text before the result, such as a currency.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_calculationFormatPrefix", Order = 1)]
    public LocalizedText? Prefix { get; init; }

    /// <summary>A text after the result, such as a unit.</summary>
    [StringLength(50)]
    [FormBuilderSetting("#jfbFieldSettings_calculationFormatSuffix", Order = 2)]
    public LocalizedText? Suffix { get; init; }
}
