using System.Collections.Immutable;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The rules a setting's value keeps (ADR-0056), read from .NET's validation attributes on its property:
/// <c>[Required]</c>, <c>[Range]</c>, <c>[StringLength]</c>, <c>[Length]</c> and <c>[AllowedValues]</c>. Publishing holds
/// every field's settings to them, and the designer checks them as the server does. For a list, the bounds, lengths and
/// allowed values apply to each item and the counts to the list. A setting that is not given takes its default, which
/// keeps the rules; only <see cref="Required"/> asks for a value of the field's own. Without an attribute, a text has at
/// most <see cref="TextLength"/> characters (rich text <see cref="HtmlLength"/>), a list at most
/// <see cref="ListCount"/> items, and an <c>int</c> keeps the range of <c>int</c>.
/// </summary>
public sealed record FieldSettingRules
{
    /// <summary>The most characters of a text, or of a localised text in each culture, without <c>[StringLength]</c>.</summary>
    public const int TextLength = 2000;

    /// <summary>The most characters of rich text in each culture without <c>[StringLength]</c>.</summary>
    public const int HtmlLength = 50000;

    /// <summary>The most items of a list without <c>[Length]</c>.</summary>
    public const int ListCount = 1000;

    /// <summary>The rules of a setting without any.</summary>
    public static FieldSettingRules None { get; } = new();

    /// <summary>
    /// Whether a field gives the setting a value of its own (<c>[Required]</c>): a text that is not blank, a localised text
    /// whose default text is not blank, a list with an item, an object or a value of another kind.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>The lowest value, as a whole number or a number in the wire form of ADR-0005 (<c>[Range]</c>); null for none.</summary>
    public string? Minimum { get; init; }

    /// <summary>Whether the value must be above <see cref="Minimum"/> rather than at least it.</summary>
    public bool MinimumIsExclusive { get; init; }

    /// <summary>The highest value, in the form of <see cref="Minimum"/>; null for none.</summary>
    public string? Maximum { get; init; }

    /// <summary>Whether the value must be below <see cref="Maximum"/> rather than at most it.</summary>
    public bool MaximumIsExclusive { get; init; }

    /// <summary>The fewest characters a text has, counted as UTF-16 code units, as browsers count them (<c>[StringLength]</c>).</summary>
    public int? MinimumLength { get; init; }

    /// <summary>The most characters a text has, counted as <see cref="MinimumLength"/> counts them.</summary>
    public int? MaximumLength { get; init; }

    /// <summary>The fewest items a list has (<c>[Length]</c>); a list that is not given counts as empty.</summary>
    public int? MinimumCount { get; init; }

    /// <summary>The most items a list has.</summary>
    public int? MaximumCount { get; init; }

    /// <summary>The texts a text may be (<c>[AllowedValues]</c>), compared exactly; empty for any.</summary>
    public ImmutableArray<string> AllowedValues { get; init; } = [];
}
