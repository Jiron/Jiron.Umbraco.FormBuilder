using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.FieldTypes;

/// <summary>
/// The switch of a site's composers that change the field types (ADR-0055). They are found like any site's composers,
/// so they do nothing unless a test's site turns them on, and every other test's site keeps the built-in types.
/// </summary>
internal static class SiteFieldTypes
{
    /// <summary>The configuration key that turns the composers on.</summary>
    public const string Switch = "JironFormBuilderTests:SiteFieldTypes";

    /// <summary>Whether the site turned them on.</summary>
    public static bool On(IUmbracoBuilder builder) => builder.Config.GetValue<bool>(Switch);
}

/// <summary>Adds a colour field, as a site's composer does, before the core composer runs.</summary>
[ComposeBefore(typeof(FormBuilderCoreComposer))]
public sealed class AddColourFieldTypeComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (SiteFieldTypes.On(builder))
        {
            builder.JironFormBuilder().FieldTypes().Append<ColourFieldType>();
        }
    }
}

/// <summary>Removes the signature, as a site's composer does, after the core composer ran.</summary>
[ComposeAfter(typeof(FormBuilderCoreComposer))]
public sealed class RemoveSignatureFieldTypeComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        if (SiteFieldTypes.On(builder))
        {
            builder.JironFormBuilder().FieldTypes().Remove<SignatureFieldType>();
        }
    }
}

/// <summary>
/// A choice of colours, a site's own field type, which needs the site's brand colours in its configuration
/// (<see cref="SwatchesKey"/>) and is not configured without them (ADR-0057), and shows a colour in merge tags by its
/// name with a capital (ADR-0066).
/// </summary>
/// <param name="configuration">The site's configuration, a singleton service the type takes.</param>
public sealed class ColourFieldType(IConfiguration configuration) : FormBuilderFieldTypeBase<ColourSettings>(new FieldTypeDeclaration
{
    Alias = "acme.colour",
    Name = "Colour",
    Icon = "icon-colorpicker",
    PaletteGroup = FieldPaletteGroup.Choices,
    HoldsValue = true,
    Storage = FieldStorageKind.Text,
    RuleKind = FieldKind.Choice,
    Features = FieldFeatures.Label | FieldFeatures.Help | FieldFeatures.Required | FieldFeatures.VisibilityRules,
    Messages = FieldMessageKinds.Required,
})
{
    /// <summary>The configuration key that holds the brand colours.</summary>
    public const string SwatchesKey = "Acme:Swatches";

    /// <summary>The reason the type gives while the brand colours are missing.</summary>
    public const string MissingSwatches = "Set Acme:Swatches to the brand colours.";

    /// <inheritdoc />
    public override ValueTask<FieldTypeAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(string.IsNullOrWhiteSpace(configuration[SwatchesKey]) ? FieldTypeAvailability.NotConfigured(MissingSwatches) : FieldTypeAvailability.Available);

    /// <inheritdoc />
    protected override void Display(ColourSettings settings, FieldDisplay display)
    {
        foreach (var value in display.Values)
        {
            display.Add(char.ToUpperInvariant(value.Text[0]) + value.Text[1..]);
        }
    }
}

/// <summary>The settings of a colour field: one visitors see, and one that stays on the server.</summary>
public sealed class ColourSettings
{
    /// <summary>The colours.</summary>
    [FormBuilderSetting("Swatches", Public = true)]
    public IReadOnlyList<string> Swatches { get; init; } = [];

    /// <summary>Where the colours come from.</summary>
    [FormBuilderSetting("Source")]
    public string? Source { get; init; }
}
