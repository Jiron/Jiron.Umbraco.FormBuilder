using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// The approved entries a site's pages show (ADR-0233): the entry list's query for approved entries without tests, each
/// entry's answers in the texts its export shows, of the fields a page may show only.
/// </summary>
/// <param name="cache">The published forms.</param>
/// <param name="forms">The forms.</param>
/// <param name="queries">Lists a form's entries.</param>
/// <param name="scopes">Each entry's scope: its version, its answers and the fields its rules hid.</param>
/// <param name="answers">The answers' texts.</param>
/// <param name="types">What each field type stores.</param>
/// <param name="pages">Holds a culture to the site's languages.</param>
internal sealed class FormBuilderEntryReader(
    PublishedFormCache cache,
    IFormRepository forms,
    IEntryQueryRepository queries,
    SubmissionEntries scopes,
    MergeTagAnswerTexts answers,
    FieldTypeRegistry types,
    FormPages pages) : IFormBuilderEntryReader
{
    // The types whose answers no page shows, whatever their settings: a hidden field's value is the site's, a signature
    // and a consent are the visitor's own record.
    private static readonly FrozenSet<string> NeverShown = new[] { "hidden", "signature", "consent", "captcha" }.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<FormBuilderEntryReading?> ReadApprovedAsync(string form, FormBuilderEntryReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(form);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Take, 1, nameof(request));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Take, FormBuilderEntryReadRequest.MaxTake, nameof(request));
        var after = request.After is null ? null : FormBuilderEntryService.Decode(request.After);
        var key = Guid.TryParse(form, out var parsed) ? parsed : await cache.KeyOfAsync(form, cancellationToken);
        if (key is not { } formKey || await cache.GetAsync(formKey, cancellationToken) is null || await forms.GetAsync(formKey, cancellationToken) is not { } stored)
        {
            return null;
        }

        var culture = request.Culture is null ? null : await pages.CultureAsync(request.Culture);
        var page = await queries.ListAsync(
            new EntryQuery(stored.Id) { States = [EntryState.Approved], Tests = EntryTestFilter.Exclude },
            after,
            request.Take,
            count: false,
            cancellationToken);
        var read = await scopes.ScopesAsync(stored, page.Entries, cancellationToken);
        var shown = new List<FormBuilderReadEntry>(page.Entries.Count);
        foreach (var entry in page.Entries)
        {
            if (!read.TryGetValue(entry.Id, out var scope))
            {
                continue;
            }

            scope = culture is null ? scope : scope with { Culture = culture };
            var fields = Showable(scope.Form, request.Fields);
            var texts = await answers.TextsAsync(scope, fields, cancellationToken);
            var listed = new List<FormBuilderReadAnswer>();
            foreach (var target in fields)
            {
                var label = MergeTagAnswerTexts.Label(target.Field, scope.Culture, texts.Dictionary);
                var rows = target.Repeater is { } repeater ? scope.Answers.Rows([.. repeater.Fields.Select(child => child.Key)]) : [0];
                foreach (var row in rows)
                {
                    if (answers.ExportTexts(target, scope, texts, row) is { Count: > 0 } said)
                    {
                        listed.Add(new FormBuilderReadAnswer(target.Field.Alias!, label, said, target.Repeater is null ? null : row));
                    }
                }
            }

            shown.Add(new FormBuilderReadEntry(entry.Number, entry.SubmittedUtc, listed));
        }

        return new FormBuilderEntryReading(shown, page.Next is { } next ? FormBuilderEntryService.Encode(next) : null);
    }

    // The fields a page may show, in the order asked for or the form's: answered by the visitor, not sensitive, holding no
    // file, and of no type whose answers stay the site's or the visitor's own.
    private List<AnswerField> Showable(FormDefinition definition, IReadOnlyList<string>? asked)
    {
        var all = answers.Fields(definition)
            .Where(found => found.Field.Alias is { Length: > 0 } && found.Field.Fields.Count == 0 && !found.Field.Sensitive && !(found.Repeater?.Sensitive ?? false)
                && !NeverShown.Contains(found.Field.Type)
                && types.Find(found.Field.Type) is { HoldsValue: true, Storage: not FieldStorageKind.File and not FieldStorageKind.None })
            .ToList();
        return asked is null
            ? all
            : [.. asked.Distinct(StringComparer.Ordinal).Select(alias => all.FirstOrDefault(found => string.Equals(found.Field.Alias, alias, StringComparison.Ordinal))).OfType<AnswerField>()];
    }
}
