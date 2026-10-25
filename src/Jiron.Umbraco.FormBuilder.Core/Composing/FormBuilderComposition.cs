using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Umbraco.Cms.Core.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Core.Composing;

/// <summary>
/// Where a site or an add-on changes what Jiron Form Builder is made of (section 4.9, ADR-0055), from a composer:
/// <c>builder.JironFormBuilder().FieldTypes().Append&lt;ColourFieldType&gt;()</c>.
/// </summary>
public sealed class FormBuilderComposition
{
    internal FormBuilderComposition(IUmbracoBuilder builder) => Builder = builder;

    /// <summary>The Umbraco builder the composition belongs to.</summary>
    public IUmbracoBuilder Builder { get; }

    /// <summary>The field types, the built-in ones among them.</summary>
    public FormBuilderFieldTypeCollectionBuilder FieldTypes() => Builder.WithCollectionBuilder<FormBuilderFieldTypeCollectionBuilder>();
}

/// <summary>The way into Jiron Form Builder's composition from a composer (section 4.9).</summary>
public static class FormBuilderUmbracoBuilderExtensions
{
    /// <summary>Jiron Form Builder's composition: its field types, and later its other registries.</summary>
    public static FormBuilderComposition JironFormBuilder(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return new FormBuilderComposition(builder);
    }
}
