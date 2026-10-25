namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The usual way to write a field type (section 4.9, ADR-0055): a declaration and a settings class, whose properties
/// with <see cref="FormBuilderSettingAttribute"/> become the type's settings schema, with the defaults a new instance holds.
/// </summary>
/// <typeparam name="TSettings">The type's settings class; <see cref="NoFieldSettings"/> for a type without settings.</typeparam>
public abstract class FormBuilderFieldTypeBase<TSettings> : IFormBuilderFieldType
    where TSettings : class, new()
{
    /// <summary>A type with its declaration, and the schema of <typeparamref name="TSettings"/>.</summary>
    /// <param name="declaration">What the type is.</param>
    /// <exception cref="InvalidOperationException"><typeparamref name="TSettings"/> cannot be a settings class; the message names the type and every reason.</exception>
    protected FormBuilderFieldTypeBase(FieldTypeDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        Declaration = declaration;
        try
        {
            SettingsSchema = FieldSettingsReader.Read(typeof(TSettings));
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"The settings of the field type '{declaration.Alias}' ({GetType().FullName}) cannot be read: {exception.Message}", exception);
        }
    }

    /// <inheritdoc />
    public FieldTypeDeclaration Declaration { get; }

    /// <inheritdoc />
    public FieldSettingsSchema SettingsSchema { get; }
}

/// <summary>The settings class of a field type without settings, such as a divider.</summary>
public sealed class NoFieldSettings;
