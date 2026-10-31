using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models.Validation;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The form picker's configuration editor (ADR-0129): it refuses a theme the site lacks, a folder, form or page that is
/// not a key, and more allowed folders or forms than the picker's queries can carry. Umbraco logs the messages and answers
/// the save with a general refusal, so the settings' own elements say what they can in the editor's language (ADR-0129).
/// </summary>
/// <param name="ioHelper">Umbraco's paths, which the base class takes.</param>
/// <param name="themes">The site's themes.</param>
internal sealed class FormPickerConfigurationEditor(IIOHelper ioHelper, FormBuilderThemes themes) : ConfigurationEditor<FormPickerConfiguration>(ioHelper)
{
    /// <inheritdoc />
    public override IEnumerable<ValidationResult> Validate(IDictionary<string, object> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var theme = Text(configuration, "theme");
        if (!string.IsNullOrWhiteSpace(theme) && themes.Find(theme.Trim()) is null)
        {
            yield return new ValidationResult("#jfbPicker_noSuchTheme", ["theme"]);
        }

        var redirect = Text(configuration, "redirect");
        if (!string.IsNullOrWhiteSpace(redirect) && !Guid.TryParse(redirect, out _))
        {
            yield return new ValidationResult("#jfbPicker_notAKey", ["redirect"]);
        }

        foreach (var name in new[] { "allowedFolders", "allowedForms" })
        {
            var texts = Texts(configuration, name).ToList();
            if (!texts.All(text => Guid.TryParse(text, out _)))
            {
                yield return new ValidationResult("#jfbPicker_notAKey", [name]);
            }

            if (texts.Count > SqlArgs.MaxListLength)
            {
                yield return new ValidationResult("#jfbPicker_tooMany", [name]);
            }
        }
    }

    // A setting's text, however the backoffice or a JSON body gave it.
    private static string? Text(IDictionary<string, object> configuration, string name) => configuration.TryGetValue(name, out var value)
        ? value switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement element => element.GetRawText(),
            _ => value.ToString(),
        }
        : null;

    // A list setting's texts.
    private static IEnumerable<string?> Texts(IDictionary<string, object> configuration, string name) => configuration.TryGetValue(name, out var value)
        ? value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } element => element.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetRawText()),
            IEnumerable<object> items => items.Select(item => item?.ToString()),
            null => [],
            _ => [value.ToString()],
        }
        : [];
}

/// <summary>The form picker's value editor (ADR-0129), with the check of a value against the picker's settings.</summary>
internal sealed class FormPickerValueEditor : DataValueEditor
{
    /// <summary>Creates the editor; Umbraco's value editor factory gives its services.</summary>
    /// <param name="shortStringHelper">Umbraco's string helper.</param>
    /// <param name="jsonSerializer">Umbraco's JSON serializer.</param>
    /// <param name="ioHelper">Umbraco's paths.</param>
    /// <param name="attribute">The property editor's attribute.</param>
    /// <param name="places">Where forms stand in the library.</param>
    public FormPickerValueEditor(IShortStringHelper shortStringHelper, IJsonSerializer jsonSerializer, IIOHelper ioHelper, DataEditorAttribute attribute, LibraryPlaces places)
        : base(shortStringHelper, jsonSerializer, ioHelper, attribute) =>
        Validators.Add(new FormPickerValidator(places));
}

/// <summary>
/// Checks a form picker's value when a page is saved (ADR-0129): keys only, one unless the picker takes several, each a
/// form of the library outside the trash, and each a form the settings allow. Umbraco runs it for a picker inside a
/// block too, with that property's settings.
/// </summary>
/// <param name="places">Where forms stand in the library, read synchronously as the validator must.</param>
internal sealed class FormPickerValidator(LibraryPlaces places) : IValueValidator
{
    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(object? value, string? valueType, object? dataTypeConfiguration, PropertyValidationContext validationContext)
    {
        if (FormPickerValues.Read(value) is not { } keys)
        {
            return [new ValidationResult("#jfbPicker_notAKey", ["value"])];
        }

        var settings = dataTypeConfiguration as FormPickerConfiguration ?? new FormPickerConfiguration();
        if (keys.Count > 1 && !settings.Multiple)
        {
            return [new ValidationResult("#jfbPicker_oneOnly", ["value"])];
        }

        if (keys.Count == 0)
        {
            return [];
        }

        var found = places.Of(keys);
        var problems = new List<ValidationResult>();
        if (keys.Any(key => !found.TryGetValue(key, out var place) || place.Trashed))
        {
            problems.Add(new ValidationResult("#jfbPicker_noSuchForm", ["value"]));
        }

        if (found.Values.Any(place => !place.Trashed && !settings.Allows(place.Form, place.Folders)))
        {
            problems.Add(new ValidationResult("#jfbPicker_notAllowed", ["value"]));
        }

        return problems;
    }
}
