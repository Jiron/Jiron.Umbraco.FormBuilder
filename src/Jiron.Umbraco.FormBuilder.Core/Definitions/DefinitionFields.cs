using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>Every field of a definition with its JSON path (ADR-0035), as the locks and the form check walk them.</summary>
internal static class DefinitionFields
{
    /// <summary>Every field with its path, in document order: a group's fields, each followed by its child fields.</summary>
    /// <param name="definition">The definition.</param>
    public static IEnumerable<(FormField Field, string Path)> Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        for (var p = 0; p < definition.Pages.Count; p++)
        {
            var page = JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "pages"), p);
            for (var g = 0; g < definition.Pages[p].Groups.Count; g++)
            {
                foreach (var field in Of(definition.Pages[p].Groups[g].Fields, JsonPaths.Index(JsonPaths.Property(page, "groups"), g)))
                {
                    yield return field;
                }
            }
        }
    }

    private static IEnumerable<(FormField Field, string Path)> Of(IReadOnlyList<FormField> fields, string parent)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            var path = JsonPaths.Index(JsonPaths.Property(parent, "fields"), i);
            yield return (fields[i], path);
            foreach (var child in Of(fields[i].Fields, path))
            {
                yield return child;
            }
        }
    }
}
