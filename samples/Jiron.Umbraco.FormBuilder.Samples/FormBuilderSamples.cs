using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Samples.Colour;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Samples;

/// <summary>
/// The samples by name (docs/extending.md, ADR-0234). Each is what a site writes in its own composer; none registers
/// itself, so a site, the demo or a test turns on only the ones it names, and a sample turned on twice is on once.
/// </summary>
public static class FormBuilderSamples
{
    /// <summary>
    /// A colour choice: a field type whose colours setting has the sample's own editor, shown in the designer by the
    /// sample's own preview.
    /// </summary>
    public const string Colour = "colour";

    /// <summary>A view on the form workspace that shows a developer how to place the open form on a page.</summary>
    public const string EmbedView = "embedView";

    /// <summary>Every sample's name.</summary>
    public static IReadOnlyList<string> Names { get; } = [Colour, EmbedView];

    /// <summary>Turns a sample on by its name.</summary>
    /// <param name="builder">The site's Umbraco builder, in a composer.</param>
    /// <param name="name">The sample's name, one of <see cref="Names"/>.</param>
    /// <returns>The builder.</returns>
    /// <exception cref="ArgumentException">No sample has the name.</exception>
    public static IUmbracoBuilder AddFormBuilderSample(this IUmbracoBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        switch (name)
        {
            case Colour:
                if (SampleManifests.TurnOn(
                    builder.Services,
                    Colour,
                    new
                    {
                        type = "jfbSettingEditor",
                        alias = "Jiron.FormBuilder.Samples.SettingEditor.Swatches",
                        name = "Swatches",
                        element = SampleManifests.Folder + "colour-swatches.js",
                        meta = new { editor = ColourSettings.SwatchesEditor },
                    },
                    new
                    {
                        type = "jfbFieldPreview",
                        alias = "Jiron.FormBuilder.Samples.FieldPreview.Colour",
                        name = "Colour preview",
                        element = SampleManifests.Folder + "colour-preview.js",
                        meta = new { fieldType = ColourFieldType.TypeAlias },
                    }))
                {
                    builder.JironFormBuilder().FieldTypes().Append<ColourFieldType>();
                }

                break;
            case EmbedView:
                SampleManifests.TurnOn(
                    builder.Services,
                    EmbedView,
                    new
                    {
                        type = "workspaceView",
                        alias = "Jiron.FormBuilder.Samples.WorkspaceView.Embed",
                        name = "Embed",
                        element = SampleManifests.Folder + "embed-view.js",
                        // Umbraco puts the higher weight first: after the form's own views.
                        weight = 10,
                        meta = new { label = "#jfbSamples_embed", pathname = "embed", icon = "icon-code" },
                        conditions = new[] { new { alias = "Umb.Condition.WorkspaceAlias", match = "Jiron.FormBuilder.Workspace.Form" } },
                    });
                break;
            default:
                throw new ArgumentException($"There is no sample '{name}'; the samples are {string.Join(", ", Names)}.", nameof(name));
        }

        return builder;
    }
}
