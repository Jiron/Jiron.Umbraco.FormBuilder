using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;

/// <summary>An action copied to another form (ADR-0046).</summary>
/// <param name="SourceKey">The action's key in the source form.</param>
/// <param name="Key">The copy's key in the target form.</param>
/// <param name="Unmapped">The references the copy keeps because the target has nothing that matches them.</param>
/// <param name="MergeTags">The merge tags in the copy's settings that name a field the target lacks.</param>
public sealed record CopiedAction(Guid SourceKey, Guid Key, IReadOnlyList<UnmappedReference> Unmapped, IReadOnlyList<UnmappedMergeTag> MergeTags);

/// <summary>A reference of a copied action to something of the source form that the target has no match for (ADR-0046).</summary>
/// <param name="Path">Where the reference is in the target's definition.</param>
/// <param name="SourceKey">The key it names in the source form, which the copy keeps until the editor fixes it.</param>
/// <param name="Alias">The alias of the source field it names, or of the grid a row belongs to; null for other elements.</param>
public sealed record UnmappedReference(string Path, Guid SourceKey, string? Alias);

/// <summary>
/// A merge tag in a copied action that names a field the target lacks (ADR-0046), which renders empty there until the
/// editor fixes it.
/// </summary>
/// <param name="Path">Where the text that holds the tag is in the target's definition.</param>
/// <param name="Tag">The tag's path, such as <c>field.topic</c>.</param>
/// <param name="At">Where the tag's path starts in the text, from 0.</param>
public sealed record UnmappedMergeTag(string Path, string Tag, int At);

/// <summary>The target with the copied actions, or why the copies do not fit it.</summary>
/// <param name="Target">The target's definition with the copies after its own actions, or null.</param>
/// <param name="Actions">Each copy, in the source's order.</param>
/// <param name="Problems">Why the target does not read with the copies, such as an element limit; empty when it does.</param>
internal sealed record ActionCopyResult(FormDefinition? Target, IReadOnlyList<CopiedAction> Actions, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>
/// Copies actions from one form to another (ADR-0046): each copy and its rules get new keys, a reference to a source
/// field maps to the target field with the same alias in any letter case, a grid row to the row of that field with the
/// same default label, and every other reference to the source is kept and reported. References to anything outside the
/// source form stay as they are. Merge tags name fields by their aliases and are copied as they are; each tag in a
/// copy's settings that names a field the target lacks is reported with the path of its text.
/// </summary>
internal static class ActionCopies
{
    /// <summary>The target with copies of <paramref name="actions"/> appended to its actions.</summary>
    /// <param name="source">The form the actions come from.</param>
    /// <param name="actions">The source's actions to copy, in the order the copies take.</param>
    /// <param name="target">The form they go to; it may be the source itself.</param>
    /// <param name="serializer">The reader that reads the result back through the model.</param>
    /// <param name="lackedFieldTags">
    /// The tags of a template that name a field the target lacks, each with its path and where the path starts; every
    /// text in a copy's settings may be a template, since its action type decides which are.
    /// </param>
    public static ActionCopyResult Copy(
        FormDefinition source,
        IReadOnlyList<FormAction> actions,
        FormDefinition target,
        FormDefinitionSerializer serializer,
        Func<string, IReadOnlyList<(string Path, int At)>> lackedFieldTags)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(lackedFieldTags);
        var own = DefinitionKeys.NewKeys(actions.SelectMany(action => action.Rules.Select(rule => rule.Key).Prepend(action.Key)));
        var sourceKeys = DefinitionKeys.Of(source).ToHashSet();
        var sourceFields = new Dictionary<Guid, (FormField Field, GridRow? Row)>();
        foreach (var field in DefinitionKeys.Fields(source))
        {
            sourceFields.TryAdd(field.Key, (field, null));
            foreach (var row in field.Rows)
            {
                sourceFields.TryAdd(row.Key, (field, row));
            }
        }

        var targetFields = new Dictionary<string, FormField>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in DefinitionKeys.Fields(target))
        {
            if (field.Alias is { } alias)
            {
                targetFields.TryAdd(alias, field);
            }
        }

        var document = DefinitionKeys.Document(target);
        if (document["actions"] is not JsonArray copies)
        {
            copies = [];
            document["actions"] = copies;
        }

        var copied = new List<CopiedAction>();
        var copiedNodes = DefinitionKeys.Document(source with { Actions = actions })["actions"]?.AsArray() ?? [];
        for (var i = 0; i < actions.Count; i++)
        {
            var node = copiedNodes[i]!.DeepClone();
            var unmapped = new List<UnmappedReference>();
            var path = JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "actions"), copies.Count);
            DefinitionKeys.Replace(node, path, (key, at) =>
            {
                if (own.TryGetValue(key, out var renewed))
                {
                    return renewed;
                }

                if (sourceFields.TryGetValue(key, out var found) && Match(found.Field, found.Row, targetFields) is { } match)
                {
                    return match;
                }

                if (sourceKeys.Contains(key))
                {
                    unmapped.Add(new UnmappedReference(at(), key, sourceFields.TryGetValue(key, out var field) ? field.Field.Alias : null));
                }

                return null;
            });
            var tags = new List<UnmappedMergeTag>();
            if (node["settings"] is { } settings)
            {
                Texts(settings, JsonPaths.Property(path, "settings"), (text, at) =>
                    tags.AddRange(lackedFieldTags(text).Select(tag => new UnmappedMergeTag(at, tag.Path, tag.At))));
            }

            copies.Add(node);
            copied.Add(new CopiedAction(actions[i].Key, own[actions[i].Key], unmapped, tags));
        }

        var read = serializer.ReadStored(DefinitionKeys.Text(document));
        return new ActionCopyResult(read.Definition, copied, read.Problems);
    }

    // Every text in a copy's settings with its path, in the order of the document.
    private static void Texts(JsonNode node, string path, Action<string, string> visit)
    {
        switch (node)
        {
            case JsonObject properties:
                foreach (var (name, value) in properties)
                {
                    if (value is not null)
                    {
                        Texts(value, JsonPaths.Property(path, name), visit);
                    }
                }

                break;
            case JsonArray items:
                for (var i = 0; i < items.Count; i++)
                {
                    if (items[i] is { } item)
                    {
                        Texts(item, JsonPaths.Index(path, i), visit);
                    }
                }

                break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                visit(text, path);
                break;
        }
    }

    // The target key for a source field or grid row: the field with the same alias, and in it the one row whose
    // default label is the source row's.
    private static Guid? Match(FormField field, GridRow? row, Dictionary<string, FormField> targetFields)
    {
        if (field.Alias is not { } alias || !targetFields.TryGetValue(alias, out var target))
        {
            return null;
        }

        if (row is null)
        {
            return target.Key;
        }

        if (row.Label is not { } label)
        {
            return null;
        }

        var rows = target.Rows.Where(candidate => candidate.Label?.Default == label.Default).Take(2).ToList();
        return rows.Count == 1 ? rows[0].Key : null;
    }
}
