using System.Collections.Immutable;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>The built-in field types of section 4.3.2 (ADR-0055), and what several of them share.</summary>
internal static class BuiltInFieldTypes
{
    /// <summary>A field a visitor answers: a label that may be hidden, a help text, required, and show and hide rules.</summary>
    public const FieldFeatures Input = FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.Required | FieldFeatures.VisibilityRules;

    /// <summary>A field a visitor types text into: an input with a placeholder, a default, a prefill, autocomplete and every check.</summary>
    public const FieldFeatures TextInput = Input | FieldFeatures.Placeholder | FieldFeatures.DefaultValue | FieldFeatures.Prefill | FieldFeatures.Autocomplete
        | FieldFeatures.PatternCheck | FieldFeatures.MatchesFieldCheck | FieldFeatures.UniquePerFormCheck;

    /// <summary>The checks that compare one field's answer with others.</summary>
    public const FieldFeatures Comparable = FieldFeatures.MatchesFieldCheck | FieldFeatures.UniquePerFormCheck;

    /// <summary>Options, which the field cannot be answered without.</summary>
    public const FieldFeatures Choices = FieldFeatures.Options | FieldFeatures.NeedsOptions;

    /// <summary>A label, a hidden label, a help text and show and hide rules, for a field the visitor does not answer.</summary>
    public const FieldFeatures Shown = FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.VisibilityRules;

    /// <summary>An answer that is required and may not be read.</summary>
    public const FieldMessageKinds Readable = FieldMessageKinds.Required | FieldMessageKinds.Invalid;

    /// <summary>A readable answer within a range and on a step.</summary>
    public const FieldMessageKinds Ranged = Readable | FieldMessageKinds.Min | FieldMessageKinds.Max | FieldMessageKinds.Step;

    /// <summary>Every built-in type, in the order the palette shows them.</summary>
    public static readonly ImmutableArray<Type> All =
    [
        typeof(TextFieldType),
        typeof(TextareaFieldType),
        typeof(EmailFieldType),
        typeof(PhoneFieldType),
        typeof(UrlFieldType),
        typeof(NumberFieldType),
        typeof(DateFieldType),
        typeof(TimeFieldType),
        typeof(DateTimeFieldType),
        typeof(RatingFieldType),
        typeof(ScaleFieldType),
        typeof(SliderFieldType),
        typeof(CalculationFieldType),
        typeof(DropdownFieldType),
        typeof(RadioFieldType),
        typeof(CheckboxesFieldType),
        typeof(GridFieldType),
        typeof(RankingFieldType),
        typeof(CheckboxFieldType),
        typeof(YesNoFieldType),
        typeof(ConsentFieldType),
        typeof(CountryFieldType),
        typeof(NameFieldType),
        typeof(AddressFieldType),
        typeof(FileFieldType),
        typeof(SignatureFieldType),
        typeof(RepeaterFieldType),
        typeof(HiddenFieldType),
        typeof(HeadingFieldType),
        typeof(ParagraphFieldType),
        typeof(DividerFieldType),
        typeof(ImageFieldType),
        typeof(CaptchaFieldType),
    ];
}
