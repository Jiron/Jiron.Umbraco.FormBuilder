using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

/// <summary>A visitor text of a visitor's form (ADR-0085).</summary>
/// <param name="Owner">The key of the page, group or field whose text it is.</param>
/// <param name="Name">
/// The property it is: <c>title</c>, <c>description</c>, <c>label</c>, <c>help</c>, <c>placeholder</c>, or
/// <c>settings.</c> and the setting's path, such as <c>settings.text</c>.
/// </param>
/// <param name="IsHtml">Whether it is rich text, which renders as HTML.</param>
/// <param name="Text">The text, as the visitor's form shows it.</param>
internal sealed record VisitorText(Guid Owner, string Name, bool IsHtml, string Text);

/// <summary>
/// The visitor texts of a visitor's form (ADR-0068, ADR-0085), in the form's order, each with its owner: a page's and
/// a group's title and description, a field's label, help and placeholder, and each public text setting its type marks
/// as taking merge tags, a repeatable group's children after it. A consent's text is its wording, which the server
/// renders whole (ADR-0070), so it is none.
/// </summary>
internal static class VisitorTexts
{
    /// <summary>The visitor texts of a form that <see cref="VisitorForm.Map"/> made, its texts the strings it shows.</summary>
    /// <param name="form">The visitor's form.</param>
    /// <param name="types">The field types, whose settings schemas say which settings take merge tags.</param>
    public static IReadOnlyList<VisitorText> Of(PublicForm form, FieldTypeRegistry types)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(types);
        var texts = new List<VisitorText>();
        foreach (var page in form.Pages)
        {
            Add(texts, page.Key, "title", page.Title);
            Add(texts, page.Key, "description", page.Description);
            foreach (var group in page.Groups)
            {
                Add(texts, group.Key, "title", group.Title);
                Add(texts, group.Key, "description", group.Description);
                foreach (var field in group.Fields)
                {
                    AddField(texts, field, types);
                }
            }
        }

        return texts;
    }

    private static void AddField(List<VisitorText> texts, PublicField field, FieldTypeRegistry types)
    {
        Add(texts, field.Key, "label", field.Label);
        Add(texts, field.Key, "help", field.Help);
        Add(texts, field.Key, "placeholder", field.Placeholder);
        if (field.Settings is { } settings && types.Find(field.Type) is { Type: not ConsentFieldType } traits)
        {
            AddSettings(texts, field.Key, "settings.", traits.Type.SettingsSchema.Settings, settings.Value);
        }

        foreach (var child in field.Fields)
        {
            AddField(texts, child, types);
        }
    }

    // The settings in the schema's order that take merge tags, an object's own in turn; a value that is no string shows
    // nothing in the visitor's form, so it is no text.
    private static void AddSettings(List<VisitorText> texts, Guid owner, string prefix, IReadOnlyList<FieldSettingDescriptor> schema, JsonElement value)
    {
        foreach (var setting in schema)
        {
            if (!value.TryGetProperty(setting.Name, out var given))
            {
                continue;
            }

            if (setting.Kind is FieldSettingKind.LocalizedText or FieldSettingKind.LocalizedHtml)
            {
                if (setting.SupportsMergeTags && given.ValueKind == JsonValueKind.String)
                {
                    texts.Add(new VisitorText(owner, prefix + setting.Name, setting.Kind == FieldSettingKind.LocalizedHtml, given.GetString()!));
                }
            }
            else if (setting.Kind == FieldSettingKind.Nested && given.ValueKind == JsonValueKind.Object)
            {
                AddSettings(texts, owner, $"{prefix}{setting.Name}.", setting.Settings, given);
            }
        }
    }

    private static void Add(List<VisitorText> texts, Guid owner, string name, LocalizedString? text)
    {
        if (text is not null)
        {
            texts.Add(new VisitorText(owner, name, text is LocalizedHtml, text.Default));
        }
    }
}
