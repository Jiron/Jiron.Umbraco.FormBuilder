using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>The site's own merge-tag sources (ADR-0065), in the order the picker offers them.</summary>
public sealed class FormBuilderMergeTagSourceCollection : BuilderCollectionBase<IFormBuilderMergeTagSource>
{
    /// <summary>A collection of the sources the builder creates.</summary>
    public FormBuilderMergeTagSourceCollection(Func<IEnumerable<IFormBuilderMergeTagSource>> items)
        : base(items)
    {
    }
}

/// <summary>
/// The ordered collection of a site's own merge-tag sources (ADR-0065), reached through
/// <c>builder.JironFormBuilder().MergeTagSources()</c>, for example <c>.Append&lt;CrmSource&gt;()</c>. The built-in
/// sources are not in it and cannot be replaced; each source is a singleton, created by the container.
/// </summary>
public sealed class FormBuilderMergeTagSourceCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderMergeTagSourceCollectionBuilder, FormBuilderMergeTagSourceCollection, IFormBuilderMergeTagSource>
{
    /// <inheritdoc />
    protected override FormBuilderMergeTagSourceCollectionBuilder This => this;
}

/// <summary>The site's own merge-tag filters (ADR-0065).</summary>
public sealed class FormBuilderMergeTagFilterCollection : BuilderCollectionBase<IFormBuilderMergeTagFilter>
{
    /// <summary>A collection of the filters the builder creates.</summary>
    public FormBuilderMergeTagFilterCollection(Func<IEnumerable<IFormBuilderMergeTagFilter>> items)
        : base(items)
    {
    }
}

/// <summary>
/// The ordered collection of a site's own merge-tag filters (ADR-0065), reached through
/// <c>builder.JironFormBuilder().MergeTagFilters()</c>, for example <c>.Append&lt;InitialsFilter&gt;()</c>. The built-in
/// filters are not in it and cannot be replaced; each filter is a singleton, created by the container.
/// </summary>
public sealed class FormBuilderMergeTagFilterCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderMergeTagFilterCollectionBuilder, FormBuilderMergeTagFilterCollection, IFormBuilderMergeTagFilter>
{
    /// <inheritdoc />
    protected override FormBuilderMergeTagFilterCollectionBuilder This => this;
}
