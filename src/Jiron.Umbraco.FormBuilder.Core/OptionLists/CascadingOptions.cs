using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// A field's options for the answers a request gives for its chain of parents (ADR-0081, ADR-0086): each parent's
/// answers held to the options that parent offers, its own or those its list or source resolves for its own held
/// answers in turn, each once and in their order, so that no text a request gives reaches a source or becomes a cache key
/// unless an option of the form has it. The public API's <c>options</c> endpoint answers with them.
/// </summary>
internal sealed class CascadingOptions(IFormBuilderOptionService options)
{
    /// <summary>
    /// The field's options for the held answers of its parent; unavailable when its source cannot give them, or when a
    /// parent up the chain is a field whose options are unknown.
    /// </summary>
    /// <param name="form">The form's definition, the version the field is of.</param>
    /// <param name="field">The field, one of the form's.</param>
    /// <param name="answers">A field's answers as the request gives them, by the field's key; empty for none.</param>
    /// <param name="culture">The culture the labels are in, one of the site's.</param>
    /// <param name="page">The content page the form is on, or null.</param>
    /// <param name="cancellationToken">Cancels the sources' work.</param>
    public Task<FormBuilderResolvedOptions> GetAsync(
        FormDefinition form,
        FormField field,
        Func<Guid, IReadOnlyList<string>> answers,
        string culture,
        Guid? page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(culture);
        return new Walk(options, Fields(form), answers, culture, page).ResolveAsync(field, [], cancellationToken);
    }

    // Every field of the form by its key, a repeatable group's children among them; the first of a key twice.
    private static Dictionary<Guid, FormField> Fields(FormDefinition form)
    {
        var fields = new Dictionary<Guid, FormField>();
        foreach (var field in form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            fields.TryAdd(field.Key, field);
            foreach (var child in field.Fields)
            {
                fields.TryAdd(child.Key, child);
            }
        }

        return fields;
    }

    // One request's walk up a chain of parents; a chain that leads back to a field it is asking for ends there.
    private sealed class Walk(
        IFormBuilderOptionService options,
        Dictionary<Guid, FormField> fields,
        Func<Guid, IReadOnlyList<string>> answers,
        string culture,
        Guid? page)
    {
        public async Task<FormBuilderResolvedOptions> ResolveAsync(FormField field, ImmutableHashSet<Guid> asking, CancellationToken cancellationToken)
        {
            IReadOnlyList<string> parentValues = [];
            if (field.Options?.Parent is { } parent)
            {
                if (await HeldAsync(parent, asking.Add(field.Key), cancellationToken) is not { } held)
                {
                    return FormBuilderResolvedOptions.Unavailable;
                }

                parentValues = held;
            }

            return await options.GetAsync(field, new FormBuilderOptionContext(culture) { ParentValues = parentValues, Page = page }, cancellationToken);
        }

        // The parent's answers, cleaned as answers are, that are options it offers; none for a parent the form lacks, one
        // without options, or one a chain of parents leads back to; null when its options are unknown.
        private async Task<IReadOnlyList<string>?> HeldAsync(Guid parent, ImmutableHashSet<Guid> asking, CancellationToken cancellationToken)
        {
            if (!fields.TryGetValue(parent, out var field) || field.Options is not { } given || asking.Contains(parent) || answers(parent) is not { Count: > 0 } answered)
            {
                return [];
            }

            IEnumerable<string>? offered = given.Source == OptionSource.Items
                ? given.Items.Select(item => item.Value)
                : await ResolveAsync(field, asking, cancellationToken) is { IsAvailable: true } resolved ? resolved.Options.Select(option => option.Value) : null;
            if (offered is null)
            {
                return null;
            }

            var values = offered.ToHashSet(StringComparer.Ordinal);
            return [.. answered.Select(TextSemantics.Clean).Where(values.Contains).Distinct(StringComparer.Ordinal)];
        }
    }
}
