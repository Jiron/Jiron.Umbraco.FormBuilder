using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// A repeatable group's rows (ADR-0098): the rows the page's answers hold, each a fieldset numbered after its row label
// whose fields are named by their path and follow their row's state, with the Add and Remove buttons the no-JS post
// reads (6.2i).
internal sealed partial class FormModels
{
    // The rows of a repeatable group, each row's fields built in its row, and the buttons that add and remove one: Add
    // below the most rows, Remove above the least.
    private async Task<RepeaterControlModel> RowsAsync(Build build, PublicField field, RepeaterFieldType type, ShownSettings settings, CancellationToken cancellationToken)
    {
        var limits = type.ReadSettings(build.Definition(field.Key).Settings);
        var alias = field.Alias ?? string.Empty;
        var count = build.RowAnswers.GetValueOrDefault(field.Key)?.Count ?? 0;
        var label = settings.Text("rowLabel") is { Length: > 0 } given ? given : null;
        var remove = settings.Text("removeLabel") is { Length: > 0 } removing ? removing : build.Words.RemoveRow;
        var rows = new List<RepeaterRowModel>();
        for (var row = 0; row < count; row++)
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

            rows.Add(new RepeaterRowModel
            {
                Index = row,
                Id = inRow.Prefix,
                Legend = legend,
                Remove = count > limits.MinRows
                    ? new RowButtonModel { Value = "remove:" + alias + ":" + row.ToString(CultureInfo.InvariantCulture), Text = remove, Context = legend }
                    : null,
                Fields = fields,
            });
        }

        return new RepeaterControlModel
        {
            Rows = rows,
            Add = count < limits.MaxRows
                ? new RowButtonModel { Value = "add:" + alias, Text = settings.Text("addLabel") is { Length: > 0 } adding ? adding : build.Words.AddRow }
                : null,
            FieldsCssClass = "jfb-fields jfb-columns-" + build.ColumnsOf(field.Key).ToString(CultureInfo.InvariantCulture),
            FieldView = build.Root.Views.Field,
        };
    }

    // A repeatable group's rows (ADR-0098): a post's, at most as many as a group may have, each with its child fields'
    // answers only; and never fewer than the group's least, each row added starting as on a first render, its fields as
    // a field outside a group starts. A group a rule hid posts no rows, as its controls are disabled, so once shown it
    // shows its least as a first render does.
    private List<Dictionary<Guid, FieldAnswers>> RowsOf(PublicField group, FormPostState? post, Dictionary<Guid, FormField> defined, DateTime local)
    {
        var children = group.Fields.Select(child => child.Key).ToHashSet();
        List<Dictionary<Guid, FieldAnswers>> rows = [.. (post?.Rows.GetValueOrDefault(group.Key) ?? []).Take(RepeaterFieldType.MostRows)
            .Select(row => row.Answers.Where(pair => children.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value))];
        var least = types.Find(group.Type)?.Type is RepeaterFieldType repeater ? Math.Min(repeater.ReadSettings(defined[group.Key].Settings).MinRows, RepeaterFieldType.MostRows) : 0;
        while (rows.Count < least)
        {
            var answers = new Dictionary<Guid, FieldAnswers>();
            foreach (var child in group.Fields)
            {
                if (StartOf(child, defined[child.Key], local) is { } start)
                {
                    answers[child.Key] = start;
                }
            }

            rows.Add(answers);
        }

        return rows;
    }
}
