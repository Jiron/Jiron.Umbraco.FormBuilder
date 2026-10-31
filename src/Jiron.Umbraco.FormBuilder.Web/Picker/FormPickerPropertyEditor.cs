using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The form picker (ADR-0017, ADR-0127, ADR-0129): a property that stores the keys of the forms a page picks, as text,
/// with the settings of its data type (<see cref="FormPickerConfiguration"/>): one form or several, the folders and
/// forms it may pick, and a theme and a page after a send for every form it places. Its value converters give the
/// Razor views a <see cref="Rendering.FormBuilderFormReference"/> (or a list of them) and the Content Delivery API the
/// forms' key, alias and name, or the forms themselves when the property is expanded. 6.7e adds its backoffice screens.
/// </summary>
/// <param name="dataValueEditorFactory">Umbraco's factory of value editors.</param>
/// <param name="services">The site's services, which the editors take theirs from.</param>
[DataEditor(EditorAlias, ValueType = ValueTypes.String, ValueEditorIsReusable = true)]
public sealed class FormPickerPropertyEditor(IDataValueEditorFactory dataValueEditorFactory, IServiceProvider services) : DataEditor(dataValueEditorFactory)
{
    /// <summary>The property editor's alias.</summary>
    public const string EditorAlias = "Jiron.FormBuilder.FormPicker";

    /// <inheritdoc />
    protected override IConfigurationEditor CreateConfigurationEditor() => ActivatorUtilities.CreateInstance<FormPickerConfigurationEditor>(services);

    /// <inheritdoc />
    protected override IDataValueEditor CreateValueEditor() => DataValueEditorFactory.Create<FormPickerValueEditor>(Attribute!);
}
