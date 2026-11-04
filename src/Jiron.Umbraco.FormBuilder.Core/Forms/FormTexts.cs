using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A localised text of a definition with its JSON path, without a culture (ADR-0002).</summary>
/// <param name="Path">The path of the text, such as <c>$.pages[0].title</c>.</param>
/// <param name="Text">The text in its cultures.</param>
/// <param name="PerCulture">
/// Whether the definition writes the text per culture, so a culture's text has a path of its own; false for a setting
/// written as a single text.
/// </param>
internal sealed record FormText(string Path, LocalizedString Text, bool PerCulture = true)
{
    /// <summary>The path of the text a culture's request sees, as the definition writes it.</summary>
    /// <param name="entry">The culture's text, as <see cref="LocalizedString.EntryFor"/> finds it.</param>
    public string PathOf(CultureText entry) => PerCulture ? JsonPaths.Property(Path, entry.Culture) : Path;
}

/// <summary>
/// Every localised text of a definition, in the form's order (the form check, ADR-0180): each page's and group's title and
/// description; each field's label, help, placeholder, messages and checks' messages, its options' labels and
/// descriptions, its rows' statements and the localised texts of its type's settings (nested ones too), a repeatable
/// group's children after it; the form-level validation rules' messages; the outcomes' messages; the buttons' labels and
/// the closed message. Each may name an item of Umbraco's dictionary (<c>#key</c>).
/// </summary>
internal static class FormTexts
{
    /// <summary>The texts of <paramref name="definition"/>.</summary>
    public static IReadOnlyList<FormText> Of(FormDefinition definition, FieldTypeRegistry fieldTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fieldTypes);
        var found = new List<FormText>();
        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = definition.Pages[p];
            var pagePath = JsonPaths.Index(pages, p);
            Add(found, JsonPaths.Property(pagePath, "title"), page.Title);
            Add(found, JsonPaths.Property(pagePath, "description"), page.Description);
            var groups = JsonPaths.Property(pagePath, "groups");
            for (var g = 0; g < page.Groups.Count; g++)
            {
                var group = page.Groups[g];
                var groupPath = JsonPaths.Index(groups, g);
                Add(found, JsonPaths.Property(groupPath, "title"), group.Title);
                Add(found, JsonPaths.Property(groupPath, "description"), group.Description);
                Fields(found, group.Fields, JsonPaths.Property(groupPath, "fields"), fieldTypes);
            }
        }

        var rules = JsonPaths.Property(JsonPaths.Root, "validationRules");
        for (var r = 0; r < definition.ValidationRules.Count; r++)
        {
            Add(found, JsonPaths.Property(JsonPaths.Index(rules, r), "message"), definition.ValidationRules[r].Message);
        }

        var outcomes = JsonPaths.Property(JsonPaths.Root, "outcomes");
        for (var o = 0; o < definition.Outcomes.Count; o++)
        {
            Add(found, JsonPaths.Property(JsonPaths.Index(outcomes, o), "message"), definition.Outcomes[o].Message);
        }

        var settings = JsonPaths.Property(JsonPaths.Root, "settings");
        var submission = JsonPaths.Property(settings, "submission");
        Add(found, JsonPaths.Property(submission, "submitLabel"), definition.Settings.Submission.SubmitLabel);
        Add(found, JsonPaths.Property(submission, "nextLabel"), definition.Settings.Submission.NextLabel);
        Add(found, JsonPaths.Property(submission, "backLabel"), definition.Settings.Submission.BackLabel);
        Add(found, JsonPaths.Property(JsonPaths.Property(settings, "availability"), "closedMessage"), definition.Settings.Availability.ClosedMessage);
        return found;
    }

    private static void Fields(List<FormText> found, IReadOnlyList<FormField> fields, string path, FieldTypeRegistry types)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            var fieldPath = JsonPaths.Index(path, f);
            Add(found, JsonPaths.Property(fieldPath, "label"), field.Label);
            Add(found, JsonPaths.Property(fieldPath, "help"), field.Help);
            Add(found, JsonPaths.Property(fieldPath, "placeholder"), field.Placeholder);
            var messages = JsonPaths.Property(fieldPath, "messages");
            foreach (var message in FieldMessageProperties.All)
            {
                Add(found, JsonPaths.Property(messages, message.Name), message.Read(field.Messages));
            }

            var checks = JsonPaths.Property(fieldPath, "validation");
            for (var c = 0; c < field.Validation.Count; c++)
            {
                Add(found, JsonPaths.Property(JsonPaths.Index(checks, c), "message"), field.Validation[c].Message);
            }

            var items = JsonPaths.Property(JsonPaths.Property(fieldPath, "options"), "items");
            for (var o = 0; o < (field.Options?.Items.Count ?? 0); o++)
            {
                var option = field.Options!.Items[o];
                Add(found, JsonPaths.Property(JsonPaths.Index(items, o), "label"), option.Label);
                Add(found, JsonPaths.Property(JsonPaths.Index(items, o), "description"), option.Description);
            }

            var rows = JsonPaths.Property(fieldPath, "rows");
            for (var r = 0; r < field.Rows.Count; r++)
            {
                Add(found, JsonPaths.Property(JsonPaths.Index(rows, r), "label"), field.Rows[r].Label);
            }

            if (field.Settings is { } settings && types.Find(field.Type) is { } traits)
            {
                Settings(found, JsonPaths.Property(fieldPath, "settings"), traits.Type.SettingsSchema.Settings, settings.Value);
            }

            Fields(found, field.Fields, JsonPaths.Property(fieldPath, "fields"), types);
        }
    }

    // The localised settings in the schema's order, a nested object's own in turn; a value of another shape is the settings
    // check's to refuse, and one written as a single text has only its default.
    private static void Settings(List<FormText> found, string path, IReadOnlyList<FieldSettingDescriptor> schema, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var setting in schema)
        {
            if (!value.TryGetProperty(setting.Name, out var given))
            {
                continue;
            }

            var settingPath = JsonPaths.Property(path, setting.Name);
            if (setting.Kind == FieldSettingKind.Nested)
            {
                Settings(found, settingPath, setting.Settings, given);
            }
            else if (setting.Kind is FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml && Localized(given) is { } text)
            {
                found.Add(new FormText(settingPath, text, PerCulture: given.ValueKind == JsonValueKind.Object));
            }
        }
    }

    // A setting's text as a localised text: a single text is its default; null for a value of another shape.
    private static LocalizedText? Localized(JsonElement given)
    {
        if (given.ValueKind == JsonValueKind.String)
        {
            return new LocalizedText(given.GetString()!);
        }

        if (given.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var texts = new List<KeyValuePair<string, string>>();
        foreach (var culture in given.EnumerateObject())
        {
            if (culture.Value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            texts.Add(new KeyValuePair<string, string>(culture.Name, culture.Value.GetString()!));
        }

        try
        {
            return new LocalizedText(texts);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void Add(List<FormText> found, string path, LocalizedString? text)
    {
        if (text is not null)
        {
            found.Add(new FormText(path, text));
        }
    }
}
