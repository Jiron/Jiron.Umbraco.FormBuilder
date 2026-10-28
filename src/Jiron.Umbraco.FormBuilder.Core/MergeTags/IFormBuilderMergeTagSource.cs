using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// A source of merge-tag values of a site's own (section 4.9, ADR-0014, ADR-0065), for example <c>{{ crm.segment }}</c>,
/// registered through <c>builder.JironFormBuilder().MergeTagSources().Append&lt;CrmSource&gt;()</c>. A source is a
/// singleton: it holds no state of its own and may take singleton services in its constructor. Its values reach every
/// place escaped by the place's context, so a source gives text, never markup.
/// </summary>
public interface IFormBuilderMergeTagSource
{
    /// <summary>
    /// The name before the path's first dot: a lowercase ASCII letter, then ASCII letters and digits, and none of the
    /// built-in sources' names. Start-up refuses a name that is taken or outside the grammar.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Whether the values differ per visitor (a visitor's data, the request), so that no page that may be cached shows
    /// them: visitor texts, consent texts and default values refuse such a source. Read once as the site starts, with
    /// <see cref="Name"/> and <see cref="Places"/>; a source that throws there stops the start, naming its type.
    /// </summary>
    bool IsPerVisitor { get; }

    /// <summary>
    /// The places the source may be used in, within those that take a site's sources; null for all of them. Entry
    /// titles and consent texts take no site's source. Read once as the site starts, and copied: a later change to the
    /// set changes nothing.
    /// </summary>
    IReadOnlySet<MergeTagPlace>? Places => null;

    /// <summary>The paths the picker offers for a form, each a whole path with the source's name first; none by default.</summary>
    /// <param name="form">The form whose templates the picker serves.</param>
    IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) => [];

    /// <summary>
    /// The value of a path, or null when the source does not know the path, which renders empty with an
    /// <c>unknown_path</c> warning; a known path without a value is <see cref="MergeTagValue.Empty"/>. An exception
    /// renders the tag empty and is logged without values, so a source never stops a mail or a page.
    /// </summary>
    /// <param name="request">The path after the source's name and dot (empty for the name alone), and what the template is rendered for.</param>
    /// <param name="cancellationToken">Cancelled when the rendering is.</param>
    ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken);
}

/// <summary>A source's path to resolve, and what the template is rendered for.</summary>
/// <param name="Path">The path after the source's name and dot, for example <c>segment</c>; empty for the name alone.</param>
/// <param name="Scope">The form, culture, site, entry and the query and context values.</param>
/// <param name="Place">Where the template is used.</param>
/// <param name="Context">The context the template renders in.</param>
public sealed record MergeTagSourceRequest(string Path, MergeTagScope Scope, MergeTagPlace Place, MergeTagContext Context);
