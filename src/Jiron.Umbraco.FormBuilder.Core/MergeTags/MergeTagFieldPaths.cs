using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The <c>field.*</c> tags of a template that a form does not know (ADR-0046): what an action copied from another form
/// names that its new form lacks. They are the tags the form check finds unknown in the action place, where fields are
/// named by their current and former aliases, read in the text context, where every tag is read wherever it stands.
/// </summary>
internal static class MergeTagFieldPaths
{
    /// <summary>The <c>field.*</c> paths the form does not know, each with where it starts in the text.</summary>
    /// <param name="service">The merge-tag service, which knows the sources.</param>
    /// <param name="text">The template's text.</param>
    /// <param name="form">The form the template now belongs to.</param>
    public static IReadOnlyList<(string Path, int At)> Unknown(IFormBuilderMergeTagService service, string text, FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(form);
        if (!text.Contains("{{", StringComparison.Ordinal))
        {
            return [];
        }

        var unknown = service.Check(text, MergeTagPlace.Action, MergeTagContext.Text, form)
            .Where(problem => problem.Code == MergeTagProblems.UnknownPath)
            .Select(problem => problem.At)
            .ToHashSet();
        if (unknown.Count == 0)
        {
            return [];
        }

        return
        [
            .. MergeTagEngine.Parse(text, MergeTagContext.Text).Parts
                .OfType<TagPart>()
                .Where(tag => tag.Path is { } path && path.StartsWith("field.", StringComparison.Ordinal) && unknown.Contains(tag.PathAt))
                .Select(tag => (tag.Path!, tag.PathAt)),
        ];
    }
}
