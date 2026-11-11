using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Samples.Colour;

/// <summary>
/// A choice of colours (the colour sample, docs/extending.md): the visitor picks one of the colours the field offers,
/// each a swatch with its name. The answer is the colour's value, such as <c>#1f6feb</c>, which mail, merge tags and
/// exports show with the colour's name. The colours are one setting that no built-in editor draws, a list of names and
/// values, so the backoffice draws it with the sample's own editor and shows the field with the sample's own preview.
/// </summary>
public sealed class ColourFieldType() : FormBuilderFieldTypeBase<ColourSettings>(new FieldTypeDeclaration
{
    Alias = TypeAlias,
    Name = "#jfbSamples_colour",
    Icon = "icon-colorpicker",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    Features = FieldFeatures.Label | FieldFeatures.HideLabel | FieldFeatures.Help | FieldFeatures.Required | FieldFeatures.VisibilityRules,
    Messages = FieldMessageKinds.Required,
})
{
    /// <summary>The type's alias, which its view, its preview's manifest and its headless control name.</summary>
    public const string TypeAlias = "sample.colour";

    /// <inheritdoc />
    /// <remarks>Every colour has a name and a value such as <c>#1f6feb</c>, and no value comes twice.</remarks>
    protected override void CheckSettings(ColourSettings settings, FieldSettingsCheck check)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(check);
        if (ColourSwatch.Problem(settings.Colours) is { } problem)
        {
            check.Invalid(ColourSettings.ColoursName, problem);
        }
    }

    /// <inheritdoc />
    /// <remarks>One value, a colour's value written as the field offers it.</remarks>
    protected override FieldAnswerLimits AnswerLimits(ColourSettings settings, FormField field) => new(ColourSwatch.ValueLength, 1);

    /// <inheritdoc />
    /// <remarks>The answer is one of the colours the field offers.</remarks>
    protected override void Validate(ColourSettings settings, FieldValidation validation)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(validation);
        var offered = ColourSwatch.Read(settings.Colours);
        if (validation.Values.Any(value => !offered.Any(swatch => string.Equals(swatch.Value, value.Text, StringComparison.Ordinal))))
        {
            validation.Fail(FieldMessageKinds.Invalid);
        }
    }

    /// <inheritdoc />
    /// <remarks>A colour by its name and value, such as "Blue (#1f6feb)"; a colour the field no longer offers by its value.</remarks>
    protected override void Display(ColourSettings settings, FieldDisplay display)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(display);
        var offered = ColourSwatch.Read(settings.Colours);
        foreach (var value in display.Values)
        {
            var swatch = offered.FirstOrDefault(each => string.Equals(each.Value, value.Text, StringComparison.Ordinal));
            display.Add(swatch is null ? value.Text : $"{swatch.Name} ({swatch.Value})");
        }
    }
}
