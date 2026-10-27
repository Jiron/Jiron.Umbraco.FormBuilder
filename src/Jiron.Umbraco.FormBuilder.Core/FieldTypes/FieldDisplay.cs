using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type shows a field's values as (ADR-0066): the texts of <c>{{ field.alias }}</c> and of the list of
/// the answers, in the culture the template renders in. A number or a date is shown in the culture's format with its
/// wire form behind it, so the <c>number</c>, <c>currency</c> and <c>date</c> filters can format it again; an option by
/// its label, from a list or another source the label it resolves (ADR-0077); a file by its name.
/// </summary>
public sealed class FieldDisplay
{
    private readonly CultureFormats _formats;
    private readonly DictionaryTexts _dictionary;
    private readonly IReadOnlyDictionary<string, string>? _optionLabels;
    private readonly List<DisplayText> _texts = [];

    internal FieldDisplay(
        FormField field,
        ImmutableArray<FieldValue> values,
        ImmutableArray<FieldFile> files,
        string culture,
        CultureFormats formats,
        bool isListing,
        DictionaryTexts? dictionary = null,
        IReadOnlyDictionary<string, string>? optionLabels = null)
    {
        if (!files.IsEmpty && files.Length != values.Length)
        {
            throw new ArgumentException("A field's files are one for each of its values, or none.", nameof(files));
        }

        Field = field;
        Values = values;
        Files = files;
        Culture = culture;
        IsListing = isListing;
        _formats = formats;
        _dictionary = dictionary ?? DictionaryTexts.None;
        _optionLabels = optionLabels;
    }

    /// <summary>The field, with its settings, options and rows.</summary>
    public FormField Field { get; }

    /// <summary>The field's values in the wire form, as its type read them (ADR-0058).</summary>
    public ImmutableArray<FieldValue> Values { get; }

    /// <summary>For a field that stores files, the file each value stands for, one for each value; otherwise none.</summary>
    public ImmutableArray<FieldFile> Files { get; }

    /// <summary>The culture the texts are shown in, a name such as <c>de-CH</c>; empty for the invariant culture.</summary>
    public string Culture { get; }

    /// <summary>
    /// Whether the texts are for the list of the answers (<c>entry.answers</c>), which leaves out a field whose value
    /// the visitor never saw (a hidden field, a calculation that is not shown): a type shows nothing for such a field
    /// then. A tag that names the field shows its value either way.
    /// </summary>
    public bool IsListing { get; }

    /// <summary>The word for a ticked box or a yes, in the culture.</summary>
    public string Yes => FieldDisplayWords.Yes(Culture);

    /// <summary>The word for a no, in the culture.</summary>
    public string No => FieldDisplayWords.No(Culture);

    /// <summary>The texts shown so far, in order.</summary>
    internal IReadOnlyList<DisplayText> Texts => _texts;

    /// <summary>Shows a text; an empty one shows nothing.</summary>
    /// <param name="text">The text, as the visitor reads it.</param>
    public void Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0)
        {
            _texts.Add(new DisplayText(text, null, null));
        }
    }

    /// <summary>Shows a text with the number it stands for behind it, which the <c>number</c> and <c>currency</c> filters read.</summary>
    /// <param name="text">The text, such as <c>1'234.50 kg</c>.</param>
    /// <param name="number">The number in the wire form of ADR-0005.</param>
    public void AddNumber(string text, string number)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(number);
        if (text.Length > 0)
        {
            _texts.Add(new DisplayText(text, number, null));
        }
    }

    /// <summary>Shows a text with the date, time or date-time it stands for behind it, which the <c>date</c> filter reads.</summary>
    /// <param name="text">The text, such as <c>07.03.2026</c>.</param>
    /// <param name="date">The date, time or date-time in the wire form of ADR-0005.</param>
    public void AddDate(string text, string date)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(date);
        if (text.Length > 0)
        {
            _texts.Add(new DisplayText(text, null, date));
        }
    }

    /// <summary>
    /// An editor's text in the culture: its own, its more general culture's, then the default; for a text that names a
    /// dictionary item (<c>#key</c>, ADR-0002), the item's text in the culture; empty for none.
    /// </summary>
    /// <param name="text">The text, such as a label.</param>
    public string Text(LocalizedText? text) => _dictionary.Resolve(text, Culture);

    /// <summary>
    /// A number in the wire form as the culture writes it: grouped, with its own decimals, or rounded half away from zero
    /// and padded to <paramref name="decimals"/>; as it is when it is no number in the wire form.
    /// </summary>
    /// <param name="number">The number in the wire form.</param>
    /// <param name="decimals">The places after the point, or null for the number's own.</param>
    public string Number(string number, int? decimals = null)
    {
        ArgumentNullException.ThrowIfNull(number);
        return CultureFormatting.FormatNumber(number, decimals is { } places ? Math.Clamp(places, 0, 15) : null, _formats) ?? number;
    }

    /// <summary>
    /// A date, time or date-time in the wire form as the culture writes it: a date in its short format, a time in its
    /// time format and both in its date-and-time format; as it is when it is none of them.
    /// </summary>
    /// <param name="date">The date, time or date-time in the wire form.</param>
    public string Date(string date)
    {
        ArgumentNullException.ThrowIfNull(date);
        var format = date.Length > 10 && date[10] == 'T' ? "dateTime" : date.Contains(':', StringComparison.Ordinal) ? "time" : "short";
        return CultureFormatting.FormatCalendar(date, format, _formats) ?? date;
    }

    /// <summary>
    /// The label of the field's option with this value, in the culture: its own option's, or the one its list or other
    /// source resolves (ADR-0077); the value itself when the field has no such option, such as a visitor's "Other" text,
    /// or no label for it, and when its source cannot give its options.
    /// </summary>
    /// <param name="value">The option's value.</param>
    public string OptionLabel(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Field.Options is { Source: not OptionSource.Items })
        {
            return _optionLabels is not null && _optionLabels.TryGetValue(value, out var resolved) ? resolved : value;
        }

        var option = Field.Options?.Items.FirstOrDefault(item => string.Equals(item.Value, value, StringComparison.Ordinal));
        return option?.Label is { } label && Text(label) is { Length: > 0 } text ? text : value;
    }

    /// <summary>The name of a country by its ISO 3166 alpha-2 code, in the culture's language; the code when .NET has no name for it.</summary>
    /// <param name="code">The code, such as <c>CH</c>.</param>
    public string CountryName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return CountryNames.Name(code, Culture);
    }

    /// <summary>A text shown, with the wire number or date behind it.</summary>
    internal sealed record DisplayText(string Text, string? Number, string? Date);
}
