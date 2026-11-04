using System.Collections.Immutable;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What changing a field from one type to another keeps and loses (ADR-0059), worked out from the two types'
/// declarations and settings schemas by <see cref="FieldTypeChanges"/>. The field keeps its key, alias, label, help and
/// everything else both types share.
/// </summary>
internal sealed record FieldTypeChange
{
    /// <summary>The alias of the type the field has.</summary>
    public required string From { get; init; }

    /// <summary>The alias of the type it becomes.</summary>
    public required string To { get; init; }

    /// <summary>The settings the target has with the same name and kind, whose rules take every value the source's take.</summary>
    public ImmutableArray<string> KeptSettings { get; init; } = [];

    /// <summary>
    /// The settings of the same name and kind whose rules in the target take fewer values: a value outside them is not
    /// carried over.
    /// </summary>
    public ImmutableArray<string> NarrowedSettings { get; init; } = [];

    /// <summary>The source's settings the target does not have, or has with another kind.</summary>
    public ImmutableArray<string> LostSettings { get; init; } = [];

    /// <summary>The target's required settings the change does not carry over: the field needs them before it publishes.</summary>
    public ImmutableArray<string> NeededSettings { get; init; } = [];

    /// <summary>
    /// The settings the change would carry over that the host locks on the target (ADR-0060), the kept ones first: the
    /// lock sets them, so they are neither carried over nor needed. Empty until <see cref="FieldTypeChanges.UnderLocks"/>.
    /// </summary>
    public ImmutableArray<string> LockedSettings { get; init; } = [];

    /// <summary>The common properties the source supports and the target does not.</summary>
    public FieldFeatures LostProperties { get; init; }

    /// <summary>Whether the target cannot be answered without options and the source has none.</summary>
    public bool NeedsOptions { get; init; }

    /// <summary>The error kinds the source declares and the target does not: the field's own messages for them go.</summary>
    public FieldMessageKinds LostMessages { get; init; }

    /// <summary>
    /// The operators the source's rule kind takes and the target's does not, in ordinal order: every one the source takes
    /// when the target has no rule kind. A condition on the field with one of them no longer publishes.
    /// </summary>
    public ImmutableArray<string> LostOperators { get; init; } = [];

    /// <summary>Whether both have a rule kind and they differ, so the operators that stay compare answers as the target's kind.</summary>
    public bool ComparesAnew { get; init; }

    /// <summary>
    /// Whether both store in the same columns (<c>text</c> and <c>longText</c> share them), so the entries screen filters
    /// and sorts older answers as before; otherwise older answers keep the columns they were stored in.
    /// </summary>
    public bool KeepsColumns { get; init; }
}
