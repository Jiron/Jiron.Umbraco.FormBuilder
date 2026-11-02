using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// A repeatable group's rows (ADR-0098): the rows the page's answers hold, each a fieldset numbered after its row label
// whose fields are named by their path and follow their row's state, with the Add and Remove buttons the no-JS post
// reads (ADR-0101).
internal sealed partial class FormModels
{
    // The rows of a repeatable group, each row's fields built in its row, and the buttons that add and remove one: Add
    // below the most rows, opening the page at the row added, and Remove above the least, opening it at the group.
    private async Task<RepeaterControlModel> RowsAsync(Build build, PublicField field, RepeaterFieldType type, ShownSettings settings, string id, CancellationToken cancellationToken)
    {
        var limits = type.ReadSettings(build.Definition(field.Key).Settings);
        var alias = field.Alias ?? string.Empty;
        var count = build.RowAnswers.GetValueOrDefault(field.Key)?.Count ?? 0;
        var label = settings.Text("rowLabel") is { Length: > 0 } given ? given : null;
        var remove = settings.Text("removeLabel") is { Length: > 0 } removing ? removing : build.Words.RemoveRow;
        var rows = new List<RepeaterRowModel>();
        for (var row = 0; row < count; row++)
        {
            rows.Add(await RowAsync(row));
        }

        return new RepeaterControlModel
        {
            Rows = rows,
            Template = await RowAsync(count),
            Least = limits.MinRows,
            Most = limits.MaxRows,
            Legend = label is null ? build.Words.RepeaterRow : label + " {0}",
            Removed = build.Words.RowRemoved,
            Add = new RowButtonModel { Value = FormPostFields.Add + FormPostFields.Separator + alias, Text = settings.Text("addLabel") is { Length: > 0 } adding ? adding : build.Words.AddRow, Action = build.Root.At(build.RowPrefix(alias, count)), IsHidden = count >= limits.MaxRows },
            RowCount = new HiddenInputModel(FormPostFields.RowsPrefix + alias, count.ToString(CultureInfo.InvariantCulture)),
            FieldsCssClass = "jfb-fields jfb-columns-" + build.ColumnsOf(field.Key).ToString(CultureInfo.InvariantCulture),
            FieldView = build.Root.Views.Field,
        };

        // A row at its place, each of its fields built in it; one past the others starts as a first render does.
        async Task<RepeaterRowModel> RowAsync(int row)
        {
            var number = (row + 1).ToString(CultureInfo.InvariantCulture);
            var legend = label is null ? string.Format(build.Culture, build.Words.RepeaterRow, number) : label + " " + number;
            var inRow = build.Row(field, row, legend);
            var fields = new List<FieldViewModel>();
            foreach (var child in field.Fields)
            {
                if (await FieldAsync(inRow, child, cancellationToken) is { } built)
                {
                    fields.Add(built);
                }
            }

            return new RepeaterRowModel
            {
                Index = row,
                Id = inRow.Prefix,
                Legend = legend,
                Remove = new RowButtonModel { Value = FormPostFields.Remove + FormPostFields.Separator + alias + FormPostFields.Separator + row.ToString(CultureInfo.InvariantCulture), Text = remove, Context = legend, Action = build.Root.At(id), IsHidden = count <= limits.MinRows },
                Fields = fields,
            };
        }
    }

    // A repeatable group's rows (ADR-0098): a post's, at most as many as a group may have, each with its child fields'
    // answers only, a row the visitor just added starting as on a first render (ADR-0100); and never fewer than the
    // group's least, each row added starting so, its fields as a field outside a group starts. A group a rule hid posts
    // no rows, as its controls are disabled, so once shown it shows its least as a first render does.
    private List<Dictionary<Guid, FieldAnswers>> RowsOf(PublicField group, FormPostState? post, Dictionary<Guid, FormField> defined, DateTime local, FormPrefill prefill)
    {
        var children = group.Fields.Select(child => child.Key).ToHashSet();
        List<Dictionary<Guid, FieldAnswers>> rows = [.. (post?.Rows.GetValueOrDefault(group.Key) ?? []).Take(RepeaterFieldType.MostRows)
            .Select(row => row.IsNew ? Started() : row.Answers.Where(pair => children.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value))];
        var least = types.Find(group.Type)?.Type is RepeaterFieldType repeater ? Math.Min(repeater.ReadSettings(defined[group.Key].Settings).MinRows, RepeaterFieldType.MostRows) : 0;
        while (rows.Count < least)
        {
            rows.Add(Started());
        }

        return rows;

        Dictionary<Guid, FieldAnswers> Started() => StartedRow(group, defined, local, prefill);
    }

    // A row as a first render starts it.
    private Dictionary<Guid, FieldAnswers> StartedRow(PublicField group, Dictionary<Guid, FormField> defined, DateTime local, FormPrefill prefill)
    {
        var answers = new Dictionary<Guid, FieldAnswers>();
        foreach (var child in group.Fields)
        {
            if (StartOf(child, defined[child.Key], local, prefill) is { } start)
            {
                answers[child.Key] = start;
            }
        }

        return answers;
    }
}
