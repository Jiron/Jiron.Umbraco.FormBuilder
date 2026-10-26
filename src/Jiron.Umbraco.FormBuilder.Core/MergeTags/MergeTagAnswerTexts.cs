using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>A field of a form that holds a value, and the repeatable group it is a child of, if any.</summary>
/// <param name="Field">The field.</param>
/// <param name="Repeater">The repeatable group whose child it is; null for a field of a group.</param>
internal sealed record AnswerField(FormField Field, FormField? Repeater);

/// <summary>
/// The answers as merge tags show them (ADR-0014, ADR-0066), for <c>field.*</c> and <c>entry.answers</c>: each field's
/// texts as its type writes them, what the rules hid empty, and a sensitive field masked or left out as the place
/// decides. A type whose display throws is logged with its alias and the exception's type and stack, and its field
/// shows as the default display would show it.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="cultures">The culture formats.</param>
/// <param name="logger">Where a type's failing display goes.</param>
internal sealed partial class MergeTagAnswerTexts(FieldTypeRegistry types, MergeTagCultures cultures, ILogger<MergeTagAnswerTexts> logger)
{
    /// <summary>What a sensitive field shows where its answer may not be shown: three bullets.</summary>
    public static readonly string Masked = new((char)0x2022, 3);

    /// <summary>How a field shows in a place.</summary>
    private enum Showing
    {
        /// <summary>Its answer.</summary>
        Shown,

        /// <summary>Its answer masked.</summary>
        Masked,

        /// <summary>Nothing: hidden by the rules, sensitive where its tags render empty, or left out of the answers.</summary>
        Nothing,
    }

    /// <summary>The fields of a form that hold a value in the order of the form, each repeatable group followed by its children.</summary>
    public IEnumerable<AnswerField> Fields(FormDefinition form)
    {
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (!HoldsValue(field))
            {
                continue;
            }

            yield return new AnswerField(field, null);
            foreach (var child in field.Fields.Where(HoldsValue))
            {
                yield return new AnswerField(child, field);
            }
        }
    }

    /// <summary>
    /// The field a tag names by its alias: a field whose alias it is, before one that had it before (ADR-0002); null for
    /// none. A child of a repeatable group is found by its own alias too.
    /// </summary>
    public AnswerField? Find(FormDefinition form, string alias) =>
        Fields(form).FirstOrDefault(found => found.Field.Alias == alias)
        ?? Fields(form).FirstOrDefault(found => found.Field.FormerAliases.Contains(alias, StringComparer.Ordinal));

    /// <summary>A repeatable group's child by its alias, before one that had it before; null for none.</summary>
    public static FormField? FindChild(FormField repeater, string alias) =>
        repeater.Fields.FirstOrDefault(child => child.Alias == alias)
        ?? repeater.Fields.FirstOrDefault(child => child.FormerAliases.Contains(alias, StringComparer.Ordinal));

    /// <summary>Whether a field is a repeatable group: its answers are its children's, row by row.</summary>
    public static bool IsRepeater(FormField field) => field.Fields.Count > 0;

    /// <summary>Whether a field's type stores its values with parts, such as a name or an address.</summary>
    public bool HasParts(FormField field) => types.Find(field.Type)?.Storage == FieldStorageKind.Json;

    /// <summary>A field's label in the culture, or its alias when it has none.</summary>
    public static string Label(FormField field, string culture) =>
        field.Label?.Resolve(culture) is { Length: > 0 } label ? label : field.Alias ?? string.Empty;

    /// <summary><c>field.&lt;alias&gt;</c>: the answer as its type shows it, a list where the type takes several values.</summary>
    public MergeTagValue Display(AnswerField target, MergeTagScope scope, MergeTagPlace place) =>
        Resolve(target, scope, place, answer => Items(Texts(target.Field, answer, scope.Culture, listing: false)));

    /// <summary><c>field.&lt;alias&gt;.value</c>: the values in the wire form, with the wire number or date behind each.</summary>
    public MergeTagValue Value(AnswerField target, MergeTagScope scope, MergeTagPlace place)
    {
        var kind = types.Find(target.Field.Type)?.KindOf(target.Field);
        return Resolve(target, scope, place, answer => [.. answer.Values.Select(value => kind switch
        {
            FieldKind.Number => new MergeTagItem(value.Text, Number: value.Text),
            FieldKind.Date or FieldKind.Time or FieldKind.DateTime => new MergeTagItem(value.Text, Date: value.Text),
            _ => new MergeTagItem(value.Text),
        })]);
    }

    /// <summary><c>field.&lt;alias&gt;.&lt;part&gt;</c>: a part of a value with parts, such as a name's <c>first</c>.</summary>
    public MergeTagValue Part(AnswerField target, string part, MergeTagScope scope, MergeTagPlace place) =>
        Resolve(target, scope, place, answer =>
            answer.Values.Select(value => FieldPartsJson.Read(value.Text)?.FirstOrDefault(found => found.Key == part).Value)
                .OfType<string>()
                .Select(text => new MergeTagItem(text))
                .ToList());

    /// <summary>
    /// <c>field.&lt;repeater&gt;</c>: a list of the answered rows, each its children's answers as <c>Label: answer</c>
    /// apart by "; ".
    /// </summary>
    public MergeTagValue Rows(FormField repeater, MergeTagScope scope, MergeTagPlace place)
    {
        var rows = RowTexts(repeater, scope, place, listing: false);
        return Of(new AnswerField(repeater, null), scope, place) switch
        {
            Showing.Shown => MergeTagValue.ListOf([.. rows.Select(row => new MergeTagItem(row))]),
            Showing.Masked when rows.Count > 0 => MergeTagValue.Of(Masked),
            _ => MergeTagValue.Empty,
        };
    }

    /// <summary>
    /// <c>entry.answers</c>: every answered field the rules did not hide, in the order of the form, as <c>Label: answer</c>
    /// lines, and as a table in HTML; what the place leaves out is not listed (ADR-0014).
    /// </summary>
    public MergeTagValue Answers(MergeTagScope scope, MergeTagPlace place)
    {
        var listed = new List<(string Label, string? Answer, IReadOnlyList<string> Rows)>();
        foreach (var field in scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Where(HoldsValue))
        {
            var showing = Listing(new AnswerField(field, null), scope, place);
            if (showing == Showing.Nothing)
            {
                continue;
            }

            var label = Label(field, scope.Culture);
            if (IsRepeater(field))
            {
                var rows = RowTexts(field, scope, place, listing: true);
                if (rows.Count > 0)
                {
                    listed.Add(showing == Showing.Masked ? (label, Masked, []) : (label, null, rows));
                }
            }
            else if (scope.Answers.Find(field.Key) is { } answer)
            {
                var answerText = showing == Showing.Masked ? Masked : Joined(Texts(field, answer, scope.Culture, listing: true));
                if (answerText.Length > 0)
                {
                    listed.Add((label, answerText, []));
                }
            }
        }

        if (listed.Count == 0)
        {
            return MergeTagValue.Empty;
        }

        var text = new StringBuilder();
        var html = new StringBuilder("<table class=\"jfb-answers\"><tbody>\n");
        foreach (var (label, answer, rows) in listed)
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            html.Append("<tr><th scope=\"row\">").Append(MergeTagEngine.HtmlText(label)).Append("</th><td>");
            if (answer is not null)
            {
                text.Append(label).Append(": ").Append(answer);
                html.Append(MergeTagEngine.HtmlText(answer));
            }
            else
            {
                text.Append(label).Append(':');
                html.Append("<ol>");
                for (var i = 0; i < rows.Count; i++)
                {
                    text.Append('\n').Append(i + 1).Append(". ").Append(rows[i]);
                    html.Append("<li>").Append(MergeTagEngine.HtmlText(rows[i])).Append("</li>");
                }

                html.Append("</ol>");
            }

            html.Append("</td></tr>\n");
        }

        html.Append("</tbody></table>");
        return new MergeTagValue([new MergeTagItem(text.ToString())], false) { Html = html.ToString() };
    }

    /// <summary>
    /// The texts a field's answer shows as, in a culture, as its type writes them; the values as they are for a type the
    /// site no longer has, and the default display for a type whose own throws.
    /// </summary>
    public IReadOnlyList<FieldDisplay.DisplayText> Texts(FormField field, MergeTagAnswer answer, string culture, bool listing)
    {
        if (types.FindType(field.Type) is not { } type)
        {
            return [.. answer.Values.Select(value => new FieldDisplay.DisplayText(value.Text, null, null))];
        }

        var formats = cultures.For(culture);
        var display = new FieldDisplay(field, answer.Values, answer.Files, culture, formats, listing);
        try
        {
            type.Display(display);
            return display.Texts;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogDisplayFailed(logger, type.Declaration.Alias, exception.GetType().FullName, exception.StackTrace);
            var fallback = new FieldDisplay(field, answer.Values, answer.Files, culture, formats, listing);
            FieldDisplayDefaults.Display(type.Declaration, fallback);
            return fallback.Texts;
        }
    }

    /// <summary>
    /// A field's value in a tag: nothing where the rules hid it or the place renders it empty, the bullets where it is
    /// masked and answered, otherwise its items, one for each answered row for a repeatable group's child.
    /// </summary>
    private MergeTagValue Resolve(AnswerField target, MergeTagScope scope, MergeTagPlace place, Func<MergeTagAnswer, IReadOnlyList<MergeTagItem>> items)
    {
        var showing = Of(target, scope, place);
        if (showing == Showing.Nothing)
        {
            return MergeTagValue.Empty;
        }

        if (target.Repeater is null)
        {
            var answer = scope.Answers.Find(target.Field.Key);
            if (showing == Showing.Masked)
            {
                return answer is null ? MergeTagValue.Empty : MergeTagValue.Of(Masked);
            }

            var found = answer is null ? [] : items(answer);
            return IsMany(target.Field) || found.Count > 1 ? MergeTagValue.ListOf(found) : found.Count == 1 ? MergeTagValue.Of(found[0]) : MergeTagValue.Empty;
        }

        var answers = scope.Answers.Rows([target.Field.Key]).Select(row => scope.Answers.Find(target.Field.Key, row)).OfType<MergeTagAnswer>().ToList();
        if (showing == Showing.Masked)
        {
            return answers.Count == 0 ? MergeTagValue.Empty : MergeTagValue.Of(Masked);
        }

        var perRow = answers.Select(items).Where(row => row.Count > 0).Select(row => row.Count == 1 ? row[0] : new MergeTagItem(string.Join(", ", row.Select(item => item.Text))));
        return MergeTagValue.ListOf([.. perRow]);
    }

    /// <summary>Each answered row of a repeatable group as its children's <c>Label: answer</c> apart by "; ", in the order of its rows.</summary>
    private List<string> RowTexts(FormField repeater, MergeTagScope scope, MergeTagPlace place, bool listing)
    {
        var rows = new List<string>();
        foreach (var row in scope.Answers.Rows(repeater.Fields.Select(child => child.Key)))
        {
            var pieces = new List<string>();
            foreach (var child in repeater.Fields.Where(HoldsValue))
            {
                var target = new AnswerField(child, repeater);
                var showing = listing ? Listing(target, scope, place) : Of(target, scope, place);
                if (showing == Showing.Nothing || scope.Answers.Find(child.Key, row) is not { } answer)
                {
                    continue;
                }

                var answerText = showing == Showing.Masked ? Masked : Joined(Texts(child, answer, scope.Culture, listing));
                if (answerText.Length > 0)
                {
                    pieces.Add(Label(child, scope.Culture) + ": " + answerText);
                }
            }

            if (pieces.Count > 0)
            {
                rows.Add(string.Join("; ", pieces));
            }
        }

        return rows;
    }

    /// <summary>
    /// How a field's tag shows in a place (ADR-0014): nothing where the rules hid it or its repeatable group; a sensitive
    /// one masked, empty in entry titles, and shown only in mail and the other actions that name it to include it.
    /// </summary>
    private static Showing Of(AnswerField target, MergeTagScope scope, MergeTagPlace place)
    {
        if (IsHidden(target, scope))
        {
            return Showing.Nothing;
        }

        if (!IsSensitive(target) || IsIncluded(target, scope, place))
        {
            return Showing.Shown;
        }

        return place == MergeTagPlace.EntryTitle ? Showing.Nothing : Showing.Masked;
    }

    /// <summary>
    /// How a field shows in <c>entry.answers</c> (ADR-0014): left out where the rules hid it, in mail when it is not for
    /// mail, and when it is sensitive, unless an action names it to include it; masked in the outcome and the copy of the
    /// answers the visitor sees.
    /// </summary>
    private static Showing Listing(AnswerField target, MergeTagScope scope, MergeTagPlace place)
    {
        if (IsHidden(target, scope) || (place == MergeTagPlace.Mail && (!target.Field.IncludeInEmails || target.Repeater?.IncludeInEmails == false)))
        {
            return Showing.Nothing;
        }

        if (!IsSensitive(target) || IsIncluded(target, scope, place))
        {
            return Showing.Shown;
        }

        return place == MergeTagPlace.Outcome ? Showing.Masked : Showing.Nothing;
    }

    private static bool IsHidden(AnswerField target, MergeTagScope scope) =>
        scope.Answers.Hidden.Contains(target.Field.Key) || (target.Repeater is { } repeater && scope.Answers.Hidden.Contains(repeater.Key));

    private static bool IsSensitive(AnswerField target) => target.Field.Sensitive || target.Repeater?.Sensitive == true;

    /// <summary>Whether mail or another action names the sensitive field, or its repeatable group, to include it.</summary>
    private static bool IsIncluded(AnswerField target, MergeTagScope scope, MergeTagPlace place) =>
        place is MergeTagPlace.Mail or MergeTagPlace.Action
        && (scope.IncludedSensitive.Contains(target.Field.Key) || (target.Repeater is { } repeater && scope.IncludedSensitive.Contains(repeater.Key)));

    /// <summary>Whether a field holds a value: by its type, or by its alias for a type the site no longer has.</summary>
    private bool HoldsValue(FormField field) => types.Find(field.Type) is { } traits ? traits.HoldsValue : field.Alias is not null;

    /// <summary>Whether a field's tag is a list whatever its answer: a type that takes several choices or files, or has rows.</summary>
    private bool IsMany(FormField field) =>
        types.Find(field.Type) is { } traits && (traits.KindOf(field) is FieldKind.Choices or FieldKind.Files || traits.Supports(FieldFeatures.Rows));

    private static List<MergeTagItem> Items(IReadOnlyList<FieldDisplay.DisplayText> texts) =>
        [.. texts.Select(text => new MergeTagItem(text.Text, text.Number, text.Date))];

    private static string Joined(IReadOnlyList<FieldDisplay.DisplayText> texts) => string.Join(", ", texts.Select(text => text.Text));

    [LoggerMessage(Level = LogLevel.Error, Message = "The field type '{FieldType}' failed to display its values with {ExceptionType}; they show as the default would show them. {StackTrace}")]
    private static partial void LogDisplayFailed(ILogger logger, string fieldType, string? exceptionType, string? stackTrace);
}
