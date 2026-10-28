using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

/// <summary>
/// A form's public projection as a visitor's page gets it in one culture (ADR-0084): each of its texts, the texts in its
/// field types' public settings among them, replaced by the one text the culture shows, and written as a plain string
/// where the projection writes a text per culture. Everything else stays as the projection has it.
/// </summary>
internal static class VisitorForm
{
    /// <summary>
    /// The projection with each text replaced by what <paramref name="show"/> makes of it; a rich text is a
    /// <see cref="LocalizedHtml"/>, so the caller can sanitise it.
    /// </summary>
    /// <param name="form">The projection.</param>
    /// <param name="show">The text a visitor sees of an editor's text.</param>
    /// <param name="types">The field types, whose settings schemas say which public settings are texts.</param>
    public static PublicForm Map(PublicForm form, Func<LocalizedString, string> show, FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(types);
        var map = new Mapper(show, types);
        return form with
        {
            Settings = form.Settings with
            {
                SubmitLabel = map.Text(form.Settings.SubmitLabel),
                NextLabel = map.Text(form.Settings.NextLabel),
                BackLabel = map.Text(form.Settings.BackLabel),
                ClosedMessage = map.Html(form.Settings.ClosedMessage),
            },
            Pages = [.. form.Pages.Select(map.Page)],
            ValidationRules = [.. form.ValidationRules.Select(rule => rule with { Message = map.Text(rule.Message) })],
        };
    }

    /// <summary>A projection <see cref="Map"/> made, as UTF-8 JSON: each text as the string it holds.</summary>
    /// <exception cref="ArgumentException">The projection holds a lone surrogate outside its texts.</exception>
    public static byte[] Write(PublicForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return JsonSerializer.SerializeToUtf8Bytes(form, VisitorJsonContext.Default.PublicForm);
    }

    private sealed class Mapper(Func<LocalizedString, string> show, FieldTypeRegistry types)
    {
        public LocalizedText? Text(LocalizedText? text) => text is null ? null : new LocalizedText(show(text));

        public LocalizedHtml? Html(LocalizedHtml? html) => html is null ? null : new LocalizedHtml(show(html));

        public PublicPage Page(PublicPage page) => page with
        {
            Title = Text(page.Title),
            Description = Html(page.Description),
            Groups = [.. page.Groups.Select(group => group with
            {
                Title = Text(group.Title),
                Description = Html(group.Description),
                Fields = [.. group.Fields.Select(Field)],
            })],
        };

        private PublicField Field(PublicField field) => field with
        {
            Label = Text(field.Label),
            Help = Text(field.Help),
            Placeholder = Text(field.Placeholder),
            Messages = Messages(field.Messages),
            Validation = [.. field.Validation.Select(check => check with { Message = Text(check.Message) })],
            Options = field.Options is { } options
                ? options with { Items = [.. options.Items.Select(item => item with { Label = Text(item.Label), Description = Text(item.Description) })] }
                : null,
            Rows = [.. field.Rows.Select(row => row with { Label = Text(row.Label) })],
            Fields = [.. field.Fields.Select(Field)],
            Settings = Settings(field.Settings, types.Find(field.Type)),
        };

        private FieldMessages Messages(FieldMessages messages) => messages with
        {
            Required = Text(messages.Required),
            Invalid = Text(messages.Invalid),
            MinLength = Text(messages.MinLength),
            MaxLength = Text(messages.MaxLength),
            MinWords = Text(messages.MinWords),
            MaxWords = Text(messages.MaxWords),
            Min = Text(messages.Min),
            Max = Text(messages.Max),
            Step = Text(messages.Step),
            DateWindow = Text(messages.DateWindow),
            Selections = Text(messages.Selections),
            FileType = Text(messages.FileType),
            FileSize = Text(messages.FileSize),
            FileCount = Text(messages.FileCount),
            Full = Text(messages.Full),
            Confirmation = Text(messages.Confirmation),
            Digits = Text(messages.Digits),
            RowCount = Text(messages.RowCount),
        };

        // The public settings a type declares as texts become the strings the culture shows; the projection holds only
        // public settings, each by the type's schema, so a setting the schema does not name is left as it is.
        private SettingsObject? Settings(SettingsObject? settings, FieldTypeTraits? traits)
        {
            if (settings is null || traits is null)
            {
                return settings;
            }

            var schema = traits.Type.SettingsSchema.Settings;
            if (!schema.Any(HoldsText))
            {
                return settings;
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteObject(writer, schema, settings.Value);
            }

            using var document = JsonDocument.Parse(buffer.ToArray());
            return SettingsObject.From(document.RootElement);
        }

        private void WriteObject(Utf8JsonWriter writer, IReadOnlyList<FieldSettingDescriptor> schema, JsonElement value)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                var setting = schema.FirstOrDefault(candidate => candidate.Name == property.Name);
                if (setting is null)
                {
                    property.WriteTo(writer);
                    continue;
                }

                writer.WritePropertyName(property.Name);
                if (IsText(setting.Kind))
                {
                    writer.WriteStringValue(Show(setting.Kind, property.Value));
                }
                else if (setting.Kind == FieldSettingKind.Nested && property.Value.ValueKind == JsonValueKind.Object)
                {
                    WriteObject(writer, setting.Settings, property.Value);
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        // A text per culture as the settings write one: an object of culture names and texts, the default among them.
        // Anything else shows nothing rather than something unchecked, and keeps its place, so no object goes empty.
        private string Show(FieldSettingKind kind, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var texts = new List<KeyValuePair<string, string>>();
            foreach (var culture in value.EnumerateObject())
            {
                if (culture.Value.ValueKind != JsonValueKind.String)
                {
                    return string.Empty;
                }

                texts.Add(new(culture.Name, culture.Value.GetString()!));
            }

            try
            {
                return show(kind == FieldSettingKind.LocalizedHtml ? new LocalizedHtml(texts) : new LocalizedText(texts));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static bool IsText(FieldSettingKind kind) => kind is FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml;

        // A list holds no texts (the settings reader refuses one), so only texts and objects that hold them count.
        private static bool HoldsText(FieldSettingDescriptor setting) =>
            IsText(setting.Kind) || setting.Settings.Any(HoldsText);
    }
}
