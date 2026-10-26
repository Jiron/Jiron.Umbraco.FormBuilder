using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>What an entry stores of its answers (ADR-0070).</summary>
/// <param name="Values">The rows of <c>jfbEntryValue</c>.</param>
/// <param name="FileIds">The uploads the entry takes.</param>
/// <param name="Wordings">The consent wordings its values name, each once.</param>
internal sealed record EntryRows(IReadOnlyList<NewEntryValue> Values, IReadOnlyList<int> FileIds, IReadOnlyList<ConsentWording> Wordings);

/// <summary>
/// Turns an entry's answers into the rows it stores (ADR-0070), over the version it is submitted with and the form's
/// field catalogue: each value at its field's catalogue row, or a grid's at its row's, with the column the row's storage
/// kind fills (ADR-0058); its position among the field's values as <c>valueIndex</c>, so a multi-value answer, an
/// "Other" text and a ranking keep their order, and its repeatable group's row as <c>rowIndex</c>; a file value with
/// the upload its token names, and a ticked consent with the wording it was shown.
/// </summary>
/// <remarks>
/// Every refusal names the field and never a value: the values are a visitor's answers, and messages end up in logs.
/// </remarks>
internal static class EntryValueWriter
{
    /// <summary>The rows of an entry's answers.</summary>
    /// <param name="formId">The entry's form.</param>
    /// <param name="definition">The version the entry is submitted with.</param>
    /// <param name="catalogue">The form's field catalogue.</param>
    /// <param name="types">The site's field types.</param>
    /// <param name="answers">The answers, at most one for each field and row.</param>
    /// <param name="wordings">The wording each consent was shown, by the field's key.</param>
    /// <exception cref="ArgumentException">
    /// An answer names a field the version does not have, or one the catalogue lacks; a field has two answers in one row,
    /// or a row outside a repeatable group; a value answers a grid row the field does not have; a field that stores files
    /// has not one upload for each value, or an upload of another form, field or entry, or one named twice; another field
    /// has uploads; a ticked consent stores the hash of another wording than the one given for it; or a value is not in
    /// its storage kind's form (<see cref="EntryValueColumns.Of"/>).
    /// </exception>
    public static EntryRows Of(
        int formId,
        FormDefinition definition,
        IReadOnlyList<CatalogueField> catalogue,
        FieldTypeRegistry types,
        IEnumerable<EntryFieldAnswer> answers,
        IReadOnlyDictionary<Guid, ConsentWording> wordings)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(wordings);
        var places = Places(definition);
        var rows = catalogue.ToDictionary(field => field.Values.FieldKey);
        var answered = new HashSet<(Guid Field, int Row)>();
        var taken = new HashSet<int>();
        var values = new List<NewEntryValue>();
        var files = new List<int>();
        var needed = new Dictionary<string, ConsentWording>(StringComparer.Ordinal);
        foreach (var answer in answers)
        {
            if (answer is null)
            {
                throw new ArgumentException("An answer is never null.", nameof(answers));
            }

            if (!places.TryGetValue(answer.Field, out var place))
            {
                throw new ArgumentException($"An answer names the field {answer.Field}, which the entry's version does not have.", nameof(answers));
            }

            if (!answered.Add((answer.Field, answer.Row)))
            {
                throw new ArgumentException($"The field {answer.Field} has two answers in the row {answer.Row}.", nameof(answers));
            }

            if (answer.Row > 0 && !place.InRepeater)
            {
                throw new ArgumentException($"The field {answer.Field} is not in a repeatable group, so it has no rows.", nameof(answers));
            }

            var row = Row(rows, answer.Field);
            var storage = row.Values.StorageKind;
            CheckFiles(formId, answer, storage, taken);
            if (answer.Values.Length > 0 && types.FindType(place.Field.Type) is ConsentFieldType)
            {
                NeedWording(answer, wordings, needed);
            }

            var own = 0;
            var inRows = new Dictionary<Guid, int>();
            foreach (var value in answer.Values)
            {
                if (value.Row is { } gridRow)
                {
                    var rowField = place.Field.Rows.Any(each => each.Key == gridRow) ? Row(rows, gridRow) : null;
                    if (rowField is null || rowField.Values.ParentFieldKey != answer.Field)
                    {
                        throw new ArgumentException($"A value of the field {answer.Field} answers a row that it does not have.", nameof(answers));
                    }

                    var index = inRows.GetValueOrDefault(gridRow);
                    inRows[gridRow] = index + 1;
                    values.Add(EntryValueColumns.Of(rowField.Values.StorageKind, value, rowField.Id, rowIndex: answer.Row, valueIndex: index));
                    continue;
                }

                var file = storage == FieldStorageKind.File ? answer.Files[own] : null;
                values.Add(EntryValueColumns.Of(storage, value, row.Id, file, answer.Row, own));
                if (file is not null)
                {
                    files.Add(file.Id);
                }

                own++;
            }
        }

        return new EntryRows(values, files, [.. needed.Values]);
    }

    // Every field of the version by its key, and whether it is a repeatable group's child; groups do not nest (ADR-0035).
    private static Dictionary<Guid, Place> Places(FormDefinition definition)
    {
        var places = new Dictionary<Guid, Place>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            places[field.Key] = new Place(field, InRepeater: false);
            foreach (var child in field.Fields)
            {
                places[child.Key] = new Place(child, InRepeater: true);
            }
        }

        return places;
    }

    private static CatalogueField Row(Dictionary<Guid, CatalogueField> catalogue, Guid key) =>
        catalogue.TryGetValue(key, out var row)
            ? row
            : throw new ArgumentException($"The field catalogue has no row for {key}: the version's publish, or its preview, writes one first.", nameof(catalogue));

    // One upload for each value of a field that stores files, of the entry's form and that field, waiting for an entry,
    // and named by no other value; none for any other field.
    private static void CheckFiles(int formId, EntryFieldAnswer answer, FieldStorageKind storage, HashSet<int> taken)
    {
        if (storage != FieldStorageKind.File)
        {
            if (!answer.Files.IsEmpty)
            {
                throw new ArgumentException($"The field {answer.Field} stores no files, so its answer has no uploads.", nameof(answer));
            }

            return;
        }

        if (answer.Files.Length != answer.Values.Length)
        {
            throw new ArgumentException($"The field {answer.Field} stores files: its answer has one upload for each value.", nameof(answer));
        }

        foreach (var file in answer.Files)
        {
            if (file.FormId != formId || file.FieldKey != answer.Field || file.EntryId is not null || !taken.Add(file.Id))
            {
                throw new ArgumentException($"An upload of the field {answer.Field} belongs to another form, field or entry, or is named twice.", nameof(answer));
            }
        }
    }

    // A ticked consent stores the hash of the wording it was shown, which the entry stores with it.
    private static void NeedWording(EntryFieldAnswer answer, IReadOnlyDictionary<Guid, ConsentWording> wordings, Dictionary<string, ConsentWording> needed)
    {
        if (!wordings.TryGetValue(answer.Field, out var wording) || wording is null || answer.Values.Any(value => !string.Equals(value.StoredText, wording.Hash, StringComparison.Ordinal)))
        {
            throw new ArgumentException($"The consent {answer.Field} stores the hash of another wording than the one it was shown.", nameof(wordings));
        }

        needed.TryAdd(wording.Hash, wording);
    }

    private sealed record Place(FormField Field, bool InRepeater);
}
