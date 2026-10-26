using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// What a field type's check of an upload reports to (ADR-0071): the file's name and its first bytes, which the upload
/// endpoint reads before it stores the file (ADR-0006), and each error by its kind, which refuses the upload with that
/// kind's message. It runs only for a field whose type stores files; the field's limit holds the file's size apart.
/// </summary>
public sealed class FieldUploadCheck
{
    /// <summary>
    /// The most first bytes of a file a check sees: enough for every kind of file to name itself, such as a tar archive,
    /// which does so at byte 257.
    /// </summary>
    public const int HeadLength = 512;

    private readonly FieldErrorList _errors;

    internal FieldUploadCheck(FormField field, string name, ReadOnlyMemory<byte> head, FieldMessageKinds declared)
    {
        Field = field;
        Name = name;
        Head = head;
        _errors = new FieldErrorList(declared);
    }

    /// <summary>The field, with its settings.</summary>
    public FormField Field { get; }

    /// <summary>The file's name as the visitor's device gave it, such as <c>plan.pdf</c>.</summary>
    public string Name { get; }

    /// <summary>The file's first bytes: <see cref="HeadLength"/> of them, or all of a shorter file.</summary>
    public ReadOnlyMemory<byte> Head { get; }

    /// <summary>The errors reported, in order, each once.</summary>
    public ImmutableArray<FieldMessageKinds> Errors => _errors.Errors;

    /// <summary>Reports an error, which refuses the upload.</summary>
    /// <param name="kind">
    /// One error kind: <see cref="FieldMessageKinds.Invalid"/>, which every type may raise, or one the type's declaration
    /// lists in <see cref="FieldTypeDeclaration.Messages"/>.
    /// </param>
    /// <exception cref="ArgumentException">The kind is not exactly one kind, or the type does not declare it.</exception>
    public void Fail(FieldMessageKinds kind) => _errors.Fail(kind);
}
