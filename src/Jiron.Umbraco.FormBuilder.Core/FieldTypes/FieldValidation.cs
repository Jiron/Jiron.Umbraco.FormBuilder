using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Numerics;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type's check of a shown field's values reports to (ADR-0058): each error by its kind, whose message the
/// visitor sees at the field. It runs only on a field that has values once the rules leave it visible; a field without
/// values fails with <c>required</c> before it, when it is required. Besides the field and its answers, it says whether
/// the field is required, what day it is where the site is, and which files a file field's upload tokens stand for
/// (ADR-0062).
/// </summary>
public sealed class FieldValidation
{
    // Each error kind on its own: a value the enum names that is one flag, so neither none, nor a combination (even one
    // the enum might name some day), nor a flag it does not name.
    private static readonly FrozenSet<FieldMessageKinds> Kinds = Enum.GetValues<FieldMessageKinds>().Where(kind => BitOperations.IsPow2((uint)kind)).ToFrozenSet();

    private readonly FieldMessageKinds _declared;
    private readonly List<FieldMessageKinds> _errors = [];

    internal FieldValidation(FormField field, FieldAnswers answers, ImmutableArray<FieldValue> values, FieldMessageKinds declared, FieldCheckInput input)
    {
        Field = field;
        Answers = answers;
        Values = values;
        _declared = declared;
        IsRequired = input.IsRequired;
        Today = input.Today;
        Files = input.Files;
    }

    /// <summary>The field, with its settings, options and rows.</summary>
    public FormField Field { get; }

    /// <summary>What the visitor gave, such as an email's confirmation, which the values leave out.</summary>
    public FieldAnswers Answers { get; }

    /// <summary>The values the type read.</summary>
    public ImmutableArray<FieldValue> Values { get; }

    /// <summary>
    /// Whether the field is required, by its own setting or by a rule: a required name with no required parts of its own
    /// needs every part it asks for (ADR-0056).
    /// </summary>
    public bool IsRequired { get; }

    /// <summary>The date in the site's time zone, from which a date window relative to today counts (ADR-0005).</summary>
    public DateOnly Today { get; }

    /// <summary>
    /// For a field that stores files, the file each value's upload token stands for, in the values' order; none for any
    /// other field.
    /// </summary>
    public ImmutableArray<FieldFile> Files { get; }

    /// <summary>The errors reported, in order, each once.</summary>
    public ImmutableArray<FieldMessageKinds> Errors => [.. _errors];

    /// <summary>Reports an error.</summary>
    /// <param name="kind">
    /// One error kind: <see cref="FieldMessageKinds.Invalid"/>, which every type may raise, or one the type's declaration
    /// lists in <see cref="FieldTypeDeclaration.Messages"/>.
    /// </param>
    /// <exception cref="ArgumentException">The kind is not exactly one kind, or the type does not declare it.</exception>
    public void Fail(FieldMessageKinds kind)
    {
        if (!Kinds.Contains(kind))
        {
            throw new ArgumentException($"'{kind}' is not exactly one error kind.", nameof(kind));
        }

        if (kind != FieldMessageKinds.Invalid && !_declared.HasFlag(kind))
        {
            throw new ArgumentException($"The field type does not declare the error kind '{kind}'.", nameof(kind));
        }

        if (!_errors.Contains(kind))
        {
            _errors.Add(kind);
        }
    }
}
