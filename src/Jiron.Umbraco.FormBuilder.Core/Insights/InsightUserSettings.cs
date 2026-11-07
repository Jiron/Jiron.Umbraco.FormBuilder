using System.Collections.Frozen;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Insights;

/// <summary>
/// A user's own choice of the insights' boxes that show their table in place of their chart (ADR-0211), in
/// <c>jfbUserSetting</c> under <see cref="TablesName"/>, the same on every device and for every form. A stored value it
/// cannot read counts as none.
/// </summary>
/// <param name="settings">The user settings.</param>
internal sealed class InsightUserSettings(IUserSettingRepository settings)
{
    /// <summary>The name the choice is stored under.</summary>
    public const string TablesName = "insights.tables";

    /// <summary>The boxes with a chart: entries per period, the pages reached, the fields that stop visitors, the website pages, the hours and the answers.</summary>
    public static readonly FrozenSet<string> Boxes = FrozenSet.Create(StringComparer.Ordinal, "entries", "funnel", "fields", "origins", "hours", "answers");

    /// <summary>The boxes the user shows as tables.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    public async Task<IReadOnlyList<string>> GetTablesAsync(Guid userKey, CancellationToken cancellationToken = default)
    {
        var stored = await settings.GetAsync(userKey, TablesName, cancellationToken);
        try
        {
            return stored is null ? [] : [.. (JsonSerializer.Deserialize<List<string>>(stored) ?? []).Where(Boxes.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Keeps the boxes the user shows as tables; none removes the setting.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="tables">The boxes, each one of <see cref="Boxes"/>.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    /// <exception cref="FormBuilderValidationException">A box that is none of them.</exception>
    public async Task SetTablesAsync(Guid userKey, IReadOnlyCollection<string> tables, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.FirstOrDefault(box => !Boxes.Contains(box)) is { } unknown)
        {
            throw FormBuilderValidationException.For("unknown_box", "tables", $"'{unknown}' is no box of insights.");
        }

        if (tables.Count == 0)
        {
            await settings.DeleteAsync(userKey, TablesName, cancellationToken);
            return;
        }

        await settings.SetAsync(userKey, TablesName, JsonSerializer.Serialize(tables.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()), cancellationToken);
    }
}
