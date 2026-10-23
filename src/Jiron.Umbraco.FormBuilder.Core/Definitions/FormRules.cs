using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// A rule (section 4.3.3, ADR-0002): its effect applies while its conditions match. An element holds at most one rule
/// per effect, and <see cref="RuleEffect.Show"/> and <see cref="RuleEffect.Hide"/> exclude each other. A switched-off
/// rule is kept but ignored by both runtimes.
/// </summary>
public sealed record FormRule
{
    /// <summary>The rule's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>What the rule does while it matches.</summary>
    public required RuleEffect Effect { get; init; }

    /// <summary>Whether the rule applies; a switched-off rule is kept, so an editor can try the form without it.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Whether all conditions must match or one is enough.</summary>
    public RuleMatch Match { get; init; } = RuleMatch.All;

    /// <summary>The conditions and nested groups.</summary>
    public IReadOnlyList<RuleItem> Conditions { get; init; } = [];
}

/// <summary>A condition or a nested group of conditions.</summary>
[JsonConverter(typeof(RuleItemConverter))]
public abstract record RuleItem
{
    private protected RuleItem()
    {
    }
}

/// <summary>
/// A condition: an answer (<see cref="Field"/>) or the context (<see cref="Context"/>) compared by an operator. Its
/// operators, operands and context subjects are those of section 4.3.3 and ADR-0005; publishing checks them, and a
/// draft keeps what fails the check.
/// </summary>
public sealed record RuleCondition : RuleItem
{
    /// <summary>The key of the field whose answers the condition looks at.</summary>
    public Guid? Field { get; init; }

    /// <summary>
    /// The context the condition looks at instead of an answer: <c>culture</c>, <c>memberSignedIn</c>,
    /// <c>memberGroup</c>, <c>today</c> or <c>query.</c> and an allowed query value's name.
    /// </summary>
    public string? Context { get; init; }

    /// <summary>The operator, for example <c>equals</c> or <c>isOneOf</c>.</summary>
    public required string Op { get; init; }

    /// <summary>The operand, absent for operators that take none.</summary>
    public ConditionOperand? Value { get; init; }
}

/// <summary>A nested group: conditions of which all must match, or one.</summary>
public sealed record RuleGroup : RuleItem
{
    /// <summary>Whether all conditions must match or one is enough.</summary>
    public RuleMatch Match { get; init; } = RuleMatch.All;

    /// <summary>The conditions and further nested groups.</summary>
    public IReadOnlyList<RuleItem> Conditions { get; init; } = [];
}

/// <summary>
/// A condition's operand (ADR-0002): a text (<c>"x"</c>), another answer (<c>{ "field": key }</c>), a date relative to
/// today (<c>{ "today": "+2d" }</c>), or a list of those (<c>["x", { "field": key }]</c>).
/// </summary>
[JsonConverter(typeof(ConditionOperandConverter))]
public abstract record ConditionOperand
{
    private protected ConditionOperand()
    {
    }

    /// <summary>A text, compared as the condition's field compares (ADR-0005).</summary>
    /// <param name="Value">The text.</param>
    public sealed record Text(string Value) : ConditionOperand;

    /// <summary>Another field's answers, for example "departure after arrival".</summary>
    /// <param name="Key">The other field's key.</param>
    public sealed record Field(Guid Key) : ConditionOperand;

    /// <summary>A date relative to today in the site's time zone, such as <c>+2d</c> or <c>-1m+1d</c>.</summary>
    /// <param name="Offset">The offset; empty for today itself.</param>
    public sealed record Today(string Offset) : ConditionOperand;

    /// <summary>A list of texts, answers and dates, for <c>isOneOf</c>, <c>isNoneOf</c> and <c>between</c>.</summary>
    public sealed record List : ConditionOperand
    {
        /// <summary>A list of the items given.</summary>
        /// <param name="items">At least one item, and no list among them.</param>
        /// <exception cref="ArgumentException">The list is empty, or holds a list or null.</exception>
        public List(IReadOnlyList<ConditionOperand> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            if (items.Count == 0)
            {
                throw new ArgumentException("A list operand has at least one item.", nameof(items));
            }

            if (items.Any(item => item is null or List))
            {
                throw new ArgumentException("A list operand holds texts, answers and dates, not lists.", nameof(items));
            }

            Items = [.. items];
        }

        /// <summary>The items, at least one; none is a list.</summary>
        public IReadOnlyList<ConditionOperand> Items { get; }

        /// <inheritdoc />
        public bool Equals(List? other) => other is not null && Items.SequenceEqual(other.Items);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = default(HashCode);
            foreach (var item in Items)
            {
                hash.Add(item);
            }

            return hash.ToHashCode();
        }
    }
}

/// <summary>
/// A form-level validation rule (ADR-0002): while its conditions match, <see cref="Field"/> shows
/// <see cref="Message"/> as an error, for checks across fields.
/// </summary>
public sealed record FormValidationRule
{
    /// <summary>The rule's stable key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Whether the rule applies.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The key of the field that shows the error.</summary>
    public required Guid Field { get; init; }

    /// <summary>The error message.</summary>
    public LocalizedText? Message { get; init; }

    /// <summary>Whether all conditions must match or one is enough.</summary>
    public RuleMatch Match { get; init; } = RuleMatch.All;

    /// <summary>The conditions and nested groups.</summary>
    public IReadOnlyList<RuleItem> Conditions { get; init; } = [];
}
