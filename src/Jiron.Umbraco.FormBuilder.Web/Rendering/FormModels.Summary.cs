using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// The summary page (ADR-0122): with the setting on, the last page the rules show, when they show a page before it, shows
// the answers of every page they show before it, as the outcome's copy writes them, each with a "Change" that opens its
// page; after a send from it, an error of an earlier page shows in its row and the error summary links to its "Change".
internal sealed partial class FormModels
{
    // The page that shows the summary: the last page the rules show, with the setting on and a page shown before it.
    private static PublicPage? SummaryPageOf(PublicFormSettings settings, List<PublicPage> shownPages, PublicPage? current) =>
        settings.SummaryPage && shownPages.Count > 1 && current is not null && current == shownPages[^1] ? current : null;

    // The answers of the pages before the summary page, a section for each that the rules show, from the models those
    // pages were built into. The error links of their fields (by each field's range in the build's errors) are moved to
    // the fields' "Change".
    private async Task<SummaryViewModel> SummaryAsync(
        Build build,
        IReadOnlyList<PageViewModel> built,
        List<PublicPage> shownPages,
        PageRead read,
        FormState state,
        Dictionary<Guid, (int From, int To)> linked,
        CancellationToken cancellationToken)
    {
        var (render, root, words, culture) = (build.Render, build.Root, build.Words, build.Culture);
        var definition = render.Form.Published.Definition;
        var scope = render.Shown.Scope with { Answers = SummaryAnswers(definition, read, build.Hidden, state, build.Post) };
        var sections = new List<(PageViewModel Page, int Number, List<FieldViewModel> Fields)>();
        foreach (var page in built)
        {
            var number = shownPages.FindIndex(shown => shown.Key == page.Key);
            if (number < 0 || number == shownPages.Count - 1)
            {
                continue;
            }

            var fields = page.Groups.Where(group => !group.IsHidden).SelectMany(group => group.Fields)
                .Where(field => !field.IsHidden && field.LabelElement != FieldLabelElement.None && field.Control is not CaptchaControlModel)
                .ToList();
            sections.Add((page, number + 1, fields));
        }

        var listed = sections.SelectMany(section => section.Fields).Select(field => build.Definition(field.Key)).ToList();
        var texts = await answerTexts.TextsAsync(
            scope,
            listed.SelectMany(field => field.Fields.Select(child => new AnswerField(child, field)).Prepend(new AnswerField(field, null))),
            cancellationToken);
        return new SummaryViewModel
        {
            Classes = root.Classes,
            Sections =
            [
                .. sections.Select(section => new SummarySectionModel
                {
                    Page = section.Page.Key,
                    Title = section.Page.Title ?? string.Format(culture, words.Page, section.Number),
                    HeadingId = root.Id + "-answers-" + section.Page.Key.ToString("N"),
                    Answers = [.. section.Fields.Select(field => Answer(build, field, scope, texts, linked))],
                }),
            ],
        };
    }

    // One field's row: its label, its answer as the outcome's copy writes it, and its "Change", which its error links
    // in the error summary now lead to; a calculation, which nobody types, has no "Change".
    private SummaryAnswerModel Answer(Build build, FieldViewModel field, MergeTagScope scope, ScopeTexts texts, Dictionary<Guid, (int From, int To)> linked)
    {
        var (root, words) = (build.Root, build.Words);
        var defined = build.Definition(field.Key);
        var (isMasked, answer) = answerTexts.Summary(defined, scope, texts);
        var label = field.Label ?? field.Alias ?? string.Empty;
        var change = field.Control is CalculationControlModel || field.Alias is null
            ? null
            : new SummaryChangeModel(root.Id + "-change-" + field.Alias, FormPostFields.Goto + FormPostFields.Separator + field.Alias, root.At(field.Id), words.Change, label);
        string? error = field.Error;
        if (linked.TryGetValue(field.Key, out var range))
        {
            for (var at = range.From; at < range.To; at++)
            {
                // A repeatable group's own error, or else its first row's, which names the row.
                error ??= build.Errors[at].Message;
                if (change is not null)
                {
                    build.Errors[at] = build.Errors[at] with { Target = change.Id };
                }
            }
        }

        return new SummaryAnswerModel
        {
            Field = field.Key,
            Label = label,
            Texts = [.. answer.Select(text => (IReadOnlyList<string>)text.Split('\n'))],
            IsRows = MergeTagAnswerTexts.IsRepeater(defined),
            IsMasked = isMasked,
            Mask = MergeTagAnswerTexts.Masked,
            EmptyText = words.NotAnswered,
            HiddenText = words.HiddenAnswer,
            Change = change,
            Error = error,
            ErrorId = root.Id + "-change-" + (field.Alias ?? field.Key.ToString("N")) + "-error",
        };
    }

    // The page's answers as the summary lists them: as visitor texts recall them, with the names and sizes of the files
    // the post kept, so a file field shows its files' names, and each calculation's value as the rules work it out, in
    // each row of a repeatable group too.
    private static MergeTagAnswers SummaryAnswers(FormDefinition definition, PageRead read, HashSet<string> hidden, FormState state, FormPostState? post)
    {
        static IReadOnlyList<FieldFile>? FilesOf(ImmutableArray<FieldValue> values, IReadOnlyList<FormPostFile>? kept)
        {
            if (kept is not { Count: > 0 })
            {
                return null;
            }

            var files = new List<FieldFile>();
            foreach (var value in values)
            {
                if (kept.FirstOrDefault(file => file.Token == value.Text) is not { } file)
                {
                    return null;
                }

                files.Add(new FieldFile(file.Name, file.Size));
            }

            return files;
        }

        // A calculation's value as the rules work it out, as its answer; none without one.
        static IReadOnlyList<FieldValue>? Calculated(IReadOnlyDictionary<string, string?> calculations, FormField field) =>
            calculations.GetValueOrDefault(Id(field.Key)) is { Length: > 0 } value ? [new FieldValue(value)] : null;

        var answers = new List<MergeTagAnswer>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (read.Fields.TryGetValue(field.Key, out var values))
            {
                answers.Add(new MergeTagAnswer(field.Key, values, FilesOf(values, post?.Files.GetValueOrDefault(field.Key))));
            }
            else if (Calculated(state.Calculations, field) is { } calculated)
            {
                answers.Add(new MergeTagAnswer(field.Key, calculated));
            }

            if (!read.Rows.TryGetValue(field.Key, out var rows))
            {
                continue;
            }

            var states = state.Rows.GetValueOrDefault(Id(field.Key));
            var posted = post?.Rows.GetValueOrDefault(field.Key);
            for (var at = 0; at < rows.Count; at++)
            {
                var shown = states is not null && at < states.Count ? states[at] : null;
                foreach (var child in field.Fields.Where(child => shown?.Hidden.Contains(Id(child.Key)) != true))
                {
                    if (rows[at].TryGetValue(child.Key, out var given))
                    {
                        var kept = posted is not null && at < posted.Count ? posted[at].Files.GetValueOrDefault(child.Key) : null;
                        answers.Add(new MergeTagAnswer(child.Key, given, FilesOf(given, kept), row: at));
                    }
                    else if (shown is not null && Calculated(shown.Calculations, child) is { } inRow)
                    {
                        answers.Add(new MergeTagAnswer(child.Key, inRow, row: at));
                    }
                }
            }
        }

        return new MergeTagAnswers(answers, AllFields(definition).Where(field => hidden.Contains(Id(field.Key))).Select(field => field.Key));
    }
}
