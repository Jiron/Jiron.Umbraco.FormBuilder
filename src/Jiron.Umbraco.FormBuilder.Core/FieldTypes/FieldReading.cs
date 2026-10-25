using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type reads a visitor's answers into (ADR-0058): the values in the wire form of ADR-0005, which rules
/// compare and the entry stores, or that the answers cannot be read. The pipeline reads every field before the rules
/// run, and reports an unreadable answer as <c>invalid</c> only once the rules leave the field visible.
/// </summary>
public sealed class FieldReading
{
    private readonly List<FieldValue> _values = [];

    internal FieldReading(FormField field, FieldAnswers answers)
    {
        Field = field;
        Answers = answers;
    }

    /// <summary>The field the answers are for, with its settings, options and rows.</summary>
    public FormField Field { get; }

    /// <summary>What the visitor gave, cleaned, within the type's limits.</summary>
    public FieldAnswers Answers { get; }

    /// <summary>Whether the answers cannot be read; the values read so far then count for nothing.</summary>
    public bool IsInvalid { get; private set; }

    /// <summary>The values read, in order; none once the answers cannot be read.</summary>
    public ImmutableArray<FieldValue> Values => IsInvalid ? [] : [.. _values];

    /// <summary>Adds a value of the field.</summary>
    /// <param name="text">The value in the wire form.</param>
    /// <exception cref="ArgumentException">The text is null or empty.</exception>
    public void Add(string text) => _values.Add(new FieldValue(text));

    /// <summary>Adds a value of one of the field's grid rows.</summary>
    /// <param name="text">The value in the wire form.</param>
    /// <param name="row">The row's key, one of <see cref="FormField.Rows"/>.</param>
    /// <exception cref="ArgumentException">The text is null or empty, or the field has no row with that key.</exception>
    public void Add(string text, Guid row) => Add(new FieldValue(text, row));

    /// <summary>Adds a value, such as one that stores another text than the one rules compare.</summary>
    /// <param name="value">The value; its row, if it has one, is one of <see cref="FormField.Rows"/>.</param>
    /// <exception cref="ArgumentException">The field has no row with the value's key.</exception>
    public void Add(FieldValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Row is { } row && !Field.Rows.Any(each => each.Key == row))
        {
            throw new ArgumentException($"The field has no row '{row}'.", nameof(value));
        }

        _values.Add(value);
    }

    /// <summary>The answers cannot be read, such as a date that is no date: the field fails with <c>invalid</c> while it is shown.</summary>
    public void Invalid() => IsInvalid = true;
}
