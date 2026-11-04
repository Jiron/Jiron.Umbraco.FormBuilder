using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// A text of a definition in which merge tags render (ADR-0014): where it is used, the context it renders in, whose text
/// it is (a visitor text recalls only the answers of the fields before its owner), its JSON path and its text.
/// </summary>
internal sealed record FormTemplate(MergeTagPlace Place, MergeTagContext Context, Guid? Owner, string Path, string Text)
{
    /// <summary>Whether an action owns it, whose type's own check refuses its errors (ADR-0158).</summary>
    public bool OfAction { get; init; }
}

/// <summary>
/// Every text of a definition in which merge tags render, culture by culture, in the form's order (the form check,
/// ADR-0177): each page's and group's title and description; each field's label, help, placeholder, default value
/// (rendered on the server for a type that keeps its value there) and each text setting its type marks as taking tags, a
/// consent's being its consent text, a repeatable group's children after it; each outcome's message and redirect; the
/// entry title; and each action's text settings that take tags.
/// </summary>
internal static class FormTemplates
{
    /// <summary>The templates of <paramref name="definition"/>.</summary>
    public static IReadOnlyList<FormTemplate> Of(FormDefinition definition, FieldTypeRegistry fieldTypes, ActionTypeRegistry actionTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fieldTypes);
        ArgumentNullException.ThrowIfNull(actionTypes);
        var found = new List<FormTemplate>();
        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = definition.Pages[p];
            var pagePath = JsonPaths.Index(pages, p);
            Localized(found, MergeTagPlace.VisitorText, page.Key, JsonPaths.Property(pagePath, "title"), page.Title);
            Localized(found, MergeTagPlace.VisitorText, page.Key, JsonPaths.Property(pagePath, "description"), page.Description);
            var groups = JsonPaths.Property(pagePath, "groups");
            for (var g = 0; g < page.Groups.Count; g++)
            {
                var group = page.Groups[g];
                var groupPath = JsonPaths.Index(groups, g);
                Localized(found, MergeTagPlace.VisitorText, group.Key, JsonPaths.Property(groupPath, "title"), group.Title);
                Localized(found, MergeTagPlace.VisitorText, group.Key, JsonPaths.Property(groupPath, "description"), group.Description);
                Fields(found, group.Fields, JsonPaths.Property(groupPath, "fields"), fieldTypes);
            }
        }

        var outcomes = JsonPaths.Property(JsonPaths.Root, "outcomes");
        for (var o = 0; o < definition.Outcomes.Count; o++)
        {
            var outcome = definition.Outcomes[o];
            var path = JsonPaths.Index(outcomes, o);
            Localized(found, MergeTagPlace.Outcome, null, JsonPaths.Property(path, "message"), outcome.Message);
            if (outcome.Redirect?.Url is { } url)
            {
                found.Add(new FormTemplate(MergeTagPlace.Redirect, MergeTagContext.Url, null, JsonPaths.Property(JsonPaths.Property(path, "redirect"), "url"), url));
            }
        }

        if (definition.Settings.Submission.EntryTitle is { } title)
        {
            var path = JsonPaths.Property(JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "settings"), "submission"), "entryTitle");
            found.Add(new FormTemplate(MergeTagPlace.EntryTitle, MergeTagContext.Text, null, path, title));
        }

        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var a = 0; a < definition.Actions.Count; a++)
        {
            var action = definition.Actions[a];
            if (action.Settings is { } settings && actionTypes.Find(action.Type) is { } type)
            {
                var place = action.Type == EmailActionType.Alias ? MergeTagPlace.Mail : MergeTagPlace.Action;
                var start = found.Count;
                Settings(found, place, null, JsonPaths.Property(JsonPaths.Index(actions, a), "settings"), type.SettingsSchema.Settings, settings.Value);
                for (var i = start; i < found.Count; i++)
                {
                    found[i] = found[i] with { OfAction = true };
                }
            }
        }

        return found;
    }

    private static void Fields(List<FormTemplate> found, IReadOnlyList<FormField> fields, string path, FieldTypeRegistry types)
    {
        for (var f = 0; f < fields.Count; f++)
        {
            var field = fields[f];
            var fieldPath = JsonPaths.Index(path, f);
            Localized(found, MergeTagPlace.VisitorText, field.Key, JsonPaths.Property(fieldPath, "label"), field.Label);
            Localized(found, MergeTagPlace.VisitorText, field.Key, JsonPaths.Property(fieldPath, "help"), field.Help);
            Localized(found, MergeTagPlace.VisitorText, field.Key, JsonPaths.Property(fieldPath, "placeholder"), field.Placeholder);
            var traits = types.Find(field.Type);
            if (field.DefaultValue is { } given)
            {
                // A value kept on the server renders there, with every source (ADR-0014); any other renders in the page too.
                var place = traits is { ServerOnlyDefault: true } ? MergeTagPlace.HiddenValue : MergeTagPlace.DefaultValue;
                var defaultPath = JsonPaths.Property(fieldPath, "defaultValue");
                for (var v = 0; v < given.Values.Count; v++)
                {
                    found.Add(new FormTemplate(place, MergeTagContext.Text, field.Key, given.IsList ? JsonPaths.Index(defaultPath, v) : defaultPath, given.Values[v]));
                }
            }

            if (field.Settings is { } settings && traits is not null)
            {
                // A consent's text is its consent wording, which takes fewer sources (ADR-0014, ADR-0070).
                var place = traits.Type is ConsentFieldType ? MergeTagPlace.ConsentText : MergeTagPlace.VisitorText;
                Settings(found, place, field.Key, JsonPaths.Property(fieldPath, "settings"), traits.Type.SettingsSchema.Settings, settings.Value);
            }

            Fields(found, field.Fields, JsonPaths.Property(fieldPath, "fields"), types);
        }
    }

    // The settings in the schema's order that take merge tags, a nested object's own in turn; a value of another shape is
    // the settings check's to refuse.
    private static void Settings(List<FormTemplate> found, MergeTagPlace place, Guid? owner, string path, IReadOnlyList<FieldSettingDescriptor> schema, JsonElement value)
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
                Settings(found, place, owner, settingPath, setting.Settings, given);
            }
            else if (setting.SupportsMergeTags)
            {
                var context = setting.Kind == FieldSettingKind.LocalizedHtml || place == MergeTagPlace.ConsentText ? MergeTagContext.Html : MergeTagContext.Text;
                if (given.ValueKind == JsonValueKind.String)
                {
                    found.Add(new FormTemplate(place, context, owner, settingPath, given.GetString()!));
                }
                else if (given.ValueKind == JsonValueKind.Object)
                {
                    foreach (var culture in given.EnumerateObject().Where(culture => culture.Value.ValueKind == JsonValueKind.String))
                    {
                        found.Add(new FormTemplate(place, context, owner, JsonPaths.Property(settingPath, culture.Name), culture.Value.GetString()!));
                    }
                }
            }
        }
    }

    private static void Localized(List<FormTemplate> found, MergeTagPlace place, Guid? owner, string path, LocalizedString? text)
    {
        if (text is null)
        {
            return;
        }

        var context = text is LocalizedHtml ? MergeTagContext.Html : MergeTagContext.Text;
        foreach (var (culture, value) in text.Texts)
        {
            found.Add(new FormTemplate(place, context, owner, JsonPaths.Property(path, culture), value));
        }
    }
}
