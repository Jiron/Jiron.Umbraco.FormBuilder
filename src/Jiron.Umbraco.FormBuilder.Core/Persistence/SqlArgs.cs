using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// The arguments of one statement, numbered <c>@0</c>, <c>@1</c> and so on in the order they are added, so every
/// placeholder has its value and no value goes unused: NPoco binds them by their numbers (ADR-0036).
/// </summary>
internal sealed class SqlArgs
{
    /// <summary>
    /// The most values one <c>IN</c> list names: well inside the 2,100 parameters SQL Server takes in a statement, with
    /// room for the statement's own.
    /// </summary>
    public const int MaxListLength = 1000;

    private readonly List<object?> _values = [];

    /// <summary>How many values the statement has.</summary>
    public int Count => _values.Count;

    /// <summary>The values, in the order of their numbers.</summary>
    public object?[] Values => [.. _values];

    /// <summary>Adds a value and returns its placeholder.</summary>
    public string Add(object? value)
    {
        _values.Add(value);
        return Placeholder(_values.Count - 1);
    }

    /// <summary>Adds the values and returns their placeholders, joined by commas, for an <c>IN</c> list.</summary>
    public string List(IEnumerable<object?> values) => string.Join(", ", values.Select(Add));

    /// <summary>
    /// The placeholders <c>@first</c> to <c>@(first + count - 1)</c>, joined by commas, for a statement whose values are
    /// passed as an array in that order.
    /// </summary>
    public static string Placeholders(int first, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(first);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return string.Join(", ", Enumerable.Range(first, count).Select(Placeholder));
    }

    private static string Placeholder(int index) => "@" + index.ToString(CultureInfo.InvariantCulture);
}
