using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// <c>field.&lt;alias&gt;</c> (ADR-0014, ADR-0066): the answer as its type shows it in the culture; <c>.value</c>, the
/// values in the wire form; <c>.label</c>, the field's label; <c>.&lt;part&gt;</c>, a part of a value with parts, such as
/// <c>field.name.first</c>. A repeatable group's child, <c>field.&lt;repeater&gt;.&lt;child&gt;</c> or by its own alias,
/// is a list with an item for each answered row, and the group itself a list of its rows. A field's former aliases
/// resolve to it, after the fields whose alias it is, except in visitor texts, which the browser renders too without
/// them (ADR-0068). Texts that name a dictionary item show the item's text (ADR-0067), and an option from a list or
/// another source the label it resolves (ADR-0077).
/// </summary>
/// <param name="answers">The answers as merge tags show them.</param>
internal sealed class FieldMergeTagSource(MergeTagAnswerTexts answers) : IFiniteMergeTagSource
{
    /// <inheritdoc />
    public string Name => "field";

    /// <inheritdoc />
    public bool IsPerVisitor => true;

    /// <inheritdoc />
    /// <remarks>Each field that holds a value, by its label, a repeatable group's child under the group's alias.</remarks>
    public IEnumerable<MergeTagPathInfo> Paths(FormDefinition form) =>
        answers.Fields(form)
            .Where(found => found.Field.Alias is not null && (found.Repeater is null || found.Repeater.Alias is not null))
            .Select(found => new MergeTagPathInfo(
                found.Repeater is { } repeater ? $"field.{repeater.Alias}.{found.Field.Alias}" : $"field.{found.Field.Alias}",
                found.Field.Label?.Default));

    /// <inheritdoc />
    /// <remarks>Any path that names a field, with <c>value</c>, <c>label</c> or, for a value with parts, any part after it.</remarks>
    public bool Knows(string path, FormDefinition form, MergeTagPlace place) =>
        path.StartsWith("field.", StringComparison.Ordinal) && Locate(path["field.".Length..], form, place) is not null;

    /// <summary>The key of the field a path after <c>field.</c> names in a place; null when it names none.</summary>
    /// <param name="path">The path after <c>field.</c>.</param>
    /// <param name="form">The form.</param>
    /// <param name="place">Where the template is used.</param>
    public Guid? FieldOf(string path, FormDefinition form, MergeTagPlace place) => Locate(path, form, place)?.Target.Field.Key;

    /// <inheritdoc />
    public async ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken)
    {
        var scope = request.Scope;
        if (Locate(request.Path, scope.Form, request.Place) is not { } tag)
        {
            return null;
        }

        return tag.Member switch
        {
            "label" => MergeTagValue.Of(MergeTagAnswerTexts.Label(tag.Target.Field, scope.Culture, (await answers.TextsAsync(scope, cancellationToken)).Dictionary)),
            "value" => answers.Value(tag.Target, scope, request.Place),
            { } part => answers.Part(tag.Target, part, scope, request.Place),
            null when tag.Target.Repeater is null && MergeTagAnswerTexts.IsRepeater(tag.Target.Field) => answers.Rows(tag.Target.Field, scope, request.Place, await answers.TextsAsync(scope, cancellationToken)),
            null => answers.Display(tag.Target, scope, request.Place, await answers.TextsAsync(scope, cancellationToken)),
        };
    }

    /// <summary>
    /// The field a path names and what of it: null for the answer itself, <c>value</c>, <c>label</c> or a part; null when
    /// the path names no field, or something the field does not have. Visitor texts name fields by their current aliases.
    /// </summary>
    private (AnswerField Target, string? Member)? Locate(string path, FormDefinition form, MergeTagPlace place)
    {
        var formerAliases = place != MergeTagPlace.VisitorText;
        var segments = path.Split('.');
        if (answers.Find(form, segments[0], formerAliases) is not { } target)
        {
            return null;
        }

        var rest = segments.AsSpan(1);
        if (target.Repeater is null && MergeTagAnswerTexts.IsRepeater(target.Field) && rest.Length > 0
            && MergeTagAnswerTexts.FindChild(target.Field, rest[0], formerAliases) is { } child)
        {
            target = new AnswerField(child, target.Field);
            rest = rest[1..];
        }

        var isGroup = target.Repeater is null && MergeTagAnswerTexts.IsRepeater(target.Field);
        return rest switch
        {
            [] => (target, null),
            ["label"] => (target, "label"),
            ["value"] when !isGroup => (target, "value"),
            [{ Length: > 0 } part] when !isGroup && answers.HasParts(target.Field) => (target, part),
            _ => null,
        };
    }
}
