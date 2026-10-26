using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
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
    /// first write is the form's counter (ADR-0038); a wording is stored once, under its hash. Null when the form does
    /// not exist; refused as <see cref="IEntryRepository.CreateAsync"/> refuses.
    /// </summary>
    /// <param name="entry">The entry, without values or uploads: its answers give them.</param>
    /// <param name="definition">The version <see cref="NewEntry.FormVersionId"/> names, which the answers were read with.</param>
    /// <param name="answers">The answers, at most one for each field and row.</param>
    /// <param name="wordings">The wording each ticked consent was shown, by the field's key (<see cref="ConsentWordings"/>).</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <exception cref="ArgumentException">
    /// The entry already has values or uploads, or the answers are refused (<see cref="EntryValueWriter.Of"/>).
    /// </exception>
    public async Task<StoredEntry?> CreateAsync(
        NewEntry entry,
        FormDefinition definition,
        IEnumerable<EntryFieldAnswer> answers,
        IReadOnlyDictionary<Guid, ConsentWording> wordings,
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
        return await database.WriteAsync(
            async _ =>
            {
                var stored = await entries.CreateAsync(entry with { Values = rows.Values, FileIds = rows.FileIds }, cancellationToken);
                if (stored is not null)
                {
                    foreach (var wording in rows.Wordings)
                    {
                        await consentTexts.AddAsync(wording.Culture, wording.Text, cancellationToken);
                    }
                }

                return stored;
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
