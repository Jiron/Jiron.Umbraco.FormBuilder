using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>An option with places that an entry chose.</summary>
/// <param name="Field">The choice field.</param>
/// <param name="Value">The option's value.</param>
/// <param name="Label">Its label.</param>
/// <param name="Places">How many places it has.</param>
internal sealed record EntryChosenPlace(FormField Field, string Value, string Label, int Places);

/// <summary>An option a move or an edit would overbook.</summary>
/// <param name="Field">The choice field.</param>
/// <param name="FieldLabel">Its label.</param>
/// <param name="Option">The option's value.</param>
/// <param name="OptionLabel">Its label.</param>
/// <param name="Places">How many places it has.</param>
/// <param name="Taken">How many the entries that hold places took.</param>
internal sealed record EntryFullOption(Guid Field, string FieldLabel, string Option, string OptionLabel, int Places, int Taken);

/// <summary>A field whose answer must be unique and that another entry holds, or that two of the moving entries share.</summary>
/// <param name="Field">The field.</param>
/// <param name="FieldLabel">Its label.</param>
internal sealed record EntryHeldAnswer(Guid Field, string FieldLabel);

/// <summary>What a move or an edit asks first (ADR-0208): the options it would overbook and the answers it would double.</summary>
/// <param name="Full">The options past their places.</param>
/// <param name="Held">The answers another entry holds.</param>
internal sealed record EntryMoveCheck(IReadOnlyList<EntryFullOption> Full, IReadOnlyList<EntryHeldAnswer> Held)
{
    /// <summary>Nothing to ask.</summary>
    public static readonly EntryMoveCheck None = new([], []);

    /// <summary>Whether there is nothing to ask.</summary>
    public bool IsEmpty => Full.Count == 0 && Held.Count == 0;
}

/// <summary>
/// Reads, before entries come back into the states that hold places and unique answers, what they would overbook and
/// what they would double (ADR-0090, ADR-0091, ADR-0208), so the user is asked first. The repository refuses neither; the
/// counts it reads hold the other entries that hold places, and the moving entries hold none yet. The options of an
/// option list or a source are asked of it as a submission asks, in the entry's culture and page and for its parent's
/// answers; one it cannot give is not asked about.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="store">The stored answers.</param>
/// <param name="documents">Reads stored versions.</param>
/// <param name="types">The field types.</param>
/// <param name="options">The options of lists and sources.</param>
internal sealed class EntryMoveChecks(IFormRepository forms, IEntryRepository entries, EntryAnswerStore store, FormDocuments documents, FieldTypeRegistry types, IFormBuilderOptionService options)
{
    /// <summary>What moving these entries of the form into a state that holds places would overbook or double.</summary>
    /// <param name="form">The form.</param>
    /// <param name="moving">Its entries in a state that holds nothing, which would come back.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryMoveCheck> MoveAsync(StoredForm form, IReadOnlyList<StoredEntry> moving, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(moving);
        var coming = moving.Where(entry => !entry.IsTest).ToList();
        if (coming.Count == 0)
        {
            return EntryMoveCheck.None;
        }

        var stored = await store.ReadManyAsync(form.Id, [.. coming.Select(entry => entry.Id)], cancellationToken);
        var definitions = new Dictionary<int, FormDefinition?>();
        var places = new List<EntryChosenPlace>();
        var unique = new List<(FormField Field, long Entry, AnswerValue Value)>();
        var resolved = new Dictionary<string, IReadOnlyDictionary<string, EntryChosenPlace>>(StringComparer.Ordinal);
        foreach (var entry in coming)
        {
            if (!definitions.TryGetValue(entry.FormVersionId, out var definition))
            {
                definition = definitions[entry.FormVersionId] = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version ? documents.Readable(version) : null;
            }

            if (definition is not null && stored.TryGetValue(entry.Id, out var answers))
            {
                await CollectAsync(definition, answers, entry, places, unique, resolved, cancellationToken);
            }
        }

        return await CheckAsync(form.Id, places, unique, cancellationToken);
    }

    /// <summary>
    /// What an edit of an entry that holds places would overbook or double: the options it newly chooses and the unique
    /// answers it newly gives, against the other entries.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="chosen">The options newly chosen.</param>
    /// <param name="given">The answers newly given to fields that must be unique, each with its field.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public Task<EntryMoveCheck> EditAsync(StoredForm form, IReadOnlyList<EntryChosenPlace> chosen, IReadOnlyList<(FormField Field, AnswerValue Value)> given, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        return CheckAsync(form.Id, [.. chosen], [.. given.Select(item => (item.Field, 0L, item.Value))], cancellationToken);
    }

    /// <summary>The options with places of a field's own items, by value.</summary>
    /// <param name="field">The field.</param>
    public static IReadOnlyDictionary<string, EntryChosenPlace> OptionsWithPlaces(FormField field) =>
        field.Options is { Source: OptionSource.Items } own
            ? own.Items.Where(item => item.Places is not null).GroupBy(item => item.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => Place(field, group.First().Value, group.First().Label?.Default, group.First().Places), StringComparer.Ordinal)
            : new Dictionary<string, EntryChosenPlace>(StringComparer.Ordinal);

    /// <summary>Whether a field's answer must be unique, by a check of a type that compares its values.</summary>
    /// <param name="field">The field.</param>
    public bool MustBeUnique(FormField field) =>
        field.Validation.Any(check => check is UniquePerFormCheck)
        && types.Find(field.Type) is { } traits
        && traits.Features.HasFlag(FieldFeatures.UniquePerFormCheck);

    /// <summary>How a field's stored value compares with the others of its field.</summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    public AnswerValue? ComparedBy(FormField field, FieldValue value) =>
        types.Find(field.Type) is { } traits ? EntryValueColumns.ComparedBy(traits.Storage, field.Key, value) : null;

    /// <summary>A field's label as the question shows it: its default text, or its alias.</summary>
    /// <param name="field">The field.</param>
    public static string LabelOf(FormField field) => field.Label?.Default is { Length: > 0 } label ? label : field.Alias ?? string.Empty;

    private static EntryChosenPlace Place(FormField field, string value, string? label, int? places) =>
        new(field, value, label is { Length: > 0 } ? label : value, Math.Max(0, places ?? 0));

    // The options with places an entry chose and its answers that must be unique, its repeatable groups' rows among them.
    private async Task CollectAsync(
        FormDefinition definition,
        EntryAnswers answers,
        StoredEntry entry,
        List<EntryChosenPlace> places,
        List<(FormField, long, AnswerValue)> unique,
        Dictionary<string, IReadOnlyDictionary<string, EntryChosenPlace>> resolved,
        CancellationToken cancellationToken)
    {
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)))
        {
            var mustBeUnique = MustBeUnique(field);
            var own = OptionsWithPlaces(field);
            var fromSource = field.Options is { Source: not OptionSource.Items };
            if (own.Count == 0 && !fromSource && !mustBeUnique)
            {
                continue;
            }

            foreach (var answer in answers.All.Where(answer => answer.Field == field.Key && answer.Values.Any()))
            {
                var offered = fromSource ? await OfferedAsync(field, answers, answer.Row, entry, resolved, cancellationToken) : own;
                foreach (var value in answer.Values)
                {
                    if (offered.TryGetValue(value.Text, out var place))
                    {
                        places.Add(place);
                    }

                    if (mustBeUnique && ComparedBy(field, value.Value) is { } compared)
                    {
                        unique.Add((field, entry.Id, compared));
                    }
                }
            }
        }
    }

    // The options with places a field's list or source gives an entry, asked once for each culture, page and parent answers.
    private async Task<IReadOnlyDictionary<string, EntryChosenPlace>> OfferedAsync(
        FormField field,
        EntryAnswers answers,
        int row,
        StoredEntry entry,
        Dictionary<string, IReadOnlyDictionary<string, EntryChosenPlace>> resolved,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> parentValues = field.Options?.Parent is { } parent
            ? [.. (answers.Find(parent, row) ?? answers.Find(parent))?.Values.Select(value => value.Text) ?? []]
            : [];
        var key = string.Join((char)31, [field.Key.ToString("N"), entry.Culture, entry.ContentKey?.ToString("N") ?? string.Empty, .. parentValues]);
        if (!resolved.TryGetValue(key, out var found))
        {
            var given = await options.GetAsync(field, new FormBuilderOptionContext(entry.Culture) { ParentValues = parentValues, Page = entry.ContentKey }, cancellationToken);
            found = resolved[key] = given.Options.Where(option => option.Places is not null).GroupBy(option => option.Value, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => Place(field, group.First().Value, group.First().Label, group.First().Places), StringComparer.Ordinal);
        }

        return found;
    }

    // The options past their places and the answers held, in the order they were collected.
    private async Task<EntryMoveCheck> CheckAsync(int formId, List<EntryChosenPlace> places, List<(FormField Field, long Entry, AnswerValue Value)> unique, CancellationToken cancellationToken)
    {
        var full = new List<EntryFullOption>();
        if (places.Count > 0)
        {
            var counts = await entries.GetPlacesTakenAsync(formId, [.. places.Select(place => new OptionPlace(place.Field.Key, place.Value)).Distinct()], cancellationToken);
            foreach (var group in places.GroupBy(place => new OptionPlace(place.Field.Key, place.Value)))
            {
                var place = group.First();
                var taken = counts.GetValueOrDefault(group.Key);
                if (taken + group.Count() > place.Places)
                {
                    full.Add(new EntryFullOption(place.Field.Key, LabelOf(place.Field), place.Value, place.Label, place.Places, taken));
                }
            }
        }

        var held = new List<EntryHeldAnswer>();
        if (unique.Count > 0)
        {
            var taken = await entries.GetTakenAnswersAsync(formId, [.. unique.Select(item => item.Value).Distinct()], cancellationToken);
            var shared = unique.GroupBy(item => item.Value).Where(group => group.Select(item => item.Entry).Distinct().Count() > 1).Select(group => group.Key).ToHashSet();
            foreach (var field in unique.Where(item => taken.Contains(item.Value) || shared.Contains(item.Value)).Select(item => item.Field).DistinctBy(field => field.Key))
            {
                held.Add(new EntryHeldAnswer(field.Key, LabelOf(field)));
            }
        }

        return full.Count == 0 && held.Count == 0 ? EntryMoveCheck.None : new EntryMoveCheck(full, held);
    }
}
