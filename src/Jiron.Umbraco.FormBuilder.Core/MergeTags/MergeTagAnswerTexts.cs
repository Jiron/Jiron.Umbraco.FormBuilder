using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>A field of a form that holds a value, and the repeatable group it is a child of, if any.</summary>
/// <param name="Field">The field.</param>
/// <param name="Repeater">The repeatable group whose child it is; null for a field of a group.</param>
internal sealed record AnswerField(FormField Field, FormField? Repeater);

/// <summary>
/// What a tag's answers show besides their values: the dictionary's texts that the form names, and the labels of the
/// options that lists and other sources resolve for the fields the tag shows (ADR-0077, ADR-0083), by field and row, and
/// then by value.
/// </summary>
/// <param name="Dictionary">The dictionary's texts in the scope's culture.</param>
/// <param name="OptionLabels">
/// For each field the tag shows whose options come from a list or another source, its options' labels by value in each
/// row it has an answer in; row 0 for a field outside repeatable groups.
/// </param>
internal sealed record ScopeTexts(DictionaryTexts Dictionary, IReadOnlyDictionary<(Guid Field, int Row), IReadOnlyDictionary<string, string>> OptionLabels)
{
    /// <summary>
    /// A field's options' labels by value in a row; null for a field whose options are its own, one the tag does not show
    /// or the rules hid, and in a row where it has no answer or its parent's options are unknown.
    /// </summary>
    public IReadOnlyDictionary<string, string>? LabelsOf(Guid field, int row) => OptionLabels.GetValueOrDefault((field, row));
}

/// <summary>
/// The answers as merge tags show them (ADR-0014, ADR-0066), for <c>field.*</c> and <c>entry.answers</c>: each field's
/// texts as its type writes them, what the rules hid empty, and a sensitive field masked or left out as the place
/// decides. A label, an option's label or another text of the field that names a dictionary item (<c>#key</c>, ADR-0002)
/// shows the item's text in the culture (ADR-0067), and an option from a list or another source the label the source
/// resolves in the culture (ADR-0077). A type whose display throws is logged with its alias and the exception's type
/// and stack, and its field shows as the default display would show it.
/// </summary>
/// <param name="types">The field types.</param>
/// <param name="cultures">The culture formats.</param>
/// <param name="dictionary">Umbraco's dictionary.</param>
/// <param name="options">The options of lists and other sources.</param>
/// <param name="logger">Where a type's failing display goes.</param>
internal sealed partial class MergeTagAnswerTexts(
    FieldTypeRegistry types,
    MergeTagCultures cultures,
    MergeTagDictionary dictionary,
    IFormBuilderOptionService options,
    ILogger<MergeTagAnswerTexts> logger)
{
    /// <summary>What a sensitive field shows where its answer may not be shown: three bullets.</summary>
    public static readonly string Masked = new((char)0x2022, 3);

    /// <summary>What each scope read for its answers' texts, kept for the scope's other tags.</summary>
    private readonly ConditionalWeakTable<MergeTagScope, ScopeReads> _reads = new();

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
    /// The field a tag names by its alias: a field whose alias it is, before one that had it before (ADR-0002) when
    /// <paramref name="formerAliases"/> count; null for none. A child of a repeatable group is found by its own alias too.
    /// </summary>
    public AnswerField? Find(FormDefinition form, string alias, bool formerAliases = true) =>
        Fields(form).FirstOrDefault(found => found.Field.Alias == alias)
        ?? (formerAliases ? Fields(form).FirstOrDefault(found => found.Field.FormerAliases.Contains(alias, StringComparer.Ordinal)) : null);

    /// <summary>A repeatable group's child by its alias, before one that had it before when <paramref name="formerAliases"/> count; null for none.</summary>
    public static FormField? FindChild(FormField repeater, string alias, bool formerAliases = true) =>
        repeater.Fields.FirstOrDefault(child => child.Alias == alias)
        ?? (formerAliases ? repeater.Fields.FirstOrDefault(child => child.FormerAliases.Contains(alias, StringComparer.Ordinal)) : null);

    /// <summary>Whether a field is a repeatable group: its answers are its children's, row by row.</summary>
    public static bool IsRepeater(FormField field) => field.Fields.Count > 0;

    /// <summary>Whether a field's type stores its values with parts, such as a name or an address.</summary>
    public bool HasParts(FormField field) => types.Find(field.Type)?.Storage == FieldStorageKind.Json;

    /// <summary>A field's label in the culture, the dictionary's text for one that names an item, or its alias when it has none.</summary>
    public static string Label(FormField field, string culture, DictionaryTexts texts) =>
        texts.Resolve(field.Label, culture) is { Length: > 0 } label ? label : field.Alias ?? string.Empty;

    /// <summary>
    /// What the scope's answers show besides their values, in the scope's culture, each read once per scope: the texts of
    /// the dictionary items that its form names in the texts its answers show (labels, options' labels, grid rows and the
    /// strings of the fields' settings), not read at all for a form that names none; and the labels of the options that a
    /// list or another source resolves for <paramref name="showing"/>, the fields whose answers a tag shows, each that is
    /// answered and that the rules did not hide, on the scope's page and for the parent's answers that are options the
    /// parent offers, as a submission's check holds them (ADR-0077, ADR-0081): a repeatable group's child for each row
    /// it has an answer in, with that row's answer of a parent in the same group, and every row's answer of a parent in
    /// another; none for a source that cannot give them or whose parent's options are unknown. What a list or source
    /// resolves, the parent's options among it, is read once per scope for each field and parent answers it is asked for,
    /// when a tag first needs it (ADR-0083).
    /// </summary>
    /// <param name="scope">What the template renders for.</param>
    /// <param name="showing">The fields whose answers the tag shows; none for a tag that shows a label or a value.</param>
    /// <param name="cancellationToken">Cancelled when the rendering is.</param>
    public async Task<ScopeTexts> TextsAsync(MergeTagScope scope, IEnumerable<AnswerField> showing, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(showing);
        if (!_reads.TryGetValue(scope, out var reads))
        {
            var keys = DictionaryKeys(scope.Form, scope.Culture);
            var made = new ScopeReads(keys.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(keys, scope.Culture), FieldsByKey(scope.Form));

            // Two renderings that read a scope's texts at once keep the first.
            reads = _reads.GetValue(scope, _ => made);
        }

        var labels = new Dictionary<(Guid Field, int Row), IReadOnlyDictionary<string, string>>();
        var asked = new HashSet<Guid>();
        foreach (var found in showing.Where(found => asked.Add(found.Field.Key)))
        {
            foreach (var (row, byValue) in await OptionLabelsAsync(found, scope, reads, cancellationToken))
            {
                labels[(found.Field.Key, row)] = byValue;
            }
        }

        return new ScopeTexts(reads.Dictionary, labels);
    }

    /// <summary><c>field.&lt;alias&gt;</c>: the answer as its type shows it, a list where the type takes several values.</summary>
    public MergeTagValue Display(AnswerField target, MergeTagScope scope, MergeTagPlace place, ScopeTexts texts) =>
        Resolve(target, scope, place, answer => Items(Texts(target.Field, answer, scope.Culture, listing: false, texts, Shared(place))));

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
    public MergeTagValue Rows(FormField repeater, MergeTagScope scope, MergeTagPlace place, ScopeTexts texts)
    {
        var rows = RowTexts(repeater, scope, place, listing: false, texts);
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
    public MergeTagValue Answers(MergeTagScope scope, MergeTagPlace place, ScopeTexts texts)
    {
        var listed = new List<(string Label, string? Answer, IReadOnlyList<string> Rows)>();
        foreach (var field in scope.Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Where(HoldsValue))
        {
            var showing = Listing(new AnswerField(field, null), scope, place);
            if (showing == Showing.Nothing)
            {
                continue;
            }

            var label = Label(field, scope.Culture, texts.Dictionary);
            if (IsRepeater(field))
            {
                var rows = RowTexts(field, scope, place, listing: true, texts);
                if (rows.Count > 0)
                {
                    listed.Add(showing == Showing.Masked ? (label, Masked, []) : (label, null, rows));
                }
            }
            else if (scope.Answers.Find(field.Key) is { } answer)
            {
                var answerText = showing == Showing.Masked ? Masked : Joined(Texts(field, answer, scope.Culture, listing: true, texts, shared: false));
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
    /// A field's answer as the summary page lists it (ADR-0122), as <c>entry.answers</c> lists it in the outcome: the
    /// texts of its values apart by ", ", or for a repeatable group one text for each answered row, its children's
    /// <c>Label: answer</c> apart by "; "; none where it is unanswered or the rules hid it. A sensitive field is masked,
    /// with no texts.
    /// </summary>
    /// <param name="field">A field of a group, which may be a repeatable group.</param>
    /// <param name="scope">The answers, with the fields the rules hid.</param>
    /// <param name="texts">The dictionary's texts and the options' labels the scope's fields show.</param>
    public (bool IsMasked, IReadOnlyList<string> Texts) Summary(FormField field, MergeTagScope scope, ScopeTexts texts)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(texts);
        var showing = Listing(new AnswerField(field, null), scope, MergeTagPlace.Outcome);
        if (showing == Showing.Nothing)
        {
            return (false, []);
        }

        if (IsRepeater(field))
        {
            var rows = RowTexts(field, scope, MergeTagPlace.Outcome, listing: true, texts);
            return showing == Showing.Masked && rows.Count > 0 ? (true, []) : (false, rows);
        }

        if (scope.Answers.Find(field.Key) is not { } answer)
        {
            return (false, []);
        }

        if (showing == Showing.Masked)
        {
            return (true, []);
        }

        var joined = Joined(Texts(field, answer, scope.Culture, listing: true, texts));
        return (false, joined.Length > 0 ? [joined] : []);
    }

    /// <summary>
    /// The texts a field's answer shows as, in a culture, as its type writes them, with the dictionary's texts for the
    /// field's texts that name an item and the labels its list or source resolved; the values as they are for a type the
    /// site no longer has, and the default display for a type whose own throws. Where the browser renders the same texts
    /// (<paramref name="shared"/>, ADR-0068), only a built-in type writes its own; a site's type shows the default of its
    /// rule kind, which the browser knows too.
    /// </summary>
    public IReadOnlyList<FieldDisplay.DisplayText> Texts(FormField field, MergeTagAnswer answer, string culture, bool listing, ScopeTexts texts, bool shared = false)
    {
        if (types.FindType(field.Type) is not { } type)
        {
            return [.. answer.Values.Select(value => new FieldDisplay.DisplayText(value.Text, null, null))];
        }

        var formats = cultures.For(culture);
        var labels = texts.LabelsOf(field.Key, answer.Row);
        var display = new FieldDisplay(field, answer.Values, answer.Files, culture, formats, listing, texts.Dictionary, labels);
        try
        {
            if (shared && !BuiltInFieldTypes.All.Contains(type.GetType()))
            {
                FieldDisplayDefaults.Display(type.Declaration, display);
            }
            else
            {
                type.Display(display);
            }

            return display.Texts;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogDisplayFailed(logger, type.Declaration.Alias, exception.GetType().FullName, exception.StackTrace);
            var fallback = new FieldDisplay(field, answer.Values, answer.Files, culture, formats, listing, texts.Dictionary, labels);
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
    private List<string> RowTexts(FormField repeater, MergeTagScope scope, MergeTagPlace place, bool listing, ScopeTexts texts)
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

                var answerText = showing == Showing.Masked ? Masked : Joined(Texts(child, answer, scope.Culture, listing, texts, !listing && Shared(place)));
                if (answerText.Length > 0)
                {
                    pieces.Add(Label(child, scope.Culture, texts.Dictionary) + ": " + answerText);
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
    /// How a field's tag shows in a place (ADR-0014): nothing where the rules hid it or its repeatable group, or in a
    /// visitor text when its value stays on the server (a hidden field, ADR-0068); a sensitive one masked, empty in entry
    /// titles, and shown only in mail and the other actions that name it to include it.
    /// </summary>
    private Showing Of(AnswerField target, MergeTagScope scope, MergeTagPlace place)
    {
        if (IsHidden(target, scope) || (place == MergeTagPlace.VisitorText && types.Find(target.Field.Type) is { ServerOnlyDefault: true }))
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

    /// <summary>Whether the browser renders the place's texts too, from the same answers (ADR-0068).</summary>
    private static bool Shared(MergeTagPlace place) => place == MergeTagPlace.VisitorText;

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

    /// <summary>
    /// The labels of the options a list or another source resolves for a field the rules did not hide, by value, in each
    /// row it has an answer in (row 0 outside repeatable groups), a grid's answer of its rows among them; none for a field
    /// whose options are its own, and none in a row where its parent's options are unknown. A repeatable group's child
    /// asks for each row as the submission's check does, so two rows whose parents differ each show the labels of their
    /// own parent's options, even where those share a value (ADR-0083). A value without a label shows itself.
    /// </summary>
    private async Task<List<(int Row, IReadOnlyDictionary<string, string> Labels)>> OptionLabelsAsync(AnswerField found, MergeTagScope scope, ScopeReads reads, CancellationToken cancellationToken)
    {
        var field = found.Field;
        var labels = new List<(int Row, IReadOnlyDictionary<string, string> Labels)>();
        if (field.Options is not { Source: not OptionSource.Items } || IsHidden(found, scope))
        {
            return labels;
        }

        IEnumerable<int> rows = found.Repeater is null ? [0] : scope.Answers.Rows([field.Key]);
        foreach (var row in rows)
        {
            InRow? where = found.Repeater is { } repeater ? new InRow(repeater.Key, row) : null;
            if (scope.Answers.Find(field.Key, row) is not null && await ResolvedAsync(field, scope, reads, [], where, cancellationToken) is { } resolved)
            {
                labels.Add((row, resolved.Labels));
            }
        }

        return labels;
    }

    /// <summary>
    /// A field's options from its list or source, in the scope's culture and on its page, for its parent's answers held
    /// to the options the parent offers, so that no text of the scope's answers that the parent does not offer reaches a
    /// source or becomes a cache key, as a submission's check holds them (ADR-0081); read once per scope for the field
    /// and those parent answers (ADR-0083). Null when the parent's options are unknown. In a repeatable group's row
    /// (<paramref name="where"/>), a parent in the same group answers in that row.
    /// </summary>
    private async Task<Resolved?> ResolvedAsync(FormField field, MergeTagScope scope, ScopeReads reads, ImmutableHashSet<Guid> asking, InRow? where, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> parentValues = [];
        if (field.Options?.Parent is { } parent)
        {
            if (await HeldParentAnswersAsync(parent, scope, reads, asking.Add(field.Key), where, cancellationToken) is not { } held)
            {
                return null;
            }

            parentValues = held;
        }

        // Each value after its length, so no two lists of values make one key.
        var key = (field.Key, string.Concat(parentValues.Select(value => $"{value.Length}:{value}")));
        if (reads.Find(key) is { } known)
        {
            return known;
        }

        var resolved = await options.GetAsync(field, new FormBuilderOptionContext(scope.Culture) { ParentValues = parentValues, Page = scope.Page }, cancellationToken);
        return reads.Keep(key, Resolved.Of(resolved));
    }

    /// <summary>
    /// The parent's answers that are options the parent offers, its own or those its list or source resolves, in their
    /// order: in the row of <paramref name="where"/> for a parent in the same repeatable group, otherwise in every row;
    /// none for a parent the rules hid, by its own rule or its repeatable group's, one the form lacks or one that a chain
    /// of parents leads back to; null when the parent's options are unknown.
    /// </summary>
    private async Task<IReadOnlyList<string>?> HeldParentAnswersAsync(Guid parent, MergeTagScope scope, ScopeReads reads, ImmutableHashSet<Guid> asking, InRow? where, CancellationToken cancellationToken)
    {
        if (!reads.Fields.TryGetValue(parent, out var found) || found.Field.Options is not { } parentOptions || asking.Contains(parent) || IsHidden(found, scope))
        {
            return [];
        }

        var answers = where is { } at && found.Repeater?.Key == at.Repeater ? AnswersOf(scope, parent, at.Row) : AnswersOf(scope, parent);
        if (answers.Count == 0)
        {
            return [];
        }

        var offered = parentOptions.Source == OptionSource.Items
            ? parentOptions.Items.Select(item => item.Value)
            : (await ResolvedAsync(found.Field, scope, reads, asking, where, cancellationToken))?.Values;
        if (offered is null)
        {
            return null;
        }

        var values = offered.ToHashSet(StringComparer.Ordinal);
        return [.. answers.Where(values.Contains)];
    }

    /// <summary>
    /// Every field of the form by its key with its repeatable group, if any, a group's children among them; the first of
    /// a key twice.
    /// </summary>
    private static FrozenDictionary<Guid, AnswerField> FieldsByKey(FormDefinition form)
    {
        var fields = new Dictionary<Guid, AnswerField>();
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            fields.TryAdd(field.Key, new AnswerField(field, null));
            foreach (var child in field.Fields)
            {
                fields.TryAdd(child.Key, new AnswerField(child, field));
            }
        }

        return fields.ToFrozenDictionary();
    }

    /// <summary>
    /// What one scope read for its answers' texts, kept for its other tags: the dictionary's texts, the form's fields by
    /// key, and the options each list or source resolved for a field and the parent answers it was asked for, which a
    /// later answer the reader gives the scope (ADR-0083) cannot make stale. Two renderings that read one at once keep
    /// the first.
    /// </summary>
    private sealed class ScopeReads(DictionaryTexts dictionary, FrozenDictionary<Guid, AnswerField> fields)
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<(Guid Field, string Parents), Resolved> _resolved = [];

        public DictionaryTexts Dictionary => dictionary;

        public FrozenDictionary<Guid, AnswerField> Fields => fields;

        public Resolved? Find((Guid Field, string Parents) key)
        {
            lock (_lock)
            {
                return _resolved.GetValueOrDefault(key);
            }
        }

        public Resolved Keep((Guid Field, string Parents) key, Resolved resolved)
        {
            lock (_lock)
            {
                return _resolved.TryAdd(key, resolved) ? resolved : _resolved[key];
            }
        }
    }

    /// <summary>What a list or source resolved: its options' values, null when it could not give them, and their labels by value.</summary>
    private sealed record Resolved(IReadOnlyList<string>? Values, IReadOnlyDictionary<string, string> Labels)
    {
        public static Resolved Of(FormBuilderResolvedOptions resolved)
        {
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in resolved.Options)
            {
                if (option.Label is { Length: > 0 } label)
                {
                    labels.TryAdd(option.Value, label);
                }
            }

            return new Resolved(resolved.IsAvailable ? [.. resolved.Options.Select(option => option.Value)] : null, labels);
        }
    }

    /// <summary>A field's values in every row it has an answer in, each once, in their order.</summary>
    private static List<string> AnswersOf(MergeTagScope scope, Guid field) =>
        [.. scope.Answers.Rows([field]).SelectMany(row => ValuesIn(scope, field, row)).Distinct(StringComparer.Ordinal)];

    /// <summary>A field's values in one row (0 outside repeatable groups), each once, in their order.</summary>
    private static List<string> AnswersOf(MergeTagScope scope, Guid field, int row) =>
        [.. ValuesIn(scope, field, row).Distinct(StringComparer.Ordinal)];

    // A parent's answers: a grid's values, which name their own rows, are not choices a child's options depend on.
    private static IEnumerable<string> ValuesIn(MergeTagScope scope, Guid field, int row) =>
        scope.Answers.Find(field, row)?.Values.Where(value => value.Row is null).Select(value => value.Text) ?? [];

    /// <summary>A row of a repeatable group, where its children's options are asked for that row's answers.</summary>
    /// <param name="Repeater">The repeatable group's key.</param>
    /// <param name="Row">The row, from 0.</param>
    private readonly record struct InRow(Guid Repeater, int Row);

    /// <summary>The dictionary keys the texts of the form's fields that hold a value name in a culture.</summary>
    private HashSet<string> DictionaryKeys(FormDefinition form, string culture)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(LocalizedString? text)
        {
            if (text?.Resolve(culture) is { } resolved && LocalizedString.TryGetDictionaryKey(resolved, out var key))
            {
                keys.Add(key);
            }
        }

        foreach (var field in Fields(form).Select(found => found.Field))
        {
            Add(field.Label);
            foreach (var option in field.Options?.Items ?? [])
            {
                Add(option.Label);
            }

            foreach (var row in field.Rows)
            {
                Add(row.Label);
            }

            if (field.Settings is { } settings)
            {
                AddSettingKeys(settings.Value, keys);
            }
        }

        return keys;
    }

    /// <summary>Every string of a field's settings that names a dictionary item, in any culture of a text.</summary>
    private static void AddSettingKeys(JsonElement value, HashSet<string> keys)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in value.EnumerateObject())
                {
                    AddSettingKeys(property.Value, keys);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    AddSettingKeys(item, keys);
                }

                break;
            case JsonValueKind.String when LocalizedString.TryGetDictionaryKey(value.GetString()!, out var key):
                keys.Add(key);
                break;
        }
    }

    private static List<MergeTagItem> Items(IReadOnlyList<FieldDisplay.DisplayText> texts) =>
        [.. texts.Select(text => new MergeTagItem(text.Text, text.Number, text.Date))];

    private static string Joined(IReadOnlyList<FieldDisplay.DisplayText> texts) => string.Join(", ", texts.Select(text => text.Text));

    [LoggerMessage(Level = LogLevel.Error, Message = "The field type '{FieldType}' failed to display its values with {ExceptionType}; they show as the default would show them. {StackTrace}")]
    private static partial void LogDisplayFailed(ILogger logger, string fieldType, string? exceptionType, string? stackTrace);
}
