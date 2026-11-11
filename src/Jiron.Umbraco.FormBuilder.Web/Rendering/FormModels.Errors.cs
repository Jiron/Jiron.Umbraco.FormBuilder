using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// The errors a post left (ADR-0099): each field's message, the editor's own or the package's default, under the field
// and in the error summary, whose heading counts its links; and the empty submission's message, linked to the page's
// first field.
internal sealed partial class FormModels
{
    // A field's error as the page shows it: the message under the field, and the text of its summary link, which puts
    // the field's label (and in a row the row's legend) before a default. An editor's own message, the field's for the
    // kind, the check's or the validation rule's, shows as written in both; an empty one is none.
    private static (string Message, string Link) ErrorOf(Build build, PublicField field, IFormBuilderFieldType type, ShownSettings settings, FormPostError error, string? label)
    {
        var check = error.Code == SubmissionProblems.Check && error.Check is { } at && at >= 0 && at < field.Validation.Count ? field.Validation[at] : null;
        var own = error.Code switch
        {
            SubmissionProblems.Check => Text(check?.Message),
            SubmissionProblems.Rule => Text(build.Render.Shown.Form.ValidationRules.FirstOrDefault(rule => rule.Key == error.Rule)?.Message),
            SubmissionProblems.Custom => error.Text is { Length: > 0 } said ? said : null,
            _ => Text(FieldMessageProperties.All.FirstOrDefault(property => property.Name == error.Code)?.Read(field.Messages)),
        };
        if (own is not null)
        {
            return (own, own);
        }

        var shown = new VisitorError
        {
            Code = error.Code,
            Field = new MessageField
            {
                Type = field.Type,
                BuiltIn = BuiltInFieldTypes.All.Contains(type.GetType()),
                Settings = settings.Json(),
                Options = [.. (field.Options?.Items ?? []).Select(option => new MessageOption(option.Value, Text(option.Label) ?? string.Empty))],
            },
            Check = check switch
            {
                PatternCheck => "pattern",
                MatchesFieldCheck => "matchesField",
                UniquePerFormCheck => "uniquePerForm",
                _ => null,
            },
            Other = check is MatchesFieldCheck matches ? LabelOf(build, matches.Field) : null,
            GridRow = error.GridRow is not null,
            Answers = (build.Answers.GetValueOrDefault(field.Key) ?? FieldAnswers.None).Values,
            Rows = build.Post?.Rows.GetValueOrDefault(field.Key)?.Count,
        };
        var culture = build.Render.Culture;
        var texts = build.Render.Shown.Texts;
        var message = VisitorMessages.Default(shown, culture, build.Formats, build.Today.DayNumber, texts);

        // A grid shows its errors below its rows, so a default at one of them names the row there and in the summary.
        var gridRow = error.GridRow is { } rowKey ? Text(field.Rows.FirstOrDefault(each => each.Key == rowKey)?.Label) : null;
        var row = gridRow ?? build.Place?.Legend;
        return (gridRow is null ? message : VisitorMessages.SummaryEntry(message, gridRow, null, culture, texts), VisitorMessages.SummaryEntry(message, label, row, culture, texts));
    }

    // The error summary's links: the empty submission's and a site's handler's errors at the form first, linked to the
    // first field of the page that takes an answer (the form without one), then the fields' in the page's order. A form
    // without the summary still shows those, which belong to no field; a handler's error without words shows the words
    // of a check without its own message (ADR-0233).
    private static List<ErrorLinkModel> Summary(Build build, bool everyError, PageViewModel? current)
    {
        var links = new List<ErrorLinkModel>();
        var formErrors = build.Post?.FormErrors ?? [];
        if (build.Post?.IsEmpty == true || formErrors.Count > 0)
        {
            var first = current?.Groups.Where(group => !group.IsHidden).SelectMany(group => group.Fields)
                .FirstOrDefault(field => !field.IsHidden && field.LabelElement != FieldLabelElement.None);
            var words = VisitorMessages.Words(build.Render.Culture, build.Render.Shown.Texts);
            if (build.Post?.IsEmpty == true)
            {
                links.Add(new ErrorLinkModel(words.Empty, first?.Id ?? build.Root.Id));
            }

            links.AddRange(formErrors.Select(text => new ErrorLinkModel(text is { Length: > 0 } said ? said : words.CheckThis, first?.Id ?? build.Root.Id)));
        }

        if (everyError)
        {
            links.AddRange(build.Errors);
        }

        return links;
    }

    // The label of a field of the form as the page shows it, a repeatable group's child among them; null for none.
    private static string? LabelOf(Build build, Guid key) =>
        build.Render.Shown.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field))
            .FirstOrDefault(field => field.Key == key) is { } other
            ? build.Text(other.Key, "label", other.Label)
            : null;
}
