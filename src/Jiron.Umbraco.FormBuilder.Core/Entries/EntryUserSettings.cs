using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>A filter set a user saved (ADR-0207).</summary>
/// <param name="Key">The set.</param>
/// <param name="Name">Its name, 1 to <see cref="EntryUserSettings.MaxSetNameLength"/> characters.</param>
/// <param name="Form">The form it belongs to, or none for a set of every form's entries.</param>
/// <param name="Filter">What it filters.</param>
internal sealed record EntryFilterSet(Guid Key, string Name, Guid? Form, EntryListFilter Filter);

/// <summary>What the export dialog last exported of a form, which it opens with (ADR-0210).</summary>
/// <param name="Format">The format's name.</param>
/// <param name="Fields">The fields chosen, or null for every field.</param>
/// <param name="Sensitive">Whether sensitive fields went along.</param>
/// <param name="Options">The delimiter, the headers and the byte-order mark.</param>
internal sealed record EntryExportChoices(string Format, IReadOnlyList<Guid>? Fields, bool Sensitive, FormBuilderExportOptions Options);

/// <summary>
/// A user's own choices for the entries lists (ADR-0207) in <c>jfbUserSetting</c> (ADR-0008): the columns chosen for
/// each form, the filter sets and each form's last export (ADR-0210), the same on every device. It checks their shape; <see cref="EntryLists"/> checks
/// them against the forms. A stored value it cannot read counts as none, so a list always opens.
/// </summary>
/// <param name="settings">The user settings.</param>
internal sealed class EntryUserSettings(IUserSettingRepository settings)
{
    /// <summary>The most columns a form's list shows beside its details.</summary>
    public const int MaxColumns = 20;

    /// <summary>The most filter sets a user keeps.</summary>
    public const int MaxFilterSets = 50;

    /// <summary>The longest name of a filter set.</summary>
    public const int MaxSetNameLength = 60;

    /// <summary>The name the filter sets are stored under.</summary>
    public const string FilterSetsName = "entries.filterSets";

    /// <summary>The start of the name a form's columns are stored under; the form's key follows.</summary>
    public const string ColumnsPrefix = "entries.columns.";

    /// <summary>The start of the name a form's last export is stored under; the form's key follows.</summary>
    public const string ExportPrefix = "entries.export.";

    /// <summary>What the user last exported of a form; null until they export it.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="formKey">The form.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<EntryExportChoices?> GetExportChoicesAsync(Guid userKey, Guid formKey, CancellationToken cancellationToken = default) =>
        Read<EntryExportChoices>(await settings.GetAsync(userKey, ExportName(formKey), cancellationToken)) is { Format: not null, Options: not null } choices
            ? choices with { Fields = choices.Fields?.Distinct().ToList() }
            : null;

    /// <summary>Keeps what the user exported of a form, for the dialog to open with next time.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="formKey">The form.</param>
    /// <param name="choices">The choices, as the export checked them.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    public Task SetExportChoicesAsync(Guid userKey, Guid formKey, EntryExportChoices choices, CancellationToken cancellationToken = default) =>
        settings.SetAsync(userKey, ExportName(formKey), JsonSerializer.Serialize(choices, EntryListJson.Options), cancellationToken);

    /// <summary>The fields a user chose as a form's columns, in their order; none until they choose.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="formKey">The form.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<IReadOnlyList<Guid>> GetColumnsAsync(Guid userKey, Guid formKey, CancellationToken cancellationToken = default) =>
        Read<List<Guid>>(await settings.GetAsync(userKey, ColumnsName(formKey), cancellationToken)) is { } columns
            ? [.. columns.Distinct().Take(MaxColumns)]
            : [];

    /// <summary>Keeps the fields a user chose as a form's columns; no columns removes the setting.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="formKey">The form.</param>
    /// <param name="columns">The fields, at most <see cref="MaxColumns"/>, each once.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <exception cref="FormBuilderValidationException">Too many columns, or one twice.</exception>
    public async Task SetColumnsAsync(Guid userKey, Guid formKey, IReadOnlyList<Guid> columns, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Count > MaxColumns)
        {
            throw FormBuilderValidationException.For("too_many_columns", "columns", $"A list shows at most {MaxColumns} columns.");
        }

        if (columns.Distinct().Count() != columns.Count)
        {
            throw FormBuilderValidationException.For("column_twice", "columns", "A list shows each field once.");
        }

        if (columns.Count == 0)
        {
            await settings.DeleteAsync(userKey, ColumnsName(formKey), cancellationToken);
            return;
        }

        await settings.SetAsync(userKey, ColumnsName(formKey), JsonSerializer.Serialize(columns, EntryListJson.Options), cancellationToken);
    }

    /// <summary>The user's filter sets, in the order they were first saved; a set that cannot be read is left out.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<IReadOnlyList<EntryFilterSet>> GetFilterSetsAsync(Guid userKey, CancellationToken cancellationToken = default) =>
        Read<List<EntryFilterSet>>(await settings.GetAsync(userKey, FilterSetsName, cancellationToken)) is { } sets
            ? [.. sets.Where(set => set is { Filter: not null } && set.Key != Guid.Empty && IsName(set.Name)).DistinctBy(set => set.Key).Take(MaxFilterSets)]
            : [];

    /// <summary>
    /// Saves a filter set: a new one after the others, one with a known key in its place. Its name is trimmed, and no
    /// other set of the same form, or of every form, has it, whatever the case.
    /// </summary>
    /// <param name="userKey">The user.</param>
    /// <param name="set">The set, its filter checked against its form already.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <returns>The set as saved.</returns>
    /// <exception cref="FormBuilderValidationException">An empty or long name, one taken, or one set too many.</exception>
    public async Task<EntryFilterSet> SaveFilterSetAsync(Guid userKey, EntryFilterSet set, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(set);
        if (set.Key == Guid.Empty)
        {
            throw FormBuilderValidationException.For("invalid_value", "key", "A filter set has a key.");
        }

        var name = (set.Name ?? string.Empty).Trim();
        if (!IsName(name))
        {
            throw FormBuilderValidationException.For("invalid_name", "name", $"A filter set's name has 1 to {MaxSetNameLength} characters.");
        }

        var saved = set with { Name = name };
        var sets = (await GetFilterSetsAsync(userKey, cancellationToken)).ToList();
        if (sets.Any(other => other.Key != saved.Key && other.Form == saved.Form && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw FormBuilderValidationException.For("name_taken", "name", "Another filter set has this name.");
        }

        var index = sets.FindIndex(other => other.Key == saved.Key);
        if (index >= 0)
        {
            sets[index] = saved;
        }
        else if (sets.Count >= MaxFilterSets)
        {
            throw FormBuilderValidationException.For("too_many_filter_sets", "key", $"You keep at most {MaxFilterSets} filter sets. Delete one first.");
        }
        else
        {
            sets.Add(saved);
        }

        await settings.SetAsync(userKey, FilterSetsName, JsonSerializer.Serialize(sets, EntryListJson.Options), cancellationToken);
        return saved;
    }

    /// <summary>Deletes a filter set; false when the user has none with that key.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="key">The set.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    public async Task<bool> DeleteFilterSetAsync(Guid userKey, Guid key, CancellationToken cancellationToken = default)
    {
        var sets = (await GetFilterSetsAsync(userKey, cancellationToken)).ToList();
        if (sets.RemoveAll(set => set.Key == key) == 0)
        {
            return false;
        }

        if (sets.Count == 0)
        {
            await settings.DeleteAsync(userKey, FilterSetsName, cancellationToken);
        }
        else
        {
            await settings.SetAsync(userKey, FilterSetsName, JsonSerializer.Serialize(sets, EntryListJson.Options), cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Forgets a form deleted for good in every user's choices: its columns and its last export go, and so do the filter
    /// sets of its own list; an Inbox set no longer names it, and one that named only forms that are gone goes too, rather
    /// than come to mean every form.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="cancellationToken">Stops the reads and writes.</param>
    public async Task ForgetFormAsync(Guid formKey, CancellationToken cancellationToken = default)
    {
        foreach (var name in new[] { ColumnsName(formKey), ExportName(formKey) })
        {
            foreach (var stored in await settings.ListAsync(name, cancellationToken))
            {
                await settings.DeleteAsync(stored.UserKey, name, cancellationToken);
            }
        }

        foreach (var stored in await settings.ListAsync(FilterSetsName, cancellationToken))
        {
            if (Read<List<EntryFilterSet>>(stored.Json) is not { } sets || !sets.Any(set => set?.Form == formKey || set?.Filter?.Forms.Contains(formKey) == true))
            {
                continue;
            }

            var kept = new List<EntryFilterSet>();
            foreach (var set in sets.Where(set => set is not null && set.Form != formKey))
            {
                if (set.Filter is { } filter && filter.Forms.Contains(formKey))
                {
                    var forms = filter.Forms.Where(key => key != formKey).ToList();
                    if (forms.Count > 0)
                    {
                        kept.Add(set with { Filter = filter with { Forms = forms } });
                    }

                    continue;
                }

                kept.Add(set);
            }

            if (kept.Count == 0)
            {
                await settings.DeleteAsync(stored.UserKey, FilterSetsName, cancellationToken);
            }
            else
            {
                await settings.SetAsync(stored.UserKey, FilterSetsName, JsonSerializer.Serialize(kept, EntryListJson.Options), cancellationToken);
            }
        }
    }

    /// <summary>The name a form's columns are stored under.</summary>
    internal static string ColumnsName(Guid formKey) => ColumnsPrefix + formKey.ToString("D");

    /// <summary>The name a form's last export is stored under.</summary>
    internal static string ExportName(Guid formKey) => ExportPrefix + formKey.ToString("D");

    private static bool IsName(string? name) => name is { Length: > 0 and <= MaxSetNameLength } && name.Trim().Length == name.Length;

    private static T? Read<T>(string? json)
        where T : class
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, EntryListJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
