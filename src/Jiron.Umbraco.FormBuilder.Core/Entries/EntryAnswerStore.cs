using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// Stores an entry with its answers, and reads its answers back typed (ADR-0070): the one place that decides which row
/// each value is stored as (<see cref="EntryValueWriter"/>) and how a row reads again (<see cref="EntryValueReader"/>).
/// </summary>
internal sealed class EntryAnswerStore(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IConsentTextRepository consentTexts,
    IFormRepository forms,
    FieldTypeRegistry types)
{
    /// <summary>
    /// Stores an entry with its answers, its uploads and the consent wordings they name, in one unit of work whose
    /// first write is the form's counter (ADR-0038); a wording is stored once, under its hash. The entry's search text
    /// is made of its values here (ADR-0003, ADR-0088): their texts, a value with parts by its parts' texts, but no
    /// sensitive field's (a grid row's by its grid), no file, no yes or no and no consent's wording hash. Null when the
    /// form does not exist; refused as <see cref="IEntryRepository.CreateAsync"/> refuses.
    /// </summary>
    /// <param name="entry">The entry, without values or uploads: its answers give them.</param>
    /// <param name="definition">The version <see cref="NewEntry.FormVersionId"/> names, which the answers were read with.</param>
    /// <param name="answers">The answers, at most one for each field and row.</param>
    /// <param name="wordings">The wording each ticked consent was shown, by the field's key (<see cref="ConsentWordings"/>).</param>
    /// <param name="alongside">
    /// What the unit stores besides, once the entry is stored (its event, its action runs and its counts, ADR-0088), or
    /// null; its writes join the unit, so a refusal there rolls the entry back too.
    /// </param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <exception cref="ArgumentException">
    /// The entry already has values or uploads, or the answers are refused (<see cref="EntryValueWriter.Of"/>).
    /// </exception>
    public async Task<StoredEntry?> CreateAsync(
        NewEntry entry,
        FormDefinition definition,
        IEnumerable<EntryFieldAnswer> answers,
        IReadOnlyDictionary<Guid, ConsentWording> wordings,
        Func<StoredEntry, Task>? alongside = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Values.Count > 0 || entry.FileIds.Count > 0)
        {
            throw new ArgumentException("The entry's answers give its values and uploads, so it comes without any.", nameof(entry));
        }

        // Read before the unit, so the unit's first statement is its write (ADR-0007). A publish only adds rows or ends
        // them (ADR-0044), and a row removed meanwhile makes the repository refuse the entry (MissingField).
        var catalogue = await forms.ListFieldsAsync(entry.FormId, cancellationToken);
        if (catalogue.Count == 0 && await forms.GetByIdAsync(entry.FormId, cancellationToken) is null)
        {
            return null;
        }

        var rows = EntryValueWriter.Of(entry.FormId, definition, catalogue, types, answers, wordings);
        var searchText = EntrySearchText.For(Searchable(definition, catalogue, rows.Values));
        return await database.WriteAsync(
            async _ =>
            {
                var stored = await entries.CreateAsync(entry with { Values = rows.Values, FileIds = rows.FileIds, SearchText = searchText }, cancellationToken);
                if (stored is not null)
                {
                    foreach (var wording in rows.Wordings)
                    {
                        await consentTexts.AddAsync(wording.Culture, wording.Text, cancellationToken);
                    }

                    if (alongside is not null)
                    {
                        await alongside(stored);
                    }
                }

                return stored;
            },
            cancellationToken);
    }

    /// <summary>The search text of stored values, as <see cref="CreateAsync"/> makes it of new ones: what is left when some go.</summary>
    /// <param name="definition">The version the entry was submitted with.</param>
    /// <param name="catalogue">The form's field catalogue.</param>
    /// <param name="values">The values that stay.</param>
    public string SearchTextOf(FormDefinition definition, IReadOnlyList<CatalogueField> catalogue, IEnumerable<StoredEntryValue> values) =>
        EntrySearchText.For(Searchable(definition, catalogue, [.. values.Select(value => new NewEntryValue(value.FieldId, value.Text, value.Number, value.Date, value.Bool, value.FileId, value.RowIndex, value.ValueIndex)
        {
            // A value with parts stores JSON, and searches by the parts' texts in its key; any other by all of its text, as the key is cut.
            KeyText = value.Text is { } text && value.TextKey is { } key && !string.Equals(EntryTextKey.For(text), key, StringComparison.Ordinal) ? key : null,
        })]));

    // The texts search finds the entry by: its values' texts, a value with parts by its parts' texts (its key's text),
    // but no sensitive field's, no file, no yes or no and no consent's wording hash.
    private IEnumerable<string> Searchable(FormDefinition definition, IReadOnlyList<CatalogueField> catalogue, IReadOnlyList<NewEntryValue> values)
    {
        var fields = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).ToDictionary(field => field.Key);
        var rows = catalogue.ToDictionary(field => field.Id);
        foreach (var value in values)
        {
            if (value.FileId is not null || value.Bool is not null || !rows.TryGetValue(value.FieldId, out var row))
            {
                continue;
            }

            // A grid's row is its own row of the catalogue, whose parent is the grid.
            var owner = fields.GetValueOrDefault(row.Values.FieldKey) ?? (row.Values.ParentFieldKey is { } parent ? fields.GetValueOrDefault(parent) : null);
            if (owner is not null && !owner.Sensitive && types.FindType(owner.Type) is not ConsentFieldType && (value.KeyText ?? value.Text) is { Length: > 0 } text)
            {
                yield return text;
            }
        }
    }

    /// <summary>
    /// The answers of entries of one form, typed, by entry (ADR-0207): one read of their values, one of their files and
    /// one of the form's catalogue, and each consent wording they name once, for a page of a list. An entry without
    /// values has no answers; the entries are the form's.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="entryIds">Its entries.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public Task<IReadOnlyDictionary<long, EntryAnswers>> ReadManyAsync(int formId, IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var ids = entryIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<long, EntryAnswers>>(new Dictionary<long, EntryAnswers>());
        }

        return database.ReadAsync<IReadOnlyDictionary<long, EntryAnswers>>(
            async _ =>
            {
                var catalogue = await forms.ListFieldsAsync(formId, cancellationToken);
                var values = await entries.ListValuesAsync(ids, cancellationToken);
                var files = (await entries.ListFilesAsync(ids, cancellationToken)).Where(file => file.EntryId is not null).ToLookup(file => file.EntryId!.Value);
                var wordings = new Dictionary<string, StoredConsentText>(StringComparer.Ordinal);
                foreach (var hash in EntryValueReader.ConsentHashes(catalogue, values, types))
                {
                    if (await consentTexts.GetAsync(hash, cancellationToken) is { } wording)
                    {
                        wordings[hash] = wording;
                    }
                }

                var byEntry = values.ToLookup(value => value.EntryId);
                return ids.ToDictionary(id => id, id => EntryValueReader.Read(catalogue, [.. byEntry[id]], [.. files[id]], wordings, types));
            },
            cancellationToken);
    }

    /// <summary>An entry's answers, typed; null when the entry does not exist.</summary>
    /// <param name="entryId">The entry's id.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public Task<EntryAnswers?> ReadAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync<EntryAnswers?>(
            async _ =>
            {
                if (await entries.GetByIdAsync(entryId, cancellationToken) is not { } entry)
                {
                    return null;
                }

                var values = await entries.ListValuesAsync([entryId], cancellationToken);
                var files = await entries.ListFilesAsync(entryId, cancellationToken);
                var catalogue = await forms.ListFieldsAsync(entry.FormId, cancellationToken);
                var wordings = new Dictionary<string, StoredConsentText>(StringComparer.Ordinal);
                foreach (var hash in EntryValueReader.ConsentHashes(catalogue, values, types))
                {
                    if (await consentTexts.GetAsync(hash, cancellationToken) is { } wording)
                    {
                        wordings[hash] = wording;
                    }
                }

                return EntryValueReader.Read(catalogue, values, files, wordings, types);
            },
            cancellationToken);
}
