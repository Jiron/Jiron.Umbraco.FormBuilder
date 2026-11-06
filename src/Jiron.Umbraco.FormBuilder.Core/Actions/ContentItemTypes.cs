using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>A property of a document type that a content action can set (ADR-0192).</summary>
/// <param name="Alias">The property's alias, which the mapping names.</param>
/// <param name="Name">The property's name, as Umbraco shows it.</param>
/// <param name="Group">The name of the group it sits in; null for none.</param>
/// <param name="FieldTypes">The field types whose answer it can hold, in the palette's order.</param>
/// <param name="Value">
/// What a value written for it must be: <c>text</c>, which may hold merge tags, <c>wholeNumber</c>, <c>decimal</c>,
/// <c>date</c> or <c>boolean</c>; null where it takes only a field's answer.
/// </param>
internal sealed record ContentItemProperty(string Alias, string Name, string? Group, IReadOnlyList<string> FieldTypes, string? Value);

/// <summary>A document type a content action can create, with the properties it can set (ADR-0192).</summary>
/// <param name="Alias">The type's alias, which the action names.</param>
/// <param name="Name">The type's name.</param>
/// <param name="Properties">Its properties, its compositions' included, in the order its groups show them.</param>
internal sealed record ContentItemType(string Alias, string Name, IReadOnlyList<ContentItemProperty> Properties);

/// <summary>
/// The document types a content action can create, each with the properties a form can set (ADR-0192): those whose
/// editor holds a field's answer or a value written for it, by the content action's own rules. Umbraco shows document
/// types only to users of its Settings section, so the designer asks here.
/// </summary>
/// <param name="contentTypes">The site's document types.</param>
/// <param name="fieldTypes">The site's field types.</param>
internal sealed class ContentItemTypes(IContentTypeService contentTypes, FieldTypeRegistry fieldTypes)
{
    /// <summary>A document type a content item may have, with what a form can set of it; null for none, or for an element type.</summary>
    /// <param name="alias">The type's alias.</param>
    public ContentItemType? Find(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias) || contentTypes.Get(alias) is not { IsElement: false } type)
        {
            return null;
        }

        var properties = new List<ContentItemProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(IPropertyType property, string? group)
        {
            if (!seen.Add(property.Alias))
            {
                return;
            }

            var editor = property.PropertyEditorAlias;
            var fits = fieldTypes.All.Where(traits => traits.HoldsValue && ContentActionType.Fits(traits.Alias, editor)).Select(traits => traits.Alias).ToList();
            var value = ContentActionType.ValueKindOf(editor);
            if (fits.Count > 0 || value is not null)
            {
                properties.Add(new ContentItemProperty(property.Alias, property.Name ?? property.Alias, group, fits, value));
            }
        }

        // In the order Umbraco shows them: the groups outside every tab, then each tab, its own properties before its groups.
        // A tab's group is named after it (tab/group); a type and its compositions' containers of one alias are one.
        var containers = type.CompositionPropertyGroups
            .GroupBy(container => (container.Type, container.Alias))
            .Select(same => new Container(
                same.Key.Type,
                same.Key.Alias,
                same.First().Name ?? same.Key.Alias,
                same.Min(container => container.SortOrder),
                [.. same.SelectMany(container => container.PropertyTypes ?? Enumerable.Empty<IPropertyType>()).OrderBy(property => property.SortOrder)]))
            .ToList();
        void AddAll(Container container)
        {
            foreach (var property in container.Properties)
            {
                Add(property, container.Name);
            }
        }

        foreach (var group in GroupsIn(containers, tab: null))
        {
            AddAll(group);
        }

        foreach (var tab in containers.Where(container => container.Type == PropertyGroupType.Tab).OrderBy(container => container.SortOrder))
        {
            AddAll(tab);
            foreach (var group in GroupsIn(containers, tab.Alias))
            {
                AddAll(group);
            }
        }

        foreach (var property in type.CompositionPropertyTypes.OrderBy(property => property.SortOrder))
        {
            Add(property, null);
        }

        return new ContentItemType(type.Alias, type.Name ?? type.Alias, properties);
    }

    // The groups in a tab, or outside every tab, in their order.
    private static IEnumerable<Container> GroupsIn(List<Container> containers, string? tab) =>
        containers
            .Where(container => container.Type == PropertyGroupType.Group && TabOf(container.Alias) == tab)
            .OrderBy(container => container.SortOrder);

    private static string? TabOf(string alias) => alias.IndexOf('/', StringComparison.Ordinal) is var slash and > 0 ? alias[..slash] : null;

    // A tab or a group of the type and its compositions, with its properties.
    private sealed record Container(PropertyGroupType Type, string Alias, string Name, int SortOrder, IReadOnlyList<IPropertyType> Properties);
}
