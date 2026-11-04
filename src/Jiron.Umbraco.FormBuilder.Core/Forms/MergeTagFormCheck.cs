using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// The merge tags of every text where they render (ROADMAP 9.5b, ADR-0014, ADR-0177), each checked in its place: a tag
/// that names nothing renders empty and is a warning; a malformed tag, one past a limit, a source or filter the place
/// does not take (a consent text takes <c>form</c>, <c>page</c>, <c>site</c> and <c>dictionary</c> only) and a visitor
/// text that recalls a later field's answer (section 4.3.3) are errors. An action's type checks its own templates, in
/// the contexts it renders them in, and warns through its settings check (ADR-0158). A redirect that puts answers into
/// its address is a warning: answers in addresses end up in logs and browser history.
/// </summary>
/// <param name="tags">The merge-tag service, which knows the site's sources.</param>
/// <param name="fieldTypes">The field types, whose settings schemas say which settings take tags.</param>
internal sealed class MergeTagFormCheck(IFormBuilderMergeTagService tags, FieldTypeRegistry fieldTypes) : IFormCheck
{
    /// <summary>A redirect whose address recalls answers (a warning).</summary>
    public const string AnswersInUrl = "answers_in_url";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        foreach (var template in FormTemplates.Of(definition, fieldTypes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var problem in tags.Check(template.Text, template.Place, template.Context, definition, template.Owner))
            {
                var severity = problem.IsWarning ? FormCheckSeverity.Warning : FormCheckSeverity.Error;
                issues.Add(new FormCheckIssue(severity, problem.Code, template.Path, Sentence(problem)));
            }

            if (template.Place == MergeTagPlace.Redirect && RecallsAnswers(template.Text))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, AnswersInUrl, template.Path, "The redirect puts answers into its address, which ends up in logs and browser history."));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>(issues);
    }

    /// <summary>
    /// Whether an address names an answer: a field's value or a part of it (<c>field.</c>, but not a field's
    /// <c>.label</c>), or the entry's answers (<c>entry.answers</c>, <c>entry.table</c>); the entry's number, reference,
    /// date, state and address are none.
    /// </summary>
    public static bool RecallsAnswers(string url) =>
        MergeTagEngine.Parse(url, MergeTagContext.Url).Parts
            .OfType<TagPart>()
            .Any(tag => tag.Path is { } path
                && ((path.StartsWith("field.", StringComparison.Ordinal) && !path.EndsWith(".label", StringComparison.Ordinal))
                    || path is "entry.answers" or "entry.table"));

    /// <summary>A merge-tag problem as a sentence the editor reads.</summary>
    public static string Sentence(MergeTagProblem problem)
    {
        var what = problem.Code switch
        {
            MergeTagProblems.UnknownPath => "names nothing this form or site has, so it shows nothing",
            MergeTagProblems.UnknownFilter => "has a filter nobody knows, so it shows nothing",
            MergeTagProblems.FieldAfterText => "recalls the answer of a field that comes after this text",
            MergeTagProblems.SourceNotForPlace => "uses a source this text can't take",
            MergeTagProblems.FilterNotForPlace => "uses a site's own filter, which this text can't take",
            MergeTagProblems.TooManyTags => $"is one too many: a text takes at most {MergeTagEngine.MaxTags} tags",
            MergeTagProblems.TooManyFilters => $"has too many filters: a tag takes at most {MergeTagEngine.MaxFilters}",
            MergeTagProblems.TooLong => "makes the text longer than a text may be",
            _ => "is not written as a merge tag is, so it shows as written",
        };
        return $"The merge tag at character {problem.At + 1} {what}.";
    }
}
