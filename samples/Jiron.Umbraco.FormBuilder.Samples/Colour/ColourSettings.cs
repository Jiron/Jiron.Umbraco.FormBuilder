using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Samples.Colour;

/// <summary>
/// The settings of a colour field: its colours, which the sample's own editor draws, and whether their names show
/// beside the swatches, which the built-in switch draws. Both are public, so <c>&lt;jfb-form&gt;</c> gets them.
/// </summary>
public sealed class ColourSettings
{
    /// <summary>The name of the colours setting in a field's settings.</summary>
    public const string ColoursName = "colours";

    /// <summary>The editor's alias that the sample's <c>jfbSettingEditor</c> manifest names.</summary>
    public const string SwatchesEditor = "sample.swatches";

    /// <summary>
    /// The colours the visitor chooses from, in order: a JSON list of <c>{ "name": "Blue", "value": "#1f6feb" }</c>,
    /// which <see cref="ColourSwatch.Read"/> reads and the field type checks when a form is published.
    /// </summary>
    [FormBuilderSetting("#jfbSamples_colours", Description = "#jfbSamples_coloursHelp", Editor = SwatchesEditor, Order = 1, Public = true)]
    public JsonElement Colours { get; init; } = ColourSwatch.Write(
    [
        new ColourSwatch("Blue", "#1f6feb"),
        new ColourSwatch("Green", "#2da44e"),
        new ColourSwatch("Red", "#cf222e"),
    ]);

    /// <summary>Whether each colour's name shows beside its swatch; hidden, it is still each swatch's accessible name.</summary>
    [FormBuilderSetting("#jfbSamples_showNames", Group = FieldSettingGroup.Appearance, Order = 2, Public = true)]
    public bool ShowNames { get; init; } = true;
}
