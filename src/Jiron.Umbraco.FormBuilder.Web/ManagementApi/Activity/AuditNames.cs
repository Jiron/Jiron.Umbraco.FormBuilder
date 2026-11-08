using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Activity;

/// <summary>
/// The names the audit log shows for a page of actions (ADR-0219): the users who did them, and every item they are on or
/// name in their details, as it is named now. The details say by their property what a key is (<c>form</c>, <c>folder</c>,
/// <c>group</c>, <c>entry</c>, <c>field</c>), the action's entity type what its item is. An item that is gone gets no name.
/// </summary>
internal sealed class AuditNames(
    IUserService users,
    IUserGroupService userGroups,
    SettingsSites sites,
    IFormRepository forms,
    IFolderRepository folders,
    IOptionListRepository optionLists,
    IThemeRepository themes,
    IEmailLayoutRepository layouts,
    IFormTemplateRepository templates,
    IEntryRepository entries,
    FormDocuments documents)
{
    /// <summary>The names of a page: users by key, and items by key.</summary>
    public async Task<(IReadOnlyDictionary<Guid, string> Users, IReadOnlyDictionary<Guid, AuditNameResponseModel> Items)> OfAsync(
        IReadOnlyList<StoredAuditEntry> page,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        var userNames = await UserNames.OfAsync(users, page.Select(entry => entry.UserKey));
        var items = new Dictionary<Guid, AuditNameResponseModel>();
        var fieldsOfForms = new Dictionary<Guid, IReadOnlyDictionary<Guid, string>?>();
        IReadOnlyList<SettingsSite>? siteList = null;
        foreach (var entry in page)
        {
            using var details = JsonDocument.Parse(entry.Details);
            var root = details.RootElement;
            if (entry.EntityKey is { } key && !items.ContainsKey(key))
            {
                if (entry.EntityType == "settings")
                {
                    siteList ??= await sites.ListAsync(cancellationToken);
                    if (siteList.FirstOrDefault(site => site.Key == key) is { } site)
                    {
                        items[key] = new AuditNameResponseModel { Kind = "site", Name = site.Name };
                    }
                }
                else
                {
                    await AddAsync(items, entry.EntityType, key, cancellationToken);
                }
            }

            foreach (var property in root.EnumerateObject())
            {
                var kind = property.Name switch
                {
                    "form" or "source" => "form",
                    "template" => "template",
                    "group" => "userGroup",
                    "entry" => "entry",
                    "folder" or "parent" or "previousParent" or "previousFolder" => "folder",
                    _ => null,
                };
                if (kind is not null && property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var named) && !items.ContainsKey(named))
                {
                    await AddAsync(items, kind, named, cancellationToken);
                }
            }

            await AddFieldsAsync(items, fieldsOfForms, entry, root, cancellationToken);
        }

        return (userNames, items);
    }

    // The fields a revealed, edited or downloaded answer names, by their label in the form's draft.
    private async Task AddFieldsAsync(
        Dictionary<Guid, AuditNameResponseModel> items,
        Dictionary<Guid, IReadOnlyDictionary<Guid, string>?> fieldsOfForms,
        StoredAuditEntry entry,
        JsonElement root,
        CancellationToken cancellationToken)
    {
        var keys = new List<Guid>();
        if (root.TryGetProperty("field", out var one) && one.ValueKind == JsonValueKind.String && one.TryGetGuid(out var field))
        {
            keys.Add(field);
        }

        if (root.TryGetProperty("fields", out var many) && many.ValueKind == JsonValueKind.Array)
        {
            keys.AddRange(many.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String && item.TryGetGuid(out _)).Select(item => item.GetGuid()));
        }

        Guid? formKey = root.TryGetProperty("form", out var form) && form.ValueKind == JsonValueKind.String && form.TryGetGuid(out var key)
            ? key
            : entry.EntityType == FormAudit.EntityType ? entry.EntityKey : null;
        if (keys.Count == 0 || formKey is not { } formOf)
        {
            return;
        }

        if (!fieldsOfForms.TryGetValue(formOf, out var labels))
        {
            fieldsOfForms[formOf] = labels = await LabelsAsync(formOf, cancellationToken);
        }

        foreach (var fieldKey in keys)
        {
            if (labels?.GetValueOrDefault(fieldKey) is { } label)
            {
                items.TryAdd(fieldKey, new AuditNameResponseModel { Kind = "field", Name = label });
            }
        }
    }

    private async Task<IReadOnlyDictionary<Guid, string>?> LabelsAsync(Guid formKey, CancellationToken cancellationToken)
    {
        if (await forms.GetAsync(formKey, cancellationToken) is not { DraftVersionId: { } draft }
            || await forms.GetVersionByIdAsync(draft, cancellationToken) is not { } version)
        {
            return null;
        }

        var labels = new Dictionary<Guid, string>();
        foreach (var field in DefinitionKeys.Fields(documents.Readable(version)))
        {
            labels.TryAdd(field.Key, field.Label?.Default is { Length: > 0 } label ? label : field.Alias ?? field.Key.ToString("D"));
        }

        return labels;
    }

    private async Task AddAsync(Dictionary<Guid, AuditNameResponseModel> items, string kind, Guid key, CancellationToken cancellationToken)
    {
        AuditNameResponseModel? name = kind switch
        {
            "form" => await forms.GetAsync(key, cancellationToken) is { } form ? Named(kind, form.Name) : null,
            "folder" => await folders.GetAsync(key, cancellationToken) is { } folder ? Named(kind, folder.Name) : null,
            "optionList" => await optionLists.GetAsync(key, cancellationToken) is { } list ? Named(kind, list.Name) : null,
            "theme" => await themes.GetAsync(key, cancellationToken) is { } theme ? Named(kind, theme.Name) : null,
            "emailLayout" => await layouts.GetAsync(key, cancellationToken) is { } layout ? Named(kind, layout.Name) : null,
            "template" => await templates.GetAsync(key, cancellationToken) is { } template ? Named(kind, template.Name) : null,
            "userGroup" => await userGroups.GetAsync(key) is { } group ? Named("userGroup", group.Name ?? group.Alias) : null,
            "entry" => await EntryAsync(key, cancellationToken),
            _ => null,
        };
        if (name is not null)
        {
            items[key] = name;
        }
    }

    private async Task<AuditNameResponseModel?> EntryAsync(Guid key, CancellationToken cancellationToken) =>
        await entries.GetAsync(key, cancellationToken) is { } entry && await forms.GetByIdAsync(entry.FormId, cancellationToken) is { } form
            ? new AuditNameResponseModel { Kind = "entry", Name = form.Name, Number = entry.Number }
            : null;

    private static AuditNameResponseModel Named(string kind, string name) => new() { Kind = kind, Name = name };
}
