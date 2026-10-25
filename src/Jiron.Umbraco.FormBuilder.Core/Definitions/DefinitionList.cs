using System.Collections;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// Makes the lists of the definition model (ADR-0034). Every list a model record, a value type or the public projection
/// holds is a <see cref="DefinitionList{T}"/>, whoever built the record: its <c>init</c> accessor copies what it is
/// given.
/// </summary>
internal static class DefinitionList
{
    /// <summary>The items as a list nobody can change: the list itself when it is one already, otherwise a copy.</summary>
    /// <exception cref="ArgumentNullException">The items are null.</exception>
    /// <exception cref="ArgumentException">An item is null: a list of the model holds none.</exception>
    public static DefinitionList<T> Of<T>(IEnumerable<T> items) => DefinitionList<T>.Of(items);
}

/// <summary>
/// A list of the definition model. It is read-only, and no cast changes it: it implements no collection interface that
/// adds, removes or replaces items. It holds no null item. It equals another when both hold equal items in the same
/// order, so a record that holds lists compares by value, as records of values do (ADR-0034).
/// </summary>
/// <typeparam name="T">The items' type.</typeparam>
internal sealed class DefinitionList<T> : IReadOnlyList<T>, IEquatable<DefinitionList<T>>
{
    /// <summary>The empty list, which every record starts with.</summary>
    public static readonly DefinitionList<T> Empty = new([]);

    private readonly T[] _items;

    // Takes the array as its own: only Of creates a list, from an array nothing else holds.
    private DefinitionList(T[] items) => _items = items;

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <summary>The items, for a loop that allocates nothing.</summary>
    public ReadOnlySpan<T> AsSpan() => _items;

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Equals(DefinitionList<T>? other) => other is not null && (ReferenceEquals(this, other) || _items.SequenceEqual(other._items));

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DefinitionList<T>);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <summary>The items as a list nobody can change: the list itself when it is one already, otherwise a copy.</summary>
    /// <exception cref="ArgumentNullException">The items are null.</exception>
    /// <exception cref="ArgumentException">An item is null: a list of the model holds none.</exception>
    internal static DefinitionList<T> Of(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items is DefinitionList<T> list)
        {
            return list;
        }

        T[] copy = [.. items];
        foreach (var item in copy)
        {
            if (item is null)
            {
                throw new ArgumentException($"A list of the definition holds no null; one of its {typeof(T).Name} items is null.", nameof(items));
            }
        }

        return copy.Length == 0 ? Empty : new DefinitionList<T>(copy);
    }
}
