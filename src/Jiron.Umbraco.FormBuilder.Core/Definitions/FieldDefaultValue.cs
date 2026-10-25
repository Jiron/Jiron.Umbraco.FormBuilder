using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// A field's default value (ADR-0002): one text, or a list of texts for a type with several answers. Merge tags are
/// allowed (ADR-0014). In JSON it is a string or an array of strings; a definition writes no default as <c>null</c>.
/// </summary>
[JsonConverter(typeof(FieldDefaultValueConverter))]
public sealed class FieldDefaultValue : IEquatable<FieldDefaultValue>
{
    private readonly DefinitionList<string> _values;

    private FieldDefaultValue(DefinitionList<string> values, bool isList)
    {
        _values = values;
        IsList = isList;
    }

    /// <summary>Whether the default is a list (written as a JSON array), even of one value.</summary>
    public bool IsList { get; }

    /// <summary>The value, or the values of a list.</summary>
    public IReadOnlyList<string> Values => _values;

    /// <summary>A default of one text.</summary>
    public static FieldDefaultValue Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new FieldDefaultValue(DefinitionList.Of([value]), isList: false);
    }

    /// <summary>A default of several values, for a type with several answers.</summary>
    /// <exception cref="ArgumentException">The list is empty (use no default instead) or holds a null.</exception>
    public static FieldDefaultValue OfList(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var list = DefinitionList.Of(values);
        if (list.Count == 0 || list.Any(value => value is null))
        {
            throw new ArgumentException("A list default has at least one value and no null.", nameof(values));
        }

        return new FieldDefaultValue(list, isList: true);
    }

    /// <inheritdoc />
    public bool Equals(FieldDefaultValue? other) =>
        other is not null && IsList == other.IsList && _values.SequenceEqual(other._values, StringComparer.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FieldDefaultValue);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(IsList);
        foreach (var value in _values.AsSpan())
        {
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
