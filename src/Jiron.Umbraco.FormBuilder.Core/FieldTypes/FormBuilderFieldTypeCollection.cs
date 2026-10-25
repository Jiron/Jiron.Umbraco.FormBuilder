using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>The field types a site registered, in the order the palette shows them (ADR-0055).</summary>
public sealed class FormBuilderFieldTypeCollection : BuilderCollectionBase<IFormBuilderFieldType>
{
    /// <summary>A collection of the types the builder creates.</summary>
    public FormBuilderFieldTypeCollection(Func<IEnumerable<IFormBuilderFieldType>> items)
        : base(items)
    {
    }
}

/// <summary>
/// The ordered collection of field types (ADR-0055), reached through <c>builder.JironFormBuilder().FieldTypes()</c>. It
/// holds the built-in types of section 4.3.2 from the moment it is made, so a composer adds, inserts, replaces or removes
/// a type whether it runs before Jiron Form Builder's own composer or after it, for example
/// <c>.Append&lt;ColourFieldType&gt;()</c>, <c>.Replace&lt;TextFieldType, MyTextFieldType&gt;()</c> or
/// <c>.Remove&lt;SignatureFieldType&gt;()</c>. Each type is a singleton, created by the container.
/// </summary>
public sealed class FormBuilderFieldTypeCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderFieldTypeCollectionBuilder, FormBuilderFieldTypeCollection, IFormBuilderFieldType>
{
    /// <summary>A builder that holds the built-in types.</summary>
    public FormBuilderFieldTypeCollectionBuilder() => Append(BuiltInFieldTypes.All);

    /// <inheritdoc />
    protected override FormBuilderFieldTypeCollectionBuilder This => this;
}
