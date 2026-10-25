using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>A row of the field catalogue that a published definition has (ADR-0003, ADR-0044).</summary>
/// <param name="FieldKey">The field's key, or a grid row's.</param>
/// <param name="Alias">The field's alias; a grid row's is its grid's.</param>
/// <param name="Label">The default text of its label, empty without one.</param>
/// <param name="Type">The field type's alias.</param>
/// <param name="Storage">Which column holds its values.</param>
/// <param name="IsSensitive">Whether its values are sensitive (ADR-0009).</param>
/// <param name="ParentFieldKey">The repeatable group or grid it belongs to, or null.</param>
internal sealed record CatalogueEntry(Guid FieldKey, string Alias, string Label, string Type, FieldStorageKind Storage, bool IsSensitive, Guid? ParentFieldKey);

/// <summary>
/// What the field catalogue holds for a published definition, and how a publish updates it (ADR-0003, ADR-0044): a row
/// for every field that holds a value (the children of repeatable groups included), for each row of a grid, and for
/// the grid and the repeatable group themselves, in document order.
/// </summary>
internal static class FormCatalogue
{
    /// <summary>
    /// The rows of a definition in document order. A field whose type the site lacks has none; publishing refuses it
    /// anyway (ADR-0035).
    /// </summary>
    public static IReadOnlyList<CatalogueEntry> Of(FormDefinition definition, FieldTypeCatalog types)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(types);
        var entries = new List<CatalogueEntry>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            Add(field, null, types, entries);
        }

        return entries;
    }

    /// <summary>
    /// The values a publish writes for each row, and the stored rows it changes: <paramref name="entries"/> in document
    /// order, then the stored rows that the publish dropped. A new row starts at <paramref name="version"/>, a returning
    /// one is live again, and a dropped one ends at <paramref name="previousPublish"/>, the last publish that had it.
    /// Rows that need no change are left out.
    /// </summary>
    public static IReadOnlyList<(CatalogueFieldValues Values, bool IsNew)> Changes(
        IReadOnlyList<CatalogueEntry> entries,
        IReadOnlyList<CatalogueField> stored,
        int version,
        int? previousPublish)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(stored);
        var byKey = stored.ToDictionary(field => field.Values.FieldKey);
        var changes = new List<(CatalogueFieldValues, bool)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var current = byKey.GetValueOrDefault(entry.FieldKey)?.Values;
            var values = new CatalogueFieldValues(
                entry.FieldKey,
                entry.Alias,
                entry.Label,
                entry.Type,
                entry.Storage,
                entry.IsSensitive,
                index,
                current?.FirstVersionNumber ?? version,
                LastVersionNumber: null,
                entry.ParentFieldKey);
            if (current is null)
            {
                changes.Add((values, true));
            }
            else if (values != current)
            {
                changes.Add((values, false));
            }
        }

        var published = entries.Select(entry => entry.FieldKey).ToHashSet();
        foreach (var field in stored.Select(row => row.Values))
        {
            // A row of an earlier publish that this one dropped ends where the last publish that had it was; a row only a
            // draft has (preview entries, ADR-0003) stays as it is.
            if (!published.Contains(field.FieldKey) && field.FirstVersionNumber is { } first && field.LastVersionNumber is null)
            {
                changes.Add((field with { LastVersionNumber = previousPublish ?? first }, false));
            }
        }

        return changes;
    }

    private static void Add(FormField field, Guid? parent, FieldTypeCatalog types, List<CatalogueEntry> entries)
    {
        if (types.Find(field.Type) is not { HoldsValue: true } type || field.Alias is not { } alias)
        {
            return;
        }

        entries.Add(new CatalogueEntry(field.Key, alias, field.Label?.Default ?? string.Empty, field.Type, type.Storage, field.Sensitive, parent));
        foreach (var row in field.Rows)
        {
            entries.Add(new CatalogueEntry(row.Key, alias, row.Label?.Default ?? string.Empty, field.Type, FieldStorageKind.Text, field.Sensitive, field.Key));
        }

        foreach (var child in field.Fields)
        {
            Add(child, field.Key, types, entries);
        }
    }
}
