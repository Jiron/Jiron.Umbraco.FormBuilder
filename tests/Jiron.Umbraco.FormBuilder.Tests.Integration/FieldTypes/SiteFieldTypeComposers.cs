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

/// <summary>A choice of colours, a site's own field type.</summary>
public sealed class ColourFieldType() : FormBuilderFieldTypeBase<ColourSettings>(new FieldTypeDeclaration
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
});

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
