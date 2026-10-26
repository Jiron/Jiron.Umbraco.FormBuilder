using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type's check of a field's rows reports to (ADR-0071): how many rows a submission gives a visible field
/// whose type has child fields, a repeatable group, and each error by its kind, whose message the visitor sees at the
/// field. The child fields' own checks run row by row, apart from it.
/// </summary>
public sealed class FieldRowsValidation
{
    private readonly FieldErrorList _errors;

    internal FieldRowsValidation(FormField field, int rows, FieldMessageKinds declared)
    {
        Field = field;
        Rows = rows;
        _errors = new FieldErrorList(declared);
    }

    /// <summary>The field, with its settings and its child fields.</summary>
    public FormField Field { get; }

    /// <summary>The number of rows the submission gives the field, none or more.</summary>
    public int Rows { get; }

    /// <summary>The errors reported, in order, each once.</summary>
    public ImmutableArray<FieldMessageKinds> Errors => _errors.Errors;

    /// <summary>Reports an error.</summary>
    /// <param name="kind">
    /// One error kind: <see cref="FieldMessageKinds.Invalid"/>, which every type may raise, or one the type's declaration
    /// lists in <see cref="FieldTypeDeclaration.Messages"/>, such as <see cref="FieldMessageKinds.RowCount"/>.
    /// </param>
    /// <exception cref="ArgumentException">The kind is not exactly one kind, or the type does not declare it.</exception>
    public void Fail(FieldMessageKinds kind) => _errors.Fail(kind);
}
