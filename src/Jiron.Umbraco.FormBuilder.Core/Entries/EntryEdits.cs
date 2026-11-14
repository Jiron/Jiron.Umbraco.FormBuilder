using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>A field's new answer in an edit: its values in the wire form (ADR-0005), none to clear it.</summary>
/// <param name="Field">The field.</param>
/// <param name="Values">Its values.</param>
internal sealed record EntryEditValue(Guid Field, IReadOnlyList<string> Values);

/// <summary>How an edit came out.</summary>
internal enum EntryEditStatus
{
    /// <summary>The answers are changed, or were the same already.</summary>
    Saved,

    /// <summary>The form has no such entry.</summary>
    NotFound,

    /// <summary>An answer cannot be edited, or its field's checks refuse it; nothing changed.</summary>
    Invalid,

    /// <summary>The edit would overbook an option or double a unique answer, which the user must agree to first; nothing changed.</summary>
    Asks,
}

/// <summary>What came of an edit.</summary>
/// <param name="Status">How it came out.</param>
/// <param name="Problems">Why it was refused, each at its answer's path.</param>
/// <param name="Asks">What it would overbook or double.</param>
/// <param name="Changed">How many fields it changed.</param>
internal sealed record EntryEditResult(EntryEditStatus Status, IReadOnlyList<DefinitionProblem> Problems, EntryMoveCheck? Asks, int Changed);

/// <summary>
/// Edits an entry's answers by a user's hand (ADR-0208): the fields outside repeatable groups whose answer is a text, a
/// number, a date, a choice of the field's own options or a checkbox, each read and checked by its field type as a
/// submission's is, rules aside. Files, consents, signatures, values of several parts, grids, rankings and sensitive
/// values the user may not see stay as they are. A changed choice moves its place; an edit of an entry that holds places
/// asks first before it overbooks an option or doubles a unique answer. The timeline keeps the old and new values, and
/// the audit log names the fields.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The timelines.</param>
/// <param name="audit">The audit log.</param>
/// <param name="reads">Reads an entry's answers.</param>
/// <param name="store">The search text.</param>
/// <param name="answers">The answers as texts.</param>
/// <param name="types">The field types.</param>
/// <param name="checks">What an edit would overbook or double.</param>
/// <param name="pages">The entry's site.</param>
/// <param name="timeZones">The site's time zone, for today.</param>
/// <param name="time">The clock.</param>
internal sealed class EntryEdits(
    FormBuilderDatabase database,
    IFormRepository forms,
    IEntryRepository entries,
    IEntryEventRepository events,
    IAuditRepository audit,
    SubmissionEntries reads,
    EntryAnswerStore store,
    MergeTagAnswerTexts answers,
    FieldTypeRegistry types,
    EntryMoveChecks checks,
    FormPages pages,
    SiteTimeZones timeZones,
    TimeProvider time)
{
    /// <summary>The kind of the event that records an edit; its details hold each field's old and new texts.</summary>
    public const string EditedEvent = "answersEdited";

    /// <summary>The audit action of an edit; its details name the fields, never their values.</summary>
    public const string EditedAction = "entry.answersEdited";

    /// <summary>The most answers one edit changes.</summary>
    public const int MaxAnswers = 300;

    // The types whose answers stay as the visitor gave them, or as the form worked them out.
    private static readonly FrozenSet<string> Kept = new[] { "ranking", "consent", "calculation", "captcha" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>How the panel edits a field's answer, with its values now; null when it does not edit it for the user.</summary>
    /// <param name="field">A field of a group of the entry's version.</param>
    /// <param name="maySeeSensitive">Whether the user may see sensitive values.</param>
    /// <param name="values">The answer's values now, in the wire form.</param>
    public EntryPanelEdit? EditOf(FormField field, bool maySeeSensitive, IReadOnlyList<string> values)
    {
        if (!Edits(field, maySeeSensitive))
        {
            return null;
        }

        var traits = types.Find(field.Type)!;
        var kind = traits.KindOf(field) switch
        {
            FieldKind.Number => "number",
            FieldKind.Date => "date",
            FieldKind.Choice => "choice",
            FieldKind.Choices => "choices",
            FieldKind.Checkbox => "checkbox",
            _ => traits.Storage == FieldStorageKind.LongText ? "longText" : "text",
        };
        IReadOnlyList<EntryListOption> options = field.Options is { Source: OptionSource.Items } own
            ? [.. own.Items.Select(item => new EntryListOption(item.Value, item.Label?.Default is { Length: > 0 } label ? label : item.Value))]
            : [];
        return new EntryPanelEdit(kind, values, options, field.Required);
    }

    /// <summary>Whether the panel edits a field's answer: what this service edits, for a user who may see it.</summary>
    /// <param name="field">A field of a group of the entry's version.</param>
    /// <param name="maySeeSensitive">Whether the user may see sensitive values.</param>
    public bool Edits(FormField field, bool maySeeSensitive)
    {
        ArgumentNullException.ThrowIfNull(field);
        return (maySeeSensitive || !field.Sensitive)
            && field.Fields.Count == 0
            && field.Rows.Count == 0
            && !Kept.Contains(field.Type)
            && types.Find(field.Type) is { } traits
            && traits.Storage is FieldStorageKind.Text or FieldStorageKind.LongText or FieldStorageKind.Number or FieldStorageKind.Date or FieldStorageKind.Bool
            && traits.KindOf(field) switch
            {
                FieldKind.Text or FieldKind.Number or FieldKind.Date or FieldKind.Checkbox => true,
                FieldKind.Choice or FieldKind.Choices => field.Options is { Source: OptionSource.Items },
                _ => false,
            };
    }

    /// <summary>Edits answers of an entry of a form.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="userKey">Who edits it.</param>
    /// <param name="edited">The new answers, at most <see cref="MaxAnswers"/>, each field once.</param>
    /// <param name="maySeeSensitive">Whether the user may see sensitive values, and so edit them.</param>
    /// <param name="overbook">Whether the user agreed to overbook options and double unique answers.</param>
    /// <param name="cancellationToken">Cancels the reads and the unit of work.</param>
    public async Task<EntryEditResult> EditAsync(Guid formKey, Guid entryKey, Guid userKey, IReadOnlyList<EntryEditValue> edited, bool maySeeSensitive, bool overbook, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edited);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form
            || await entries.GetAsync(entryKey, cancellationToken) is not { } entry
            || entry.FormId != form.Id
            || entry.State == EntryState.Draft
            || await reads.ReadWholeAsync(entry, form, cancellationToken) is not { } read)
        {
            return new EntryEditResult(EntryEditStatus.NotFound, [], null, 0);
        }

        var definition = read.Scope.Form;
        var fields = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).ToDictionary(field => field.Key);
        var current = fields.Keys.ToDictionary(key => key, key => (IReadOnlyList<string>)[.. read.Answers?.Find(key)?.Values.Select(value => value.Value.Text) ?? []]);
        // What the form keeps only until its actions ran is gone once they did: an edit never brings it back (ADR-0252).
        var removed = read.Timeline.Any(entryEvent => entryEvent.Kind == ActionSettlement.ValuesRemovedEvent);
        var problems = new List<DefinitionProblem>();
        var changes = new List<(FormField Field, int Index, IReadOnlyList<string> Values)>();
        foreach (var (answer, index) in edited.Select((answer, index) => (answer, index)))
        {
            var path = $"answers[{index}]";
            if (!fields.TryGetValue(answer.Field, out var field) || !Edits(field, maySeeSensitive) || (removed && ActionSettlement.KeptUntilActions(definition, field.Key)))
            {
                problems.Add(new DefinitionProblem("not_editable", path + ".field", "This answer can't be edited here."));
                continue;
            }

            IReadOnlyList<string> values = [.. answer.Values.Where(value => !string.IsNullOrEmpty(value))];
            if (!values.SequenceEqual(current[field.Key], StringComparer.Ordinal))
            {
                changes.Add((field, index, values));
                current[field.Key] = values;
            }
        }

        var zone = await timeZones.GetAsync((await pages.EntryPageAsync(entry)).Site, cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).DateTime);
        var others = current.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray());
        var readValues = new List<(FormField Field, ImmutableArray<FieldValue> Values)>();
        foreach (var (field, index, values) in changes)
        {
            var type = types.FindType(field.Type)!;
            var given = new FieldAnswers(values);
            if (FieldValues.CheckLimits(type, field, given) is { } limit)
            {
                problems.Add(Refused(index, limit));
                continue;
            }

            var found = FieldValues.Read(type, field, given);
            var errors = FieldValues.Check(type, field, found, new FieldCheckInput(field.Required, today) { Values = others });
            problems.AddRange(errors.Errors.Select(error => Refused(index, error)));
            problems.AddRange(errors.Checks.Select(check => new DefinitionProblem("check_failed", $"answers[{index}].values", field.Validation[check].Message?.Default ?? "This answer fails one of the field's checks.")));
            readValues.Add((field, found.Values));
        }

        if (problems.Count > 0)
        {
            return new EntryEditResult(EntryEditStatus.Invalid, problems, null, 0);
        }

        if (readValues.Count == 0)
        {
            return new EntryEditResult(EntryEditStatus.Saved, [], null, 0);
        }

        if (!overbook && EntryLimits.Counts(entry.State, entry.IsTest) && await AsksAsync(form, read, readValues, cancellationToken) is { IsEmpty: false } asks)
        {
            return new EntryEditResult(EntryEditStatus.Asks, [], asks, 0);
        }

        await SaveAsync(form, entry, read, readValues, userKey, cancellationToken);
        return new EntryEditResult(EntryEditStatus.Saved, [], null, readValues.Count);
    }

    private static DefinitionProblem Refused(int index, FieldMessageKinds kind) =>
        new(JsonNamingPolicy.CamelCase.ConvertName(kind.ToString()), $"answers[{index}].values", "The field's type refuses this answer.");

    // What the new choices and unique answers would overbook or double: only those the entry did not hold already.
    private async Task<EntryMoveCheck> AsksAsync(StoredForm form, EntryRead read, List<(FormField Field, ImmutableArray<FieldValue> Values)> changed, CancellationToken cancellationToken)
    {
        var chosen = new List<EntryChosenPlace>();
        var given = new List<(FormField, AnswerValue)>();
        foreach (var (field, values) in changed)
        {
            var before = read.Answers?.Find(field.Key)?.Values.Select(value => value.Value.Text).ToHashSet(StringComparer.Ordinal) ?? [];
            var options = EntryMoveChecks.OptionsWithPlaces(field);
            foreach (var value in values.Where(value => !before.Contains(value.Text)))
            {
                if (options.TryGetValue(value.Text, out var place))
                {
                    chosen.Add(place);
                }

                if (checks.MustBeUnique(field) && checks.ComparedBy(field, value) is { } compared)
                {
                    given.Add((field, compared));
                }
            }
        }

        return chosen.Count == 0 && given.Count == 0 ? EntryMoveCheck.None : await checks.EditAsync(form, chosen, given, cancellationToken);
    }

    // The new values, the search text, the timeline's event with the old and new texts, and the audit entry, in one unit. A
    // sensitive answer, or one kept only until the actions have run, leaves no copy in the event: it says the field changed,
    // without its texts (ADR-0009, ADR-0225).
    private async Task SaveAsync(StoredForm form, StoredEntry entry, EntryRead read, List<(FormField Field, ImmutableArray<FieldValue> Values)> changed, Guid userKey, CancellationToken cancellationToken)
    {
        var definition = read.Scope.Form;
        var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
        var rows = EntryValueWriter.Of(form.Id, definition, catalogue, types, changed.Select(item => new EntryFieldAnswer(item.Field.Key, item.Values)), new Dictionary<Guid, ConsentWording>());
        var keys = changed.Select(item => item.Field.Key).ToHashSet();
        var fieldIds = catalogue.Where(row => keys.Contains(row.Values.FieldKey)).Select(row => row.Id).ToHashSet();
        var kept = (await entries.ListValuesAsync([entry.Id], cancellationToken)).Where(value => !fieldIds.Contains(value.FieldId));
        var searchText = store.SearchTextOf(definition, catalogue, kept, rows.Values);
        var texts = await answers.TextsAsync(read.Scope, changed.Select(item => new AnswerField(item.Field, null)), cancellationToken);
        var details = JsonSerializer.Serialize(new
        {
            changes = changed.Select(item => (item.Field, item.Values, Withheld: item.Field.Sensitive || item.Field.Keep == FieldKeep.UntilActionsRun)).Select(item => new
            {
                field = item.Field.Key,
                label = MergeTagAnswerTexts.Label(item.Field, read.Scope.Culture, texts.Dictionary),
                sensitive = item.Withheld,
                before = item.Withheld ? [] : TextsOf(item.Field, read.Answers?.Find(item.Field.Key)?.Values.Select(value => value.Value).ToList() ?? [], read.Scope.Culture, texts),
                after = item.Withheld ? [] : TextsOf(item.Field, item.Values, read.Scope.Culture, texts),
            }),
        });
        await database.WriteAsync(
            async _ =>
            {
                await entries.ReplaceValuesAsync(entry.Id, fieldIds, rows.Values, searchText, cancellationToken);
                await events.AddAsync(new NewEntryEvent(entry.Id, EditedEvent, userKey, Details: details), cancellationToken);
                await audit.AddAsync(new NewAuditEntry(EditedAction, EntryPanels.EntityType, entry.Key, userKey, new AuditDetails().Add("form", form.Key).AddKeys("fields", keys)), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    // An answer's texts as the panel shows them, in the entry's culture.
    private string[] TextsOf(FormField field, IReadOnlyList<FieldValue> values, string culture, ScopeTexts texts) =>
        values.Count == 0 ? [] : [.. answers.Texts(field, new MergeTagAnswer(field.Key, values), culture, listing: true, texts).Select(text => text.Text)];
}
