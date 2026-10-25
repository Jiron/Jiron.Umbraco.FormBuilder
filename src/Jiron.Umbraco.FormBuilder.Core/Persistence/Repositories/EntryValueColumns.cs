using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// The row a field's value is stored as (ADR-0003, ADR-0058): its text, and the typed column its storage kind fills.
/// A value always stores the text it stores in place of its own (<see cref="FieldValue.StoredText"/>) or else its own;
/// a grid's value is stored at its row's place in the catalogue, whose storage kind is text.
/// </summary>
internal static class EntryValueColumns
{
    /// <summary>The stored text of a ticked box, and of a value that is true.</summary>
    public const string True = "true";

    /// <summary>The stored text of a value that is false.</summary>
    public const string False = "false";

    /// <summary>
    /// Whether a value of the storage kind takes the value's stored text: any text for text, long text and files (whose
    /// upload token the entry's file replaces); the wire form of a number; of a date or of a date with a time; <c>true</c>
    /// or <c>false</c>; an object of parts (<see cref="FieldPartsJson.Read"/>); and nothing for a type whose parts hold
    /// its values, except the values of its grid rows.
    /// </summary>
    public static bool Holds(FieldStorageKind storage, FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var stored = Stored(value);
        return storage switch
        {
            FieldStorageKind.Text or FieldStorageKind.LongText or FieldStorageKind.File => true,
            FieldStorageKind.Number => ExactDecimal.Parse(stored) is not null,
            FieldStorageKind.Date => CalendarValues.ParseDate(stored) is not null || CalendarValues.ParseDateTime(stored) is not null,
            FieldStorageKind.Bool => stored is True or False,
            FieldStorageKind.Json => FieldPartsJson.Read(stored) is not null,
            FieldStorageKind.None => value.Row is not null,
            _ => false,
        };
    }

    /// <summary>The row of a value of a field whose storage kind is <paramref name="storage"/>.</summary>
    /// <param name="storage">The storage kind of the value's place in the catalogue: a grid row's is text.</param>
    /// <param name="value">The value, which the storage kind holds (<see cref="Holds"/>).</param>
    /// <param name="fieldId">The catalogue row of the value's place: the field's, or its grid row's.</param>
    /// <param name="file">The entry's file a file value's upload became; only for files.</param>
    /// <param name="rowIndex">The repeater row, 0 outside a repeater.</param>
    /// <param name="valueIndex">The item of a multi-value answer, 0 for a single value.</param>
    /// <exception cref="ArgumentException">
    /// The storage kind does not take the value, holds no values of its own, or is a file's without the entry's file, or a
    /// file is given for another kind.
    /// </exception>
    public static NewEntryValue Of(FieldStorageKind storage, FieldValue value, int fieldId, StoredEntryFile? file = null, int rowIndex = 0, int valueIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (storage == FieldStorageKind.None)
        {
            throw new ArgumentException("A field whose parts hold its values stores none of its own: its grid rows store theirs as text.", nameof(storage));
        }

        if (!Holds(storage, value))
        {
            // The value is a visitor's answer, so the message leaves it out: messages end up in logs.
            throw new ArgumentException("The storage kind does not take the value: it is not in the kind's form.", nameof(value));
        }

        if ((storage == FieldStorageKind.File) != (file is not null))
        {
            throw new ArgumentException("A file value, and only a file value, comes with the entry's file its upload became.", nameof(file));
        }

        var stored = Stored(value);
        var row = new NewEntryValue(fieldId, stored, RowIndex: rowIndex, ValueIndex: valueIndex);
        return storage switch
        {
            FieldStorageKind.Number => row with { Number = StoredNumber(stored) },
            FieldStorageKind.Date => row with { Date = StoredDate(stored) },
            FieldStorageKind.Bool => row with { Bool = stored == True },
            FieldStorageKind.File => row with { Text = file!.Key.ToString("D", CultureInfo.InvariantCulture), FileId = file.Id },
            FieldStorageKind.Json => JsonRow(row, stored),
            _ => row,
        };
    }

    private static string Stored(FieldValue value) => value.StoredText ?? value.Text;

    // Fifteen digits survive a double both ways (ADR-0003); a longer number, such as a calculation's result, is text only
    // so that no reader takes a rounded column for the value.
    private static double? StoredNumber(string text) =>
        ExactDecimal.Parse(text) is { } number && ExactDecimal.Fits(number)
            ? double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
            : null;

    // The wall-clock value as it is, written as UTC (ADR-0003); a value outside what both databases store is text only.
    private static DateTime? StoredDate(string text)
    {
        var value = CalendarValues.ParseDate(text) is { } day
            ? DateOnly.FromDayNumber(day).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : new DateTime(CalendarValues.ParseDateTime(text)!.Value * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        return FormBuilderDbTime.IsStorable(new DateTimeOffset(value)) ? value : null;
    }

    private static NewEntryValue JsonRow(NewEntryValue row, string stored)
    {
        var parts = FieldPartsJson.Read(stored)!;
        return row with { Text = FieldPartsJson.Write(parts), KeyText = FieldPartsJson.Joined(parts) };
    }
}
