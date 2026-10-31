using Umbraco.Cms.Core.PropertyEditors;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The form picker (ADR-0017, ADR-0127): a property that stores the key of the form a page picks, as text. Its value
/// converters give the Razor views a <see cref="Rendering.FormBuilderFormReference"/> and the Content Delivery API the
/// form's key, alias and name, or the form itself when the property is expanded. 6.7 adds its settings and its
/// backoffice picker.
/// </summary>
/// <param name="dataValueEditorFactory">Umbraco's factory of value editors.</param>
[DataEditor(EditorAlias, ValueType = ValueTypes.String, ValueEditorIsReusable = true)]
public sealed class FormPickerPropertyEditor(IDataValueEditorFactory dataValueEditorFactory) : DataEditor(dataValueEditorFactory)
{
    /// <summary>The property editor's alias.</summary>
    public const string EditorAlias = "Jiron.FormBuilder.FormPicker";
}
