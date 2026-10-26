using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The usual way to write a field type (section 4.9, ADR-0055): a declaration and a settings class, whose properties
/// with <see cref="FormBuilderSettingAttribute"/> become the type's settings schema, with the defaults a new instance holds.
/// </summary>
/// <typeparam name="TSettings">The type's settings class; <see cref="NoFieldSettings"/> for a type without settings.</typeparam>
public abstract class FormBuilderFieldTypeBase<TSettings> : IFormBuilderFieldType
    where TSettings : class, new()
{
    private readonly JsonTypeInfo<TSettings> _settingsInfo;

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

        _settingsInfo = (JsonTypeInfo<TSettings>)FieldSettingsJson.Options.GetTypeInfo(typeof(TSettings));
    }

    /// <inheritdoc />
    public FieldTypeDeclaration Declaration { get; }

    /// <inheritdoc />
    public FieldSettingsSchema SettingsSchema { get; }

    /// <summary>A field's settings as <typeparamref name="TSettings"/>; a new instance, with its defaults, for none.</summary>
    /// <param name="settings">The field's settings, which keep the type's schema.</param>
    /// <exception cref="JsonException">The settings do not fit the class; publishing holds them to the schema first.</exception>
    public TSettings ReadSettings(SettingsObject? settings) => settings?.Bind(_settingsInfo) ?? new TSettings();

    /// <inheritdoc />
    public void CheckSettings(FieldSettingsCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        CheckSettings(ReadSettings(check.Field.Settings), check);
    }

    /// <summary>
    /// The type's own check of a field's settings, read as <typeparamref name="TSettings"/>
    /// (<see cref="IFormBuilderFieldType.CheckSettings(FieldSettingsCheck)"/>); it finds nothing unless a type overrides it.
    /// </summary>
    /// <param name="settings">The field's settings, which keep the schema and its rules.</param>
    /// <param name="check">The field and where its problems go.</param>
    protected virtual void CheckSettings(TSettings settings, FieldSettingsCheck check)
    {
    }

    /// <summary>
    /// Whether the site has the configuration the type needs
    /// (<see cref="IFormBuilderFieldType.CheckAvailabilityAsync(CancellationToken)"/>, ADR-0057); available unless a type
    /// overrides it.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the request is, or when the check took too long.</param>
    public virtual ValueTask<FieldTypeAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) => ValueTask.FromResult(FieldTypeAvailability.Available);

    /// <inheritdoc />
    public FieldAnswerLimits AnswerLimits(FormField field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return AnswerLimits(ReadSettings(field.Settings), field);
    }

    /// <inheritdoc />
    public void Read(FieldReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);
        Read(ReadSettings(reading.Field.Settings), reading);
    }

    /// <inheritdoc />
    public void Validate(FieldValidation validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        Validate(ReadSettings(validation.Field.Settings), validation);
    }

    /// <inheritdoc />
    public void Display(FieldDisplay display)
    {
        ArgumentNullException.ThrowIfNull(display);
        Display(ReadSettings(display.Field.Settings), display);
    }

    /// <summary>
    /// The longest answer a field takes (<see cref="IFormBuilderFieldType.AnswerLimits(FormField)"/>, ADR-0058), with its
    /// settings read as <typeparamref name="TSettings"/>; the interface's default unless a type overrides it.
    /// </summary>
    /// <param name="settings">The field's settings.</param>
    /// <param name="field">The field.</param>
    protected virtual FieldAnswerLimits AnswerLimits(TSettings settings, FormField field) => FieldAnswerDefaults.Limits(Declaration, field);

    /// <summary>
    /// Reads a visitor's answers (<see cref="IFormBuilderFieldType.Read(FieldReading)"/>, ADR-0058), with the field's
    /// settings read as <typeparamref name="TSettings"/>; the interface's default unless a type overrides it.
    /// </summary>
    /// <param name="settings">The field's settings.</param>
    /// <param name="reading">The field, the answers, and where the values go.</param>
    protected virtual void Read(TSettings settings, FieldReading reading) => FieldAnswerDefaults.Read(Declaration, reading);

    /// <summary>
    /// Holds a shown field's values to its settings (<see cref="IFormBuilderFieldType.Validate(FieldValidation)"/>,
    /// ADR-0058), read as <typeparamref name="TSettings"/>; it finds nothing unless a type overrides it.
    /// </summary>
    /// <param name="settings">The field's settings.</param>
    /// <param name="validation">The field, its answers and values, and where its errors go.</param>
    protected virtual void Validate(TSettings settings, FieldValidation validation)
    {
    }

    /// <summary>
    /// Writes the texts a field's values show as in merge tags (<see cref="IFormBuilderFieldType.Display(FieldDisplay)"/>,
    /// ADR-0066), with the field's settings read as <typeparamref name="TSettings"/>; the interface's default unless a
    /// type overrides it.
    /// </summary>
    /// <param name="settings">The field's settings.</param>
    /// <param name="display">The field, its values, the culture, and where the texts go.</param>
    protected virtual void Display(TSettings settings, FieldDisplay display) => FieldDisplayDefaults.Display(Declaration, display);
}

/// <summary>The settings class of a field type without settings, such as a divider.</summary>
public sealed class NoFieldSettings;
