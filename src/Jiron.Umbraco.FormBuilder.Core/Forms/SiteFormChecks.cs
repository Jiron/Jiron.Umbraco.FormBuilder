using System.Collections.Frozen;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// A theme the site lacks (ROADMAP 9.5e, ADR-0015, ADR-0180): a warning, since the form then shows with the default
/// theme. The site has a theme in code, built in or its own, or in the library.
/// </summary>
/// <param name="themes">Whether the site has a theme.</param>
internal sealed class ThemeFormCheck(FormThemes themes) : IFormCheck
{
    /// <summary>A theme the site lacks (a warning).</summary>
    public const string ThemeMissing = "theme_missing";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Settings.Appearance.Theme is not { } theme || await themes.HasAsync(theme, cancellationToken))
        {
            return [];
        }

        var path = JsonPaths.Property(JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "settings"), "appearance"), "theme");
        return [new FormCheckIssue(FormCheckSeverity.Warning, ThemeMissing, path, $"The site has no theme '{theme}', so the form shows with the default theme.")];
    }
}

/// <summary>
/// An option list the site lacks (ROADMAP 21.1, ADR-0214, ADR-0252): an error at every field that names it, since the
/// field would offer its visitors no choices, and a required one would refuse every entry. A form comes to name such a
/// list through a file whose lists the site did not take in, or when the list is deleted from another site's copy.
/// </summary>
/// <param name="optionLists">The library's option lists.</param>
internal sealed class OptionListFormCheck(IOptionListRepository optionLists) : IFormCheck
{
    /// <summary>An option list the site lacks (an error).</summary>
    public const string OptionListMissing = "option_list_missing";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        var known = new Dictionary<Guid, bool>();
        async Task VisitAsync(IReadOnlyList<FormField> fields, string path)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                var at = JsonPaths.Index(JsonPaths.Property(path, "fields"), i);
                if (fields[i].Options is { Source: OptionSource.List, OptionList: { } list })
                {
                    if (!known.TryGetValue(list, out var there))
                    {
                        known[list] = there = await optionLists.GetAsync(list, cancellationToken) is not null;
                    }

                    if (!there)
                    {
                        issues.Add(new FormCheckIssue(FormCheckSeverity.Error, OptionListMissing, JsonPaths.Property(JsonPaths.Property(at, "options"), "list"), $"The site has no option list '{list:D}', so the field would offer no choices."));
                    }
                }

                await VisitAsync(fields[i].Fields, at);
            }
        }

        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "pages"), p);
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                await VisitAsync(definition.Pages[p].Groups[g].Fields, JsonPaths.Index(JsonPaths.Property(page, "groups"), g));
            }
        }

        return issues;
    }
}

/// <summary>
/// The content, media and member groups a form names that the site lacks (ROADMAP 9.5e, ADR-0002, ADR-0046, ADR-0180),
/// and a redirect to a page that is not published: warnings, since the form goes live but that part of it does nothing.
/// A content or media item the site lacks is one that is gone or in the recycle bin. The form names content in its
/// outcomes' redirects and in the settings of its fields' and actions' types that pick a page, media in its options'
/// images and in the settings that pick media, and member groups by key in who may fill it in and by key or by their
/// exact name in its rules.
/// What an option source's configuration names is its kind's to check when the form is published (ADR-0075).
/// </summary>
/// <param name="fieldTypes">The field types, whose settings schemas say which settings pick content or media.</param>
/// <param name="actionTypes">The action types, whose settings schemas say the same.</param>
/// <param name="documents">The content tree.</param>
/// <param name="media">The media tree.</param>
/// <param name="published">The published content.</param>
/// <param name="memberGroups">The member groups.</param>
internal sealed class SiteReferencesFormCheck(
    FieldTypeRegistry fieldTypes,
    ActionTypeRegistry actionTypes,
    IDocumentNavigationQueryService documents,
    IMediaNavigationQueryService media,
    IPublishedContentCache published,
    IMemberGroupService memberGroups) : IFormCheck
{
    /// <summary>A content item the site lacks (a warning).</summary>
    public const string ContentMissing = "content_missing";

    /// <summary>A media item the site lacks (a warning).</summary>
    public const string MediaMissing = "media_missing";

    /// <summary>A member group the site lacks (a warning).</summary>
    public const string MemberGroupMissing = "member_group_missing";

    /// <summary>A redirect to a page that is not published (a warning).</summary>
    public const string RedirectUnpublished = "redirect_unpublished";

    // The editors of a setting that picks a content item.
    private static readonly FrozenSet<string> ContentEditors = new[] { "documentPicker", "contentPicker" }.ToFrozenSet(StringComparer.Ordinal);

    // The editor of a setting that picks a media item.
    private const string MediaEditor = "mediaPicker";

    private const string Gone = "or holds it in the recycle bin";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var issues = new List<FormCheckIssue>();
        await RedirectsAsync(definition, issues);
        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            var items = JsonPaths.Property(JsonPaths.Property(path, "options"), "items");
            for (var i = 0; i < (field.Options?.Items.Count ?? 0); i++)
            {
                if (field.Options!.Items[i].Image is { } image)
                {
                    Media(issues, image, JsonPaths.Property(JsonPaths.Index(items, i), "image"));
                }
            }

            if (field.Settings is { } settings && fieldTypes.Find(field.Type) is { } traits)
            {
                Settings(issues, JsonPaths.Property(path, "settings"), traits.Type.SettingsSchema.Settings, settings.Value);
            }
        }

        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var a = 0; a < definition.Actions.Count; a++)
        {
            if (definition.Actions[a].Settings is { } settings && actionTypes.Find(definition.Actions[a].Type) is { } type)
            {
                Settings(issues, JsonPaths.Property(JsonPaths.Index(actions, a), "settings"), type.SettingsSchema.Settings, settings.Value);
            }
        }

        await MemberGroupsAsync(definition, issues);
        return issues;
    }

    // Every rule's conditions in the form, with the path of their list: the pages', groups' and fields' rules, the
    // form-level validation rules, the outcomes' and the actions' rules.
    private static IEnumerable<(IReadOnlyList<RuleItem> Conditions, string Path)> ConditionsOf(FormDefinition definition)
    {
        static IEnumerable<(IReadOnlyList<RuleItem>, string)> Of(IReadOnlyList<FormRule> rules, string path) =>
            rules.Select((rule, i) => (rule.Conditions, JsonPaths.Property(JsonPaths.Index(JsonPaths.Property(path, "rules"), i), "conditions")));

        var pages = JsonPaths.Property(JsonPaths.Root, "pages");
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = JsonPaths.Index(pages, p);
            foreach (var conditions in Of(definition.Pages[p].Rules, page))
            {
                yield return conditions;
            }

            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                foreach (var conditions in Of(definition.Pages[p].Groups[g].Rules, JsonPaths.Index(JsonPaths.Property(page, "groups"), g)))
                {
                    yield return conditions;
                }
            }
        }

        foreach (var (field, path) in DefinitionFields.Of(definition))
        {
            foreach (var conditions in Of(field.Rules, path))
            {
                yield return conditions;
            }
        }

        var validation = JsonPaths.Property(JsonPaths.Root, "validationRules");
        for (var v = 0; v < definition.ValidationRules.Count; v++)
        {
            yield return (definition.ValidationRules[v].Conditions, JsonPaths.Property(JsonPaths.Index(validation, v), "conditions"));
        }

        var outcomes = JsonPaths.Property(JsonPaths.Root, "outcomes");
        for (var o = 0; o < definition.Outcomes.Count; o++)
        {
            foreach (var conditions in Of(definition.Outcomes[o].Rules, JsonPaths.Index(outcomes, o)))
            {
                yield return conditions;
            }
        }

        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var a = 0; a < definition.Actions.Count; a++)
        {
            foreach (var conditions in Of(definition.Actions[a].Rules, JsonPaths.Index(actions, a)))
            {
                yield return conditions;
            }
        }
    }

    // Each group a condition on the member's groups names, by name, with its path; nested groups' conditions too.
    private static IEnumerable<(string Name, string Path)> GroupNames(IReadOnlyList<RuleItem> items, string path)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = JsonPaths.Index(path, i);
            if (items[i] is RuleGroup group)
            {
                foreach (var named in GroupNames(group.Conditions, JsonPaths.Property(itemPath, "conditions")))
                {
                    yield return named;
                }
            }
            else if (items[i] is RuleCondition { Context: "memberGroup", Value: { } value })
            {
                var valuePath = JsonPaths.Property(itemPath, "value");
                if (value is ConditionOperand.Text text)
                {
                    yield return (text.Value, valuePath);
                }
                else if (value is ConditionOperand.List list)
                {
                    for (var k = 0; k < list.Items.Count; k++)
                    {
                        if (list.Items[k] is ConditionOperand.Text item)
                        {
                            yield return (item.Value, JsonPaths.Index(valuePath, k));
                        }
                    }
                }
            }
        }
    }

    private async Task RedirectsAsync(FormDefinition definition, List<FormCheckIssue> issues)
    {
        var outcomes = JsonPaths.Property(JsonPaths.Root, "outcomes");
        for (var o = 0; o < definition.Outcomes.Count; o++)
        {
            if (definition.Outcomes[o].Redirect?.Content is not { } page)
            {
                continue;
            }

            var path = JsonPaths.Property(JsonPaths.Property(JsonPaths.Index(outcomes, o), "redirect"), "content");
            if (!documents.TryGetParentKey(page, out _))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, ContentMissing, path, $"The site has no page with this key, {Gone}, so visitors get the outcome's message or the built-in thank-you message instead."));
            }
            else if (await published.GetByIdAsync(page, preview: false) is null)
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, RedirectUnpublished, path, "This page is not published, so visitors get the outcome's message or the built-in thank-you message until it is."));
            }
        }
    }

    // The settings in the schema's order that pick content or media, a list's items and a nested object's own in turn; a
    // value of another shape is the settings check's to refuse.
    private void Settings(List<FormCheckIssue> issues, string path, IReadOnlyList<FieldSettingDescriptor> schema, JsonElement value)
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
                Settings(issues, settingPath, setting.Settings, given);
            }
            else if (setting.Kind == FieldSettingKind.Key || setting is { Kind: FieldSettingKind.List, ItemKind: FieldSettingKind.Key })
            {
                foreach (var (key, keyPath) in Keys(given, settingPath))
                {
                    if (ContentEditors.Contains(setting.Editor))
                    {
                        Content(issues, key, keyPath);
                    }
                    else if (setting.Editor == MediaEditor)
                    {
                        Media(issues, key, keyPath);
                    }
                }
            }
        }
    }

    // The keys a setting holds: one, or a list's, each with its path.
    private static IEnumerable<(Guid Key, string Path)> Keys(JsonElement given, string path)
    {
        if (given.ValueKind == JsonValueKind.String && Guid.TryParse(given.GetString(), out var key))
        {
            yield return (key, path);
        }
        else if (given.ValueKind == JsonValueKind.Array)
        {
            var i = 0;
            foreach (var item in given.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var listed))
                {
                    yield return (listed, JsonPaths.Index(path, i));
                }

                i++;
            }
        }
    }

    private void Content(List<FormCheckIssue> issues, Guid key, string path)
    {
        if (!documents.TryGetParentKey(key, out _))
        {
            issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, ContentMissing, path, $"The site has no content item with this key, {Gone}."));
        }
    }

    private void Media(List<FormCheckIssue> issues, Guid key, string path)
    {
        if (!media.TryGetParentKey(key, out _))
        {
            issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, MediaMissing, path, $"The site has no media item with this key, {Gone}."));
        }
    }

    private async Task MemberGroupsAsync(FormDefinition definition, List<FormCheckIssue> issues)
    {
        var keys = definition.Settings.Availability.MemberGroups;
        var names = ConditionsOf(definition).SelectMany(found => GroupNames(found.Conditions, found.Path)).ToList();
        if (keys.Count == 0 && names.Count == 0)
        {
            return;
        }

        var groups = (await memberGroups.GetAllAsync()).ToList();
        var known = groups.Select(group => group.Key).ToHashSet();
        var path = JsonPaths.Property(JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "settings"), "availability"), "memberGroups");
        for (var i = 0; i < keys.Count; i++)
        {
            if (!known.Contains(keys[i]))
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, MemberGroupMissing, JsonPaths.Index(path, i), "The site has no member group with this key, so none of its members can fill in the form."));
            }
        }

        // A rule names a group by its key, or by its name exactly, as the website compares a member's groups.
        var named = groups.Select(group => group.Name).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var (name, at) in names)
        {
            if (Guid.TryParse(name, out var key))
            {
                if (!known.Contains(key))
                {
                    issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, MemberGroupMissing, at, "The site has no member group with this key, so no member is in it."));
                }
            }
            else if (!named.Contains(name))
            {
                var spelt = named.FirstOrDefault(group => string.Equals(group, name, StringComparison.OrdinalIgnoreCase));
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, MemberGroupMissing, at, spelt is null
                    ? $"The site has no member group named '{name}', so no member is in it."
                    : $"The site has no member group named '{name}', so no member is in it; names are compared exactly, and the site has '{spelt}'."));
            }
        }
    }
}

/// <summary>
/// A text that names an item of Umbraco's dictionary without a text (<c>#key</c>, ROADMAP 9.5e, ADR-0002, ADR-0067,
/// ADR-0180): a warning, since the text then shows as it is written. Each of the site's languages is asked as its
/// visitors see the form: the text of its culture, falling back as a request does, and the item's text through
/// Umbraco's languages. The warning names the languages in which the item has none, at the culture's text. A merge tag
/// of the dictionary renders empty with a warning of its own when an item is not there (ADR-0067).
/// </summary>
/// <param name="types">The field types, whose settings schemas say which settings are localised texts.</param>
/// <param name="dictionary">The dictionary.</param>
/// <param name="languages">The site's languages.</param>
internal sealed class DictionaryFormCheck(FieldTypeRegistry types, MergeTagDictionary dictionary, ILanguageService languages) : IFormCheck
{
    /// <summary>A text that names a dictionary item without a text (a warning).</summary>
    public const string DictionaryTextMissing = "dictionary_text_missing";

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var texts = FormTexts.Of(definition, types);
        if (!texts.Any(text => text.Text.Texts.Any(entry => LocalizedString.TryGetDictionaryKey(entry.Text, out _))))
        {
            return [];
        }

        // Each culture's text that names an item, in the form's order, with the languages whose visitors see it.
        var named = new List<(string Path, string Key, List<string> Languages)>();
        var byPath = new Dictionary<string, int>(StringComparer.Ordinal);
        var asked = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var language in (await languages.GetAllAsync()).Select(language => language.IsoCode))
        {
            foreach (var text in texts)
            {
                var entry = text.Text.EntryFor(language);
                if (!LocalizedString.TryGetDictionaryKey(entry.Text, out var key))
                {
                    continue;
                }

                var path = text.PathOf(entry);
                if (!byPath.TryGetValue(path, out var at))
                {
                    at = named.Count;
                    byPath[path] = at;
                    named.Add((path, key, []));
                }

                named[at].Languages.Add(language);
                if (!asked.TryGetValue(language, out var keys))
                {
                    keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    asked[language] = keys;
                }

                keys.Add(key);
            }
        }

        var found = new Dictionary<string, DictionaryTexts>(StringComparer.Ordinal);
        foreach (var (language, keys) in asked)
        {
            found[language] = await dictionary.TextsAsync(keys, language);
        }

        var issues = new List<FormCheckIssue>();
        foreach (var (path, key, seenIn) in named)
        {
            var lacking = seenIn.Where(language => !found[language].Has(key)).ToList();
            if (lacking.Count > 0)
            {
                issues.Add(new FormCheckIssue(FormCheckSeverity.Warning, DictionaryTextMissing, path, $"Umbraco's dictionary has no text for '{key}' in {string.Join(", ", lacking)}, so visitors there see the text as it is written."));
            }
        }

        return issues;
    }
}
