namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// A field type (section 4.3.2, section 4.9, ADR-0055): what it is and which settings it has. Types are registered in an
/// ordered collection, <c>builder.JironFormBuilder().FieldTypes()</c>, which holds the built-in ones from its start, so
/// a site or an add-on adds, replaces or removes types there. A type is a singleton: it holds no state of its own and may
/// take singleton services in its constructor. <see cref="FormBuilderFieldTypeBase{TSettings}"/> builds the settings
/// schema from a settings class.
/// </summary>
public interface IFormBuilderFieldType
{
    /// <summary>What the type is: its alias, how the designer shows it, how its values are stored and compared, and what it supports.</summary>
    FieldTypeDeclaration Declaration { get; }

    /// <summary>The type's settings, as the designer's settings editor and the checks read them.</summary>
    FieldSettingsSchema SettingsSchema { get; }
}
