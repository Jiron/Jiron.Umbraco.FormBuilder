namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// A value a field type read from a visitor's answers (ADR-0058): a text in the wire form of ADR-0005, which rules
/// compare and the entry stores, the field's storage kind deciding its typed column; for a grid, the row it answers;
/// and, where the entry stores another text than the one rules compare, that text.
/// </summary>
public sealed record FieldValue
{
    /// <summary>A value of the field itself.</summary>
    /// <param name="text">The value in the wire form.</param>
    /// <exception cref="ArgumentException">The text is null or empty.</exception>
    public FieldValue(string text)
        : this(text, null)
    {
    }

    /// <summary>A value of the field, or of one of its rows.</summary>
    /// <param name="text">The value in the wire form.</param>
    /// <param name="row">The key of the grid row it answers; null for the field itself.</param>
    /// <exception cref="ArgumentException">The text is null or empty, or the row's key is empty.</exception>
    public FieldValue(string text, Guid? row)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        if (row == Guid.Empty)
        {
            throw new ArgumentException("A row's key is never empty.", nameof(row));
        }

        Text = text;
        Row = row;
    }

    /// <summary>The value in the wire form.</summary>
    public string Text { get; }

    /// <summary>The key of the grid row it answers; null for the field itself.</summary>
    public Guid? Row { get; }

    /// <summary>
    /// The text the entry stores instead of <see cref="Text"/>, or null to store the value: a ticked consent is
    /// <c>true</c> to rules and stores the hash of the wording the visitor agreed to (ADR-0009).
    /// </summary>
    /// <exception cref="ArgumentException">The text is empty.</exception>
    public string? StoredText
    {
        get;
        init
        {
            if (value is not null)
            {
                ArgumentException.ThrowIfNullOrEmpty(value);
            }

            field = value;
        }
    }
}
