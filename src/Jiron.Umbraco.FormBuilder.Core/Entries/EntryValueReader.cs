using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// Reads a stored entry's rows back into typed answers (ADR-0070): each value from its stored text, read again in its
/// wire form by its catalogue row's storage kind and its type's rule kind, so what is read back is exactly what was
/// stored and a number never reads back rounded; a grid row's values as the grid's, each with its row.
/// </summary>
/// <remarks>
/// A stored text its kind cannot read, such as one stored before the field's type changed (ADR-0059), keeps its text and
/// no typed value; a field whose type the site no longer has reads by its storage kind alone; and a row without a text,
/// which no writer stores, reads as no value.
/// </remarks>
internal static class EntryValueReader
{
    /// <summary>The hashes of the consent wordings the values name, each once.</summary>
    /// <param name="catalogue">The form's field catalogue.</param>
    /// <param name="values">The entry's stored values.</param>
    /// <param name="types">The site's field types.</param>
    /// <exception cref="ArgumentException">A value names a row the catalogue does not have.</exception>
    public static IReadOnlyList<string> ConsentHashes(IReadOnlyList<CatalogueField> catalogue, IReadOnlyList<StoredEntryValue> values, FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(types);
        var rows = catalogue.ToDictionary(field => field.Id);
        return [.. values
            .Where(value => IsConsent(Place(rows, value).Values, types) && !string.IsNullOrEmpty(value.Text))
            .Select(value => value.Text!)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The entry's answers, typed.</summary>
    /// <param name="catalogue">The form's field catalogue.</param>
    /// <param name="values">The entry's stored values.</param>
    /// <param name="files">The entry's files.</param>
    /// <param name="wordings">The consent wordings the values name, by their hash; one that is missing reads as none.</param>
    /// <param name="types">The site's field types.</param>
    /// <exception cref="ArgumentException">A value names a row the catalogue does not have.</exception>
    public static EntryAnswers Read(
        IReadOnlyList<CatalogueField> catalogue,
        IReadOnlyList<StoredEntryValue> values,
        IReadOnlyList<StoredEntryFile> files,
        IReadOnlyDictionary<string, StoredConsentText> wordings,
        FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(wordings);
        ArgumentNullException.ThrowIfNull(types);
        var rows = catalogue.ToDictionary(field => field.Id);
        var byKey = catalogue.ToDictionary(field => field.Values.FieldKey, field => field.Values);
        var byId = files.ToDictionary(file => file.Id);
        var answers = new Dictionary<(Guid Field, int Row), (CatalogueFieldValues Owner, List<(int Order, int Index, EntryAnswerValue Value)> Values)>();
        foreach (var stored in values)
        {
            var place = Place(rows, stored).Values;
            if (string.IsNullOrEmpty(stored.Text))
            {
                continue;
            }

            var grid = GridOf(place, byKey);
            var owner = grid ?? place;
            var value = grid is null
                ? Typed(place, stored.Text, stored.FileId, byId, wordings, types)
                : new EntryAnswerValue { Value = new FieldValue(stored.Text, place.FieldKey) };
            if (!answers.TryGetValue((owner.FieldKey, stored.RowIndex), out var answer))
            {
                answer = (owner, []);
                answers[(owner.FieldKey, stored.RowIndex)] = answer;
            }

            answer.Values.Add((grid is null ? 0 : place.SortOrder, stored.ValueIndex, value));
        }

        return new EntryAnswers(answers
            .OrderBy(pair => pair.Value.Owner.SortOrder)
            .ThenBy(pair => pair.Key.Row)
            .Select(pair => new EntryAnswer
            {
                Field = pair.Key.Field,
                Row = pair.Key.Row,
                Repeater = pair.Value.Owner.ParentFieldKey,
                Alias = pair.Value.Owner.Alias,
                Type = pair.Value.Owner.Type,
                Storage = pair.Value.Owner.StorageKind,
                Values = [.. pair.Value.Values.OrderBy(each => each.Order).ThenBy(each => each.Index).Select(each => each.Value)],
            }));
    }

    private static CatalogueField Place(Dictionary<int, CatalogueField> rows, StoredEntryValue value) =>
        rows.TryGetValue(value.FieldId, out var row)
            ? row
            : throw new ArgumentException($"A value names the catalogue row {value.FieldId}, which the form's catalogue does not have.", nameof(value));

    // A grid's row is in the catalogue under its grid with the grid's type; a repeatable group's child never has its
    // group's type, as groups do not nest (ADR-0035, ADR-0044).
    private static CatalogueFieldValues? GridOf(CatalogueFieldValues place, Dictionary<Guid, CatalogueFieldValues> byKey) =>
        place.ParentFieldKey is { } parent && byKey.TryGetValue(parent, out var owner) && string.Equals(owner.Type, place.Type, StringComparison.Ordinal)
            ? owner
            : null;

    // A ticked consent is true to rules and stores the hash of its wording as text (ADR-0009); only the built-in consent
    // has a wording, as only its values are written with one.
    private static bool IsConsent(CatalogueFieldValues place, FieldTypeRegistry types) =>
        place.StorageKind == FieldStorageKind.Text && types.FindType(place.Type) is ConsentFieldType;

    private static EntryAnswerValue Typed(
        CatalogueFieldValues place,
        string text,
        int? fileId,
        Dictionary<int, StoredEntryFile> files,
        IReadOnlyDictionary<string, StoredConsentText> wordings,
        FieldTypeRegistry types)
    {
        var value = new EntryAnswerValue { Value = new FieldValue(text) };
        switch (place.StorageKind)
        {
            case FieldStorageKind.Number:
                return value with { Number = ExactDecimal.Parse(text) is { } number && ExactDecimal.Fits(number) ? decimal.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture) : null };
            case FieldStorageKind.Date when CalendarValues.ParseDate(text) is { } day:
                return value with { Date = DateOnly.FromDayNumber(day) };
            case FieldStorageKind.Date when CalendarValues.ParseDateTime(text) is { } seconds:
                return value with { DateTime = new DateTime(seconds * TimeSpan.TicksPerSecond, DateTimeKind.Unspecified) };
            case FieldStorageKind.Bool when text is EntryValueColumns.True or EntryValueColumns.False:
                return value with { Bool = text == EntryValueColumns.True };
            case FieldStorageKind.File:
                return value with { File = fileId is { } id ? files.GetValueOrDefault(id) : null };
            case FieldStorageKind.Json:
                return value with { Parts = FieldPartsJson.Read(text) };
            case FieldStorageKind.Text when IsConsent(place, types):
                return new EntryAnswerValue { Value = new FieldValue(FieldAnswerDefaults.Ticked) { StoredText = text }, Bool = true, Consent = wordings.GetValueOrDefault(text) };
            case FieldStorageKind.Text when types.Find(place.Type)?.Kind == FieldKind.Time && CalendarValues.ParseTime(text) is { } time:
                return value with { Time = new TimeOnly(time * TimeSpan.TicksPerSecond) };
            default:
                return value;
        }
    }
}
