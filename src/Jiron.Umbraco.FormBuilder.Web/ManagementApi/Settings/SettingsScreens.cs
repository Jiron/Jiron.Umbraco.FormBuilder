using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;

/// <summary>A settings section's screen (ADR-0218): its values in a scope, a save of its fields, and what it says besides.</summary>
internal interface ISettingsScreen
{
    /// <summary>The section's alias.</summary>
    string Section { get; }

    /// <summary>Whether a site can set the section's fields of its own.</summary>
    bool PerSite { get; }

    /// <summary>The section in a scope: all sites without a site.</summary>
    Task<SettingsSectionResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken);

    /// <summary>Saves the fields a request sends in its scope, and answers the section as it now is there.</summary>
    /// <exception cref="FormBuilderSettingsValidationException">A field is not the section's, or a value is not allowed.</exception>
    Task<SettingsSectionResponseModel> SaveAsync(SettingsSectionRequestModel request, Guid? userKey, CancellationToken cancellationToken);
}

/// <summary>What a section's screen says besides its fields, from the section's own module.</summary>
/// <typeparam name="T">The section.</typeparam>
internal interface ISettingsFacts<T>
    where T : class, ISettingsSection, new()
{
    /// <summary>The section's part of the facts in a scope.</summary>
    Task<SettingsFactsResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <param name="settings">The settings service.</param>
/// <param name="sites">Umbraco's content roots, which are the sites.</param>
/// <param name="facts">The section's own facts, if it has some.</param>
internal sealed class SettingsScreen<T>(IFormBuilderSettingsService settings, SettingsSites sites, IEnumerable<ISettingsFacts<T>> facts) : ISettingsScreen
    where T : class, ISettingsSection, new()
{
    // The section's fields, read from its record once: the screen lives as long as the application.
    private readonly IReadOnlyList<SettingsFieldDescriptor> _schema = SettingsSchema.Of<T>();

    /// <inheritdoc />
    public string Section => T.Alias;

    /// <inheritdoc />
    public bool PerSite => T.PerSite;

    /// <inheritdoc />
    public async Task<SettingsSectionResponseModel> ReadAsync(Guid? site, CancellationToken cancellationToken) =>
        await ModelAsync(await settings.GetAsync<T>(Scope(site), cancellationToken), Scope(site), cancellationToken);

    /// <inheritdoc />
    public async Task<SettingsSectionResponseModel> SaveAsync(SettingsSectionRequestModel request, Guid? userKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = Fields(request.Values);
        var site = Scope(request.Site);
        SettingsSnapshot<T> saved;
        if (site is { } key)
        {
            // The site's own fields take the values sent; the screen sends every field the site sets after the save.
            var current = await settings.GetAsync<T>(key, cancellationToken);
            saved = await settings.SaveSiteAsync(key, Merge(current.Value, values), request.Overrides ?? [], request.Version, userKey, cancellationToken);
        }
        else
        {
            // The fields sent change in the stored values, as the save reads them; every other field stays as stored.
            saved = await settings.UpdateAsync<T>(stored => Merge(stored, values), request.Version, userKey, cancellationToken);
        }

        return await ModelAsync(saved, site, cancellationToken);
    }

    // A site counts only for a section a site can set; all sites otherwise, as the service reads them (ADR-0043).
    private static Guid? Scope(Guid? site) => T.PerSite ? site : null;

    // The fields of a request, each one of the section's; any other name is refused, as nothing would keep it.
    private static JsonObject Fields(JsonElement values)
    {
        var errors = new SettingsErrors(T.Alias);
        if (values.ValueKind != JsonValueKind.Object || JsonNode.Parse(values.GetRawText()) is not JsonObject fields)
        {
            errors.Add("values", SettingsErrors.Range, "Must be an object of the section's fields by their names.");
            throw new FormBuilderSettingsValidationException(errors.Errors);
        }

        var names = SettingsSectionJson.FieldNames<T>();
        foreach (var (name, _) in fields)
        {
            if (!names.Contains(name))
            {
                errors.Add(name, SettingsErrors.Range, "Is no field of the section.");
            }
        }

        return errors.Errors.Count > 0 ? throw new FormBuilderSettingsValidationException(errors.Errors) : fields;
    }

    // The section with the fields sent in place of its own; a value of the wrong type is refused at its field.
    private static T Merge(T section, JsonObject fields)
    {
        var node = SettingsSectionJson.ToNode(section);
        foreach (var (name, value) in fields)
        {
            node[name] = value?.DeepClone();
        }

        try
        {
            return SettingsSectionJson.Read<T>(node);
        }
        catch (JsonException exception)
        {
            var errors = new SettingsErrors(T.Alias);
            var field = exception.Path is { Length: > 2 } path ? path[2..] : "values";
            errors.Add(field, SettingsErrors.Range, "Is not a value this field takes.");
            throw new FormBuilderSettingsValidationException(errors.Errors);
        }
    }

    private async Task<SettingsSectionResponseModel> ModelAsync(SettingsSnapshot<T> snapshot, Guid? site, CancellationToken cancellationToken)
    {
        var global = site is null ? null : await settings.GetAsync<T>(null, cancellationToken);
        var own = T.PerSite ? await settings.SitesOfAsync<T>(cancellationToken) : [];
        var section = facts.FirstOrDefault();
        var fields = SettingsFieldModels.Of(_schema, T.Alias, snapshot);
        return new SettingsSectionResponseModel
        {
            Section = T.Alias,
            Site = site,
            Version = snapshot.Version,
            Values = JsonSerializer.SerializeToElement(SettingsSectionJson.ToNode(snapshot.Value)),
            GlobalValues = global is null ? null : JsonSerializer.SerializeToElement(SettingsSectionJson.ToNode(global.Value)),
            Overrides = [.. snapshot.Overrides.Order(StringComparer.Ordinal)],
            SiteFields = T.PerSite ? [.. SettingsSectionJson.SiteFieldNames<T>().Order(StringComparer.Ordinal)] : [],
            Sites = T.PerSite ? [.. (await sites.ListAsync(cancellationToken)).Select(each => new SettingsSiteResponseModel(each.Key, each.Name, own.Contains(each.Key)))] : [],
            Fields = fields,
            Locks = [.. snapshot.Locks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => SettingsLockResponseModel.Of(pair.Key, pair.Value))],
            Facts = section is null ? new SettingsFactsResponseModel() : await section.ReadAsync(site, cancellationToken),
        };
    }
}

/// <summary>A section's fields in the shape the inspector's editors draw (ADR-0170, ADR-0218).</summary>
internal static class SettingsFieldModels
{
    /// <summary>
    /// The models of fields, each locked field, or every field of a locked section, read-only with its value and the key
    /// that locks it. A field the host restricts or clamps stays editable: its screen names the key where it refuses a value.
    /// </summary>
    public static IReadOnlyList<FieldSettingResponseModel> Of<T>(IReadOnlyList<SettingsFieldDescriptor> fields, string section, SettingsSnapshot<T> snapshot)
        where T : class, ISettingsSection, new()
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = SettingsSectionJson.ToNode(snapshot.Value);
        return Of(fields, section, string.Empty, values, snapshot.Locks);
    }

    private static List<FieldSettingResponseModel> Of(IReadOnlyList<SettingsFieldDescriptor> fields, string section, string prefix, JsonObject values, IReadOnlyDictionary<string, SettingsLock> locks) =>
        [.. fields.Select(field => Of(field, section, prefix + field.Name, values, locks))];

    private static FieldSettingResponseModel Of(SettingsFieldDescriptor field, string section, string path, JsonObject values, IReadOnlyDictionary<string, SettingsLock> locks)
    {
        var rule = locks.TryGetValue(path, out var own) && own.Kind == SettingsLockKind.Locked ? own : locks.GetValueOrDefault("*");
        var value = SettingsSectionJson.At(values, path);
        return new FieldSettingResponseModel
        {
            Name = field.Name,
            Kind = field.Kind,
            ItemKind = field.ItemKind,
            Choices = [.. field.Choices.Select(choice => new FieldSettingChoiceResponseModel(choice, $"#jfbGlobalSettings_{section}_{path.Replace('.', '_')}_{choice}"))],
            Settings = Of(field.Fields, section, path + ".", values, locks),
            Default = field.Default,
            Label = field.Label,
            Description = field.Description,
            Editor = field.Editor,
            Group = field.Box,
            SupportsMergeTags = false,
            IsPublic = false,
            Rules = new FieldSettingRulesResponseModel
            {
                Required = false,
                Minimum = field.Rules.Minimum,
                MinimumIsExclusive = false,
                Maximum = field.Rules.Maximum,
                MaximumIsExclusive = false,
                MaximumLength = field.Rules.MaximumLength,
                MaximumCount = field.Rules.MaximumCount,
                AllowedValues = [],
            },
            Lock = rule is null ? null : new FieldSettingLockResponseModel(false, rule.ConfigurationKey, value is null ? null : JsonSerializer.SerializeToElement(value)),
            Unit = field.Unit,
        };
    }
}
