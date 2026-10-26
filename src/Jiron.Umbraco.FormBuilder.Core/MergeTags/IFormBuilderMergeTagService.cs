using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Renders and checks merge-tag templates (section 4.9, ADR-0014, ADR-0064, ADR-0065): each in the place it is used and
/// the context it renders in, with the built-in sources, the site's own sources and filters, and the culture's formats
/// as the server writes them. Nothing in a template runs as code, and no template throws.
/// </summary>
public interface IFormBuilderMergeTagService
{
    /// <summary>
    /// Renders a template: each value escaped for its context, a tag with an error as it is written, one with warnings
    /// only as nothing, a source the place does not take refused at its path, and in a visitor text a field after the
    /// text refused at its path.
    /// </summary>
    /// <param name="text">The template's text.</param>
    /// <param name="place">Where the template is used.</param>
    /// <param name="context">The context it renders in, one the place takes.</param>
    /// <param name="scope">What it is rendered for.</param>
    /// <param name="cancellationToken">Cancels the sources' work.</param>
    /// <exception cref="ArgumentException">The place does not render in the context.</exception>
    Task<MergeTagRendering> RenderAsync(string text, MergeTagPlace place, MergeTagContext context, MergeTagScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// The problems a template has where it is used, before any value: those of the language, a source or filter the
    /// place does not take, a field a visitor text names that does not come before it, and a source no one has (a
    /// warning), in the order of the template.
    /// </summary>
    /// <param name="text">The template's text.</param>
    /// <param name="place">Where the template is used.</param>
    /// <param name="context">The context it renders in, one the place takes.</param>
    /// <param name="form">The form the template belongs to.</param>
    /// <param name="textOwner">For a visitor text, the page, group or field whose text it is (<see cref="MergeTagScope.TextOwner"/>).</param>
    /// <exception cref="ArgumentException">The place does not render in the context.</exception>
    IReadOnlyList<MergeTagProblem> Check(string text, MergeTagPlace place, MergeTagContext context, FormDefinition form, Guid? textOwner = null);

    /// <summary>The paths the picker offers in a place for a form, of every source the place takes, in the sources' order.</summary>
    /// <param name="place">Where the template is used.</param>
    /// <param name="form">The form.</param>
    IReadOnlyList<MergeTagPathInfo> Paths(MergeTagPlace place, FormDefinition form);
}
