using System.Buffers.Text;
using System.Collections.Frozen;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Insights;

/// <summary>What a field's summary is (ADR-0212), by what its type says its answers are.</summary>
internal enum AnswerSummaryKind
{
    /// <summary>One choice or several: each option's count.</summary>
    Options,

    /// <summary>A ranking: each option's average position.</summary>
    Ranking,

    /// <summary>A grid: each row's count per column.</summary>
    Grid,

    /// <summary>A number: count, average, median, lowest and highest.</summary>
    Number,

    /// <summary>A checkbox: how many ticked it.</summary>
    Checkbox,

    /// <summary>Free text: the newest answers, never summarised.</summary>
    Texts,

    /// <summary>Anything else: how often it was answered.</summary>
    Answered,
}

/// <summary>What the summaries read: the received days and the entries list's filter.</summary>
/// <param name="Span">The UTC days the entries were received on.</param>
/// <param name="Filter">The list's filter; its sort is not read.</param>
internal sealed record AnswerRequest(InsightSpan Span, EntryListFilter Filter);

/// <summary>An option's line in a summary.</summary>
/// <param name="Label">What it shows as.</param>
/// <param name="Count">How many answers chose it.</param>
/// <param name="Position">For a ranking, its average position, 1 first.</param>
/// <param name="Older">Whether no live option has it: one an older version offered.</param>
/// <param name="Other">Whether it stands for the visitors' own "Other" texts.</param>
internal sealed record AnswerOption(string Label, long Count, double? Position = null, bool Older = false, bool Other = false);

/// <summary>A grid's row in its summary.</summary>
/// <param name="Key">The row.</param>
/// <param name="Label">Its label.</param>
/// <param name="Answered">How many answered it.</param>
/// <param name="Options">Its count per column.</param>
/// <param name="Removed">Whether the live version's grid no longer has the row: one an older version asked.</param>
internal sealed record AnswerGridRow(Guid Key, string Label, long Answered, IReadOnlyList<AnswerOption> Options, bool Removed = false);

/// <summary>A number field's figures.</summary>
/// <param name="Average">The mean.</param>
/// <param name="Median">The middle answer, or the mean of the two in the middle.</param>
/// <param name="Lowest">The lowest.</param>
/// <param name="Highest">The highest.</param>
/// <param name="Groups">For a scale from 0 to 10, its three groups and score; otherwise none.</param>
internal sealed record AnswerNumberSummary(double Average, double Median, double Lowest, double Highest, AnswerScaleGroups? Groups);

/// <summary>The groups of a scale from 0 to 10 and its recommendation score.</summary>
/// <param name="Low">How many answered 0 to 6.</param>
/// <param name="Middle">How many answered 7 or 8.</param>
/// <param name="High">How many answered 9 or 10.</param>
/// <param name="Score">The share of the top group less that of the bottom one, in whole points from -100 to 100.</param>
internal sealed record AnswerScaleGroups(long Low, long Middle, long High, int Score);

/// <summary>A field's summary (ADR-0212).</summary>
/// <param name="Key">The field.</param>
/// <param name="Label">Its label, or its alias without one.</param>
/// <param name="Kind">What its summary is.</param>
/// <param name="Answered">How many answered it: entries, or rows of its repeatable group.</param>
internal sealed record AnswerSummary(Guid Key, string Label, AnswerSummaryKind Kind, long Answered)
{
    /// <summary>The repeatable group it is a child of, whose rows its answers are; none outside one.</summary>
    public Guid? Parent { get; init; }

    /// <summary>The repeatable group's label.</summary>
    public string? ParentLabel { get; init; }

    /// <summary>Whether the live version no longer has it.</summary>
    public bool Removed { get; init; }

    /// <summary>Whether an answer may choose several options, so the shares add up past the whole.</summary>
    public bool Several { get; init; }

    /// <summary>For options and a ranking, the lines.</summary>
    public IReadOnlyList<AnswerOption> Options { get; init; } = [];

    /// <summary>For a grid, its rows.</summary>
    public IReadOnlyList<AnswerGridRow> Rows { get; init; } = [];

    /// <summary>For a number, its figures; none without an answer.</summary>
    public AnswerNumberSummary? Numbers { get; init; }

    /// <summary>For a checkbox, how many ticked it.</summary>
    public long Ticked { get; init; }

    /// <summary>For free text, the newest answers.</summary>
    public IReadOnlyList<AnswerText> Texts { get; init; } = [];

    /// <summary>For free text, where more answers start; none when these are all.</summary>
    public string? More { get; init; }
}

/// <summary>The answers' summaries of a form's entries.</summary>
/// <param name="Entries">How many entries they summarise.</param>
/// <param name="Fields">Each field's summary, in the form's order.</param>
internal sealed record AnswerReport(long Entries, IReadOnlyList<AnswerSummary> Fields);

/// <summary>A page of a free-text field's answers.</summary>
/// <param name="Texts">The answers, newest entry first.</param>
/// <param name="More">Where more start; none when these are all.</param>
internal sealed record AnswerTexts(IReadOnlyList<AnswerText> Texts, string? More);

/// <summary>
/// The answer summaries of the Insights view (ROADMAP 11.5b, ADR-0212): for the entries the list's filter holds among
/// those received in a span, each field's answers summarised in the database by what its type says they are, labelled as
/// the type shows them. Never a test entry, an entry in Spam, a sensitive field or one kept only until the actions run.
/// The caller checks that the user may view the form's entries.
/// </summary>
/// <param name="forms">The forms, their catalogues and versions.</param>
/// <param name="queries">The entries query, which summarises.</param>
/// <param name="lists">The entries lists, whose filter the summaries read.</param>
/// <param name="documents">Reads stored versions.</param>
/// <param name="types">The field types.</param>
/// <param name="answers">Shows a value as its field's type does.</param>
/// <param name="options">The options of lists and other sources, which tell a visitor's own "Other" text from an option.</param>
internal sealed class AnswerSummaries(IFormRepository forms, IEntryQueryRepository queries, EntryLists lists, FormDocuments documents, FieldTypeRegistry types, MergeTagAnswerTexts answers, IFormBuilderOptionService options)
{
    /// <summary>How many free-text answers a summary shows.</summary>
    public const int TextsShown = 5;

    /// <summary>How many more each Show more brings.</summary>
    public const int TextsPerPage = 20;

    /// <summary>The longest free-text answer a summary shows; its entry holds the whole.</summary>
    public const int MaxTextLength = 500;

    // The published versions read for what an older one called an option, newest first.
    private const int VersionsRead = 20;

    // The types whose text answers are contact details or a page's own value, not text to read through.
    private static readonly FrozenSet<string> NotFreeText = FrozenSet.Create(StringComparer.Ordinal, "email", "phone", "url", "hidden");

    private static readonly JsonSerializerOptions CursorJson = Definitions.Serialization.FrozenOptions.Of(new(JsonSerializerDefaults.Web));

    /// <summary>The form's summaries, or null when the form does not exist.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="request">The span and the filter.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A filter the form cannot answer, each by its path.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget.</exception>
    public async Task<AnswerReport?> SummarizeAsync(Guid formKey, AnswerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var (query, fields, versions) = await PrepareAsync(form, request, cancellationToken);
        if (query is null)
        {
            return new AnswerReport(0, [.. fields.Select(field => new AnswerSummary(field.Listed.Key, field.Listed.Label, field.Kind, 0) { Removed = field.Listed.Removed })]);
        }

        var ids = new AnswerSummaryFields
        {
            Answered = [.. fields.Where(field => field.Kind is AnswerSummaryKind.Texts or AnswerSummaryKind.Answered).Select(field => field.Id)],
            Options = [.. fields.Where(field => field.Kind is AnswerSummaryKind.Options or AnswerSummaryKind.Ranking).Select(field => field.Id), .. fields.SelectMany(field => field.GridRows.Select(row => row.Id))],
            Numbers = [.. fields.Where(field => field.Kind == AnswerSummaryKind.Number).Select(field => field.Id)],
            Ticked = [.. fields.Where(field => field.Kind == AnswerSummaryKind.Checkbox).Select(field => field.Id)],
            Texts = [.. fields.Where(field => field.Kind == AnswerSummaryKind.Texts).Select(field => field.Id)],
            Grids = [.. fields.Where(field => field.Kind == AnswerSummaryKind.Grid && field.GridRows.Count > 0).Select(field => (field.Id, (IReadOnlyList<int>)[.. field.GridRows.Select(row => row.Id)]))],
        };
        var rows = await queries.SummarizeAsync(query, ids, TextsShown, cancellationToken);
        var counted = rows.Options.ToLookup(option => option.FieldId);
        var labels = await LabelsAsync(form, versions, fields, counted, cancellationToken);
        return new AnswerReport(rows.Entries, [.. fields.Select(field => Summary(field, rows, counted, labels))]);
    }

    /// <summary>
    /// The next free-text answers of a field after <paramref name="after"/>, the <c>more</c> of a summary or of the page
    /// before; null when the form does not exist or the field is no free text the summaries show.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="fieldKey">The field.</param>
    /// <param name="request">The span and the filter, as the summary had them.</param>
    /// <param name="after">Where the answers start.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    /// <exception cref="FormBuilderValidationException">A filter or a cursor the form cannot answer, each by its path.</exception>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget.</exception>
    public async Task<AnswerTexts?> TextsAsync(Guid formKey, Guid fieldKey, AnswerRequest request, string? after, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var cursor = after is null ? null : Decode(after);
        if (after is not null && cursor is null)
        {
            throw new FormBuilderValidationException([new DefinitionProblem("invalid_cursor", "after", "The answers changed since their last page. Load them again.")]);
        }

        var (query, fields, _) = await PrepareAsync(form, request, cancellationToken);
        if (fields.FirstOrDefault(field => field.Listed.Key == fieldKey && field.Kind == AnswerSummaryKind.Texts) is not { } found)
        {
            return null;
        }

        if (query is null)
        {
            return new AnswerTexts([], null);
        }

        var page = await queries.TextsAsync(query, found.Id, cursor, TextsPerPage, cancellationToken);
        return new AnswerTexts([.. page.Texts.Select(Cut)], page.Next is { } next ? Encode(next) : null);
    }

    // The query of the request's entries, null for a request that can hold none, and the fields the summaries show,
    // with the versions read to know them.
    private async Task<(EntryQuery? Query, IReadOnlyList<Summarised> Fields, IReadOnlyList<FormDefinition> Versions)> PrepareAsync(StoredForm form, AnswerRequest request, CancellationToken cancellationToken)
    {
        var problems = new List<DefinitionProblem>();
        var query = await lists.ExportQueryAsync(form, request.Filter, problems, cancellationToken);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
        var versions = await VersionsAsync(form, catalogue, cancellationToken);
        var fields = Fields(catalogue, versions);

        // The span within the filter's own days, if any; the entries in Spam never, whatever states it names.
        var (start, end) = (request.Span.Start.UtcDateTime, request.Span.End.UtcDateTime);
        var from = query.SubmittedFrom is { } own && own > start ? own : start;
        var before = query.SubmittedBefore is { } ends && ends < end ? ends : end;
        var states = query.States.Where(state => state != EntryState.Spam).ToList();
        return from >= before || states.Count == 0
            ? (null, fields, versions)
            : (query with { SubmittedFrom = from, SubmittedBefore = before, States = states, Sort = EntrySort.Newest }, fields, versions);
    }

    // The live version first (the published one, or the draft of a form never published), then the published versions
    // newest first, and the last version of each field the live one no longer has.
    private async Task<IReadOnlyList<FormDefinition>> VersionsAsync(StoredForm form, IReadOnlyList<CatalogueField> catalogue, CancellationToken cancellationToken)
    {
        var numbers = (await forms.ListVersionsAsync(form.Id, FormVersionKind.Published, PageRequest.First(VersionsRead), cancellationToken)).Items.Select(version => version.Number).ToList();
        numbers.AddRange(catalogue.Select(row => row.Values.LastVersionNumber).OfType<int>().Where(number => !numbers.Contains(number)).Distinct());
        var stored = (await forms.GetVersionsAsync(form.Id, numbers, cancellationToken)).OrderByDescending(version => version.Info.Number).Select(documents.Readable).ToList();
        if (form.PublishedVersionId is null && form.DraftVersionId is { } draft && await forms.GetVersionByIdAsync(draft, cancellationToken) is { } version)
        {
            stored.Insert(0, documents.Readable(version));
        }

        return stored;
    }

    // The fields the summaries show, in the list's order, each with what its summary is.
    private List<Summarised> Fields(IReadOnlyList<CatalogueField> catalogue, IReadOnlyList<FormDefinition> versions)
    {
        var ids = catalogue.ToDictionary(row => row.Values.FieldKey, row => row.Id);
        var listed = EntryLists.Fields(catalogue);
        var groups = listed.ToDictionary(field => field.Key);
        var found = new List<Summarised>();
        foreach (var field in listed)
        {
            var every = versions.Select(version => FieldOf(version, field.Key)).OfType<FormField>().ToList();
            var definition = every.FirstOrDefault();

            // A repeatable group kept only until the actions run takes its children's values with it.
            var group = field.Parent is { } owner ? versions.Select(version => FieldOf(version, owner)).FirstOrDefault(each => each is not null) : null;
            if (field.Sensitive || definition?.Keep == FieldKeep.UntilActionsRun || group?.Keep == FieldKeep.UntilActionsRun || !ids.TryGetValue(field.Key, out var id))
            {
                continue;
            }

            var type = types.FindType(field.Type);
            if (type is GridFieldType && definition is not null)
            {
                // The live version's rows in their order, then those only older versions asked, each as the newest that
                // had it named it, marked removed where the live grid no longer has it.
                var live = versions.Count > 0 ? FieldOf(versions[0], field.Key) : null;
                var rows = new List<(GridRow Row, int Id, bool Removed)>();
                foreach (var row in every.SelectMany(each => each.Rows))
                {
                    if (ids.TryGetValue(row.Key, out var rowId) && rows.TrueForAll(each => each.Row.Key != row.Key))
                    {
                        rows.Add((row, rowId, live is not null && !live.Rows.Any(each => each.Key == row.Key)));
                    }
                }

                found.Add(new Summarised(field, definition, AnswerSummaryKind.Grid, id, rows, Scale: false, Other: false, Parent: null));
                continue;
            }

            if (field.Kind == EntryListFieldKind.Group)
            {
                // A repeatable group: its children are summarised, each by its own rows.
                continue;
            }

            var kind = type is RankingFieldType ? AnswerSummaryKind.Ranking : KindOf(field, definition);
            // A scale's groups only where every version asked from 0 to 10, and Other where any offered it.
            var scale = type is ScaleFieldType scaleType && every.Count > 0 && every.TrueForAll(each => scaleType.ReadSettings(each.Settings) is { From: 0, To: 10 });
            var other = every.Exists(each => each.Settings is { } settings && settings.TryGetProperty("other", out var flag) && flag.ValueKind == JsonValueKind.True);
            var parent = field.Parent is { } parentKey ? groups.GetValueOrDefault(parentKey) : null;
            found.Add(new Summarised(field, definition, kind, id, [], scale, other, parent));
        }

        return found;
    }

    private AnswerSummaryKind KindOf(EntryListField field, FormField? definition) =>
        (definition is null ? null : types.Find(field.Type)?.KindOf(definition)) switch
        {
            FieldKind.Choice or FieldKind.Choices => AnswerSummaryKind.Options,
            FieldKind.Number when field.Kind == EntryListFieldKind.Number => AnswerSummaryKind.Number,
            FieldKind.Checkbox when field.Kind == EntryListFieldKind.Checkbox => AnswerSummaryKind.Checkbox,
            FieldKind.Text when field.Kind is EntryListFieldKind.Text or EntryListFieldKind.LongText && !NotFreeText.Contains(field.Type) => AnswerSummaryKind.Texts,
            _ => AnswerSummaryKind.Answered,
        };

    // A field's summary from what the database counted.
    private AnswerSummary Summary(Summarised field, AnswerSummaryRows rows, ILookup<int, AnswerOptionCount> counted, IReadOnlyDictionary<(int Field, string Key), Label> labels)
    {
        var answered = rows.Answered.GetValueOrDefault(field.Id);
        var summary = new AnswerSummary(field.Listed.Key, field.Listed.Label, field.Kind, answered)
        {
            Parent = field.Parent?.Key,
            ParentLabel = field.Parent?.Label,
            Removed = field.Listed.Removed,
            Several = field.Definition is { } definition && types.Find(field.Listed.Type)?.KindOf(definition) == FieldKind.Choices,
        };
        switch (field.Kind)
        {
            case AnswerSummaryKind.Options or AnswerSummaryKind.Ranking:
                return summary with { Options = Lines(field.Id, field.Definition?.Options, field.Other, field.Kind == AnswerSummaryKind.Ranking, counted, labels) };
            case AnswerSummaryKind.Grid:
                var gridRows = field.GridRows.Select(row =>
                {
                    var lines = Lines(row.Id, field.Definition?.Options, other: false, ranking: false, counted, labels);
                    return new AnswerGridRow(row.Row.Key, row.Row.Label?.Default is { Length: > 0 } label ? label : row.Row.Key.ToString("N"), rows.Answered.GetValueOrDefault(row.Id), lines, row.Removed);
                }).ToList();
                return summary with { Answered = rows.Answered.GetValueOrDefault(field.Id), Rows = gridRows };
            case AnswerSummaryKind.Number when rows.Numbers.TryGetValue(field.Id, out var numbers) && numbers.Count > 0:
                var groups = field.Scale
                    ? new AnswerScaleGroups(numbers.Low, numbers.Middle, numbers.High, (int)Math.Round((numbers.High - numbers.Low) * 100.0 / numbers.Count, MidpointRounding.AwayFromZero))
                    : null;
                return summary with { Numbers = new AnswerNumberSummary(numbers.Sum / numbers.Count, numbers.Median, numbers.Lowest, numbers.Highest, groups) };
            case AnswerSummaryKind.Checkbox:
                return summary with { Ticked = rows.Ticked.GetValueOrDefault(field.Id) };
            case AnswerSummaryKind.Texts when rows.Texts.TryGetValue(field.Id, out var page):
                return summary with { Texts = [.. page.Texts.Select(Cut)], More = page.Next is { } next ? Encode(next) : null };
            default:
                return summary;
        }
    }

    // The lines of a field's counted keys: its live own options first in their order, those nobody chose with 0, then the
    // keys no live option has by count, an older version's options marked; in a field that offers Other, or once did,
    // every key no version and no list offers is a visitor's own text, and they are one line, never counted apart. A
    // ranking's lines go by their average position.
    private static List<AnswerOption> Lines(int fieldId, FieldOptions? options, bool other, bool ranking, ILookup<int, AnswerOptionCount> counted, IReadOnlyDictionary<(int Field, string Key), Label> labels)
    {
        var byKey = counted[fieldId].ToDictionary(option => option.Key, StringComparer.Ordinal);
        var lines = new List<AnswerOption>();
        var own = options is { Source: OptionSource.Items } ? options.Items : [];
        foreach (var item in own)
        {
            var key = EntryTextKey.For(item.Value);
            var count = byKey.GetValueOrDefault(key);
            byKey.Remove(key);
            lines.Add(new AnswerOption(labels.GetValueOrDefault((fieldId, key))?.Text ?? Default(item), count?.Count ?? 0, ranking ? Position(count) : null));
        }

        long others = 0;
        foreach (var count in byKey.Values.OrderByDescending(count => count.Count).ThenBy(count => count.Key, StringComparer.Ordinal))
        {
            var label = labels.GetValueOrDefault((fieldId, count.Key));
            if (other && label is { Option: false })
            {
                others += count.Count;
                continue;
            }

            lines.Add(new AnswerOption(label?.Text ?? count.Sample, count.Count, ranking ? Position(count) : null, Older: label?.Older == true));
        }

        if (others > 0)
        {
            lines.Add(new AnswerOption(string.Empty, others, Other: true));
        }

        // The keys no own option has came by count, so the visitors' own texts stay last.
        return ranking ? [.. lines.OrderBy(line => line.Position ?? double.MaxValue)] : lines;
    }

    private static double? Position(AnswerOptionCount? count) => count is { Count: > 0 } ? Math.Round(count.Positions / (double)count.Count + 1, 1) : null;

    private static string Default(FieldOption item) => item.Label?.Default is { Length: > 0 } label ? label : item.Value;

    // What each counted key shows as: the type's text for its stored value, in the form's default language; for a key no
    // live own option has, the label of the newest version that offered it, marked older; and whether it is an option at
    // all: one the field's own options, an older version's or its list or source offer. A list or source that cannot
    // give its options now, or whose options hang on a parent's answer, leaves every key an option, as nothing tells.
    private async Task<IReadOnlyDictionary<(int Field, string Key), Label>> LabelsAsync(StoredForm form, IReadOnlyList<FormDefinition> versions, IReadOnlyList<Summarised> fields, ILookup<int, AnswerOptionCount> counted, CancellationToken cancellationToken)
    {
        var labels = new Dictionary<(int Field, string Key), Label>();
        var live = versions.Count > 0 ? versions[0] : null;
        var shown = fields.Where(field => field.Definition is not null && field.Kind is AnswerSummaryKind.Options or AnswerSummaryKind.Ranking && counted[field.Id].Any()).ToList();
        if (live is null)
        {
            return labels;
        }

        var scope = new MergeTagScope
        {
            Form = live,
            FormName = form.Name,
            FormAlias = form.Alias,
            Culture = string.Empty,
            Answers = new MergeTagAnswers(shown.Select(field => new MergeTagAnswer(field.Listed.Key, [.. counted[field.Id].Select(count => new FieldValue(count.Sample))]))),
        };
        var texts = await answers.TextsAsync(scope, shown.Select(field => new AnswerField(field.Definition!, field.Parent is null ? null : FieldOf(live, field.Parent.Key))), cancellationToken);
        foreach (var field in shown)
        {
            var own = field.Definition!.Options is { Source: OptionSource.Items } items ? items.Items.Select(item => EntryTextKey.For(item.Value)).ToHashSet(StringComparer.Ordinal) : null;
            var offered = own ?? (field.Other ? await OfferedAsync(field.Definition, cancellationToken) : null);
            foreach (var count in counted[field.Id])
            {
                if (own is not null && !own.Contains(count.Key))
                {
                    labels[(field.Id, count.Key)] = OlderLabel(versions, field.Listed.Key, count.Key) is { } older ? new Label(older, Older: true) : new Label(count.Sample, Older: false, Option: false);
                    continue;
                }

                var text = answers.Texts(field.Definition!, new MergeTagAnswer(field.Listed.Key, [new FieldValue(count.Sample)]), string.Empty, listing: false, texts) is [var first, ..] ? first.Text : null;
                labels[(field.Id, count.Key)] = new Label(text is { Length: > 0 } ? text : count.Sample, Older: false, Option: offered is null || offered.Contains(count.Key));
            }
        }

        foreach (var field in fields.Where(field => field.Kind == AnswerSummaryKind.Grid))
        {
            foreach (var (_, id, _) in field.GridRows)
            {
                foreach (var count in counted[id])
                {
                    // A column the live grid has by its label, one an older version had marked as that version named it.
                    var item = field.Definition?.Options?.Items.FirstOrDefault(each => EntryTextKey.For(each.Value) == count.Key);
                    labels[(id, count.Key)] = item is not null
                        ? new Label(Default(item), Older: false)
                        : OlderLabel(versions, field.Listed.Key, count.Key) is { } older ? new Label(older, Older: true) : new Label(count.Sample, Older: false);
                }
            }
        }

        return labels;
    }

    // The keys of the values a field's list or source offers now, in the form's default language; none when it cannot
    // give them, or when they hang on a parent's answer, which no single entry gives.
    private async Task<HashSet<string>?> OfferedAsync(FormField field, CancellationToken cancellationToken)
    {
        if (field.Options is not { Source: not OptionSource.Items, Parent: null })
        {
            return null;
        }

        var resolved = await options.GetAsync(field, new FormBuilderOptionContext(string.Empty), cancellationToken);
        return resolved.IsAvailable ? resolved.Options.Select(option => EntryTextKey.For(option.Value)).ToHashSet(StringComparer.Ordinal) : null;
    }

    // The label an older version gave an own option of the field with this key, the newest first; none when none had it.
    private static string? OlderLabel(IReadOnlyList<FormDefinition> versions, Guid field, string key) =>
        versions.Skip(1)
            .Select(version => FieldOf(version, field)?.Options?.Items.FirstOrDefault(item => EntryTextKey.For(item.Value) == key))
            .FirstOrDefault(item => item is not null) is { } found
            ? Default(found)
            : null;

    private static FormField? FieldOf(FormDefinition definition, Guid key) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).FirstOrDefault(field => field.Key == key);

    private static AnswerText Cut(AnswerText text) =>
        text.Text.Length <= MaxTextLength ? text : text with { Text = string.Concat(text.Text.AsSpan(0, MaxTextLength).TrimEnd(), "\u2026") };

    private static string Encode(AnswerTextCursor cursor) =>
        Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new CursorText(cursor.EntryId, cursor.SubmittedUtc.Ticks, cursor.Row), CursorJson));

    private static AnswerTextCursor? Decode(string text)
    {
        try
        {
            if (text.Length is > 0 and <= 512 && JsonSerializer.Deserialize<CursorText>(Base64Url.DecodeFromChars(text), CursorJson) is { E: > 0, T: >= 0 and <= 3155378975999999999, R: >= 0 } read)
            {
                return new AnswerTextCursor(read.E, new DateTime(read.T, DateTimeKind.Utc), read.R);
            }
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            // Not a cursor these answers gave.
        }

        return null;
    }

    // A cursor's parts by short names: the entry, its submission's ticks and the answer's row.
    private sealed record CursorText(long E, long T, int R);

    // What a counted key shows as, whether an older version offered it, and whether any version or list offers it at all.
    private sealed record Label(string Text, bool Older, bool Option = true);

    // A field the summaries show: as the list offers it, its definition in the newest version that has it, what its
    // summary is, its catalogue row, a grid's rows with theirs, whether it is a scale from 0 to 10, whether it offers
    // "Other", and the repeatable group it is a child of.
    private sealed record Summarised(EntryListField Listed, FormField? Definition, AnswerSummaryKind Kind, int Id, IReadOnlyList<(GridRow Row, int Id, bool Removed)> GridRows, bool Scale, bool Other, EntryListField? Parent);
}
