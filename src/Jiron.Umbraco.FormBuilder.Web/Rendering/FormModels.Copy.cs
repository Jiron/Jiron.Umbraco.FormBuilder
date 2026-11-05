using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// The copy of the answers (ADR-0123): below a message outcome that offers it, on the page after a post without
// JavaScript sent the form, the entry's answers once, as the summary page writes them without "Change", by the pages,
// groups and fields the rules showed; on a later view a line in its place. Print and "Fill in the form again" follow.
internal sealed partial class FormModels
{
    // The copy below the outcome, or null for an outcome that offers none.
    private async Task<OutcomeCopyModel?> CopyAsync(FormRender render, Root root, FormPostCopy? copy, RenderingWords words, CultureInfo culture, CancellationToken cancellationToken)
    {
        if (copy is null)
        {
            return null;
        }

        return new OutcomeCopyModel
        {
            Answers = copy.Entry is { } entry ? await CopiedAsync(CopyShown.Of(render.Shown), root.Id, root.Classes, entry, words, culture, cancellationToken) : null,
            PrintText = words.PrintAnswers,
            ShownText = words.CopyShown,
            Again = new OutcomeLinkModel(AgainUrl(render.HttpContext, root.Id), words.FillInAgain),
        };
    }

    // The entry's answers in the summary's sections: one for each page the rules showed that lists a row, in the form's
    // order as the page shows it now, and a row for each field on it that they showed and that takes an answer, from
    // the version the entry was sent with. A field with no answer reads "Not answered", unless its answer is kept only
    // until the actions run, or the entry does not record what the rules hid. The ids of its headings are below the id.
    private async Task<SummaryViewModel> CopiedAsync(CopyShown visitor, string id, ThemeClassesModel classes, EntryCopy entry, RenderingWords words, CultureInfo culture, CancellationToken cancellationToken)
    {
        var scope = entry.Scope;
        var hidden = entry.Hidden ?? new HashSet<Guid>();
        var defined = AllFields(scope.Form).ToDictionary(field => field.Key);
        var recalled = await CopyTextsAsync(visitor, scope.Answers, cancellationToken);
        var listed = new List<(PublicPage Page, List<(PublicField Shown, FormField Defined)> Fields)>();
        foreach (var page in visitor.Form.Pages.Where(page => !hidden.Contains(page.Key)))
        {
            var fields = page.Groups.Where(group => !hidden.Contains(group.Key)).SelectMany(group => group.Fields)
                .Where(field => !hidden.Contains(field.Key))
                .Select(field => (Shown: field, Defined: defined.GetValueOrDefault(field.Key)))
                .Where(field => field.Defined is not null && TakesAnswer(field.Shown, field.Defined))
                .Select(field => (field.Shown, field.Defined!))
                .ToList();
            listed.Add((page, fields));
        }

        var texts = await answerTexts.TextsAsync(
            scope,
            listed.SelectMany(page => page.Fields).Select(field => field.Defined)
                .SelectMany(field => field.Fields.Select(child => new AnswerField(child, field)).Prepend(new AnswerField(field, null))),
            cancellationToken);
        var sections = new List<(PublicPage Page, List<SummaryAnswerModel> Answers)>();
        foreach (var (page, fields) in listed)
        {
            var answers = new List<SummaryAnswerModel>();
            foreach (var (shown, field) in fields)
            {
                var (isMasked, answer) = answerTexts.Summary(field, scope, texts);
                if (!isMasked && answer.Count == 0 && (entry.Hidden is null || field.Keep == FieldKeep.UntilActionsRun))
                {
                    continue;
                }

                answers.Add(new SummaryAnswerModel
                {
                    Field = field.Key,
                    Label = Recalled(recalled, shown.Key, "label") ?? Text(shown.Label) ?? shown.Alias ?? string.Empty,
                    Texts = [.. answer.Select(text => (IReadOnlyList<string>)text.Split('\n'))],
                    IsRows = MergeTagAnswerTexts.IsRepeater(field),
                    IsMasked = isMasked,
                    Mask = MergeTagAnswerTexts.Masked,
                    EmptyText = words.NotAnswered,
                    HiddenText = words.HiddenAnswer,
                    ErrorId = id + "-copy-" + field.Key.ToString("N") + "-error",
                });
            }

            if (answers.Count > 0)
            {
                sections.Add((page, answers));
            }
        }

        return new SummaryViewModel
        {
            Classes = classes,
            Sections =
            [
                .. sections.Select((section, index) => new SummarySectionModel
                {
                    Page = section.Page.Key,
                    Title = Recalled(recalled, section.Page.Key, "title") ?? Text(section.Page.Title)
                        ?? (sections.Count > 1 ? string.Format(culture, words.Page, index + 1) : words.YourAnswers),
                    HeadingId = id + "-copy-" + section.Page.Key.ToString("N"),
                    Answers = section.Answers,
                }),
            ],
        };
    }

    // Whether a field takes an answer the copy lists, as the page builds one: one that holds a value, other than a
    // hidden field, a captcha and a calculation the page does not show.
    private bool TakesAnswer(PublicField shown, FormField field) =>
        types.Find(shown.Type) is { HoldsValue: true } traits
        && traits.Type is not (HiddenFieldType or CaptchaFieldType)
        && (traits.Type is not CalculationFieldType calculation || calculation.ReadSettings(field.Settings).Visible);

    // The labels and the page titles that hold tags, rendered for the entry's answers as the page renders them.
    private async Task<Dictionary<(Guid Owner, string Name), string>> CopyTextsAsync(CopyShown shown, MergeTagAnswers answers, CancellationToken cancellationToken)
    {
        var recalled = new Dictionary<(Guid Owner, string Name), string>();
        var scope = shown.Scope with { Answers = answers };
        foreach (var text in shown.Tagged.Where(text => text.Name is "label" or "title"))
        {
            recalled[(text.Owner, text.Name)] = (await mergeTags.RenderAsync(text.Text, MergeTagPlace.VisitorText, MergeTagContext.Text, scope with { TextOwner = text.Owner }, cancellationToken)).Text;
        }

        return recalled;
    }

    private static string? Recalled(Dictionary<(Guid Owner, string Name), string> recalled, Guid owner, string name) =>
        recalled.TryGetValue((owner, name), out var shown) && shown.Length > 0 ? shown : null;

    // The page again at the form, without the token: the sent page's address holds no other query (ADR-0100).
    private static string AgainUrl(HttpContext httpContext, string id)
    {
        // The render endpoint renders for the page that asked.
        var path = FormRenderTargets.Get(httpContext)?.Page ?? (httpContext.Request.PathBase + httpContext.Request.Path).ToUriComponent();
        return (path.Length == 0 ? "/" : path) + "#" + id;
    }
}

/// <summary>
/// The form a copy of the answers is listed by (ADR-0123): the visitor's form in the culture, its visitor texts that hold
/// tags, and the scope they render in, as a page shows them or an outcome's preview does (ADR-0187).
/// </summary>
/// <param name="Form">The visitor's form in the culture.</param>
/// <param name="Tagged">Its labels and titles that hold tags.</param>
/// <param name="Scope">The scope they render in, before the entry's answers.</param>
internal sealed record CopyShown(PublicForm Form, IReadOnlyList<VisitorText> Tagged, MergeTagScope Scope)
{
    /// <summary>The form as a page shows it.</summary>
    public static CopyShown Of(FormShown shown)
    {
        ArgumentNullException.ThrowIfNull(shown);
        return new CopyShown(shown.Form, shown.Tagged, shown.Scope);
    }
}
