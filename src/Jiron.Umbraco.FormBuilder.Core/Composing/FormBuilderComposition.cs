using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
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

    /// <summary>The action types, the built-in ones among them (ADR-0158).</summary>
    public FormBuilderActionTypeCollectionBuilder ActionTypes() => Builder.WithCollectionBuilder<FormBuilderActionTypeCollectionBuilder>();

    /// <summary>The site's own merge-tag sources, beside the built-in ones (ADR-0065).</summary>
    public FormBuilderMergeTagSourceCollectionBuilder MergeTagSources() => Builder.WithCollectionBuilder<FormBuilderMergeTagSourceCollectionBuilder>();

    /// <summary>The site's own merge-tag filters, beside the built-in ones (ADR-0065).</summary>
    public FormBuilderMergeTagFilterCollectionBuilder MergeTagFilters() => Builder.WithCollectionBuilder<FormBuilderMergeTagFilterCollectionBuilder>();

    /// <summary>The option sources, the standard lists among them (ADR-0073).</summary>
    public FormBuilderOptionSourceCollectionBuilder OptionSources() => Builder.WithCollectionBuilder<FormBuilderOptionSourceCollectionBuilder>();

    /// <summary>The captcha providers, the built-in ones among them (ADR-0142).</summary>
    public FormBuilderCaptchaProviderCollectionBuilder CaptchaProviders() => Builder.WithCollectionBuilder<FormBuilderCaptchaProviderCollectionBuilder>();

    /// <summary>The site's file scanners (ADR-0144).</summary>
    public FormBuilderFileScannerCollectionBuilder FileScanners() => Builder.WithCollectionBuilder<FormBuilderFileScannerCollectionBuilder>();

    /// <summary>The site's own spam checks (ADR-0140).</summary>
    public FormBuilderSpamCheckCollectionBuilder SpamChecks() => Builder.WithCollectionBuilder<FormBuilderSpamCheckCollectionBuilder>();

    /// <summary>The themes of the website forms, the built-in ones among them (ADR-0105).</summary>
    public FormBuilderThemeCollectionBuilder Themes() => Builder.WithCollectionBuilder<FormBuilderThemeCollectionBuilder>();
}

/// <summary>The way into Jiron Form Builder's composition from a composer (section 4.9).</summary>
public static class FormBuilderUmbracoBuilderExtensions
{
    /// <summary>Jiron Form Builder's composition: its field types, its merge-tag sources and filters, its option sources and its themes.</summary>
    public static FormBuilderComposition JironFormBuilder(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return new FormBuilderComposition(builder);
    }
}
