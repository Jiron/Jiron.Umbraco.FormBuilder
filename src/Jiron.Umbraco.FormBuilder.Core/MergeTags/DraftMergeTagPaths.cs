using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>A path the inspector's tag picker offers (ADR-0173).</summary>
/// <param name="Path">The whole path, its source's name first, such as <c>field.email</c>.</param>
/// <param name="Source">The source's name, the path's first part.</param>
/// <param name="IsSiteSource">Whether the site added the source, so the backoffice has no words of its own for it.</param>
/// <param name="Label">A field's label in the culture asked, or what the site's source shows; null to show the path.</param>
internal sealed record DraftMergeTagPath(string Path, string Source, bool IsSiteSource, string? Label);

/// <summary>
/// The paths a place's tags may use in the draft the designer holds, for the inspector's tag picker (ADR-0173): the merge
/// tag service's, in its order, each with its source, a field's with the field's label in the culture asked (the text of
/// the dictionary item a label names), and a site source's as it labels them. A visitor text offers only the answers of
/// the fields before it (ADR-0068), as the check of its tags allows.
/// </summary>
/// <param name="service">The merge tag service, which knows each place's sources.</param>
/// <param name="dictionary">Umbraco's dictionary, for a label that names an item.</param>
internal sealed class DraftMergeTagPaths(IFormBuilderMergeTagService service, MergeTagDictionary dictionary)
{
    private const string FieldPrefix = "field.";

    /// <summary>The paths of a place in a form, labelled in a culture.</summary>
    /// <param name="form">The draft's definition.</param>
    /// <param name="place">Where the tags are written.</param>
    /// <param name="culture">The culture of the labels; one that is no culture's name asks for the default texts.</param>
    /// <param name="owner">The key of the page, group or field whose text it is; null for a text after every field.</param>
    public async Task<IReadOnlyList<DraftMergeTagPath>> PathsAsync(FormDefinition form, MergeTagPlace place, string culture, Guid? owner = null)
    {
        ArgumentNullException.ThrowIfNull(form);
        var asked = AskedCulture.Of(culture);
        var keys = FieldKeys(form);
        var paths = service.Paths(place, form)
            .Where(path => place != MergeTagPlace.VisitorText || !keys.TryGetValue(path.Path, out var field) || MergeTagRecall.IsBefore(form, field, owner))
            .ToList();
        var labels = FieldLabels(form);
        var items = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (labels.TryGetValue(path.Path, out var label)
                && LocalizedString.TryGetDictionaryKey(label.Resolve(asked), out var item))
            {
                items.Add(item);
            }
        }

        var texts = items.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(items, asked);
        return
        [
            .. paths.Select(path =>
            {
                var source = path.Path.Split('.', 2)[0];
                var label = labels.TryGetValue(path.Path, out var text) ? texts.Resolve(text, asked) : path.Label;
                return new DraftMergeTagPath(path.Path, source, !MergeTagPlaces.BuiltInSources.ContainsKey(source), string.IsNullOrWhiteSpace(label) ? null : label);
            }),
        ];
    }

    // Each field's key by its path, as the field source names it: its alias, a repeatable group's child under the
    // group's alias.
    private static Dictionary<string, Guid> FieldKeys(FormDefinition form)
    {
        var keys = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (field.Alias is null)
            {
                continue;
            }

            keys.TryAdd(FieldPrefix + field.Alias, field.Key);
            foreach (var child in field.Fields)
            {
                if (child.Alias is not null)
                {
                    keys.TryAdd($"{FieldPrefix}{field.Alias}.{child.Alias}", child.Key);
                }
            }
        }

        return keys;
    }

    // Each field's label by its path, as the field source names it: its alias, a repeatable group's child under the
    // group's alias.
    private static Dictionary<string, LocalizedText> FieldLabels(FormDefinition form)
    {
        var labels = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (field.Alias is null)
            {
                continue;
            }

            if (field.Label is { } label)
            {
                labels.TryAdd(FieldPrefix + field.Alias, label);
            }

            foreach (var child in field.Fields)
            {
                if (child.Alias is not null && child.Label is { } childLabel)
                {
                    labels.TryAdd($"{FieldPrefix}{field.Alias}.{child.Alias}", childLabel);
                }
            }
        }

        return labels;
    }
}
