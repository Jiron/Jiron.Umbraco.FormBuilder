using System.Collections.Immutable;
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
    private readonly FieldErrorList _errors;

    internal FieldValidation(FormField field, FieldAnswers answers, ImmutableArray<FieldValue> values, FieldMessageKinds declared, FieldCheckInput input)
    {
        Field = field;
        Answers = answers;
        Values = values;
        _errors = new FieldErrorList(declared);
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
    public ImmutableArray<FieldMessageKinds> Errors => _errors.Errors;

    /// <summary>Reports an error.</summary>
    /// <param name="kind">
    /// One error kind: <see cref="FieldMessageKinds.Invalid"/>, which every type may raise, or one the type's declaration
    /// lists in <see cref="FieldTypeDeclaration.Messages"/>.
    /// </param>
    /// <exception cref="ArgumentException">The kind is not exactly one kind, or the type does not declare it.</exception>
    public void Fail(FieldMessageKinds kind) => _errors.Fail(kind);
}
