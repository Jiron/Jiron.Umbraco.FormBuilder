using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// A field type (section 4.3.2, section 4.9, ADR-0055): what it is and which settings it has. Types are registered in an
/// ordered collection, <c>builder.JironFormBuilder().FieldTypes()</c>, which holds the built-in ones from its start, so
/// a site or an add-on adds, replaces or removes types there. A type is a singleton: it holds no state of its own and may
/// take singleton services in its constructor. <see cref="FormBuilderFieldTypeBase{TSettings}"/> builds the settings
/// schema from a settings class. A visitor's answers go through <see cref="AnswerLimits"/>, <see cref="Read"/> and
/// <see cref="Validate"/> (ADR-0058), a file through <see cref="UploadLimit"/> and <see cref="CheckUpload"/> when it is
/// uploaded, a repeatable group's rows through <see cref="ValidateRows"/> (ADR-0071), and merge tags show the values as
/// <see cref="Display"/> writes them (ADR-0066).
/// </summary>
public interface IFormBuilderFieldType
{
    /// <summary>What the type is: its alias, how the designer shows it, how its values are stored and compared, and what it supports.</summary>
    FieldTypeDeclaration Declaration { get; }

    /// <summary>The type's settings, as the designer's settings editor and the checks read them.</summary>
    FieldSettingsSchema SettingsSchema { get; }

    /// <summary>
    /// The type's own check of a field's settings at publish (ADR-0056): what the rules of the schema cannot say, such as
    /// a minimum above the maximum. It runs only once the settings keep the schema and its rules, and reports each
    /// problem to <paramref name="check"/>; it finds nothing by default. An exception it throws is reported as a problem
    /// with the field's settings, so a broken check never stops a publish from listing what it found.
    /// </summary>
    /// <param name="check">The field and where its problems go.</param>
    void CheckSettings(FieldSettingsCheck check)
    {
    }

    /// <summary>
    /// Whether the site has the configuration the type needs (ADR-0057), for the designer's palette, which greys out a
    /// type that is not configured and shows the reason; available by default. It runs whenever the backoffice reads the
    /// field types, for every type at once, and should answer quickly: one that takes more than 5 seconds, throws or
    /// answers null counts as not configured, with a reason that its setup could not be checked, and is logged.
    /// Availability does not change publishing or rendering.
    /// </summary>
    /// <param name="cancellationToken">Cancelled when the request is, or when the check took too long.</param>
    ValueTask<FieldTypeAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) => ValueTask.FromResult(FieldTypeAvailability.Available);

    /// <summary>
    /// The longest answer a field of the type takes (ADR-0030, ADR-0058), which the pipeline holds every answer to before
    /// the rules run: the type's own ceiling, never the field's own checks. By default one text, or as many as a field can
    /// have options when the type takes several, as long as its rule kind needs (a date's ten characters, a plain text's
    /// 2,000); none for a type that holds no value of its own. The name of a part with texts counts as a text's length
    /// (ADR-0080), so a type whose answers have parts takes texts at least as long as its parts' names.
    /// <see cref="FieldAnswerLimits.None"/> makes the pipeline ignore what is posted for the field.
    /// </summary>
    /// <param name="field">The field, with its settings.</param>
    FieldAnswerLimits AnswerLimits(FormField field) => FieldAnswerDefaults.Limits(Declaration, field);

    /// <summary>
    /// Reads a visitor's answers into values in the wire form of ADR-0005 (ADR-0058), or finds them unreadable. The
    /// pipeline reads every field before the rules run, so rules compare what was read. By default the field's own texts
    /// are read as its rule kind needs: texts, choices and file tokens as they are, a checkbox's <c>true</c>, a number as
    /// posted converted exactly to the wire form, dates and times in their wire forms; two values are unreadable for a
    /// type that takes one, and so is a value given twice. Parts are left to the types that have them.
    /// </summary>
    /// <param name="reading">The field, the answers, and where the values go.</param>
    void Read(FieldReading reading) => FieldAnswerDefaults.Read(Declaration, reading);

    /// <summary>
    /// Holds a shown field's values to its settings (ADR-0058) and reports each error by its kind; it finds nothing by
    /// default. It runs only on a field with values; a field without any fails with <c>required</c> when it is required.
    /// </summary>
    /// <param name="validation">The field, its answers and values, and where its errors go.</param>
    void Validate(FieldValidation validation)
    {
    }

    /// <summary>
    /// The most bytes one file of a field of the type may have (ADR-0071), for a type that stores files: the upload
    /// endpoint refuses a file past it before it is stored (ADR-0006). Null, the default, leaves the host's limit
    /// (<c>Files:MaxFileBytes</c>), which also caps a larger one.
    /// </summary>
    /// <param name="field">The field, with its settings.</param>
    long? UploadLimit(FormField field) => null;

    /// <summary>
    /// Checks an upload's name and first bytes before the file is stored (ADR-0006, ADR-0071), for a type that stores
    /// files, and reports each problem by its kind, which refuses the upload; it finds nothing by default. The host's
    /// checks of every upload (blocked extensions, the content each extension promises) come on top of it.
    /// </summary>
    /// <param name="check">The field, the file's name and first bytes, and where its errors go.</param>
    void CheckUpload(FieldUploadCheck check)
    {
    }

    /// <summary>
    /// Holds the number of rows a submission gives a visible field to its settings (ADR-0071), for a type with child
    /// fields, and reports each error by its kind; it finds nothing by default. The child fields' checks run row by row.
    /// </summary>
    /// <param name="validation">The field, its number of rows, and where its errors go.</param>
    void ValidateRows(FieldRowsValidation validation)
    {
    }

    /// <summary>
    /// Writes the texts a field's values show as in merge tags (ADR-0066): <c>{{ field.alias }}</c> and the list of the
    /// answers, in the culture the template renders in. By default as rules compare them: texts as they are, options by
    /// their labels, a ticked box as yes, files by their names, numbers and dates in the culture's formats, and a value
    /// with parts as its parts' texts; a type whose default value stays on the server shows nothing in the list of the
    /// answers. An exception it throws is logged, and the field then shows as the default would show it.
    /// </summary>
    /// <param name="display">The field, its values, the culture, and where the texts go.</param>
    void Display(FieldDisplay display) => FieldDisplayDefaults.Display(Declaration, display);
}
