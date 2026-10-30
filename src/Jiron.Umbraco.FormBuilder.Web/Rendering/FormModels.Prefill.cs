using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// What the server prefills on a page (ADR-0124): a field's start from the signed-in member's value or from a context
/// value the page gives, by the field's key, and the fields a signed-in member's value stands in for, which the page
/// leaves out.
/// </summary>
/// <param name="Values">Each prefilled field's start, by its key.</param>
/// <param name="FromMember">The fields a signed-in member's value stands in for.</param>
internal sealed record FormPrefill(IReadOnlyDictionary<Guid, FieldAnswers> Values, IReadOnlySet<Guid> FromMember)
{
    /// <summary>Nothing prefilled.</summary>
    public static readonly FormPrefill None = new(FrozenDictionary<Guid, FieldAnswers>.Empty, FrozenSet<Guid>.Empty);
}

// Prefill (ADR-0124): the member's value first, then the context value, each when not empty; the query value is the
// page's to fill from its own address (FillsDefault), so a cached page stays the same for everyone.
internal sealed partial class FormModels
{
    /// <summary>Whether a field of the form, or of a repeatable group's rows, takes a value from the signed-in member.</summary>
    /// <param name="definition">The form's published version.</param>
    public static bool TakesFromMember(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return AllFields(definition).Any(field => field.Prefill?.Member is not null);
    }

    // The server's prefill of the form's fields that take one and whose values the page holds, a repeatable group's
    // children among them: a member's value hidden from them only where it stands in as the entry takes it
    // (FieldValues.StandsIn), so the page never leaves out a field the entry could then not take.
    private async Task<FormPrefill> PrefillAsync(FormRender render, FormDefinition definition, DateOnly today, CancellationToken cancellationToken)
    {
        var values = new Dictionary<Guid, FieldAnswers>();
        var fromMember = new HashSet<Guid>();
        var scope = render.Member is { } member ? render.Shown.Scope with { Member = member } : null;
        var listed = definition.Settings.Developer.ContextValues;
        var children = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields).Select(field => field.Key).ToHashSet();
        foreach (var field in AllFields(definition))
        {
            if (field.Prefill is not { } prefill
                || types.FindType(field.Type) is not { } type
                || type.Declaration.ServerOnlyDefault
                || !type.Declaration.Features.HasFlag(FieldFeatures.Prefill))
            {
                continue;
            }

            if (scope is not null && prefill.Member is { } name
                && TextSemantics.Clean((await mergeTags.RenderAsync("{{ member." + name + " }}", MergeTagPlace.HiddenValue, MergeTagContext.Text, scope, cancellationToken)).Text) is { Length: > 0 } text)
            {
                var given = new FieldAnswers([text]);
                if (prefill.MemberMode != PrefillMemberMode.Hide || children.Contains(field.Key))
                {
                    values[field.Key] = given;
                    continue;
                }

                if (FieldValues.StandsIn(type, field, given, today))
                {
                    fromMember.Add(field.Key);
                    continue;
                }
            }

            if (prefill.Context is { } context && listed.Contains(context, StringComparer.Ordinal)
                && TextSemantics.Clean(GivenContext(render, context) ?? string.Empty) is { Length: > 0 } fromPage)
            {
                values[field.Key] = new FieldAnswers([fromPage]);
            }
        }

        return values.Count == 0 && fromMember.Count == 0 ? FormPrefill.None : new FormPrefill(values, fromMember);
    }

    // The value the page gives under a context value's name, read in any letter case as the form's context inputs read it
    // (Context); null for none, and for one longer than they take.
    private static string? GivenContext(FormRender render, string name) =>
        render.Context.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value is { Length: <= MaxContextValueLength } value
            ? value
            : null;
}
