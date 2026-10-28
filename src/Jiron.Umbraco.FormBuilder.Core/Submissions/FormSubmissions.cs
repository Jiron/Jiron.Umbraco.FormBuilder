using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// The submission pipeline of section 4.4 (ADR-0088), which every transport runs: the published form, the gate,
/// idempotency, compatibility, availability, the answers read and checked, and one transaction that stores the entry
/// with its event, its action runs and its count; then the outcome. Spam protection comes in 7.1, the places on options
/// and <c>uniquePerForm</c> in 6.1g, and waking the action processor in 8.1.
/// </summary>
internal sealed class FormSubmissions(
    PublishedFormCache cache,
    IFormRepository forms,
    IEntryRepository entries,
    FormDocuments documents,
    FormAvailability availability,
    FormPages pages,
    SiteTimeZones timeZones,
    SubmissionReader reader,
    ConsentWordings consents,
    SubmissionEntries recorder,
    FieldTypeRegistry types,
    TimeProvider time)
{
    /// <summary>The most values a query name may hold.</summary>
    public const int MaxQueryValues = 20;

    /// <summary>The longest query or context value, in UTF-16 code units.</summary>
    public const int MaxValueLength = 2_000;

    /// <summary>Runs a submission through the pipeline.</summary>
    /// <param name="request">The submission, as its transport read it.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The form, as the site serves it, and its row read again, so a form trashed a moment ago takes nothing.
        if (await cache.GetAsync(request.Form, cancellationToken) is not { } cached
            || await forms.GetAsync(request.Form, cancellationToken) is not { TrashedUtc: null, PublishedVersionId: not null } row
            || await forms.GetVersionAsync(row.Id, cached.Published.Version, cancellationToken) is not { } version)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        var form = cached.Published;
        var definition = form.Definition;

        // The gate: members-only forms wait for 14.3, which binds a submission to the signed-in member (ADR-0012).
        if (definition.Settings.Availability.Access != MemberAccess.Anyone)
        {
            return SubmissionResult.Refused(SubmissionRefusal.MemberRequired);
        }

        // Idempotency, before anything that could refuse a retry (section 4.4).
        var digest = SubmissionDigest.Of(request);
        if (request.SubmissionId is { } posted && await entries.GetBySubmissionAsync(row.Id, posted, cancellationToken) is { } earlier)
        {
            return await RetryAsync(earlier, digest, form, cancellationToken);
        }

        // Compatibility: the aliases of the revision the page showed (ADR-0002).
        if (await AliasesAsync(request.Revision, row.Id, form, cancellationToken) is not { } aliases)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormChanged);
        }

        var unreadable = new List<string>();
        var answers = Answers(request, aliases, definition, unreadable, out var changed);
        var query = Query(request, definition, unreadable);
        var context = Context(request, definition, unreadable);
        if (unreadable.Count > 0)
        {
            return SubmissionResult.Unreadable(unreadable);
        }

        var culture = await pages.CultureAsync(request.Culture);
        var page = await pages.PageAsync(request.ContentKey);
        var scope = new MergeTagScope
        {
            Form = definition,
            FormName = form.Form.Name,
            FormAlias = form.Form.Alias,
            Culture = culture,
            Site = page.Site,
            Page = page.Content,
            Query = query,
            Context = context,
        };
        var wordings = changed ? null : await WordingsAsync(definition, answers, scope, cancellationToken);
        if (wordings is null)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormChanged);
        }

        // Availability, by the site's clock and the entries that count toward the limit.
        switch (await availability.CheckAsync(definition.Settings.Availability, row.Id, cancellationToken))
        {
            case FormUnavailable.Closed:
                return SubmissionResult.Refused(SubmissionRefusal.FormClosed);
            case FormUnavailable.Full:
                return SubmissionResult.Refused(SubmissionRefusal.EntryLimitReached);
        }

        // The answers, read by their types before the rules and checked once the rules leave them shown (ADR-0072).
        if (IsEmpty(request))
        {
            return SubmissionResult.Invalid([new SubmissionProblem(SubmissionProblems.Empty, string.Empty)]);
        }

        var zone = await timeZones.GetAsync(page.Site, cancellationToken);
        var read = await reader.ReadAsync(
            new PostedSubmission
            {
                Scope = scope,
                Now = time.GetUtcNow(),
                TimeZone = zone.Id,
                Fields = answers.Fields,
                Rows = answers.Rows,
                Uploads = await UploadsAsync(request, answers, definition, row.Id, cancellationToken),
            },
            cancellationToken);
        if (!read.IsValid)
        {
            var paths = new FieldPaths(definition);
            return aliases.IsCurrent || read.Errors.All(error => aliases.Shown.Contains(error.Field))
                ? SubmissionResult.Invalid(read.Errors.Select(paths.Problem))
                : SubmissionResult.Refused(SubmissionRefusal.FormChanged);
        }

        // One transaction (section 4.4, step 8).
        var submission = new NewSubmission(row.Id, version.Info.Id, form, read, wordings, request.SubmissionId ?? Guid.NewGuid(), digest, culture, page, context);
        try
        {
            return await recorder.CreateAsync(submission, scope, cancellationToken) is { } created
                ? SubmissionResult.Accepted(created.Receipt, created.Outcome, replayed: false)
                : SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.DuplicateSubmission)
        {
            // Another request with this submission id was stored a moment earlier: answer as a retry of it.
            return await entries.GetBySubmissionAsync(row.Id, submission.SubmissionId, cancellationToken) is { } other
                ? await RetryAsync(other, digest, form, cancellationToken)
                : SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.EntryLimitReached)
        {
            return SubmissionResult.Refused(SubmissionRefusal.EntryLimitReached);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.MissingFile)
        {
            // An upload went meanwhile, to another entry that named it at the same moment: each answer that names one is invalid.
            var paths = new FieldPaths(definition);
            return SubmissionResult.Invalid(read.Answers.Where(answer => answer.Files.Length > 0)
                .Select(answer => new SubmissionProblem(NameOf(FieldMessageKinds.Invalid), paths.Of(answer.Field, answer.Row))));
        }
    }

    // A retry of an entry's submission id: the same answers get the entry's receipt and outcome, others 409.
    private async Task<SubmissionResult> RetryAsync(StoredEntry entry, string digest, PublishedForm form, CancellationToken cancellationToken)
    {
        if (!string.Equals(entry.SubmissionHash, digest, StringComparison.Ordinal))
        {
            return SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);
        }

        var replayed = await recorder.ReplayAsync(entry, form, cancellationToken);
        return SubmissionResult.Accepted(replayed.Receipt, replayed.Outcome, replayed: true);
    }

    // The aliases of the revision the page showed: the live version's for the current revision, the newest published
    // version's that had it for an older one; null for a newer revision or one no version had.
    private async Task<Aliases?> AliasesAsync(int revision, int formId, PublishedForm form, CancellationToken cancellationToken)
    {
        if (revision == form.Form.PublicRevision)
        {
            return new Aliases(form.Definition, isCurrent: true);
        }

        if (revision < 1 || revision > form.Form.PublicRevision || await forms.GetRevisionAsync(formId, revision, cancellationToken) is not { } shown)
        {
            return null;
        }

        try
        {
            return new Aliases(documents.Readable(shown), isCurrent: false);
        }
        catch (FormBuilderValidationException)
        {
            return null;
        }
    }

    // The posted answers by the live version's keys. An alias the current revision lacks cannot be read; for an older
    // revision, one it lacks or a field it had that the live version does not means the form changed.
    private PostedAnswers Answers(SubmissionRequest request, Aliases aliases, FormDefinition definition, List<string> unreadable, out bool changed)
    {
        changed = false;
        var live = new HashSet<Guid>(Fields(definition).SelectMany(field => field.Fields.Prepend(field)).Select(field => field.Key));
        var fields = new Dictionary<Guid, PostedAnswer>();
        foreach (var (alias, answer) in request.Fields)
        {
            if (aliases.Top.TryGetValue(alias, out var group) && IsGroup(group) && answer.Values.Count == 0 && answer.Parts is not { Count: > 0 })
            {
                // An empty list for a repeatable group: no rows, as when it is left out.
                continue;
            }

            if (!aliases.Top.TryGetValue(alias, out var field) || IsGroup(field))
            {
                changed |= Unknown(aliases, field, $"values.{alias}", unreadable);
                continue;
            }

            changed |= !live.Contains(field.Key);
            fields[field.Key] = answer;
        }

        var rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>>();
        foreach (var (alias, posted) in request.Rows)
        {
            if (!aliases.Top.TryGetValue(alias, out var group) || !IsGroup(group))
            {
                changed |= Unknown(aliases, group, $"values.{alias}", unreadable);
                continue;
            }

            changed |= !live.Contains(group.Key);
            var children = group.Fields.Where(child => child.Alias is not null).ToDictionary(child => child.Alias!, StringComparer.Ordinal);
            var list = new List<IReadOnlyDictionary<Guid, PostedAnswer>>();
            for (var index = 0; index < posted.Count; index++)
            {
                var row = new Dictionary<Guid, PostedAnswer>();
                foreach (var (childAlias, answer) in posted[index])
                {
                    if (!children.TryGetValue(childAlias, out var child))
                    {
                        changed |= Unknown(aliases, null, string.Create(CultureInfo.InvariantCulture, $"values.{alias}[{index}].{childAlias}"), unreadable);
                        continue;
                    }

                    changed |= !live.Contains(child.Key);
                    row[child.Key] = answer;
                }

                list.Add(row);
            }

            rows[group.Key] = list;
        }

        return new PostedAnswers(fields, rows);
    }

    // An alias the page should not have posted: unreadable for the current revision, a change for an older one, whose
    // fields may have been taken out since. A known field posted in the wrong shape is unreadable either way.
    private static bool Unknown(Aliases aliases, FormField? field, string path, List<string> unreadable)
    {
        if (aliases.IsCurrent || field is not null)
        {
            unreadable.Add(path);
            return false;
        }

        return true;
    }

    // The query values the form lists, each name held to its limits.
    private static Dictionary<string, IReadOnlyList<string>> Query(SubmissionRequest request, FormDefinition definition, List<string> unreadable)
    {
        var listed = definition.Settings.Developer.QueryValues;
        var query = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var (name, values) in request.Query)
        {
            if (!listed.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (values.Count > MaxQueryValues || values.Any(value => value.Length > MaxValueLength))
            {
                unreadable.Add($"query.{name}");
                continue;
            }

            query[name] = values;
        }

        return query;
    }

    // The context values the form lists, each held to its limit; a public post's values are the visitor's to set, so no
    // other name is read.
    private static Dictionary<string, string> Context(SubmissionRequest request, FormDefinition definition, List<string> unreadable)
    {
        var listed = definition.Settings.Developer.ContextValues;
        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in request.Context)
        {
            if (!listed.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (value.Length > MaxValueLength)
            {
                unreadable.Add($"context.{name}");
                continue;
            }

            context[name] = value;
        }

        return context;
    }

    // The wording each ticked consent with a posted hash was shown, by the field's key; null when a hash is not the one
    // its wording has now in the culture (ADR-0009, ADR-0085). A tick without a hash is the reader's to refuse.
    private async Task<Dictionary<Guid, ConsentWording>?> WordingsAsync(FormDefinition definition, PostedAnswers answers, MergeTagScope scope, CancellationToken cancellationToken)
    {
        var wordings = new Dictionary<Guid, ConsentWording>();
        foreach (var field in Fields(definition).SelectMany(field => field.Fields.Prepend(field)))
        {
            if (types.FindType(field.Type) is not ConsentFieldType)
            {
                continue;
            }

            var hashes = Hashes(answers, field.Key).ToList();
            if (hashes.Count == 0)
            {
                continue;
            }

            if (await consents.ShownAsync(field, scope, cancellationToken) is not { } shown || hashes.Any(hash => !string.Equals(hash, shown.Hash, StringComparison.Ordinal)))
            {
                return null;
            }

            wordings[field.Key] = shown;
        }

        return wordings;
    }

    // The hashes a field's ticked answers post, in every row it is answered in.
    private static IEnumerable<string> Hashes(PostedAnswers answers, Guid field)
    {
        var posted = answers.Fields.TryGetValue(field, out var own) ? [own] : answers.Rows.Values.SelectMany(rows => rows).Select(row => row.GetValueOrDefault(field)).OfType<PostedAnswer>();
        foreach (var answer in posted)
        {
            if (answer.Values.Any(value => value is not null && TextSemantics.Clean(value) == FieldAnswerDefaults.Ticked)
                && answer.Parts?.GetValueOrDefault(ConsentFieldType.WordingPart) is { } parts
                && parts.FirstOrDefault(text => !string.IsNullOrEmpty(text)) is { } hash)
            {
                yield return TextSemantics.Clean(hash);
            }
        }
    }

    // Whether the submission holds no answer at all: no text but blanks in any value or part, a consent's wording hash
    // aside, which says what the page showed and not what the visitor answered.
    private static bool IsEmpty(SubmissionRequest request)
    {
        static bool Blank(PostedAnswer answer) =>
            answer.Values.All(IsBlank) && (answer.Parts ?? new Dictionary<string, IReadOnlyList<string?>>())
                .Where(part => part.Key != ConsentFieldType.WordingPart).All(part => part.Value.All(IsBlank));
        return request.Fields.Values.All(Blank) && request.Rows.Values.SelectMany(rows => rows).SelectMany(row => row.Values).All(Blank);
    }

    private static bool IsBlank(string? text) => text is null || TextSemantics.Clean(text).Length == 0;

    // The uploads the tokens posted for fields that store files stand for, each a file of this form, read once each.
    private async Task<Dictionary<string, StoredEntryFile>> UploadsAsync(SubmissionRequest request, PostedAnswers answers, FormDefinition definition, int formId, CancellationToken cancellationToken)
    {
        var uploads = new Dictionary<string, StoredEntryFile>(StringComparer.Ordinal);
        if (request.Tokens is not { } tokens)
        {
            return uploads;
        }

        var storing = Fields(definition).SelectMany(field => field.Fields.Prepend(field))
            .Where(field => types.FindType(field.Type)?.Declaration.Storage == FieldStorageKind.File)
            .Select(field => field.Key)
            .ToHashSet();
        var posted = answers.Fields.Concat(answers.Rows.Values.SelectMany(rows => rows).SelectMany(row => row));
        foreach (var (field, answer) in posted)
        {
            if (!storing.Contains(field))
            {
                continue;
            }

            foreach (var token in answer.Values.OfType<string>().Select(TextSemantics.Clean).Where(token => token.Length > 0 && !uploads.ContainsKey(token)))
            {
                if (tokens.FileOf(token, request.Form, field) is { } key && await entries.GetFileAsync(key, cancellationToken) is { } file && file.FormId == formId)
                {
                    uploads[token] = file;
                }
            }
        }

        return uploads;
    }

    private bool IsGroup(FormField? field) =>
        field is not null && types.FindType(field.Type) is { } type && type.Declaration.Features.HasFlag(FieldFeatures.ChildFields);

    // A kind of error as a field's messages name it; invalid for one they do not name.
    private static string NameOf(FieldMessageKinds kind) =>
        (FieldMessageProperties.All.FirstOrDefault(property => property.Kind == kind) ?? FieldMessageProperties.All.First(property => property.Kind == FieldMessageKinds.Invalid)).Name;

    private static IEnumerable<FormField> Fields(FormDefinition definition) => definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields);

    // A revision's fields outside repeatable groups by alias, and the keys of every field and grid row it has.
    private sealed class Aliases
    {
        public Aliases(FormDefinition shown, bool isCurrent)
        {
            IsCurrent = isCurrent;
            foreach (var field in Fields(shown))
            {
                if (field.Alias is { } alias)
                {
                    Top[alias] = field;
                }

                foreach (var item in field.Fields.Prepend(field))
                {
                    Shown.Add(item.Key);
                    Shown.UnionWith(item.Rows.Select(row => row.Key));
                }
            }
        }

        public bool IsCurrent { get; }

        public Dictionary<string, FormField> Top { get; } = new(StringComparer.Ordinal);

        public HashSet<Guid> Shown { get; } = [];
    }

    // The answers by the live version's keys.
    private sealed record PostedAnswers(
        IReadOnlyDictionary<Guid, PostedAnswer> Fields,
        IReadOnlyDictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> Rows);

    // Where each field, child and grid row of the live version is, as a problem's path names it.
    private sealed class FieldPaths
    {
        private readonly Dictionary<Guid, (string Alias, FormField? Group)> _fields = [];
        private readonly Dictionary<Guid, (FormField Grid, FormField? Group)> _rows = [];

        public FieldPaths(FormDefinition definition)
        {
            foreach (var field in Fields(definition))
            {
                Add(field, null);
                foreach (var child in field.Fields)
                {
                    Add(child, field);
                }
            }
        }

        public SubmissionProblem Problem(SubmissionError error)
        {
            var path = Of(error.Field, error.Row);
            if (error.Rule is { } rule)
            {
                return new SubmissionProblem(SubmissionProblems.Rule, path, Rule: rule);
            }

            return error.Check is { } check
                ? new SubmissionProblem(SubmissionProblems.Check, path, Check: check)
                : new SubmissionProblem(NameOf(error.Kind), path);
        }

        public string Of(Guid key, int? row)
        {
            if (_rows.TryGetValue(key, out var grid))
            {
                return $"{Of(grid.Grid.Key, row)}.{key:D}";
            }

            // Every problem names a field of the live version, which the reader read the answers by.
            var field = _fields[key];
            return field.Group is { Alias: { } group }
                ? string.Create(CultureInfo.InvariantCulture, $"{group}[{row ?? 0}].{field.Alias}")
                : field.Alias;
        }

        private void Add(FormField field, FormField? group)
        {
            _fields[field.Key] = (field.Alias ?? string.Empty, group);
            foreach (var row in field.Rows)
            {
                _rows[row.Key] = (field, group);
            }
        }
    }
}
