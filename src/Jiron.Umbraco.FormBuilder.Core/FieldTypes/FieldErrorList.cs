using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The errors a field type's check reports (ADR-0058, ADR-0071), the one rule its checks of values, uploads and rows
/// share: each error one kind, <see cref="FieldMessageKinds.Invalid"/> or one the type declares, kept once, in the order
/// reported.
/// </summary>
/// <param name="declared">The error kinds the type's declaration lists.</param>
internal sealed class FieldErrorList(FieldMessageKinds declared)
{
    // Each error kind on its own: a value the enum names that is one flag, so neither none, nor a combination (even one
    // the enum might name some day), nor a flag it does not name.
    private static readonly FrozenSet<FieldMessageKinds> Kinds = Enum.GetValues<FieldMessageKinds>().Where(kind => BitOperations.IsPow2((uint)kind)).ToFrozenSet();

    private readonly List<FieldMessageKinds> _errors = [];

    /// <summary>The errors reported, in order, each once.</summary>
    public ImmutableArray<FieldMessageKinds> Errors => [.. _errors];

    /// <summary>Reports an error.</summary>
    /// <param name="kind">One error kind: <see cref="FieldMessageKinds.Invalid"/>, or one the type declares.</param>
    /// <exception cref="ArgumentException">The kind is not exactly one kind, or the type does not declare it.</exception>
    public void Fail(FieldMessageKinds kind)
    {
        if (!Kinds.Contains(kind))
        {
            throw new ArgumentException($"'{kind}' is not exactly one error kind.", nameof(kind));
        }

        if (kind != FieldMessageKinds.Invalid && !declared.HasFlag(kind))
        {
            throw new ArgumentException($"The field type does not declare the error kind '{kind}'.", nameof(kind));
        }

        if (!_errors.Contains(kind))
        {
            _errors.Add(kind);
        }
    }
}
