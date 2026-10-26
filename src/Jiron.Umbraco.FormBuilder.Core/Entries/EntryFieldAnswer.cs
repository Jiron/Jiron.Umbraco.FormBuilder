using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// What an entry stores of one field in one row (ADR-0070): the values its type read (ADR-0058) and, for a field that
/// stores files, the upload each value's token names, which the pipeline resolves (6.1).
/// </summary>
internal sealed record EntryFieldAnswer
{
    /// <summary>A field's answer.</summary>
    /// <param name="field">The field's key.</param>
    /// <param name="values">The values its type read, in their order.</param>
    /// <param name="row">The row of a repeatable group's child, from 0; 0 for any other field.</param>
    /// <exception cref="ArgumentException">The field's key is empty, or a value is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The row is negative.</exception>
    public EntryFieldAnswer(Guid field, IEnumerable<FieldValue> values, int row = 0)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        if (field == Guid.Empty)
        {
            throw new ArgumentException("A field's key is never empty.", nameof(field));
        }

        Values = [.. values];
        if (Values.Any(value => value is null))
        {
            throw new ArgumentException("An answer's values are never null.", nameof(values));
        }

        Field = field;
        Row = row;
    }

    /// <summary>The field's key.</summary>
    public Guid Field { get; }

    /// <summary>The values its type read, in their order.</summary>
    public ImmutableArray<FieldValue> Values { get; }

    /// <summary>The row of a repeatable group's child, from 0; 0 for any other field.</summary>
    public int Row { get; }

    /// <summary>For a field that stores files, the upload each value's token names, in the values' order; none otherwise.</summary>
    /// <exception cref="ArgumentException">An upload is null.</exception>
    public ImmutableArray<StoredEntryFile> Files
    {
        get;
        init
        {
            if (value.IsDefault || value.Any(file => file is null))
            {
                throw new ArgumentException("An answer's uploads are never null.", nameof(value));
            }

            field = value;
        }
    } = [];
}
