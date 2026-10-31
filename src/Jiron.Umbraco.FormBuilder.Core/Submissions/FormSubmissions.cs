using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
/// with its event, its action runs and its count; then the outcome. A page's check runs the same steps but idempotency,
/// the empty submission and the transaction, and answers its own fields' errors; a receipt answers an entry as a retry
/// would (ADR-0089). The places on options and the answers that must be unique are held while the answers are read and
/// again inside the transaction (ADR-0090, ADR-0091).
/// Spam protection comes in 7.1, and waking the action processor in 8.1.
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
    OptionPlaces places,
    UniqueAnswers unique,
    FieldTypeRegistry types,
    TimeProvider time)
{
    /// <summary>The most values a query name may hold.</summary>
    public const int MaxQueryValues = 20;

    /// <summary>The longest query or context value, in UTF-16 code units.</summary>
    public const int MaxValueLength = 2_000;

    /// <summary>Where a page key that names no page of the form is, as a request names it.</summary>
    public const string PageKeyPath = "pageKey";

    /// <summary>Runs a submission through the pipeline.</summary>
    /// <param name="request">The submission, as its transport read it.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> SubmitAsync(SubmissionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServedAsync(request.Form, cancellationToken) is not { } served
            || await forms.GetVersionAsync(served.Row.Id, served.Form.Version, cancellationToken) is not { } version)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        if (IsForMembers(served.Form))
        {
            return SubmissionResult.Refused(SubmissionRefusal.MemberRequired);
        }

        // Idempotency, before anything that could refuse a retry (section 4.4).
        var digest = SubmissionDigest.Of(request);
        if (request.SubmissionId is { } posted && await entries.GetBySubmissionAsync(served.Row.Id, posted, cancellationToken) is { } earlier)
        {
            return await RetryAsync(earlier, digest, served.Form, cancellationToken);
        }

        var reading = await ReadAsync(request, served, pageKey: null, cancellationToken);
        if (reading.IsRefused)
        {
            return reading.Refusal;
        }

        // One transaction (section 4.4, step 8).
        var (scope, page, read, wordings) = reading.Read;
        var submission = new NewSubmission(served.Row.Id, version.Info.Id, served.Form, read, wordings, request.SubmissionId ?? Guid.NewGuid(), digest, scope.Culture, page, scope.Context, request.Redirect);
        try
        {
            return await recorder.CreateAsync(submission, scope, cancellationToken) is { } created
                ? SubmissionResult.Accepted(created.Receipt, created.Outcome, replayed: false)
                : SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.DuplicateSubmission)
        {
            // Another request with this submission id was stored a moment earlier: answer as a retry of it.
            return await entries.GetBySubmissionAsync(served.Row.Id, submission.SubmissionId, cancellationToken) is { } other
                ? await RetryAsync(other, digest, served.Form, cancellationToken)
                : SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.EntryLimitReached)
        {
            return SubmissionResult.Refused(SubmissionRefusal.EntryLimitReached);
        }
        catch (SubmissionInvalidException invalid)
        {
            // An option ran out of places, or another entry took a unique answer, meanwhile (ADR-0090, ADR-0091): the
            // choices past the places and the answers taken, in the order of the form.
            var paths = new FieldPaths(served.Form.Definition);
            return SubmissionResult.Invalid(invalid.Errors.OrderBy(paths.Rank).Select(paths.Problem));
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.MissingFile)
        {
            // An upload went meanwhile, to another entry that named it at the same moment: each answer that names one is invalid.
            var paths = new FieldPaths(served.Form.Definition);
            return SubmissionResult.Invalid(read.Answers.Where(answer => answer.Files.Length > 0)
                .Select(answer => new SubmissionProblem(NameOf(FieldMessageKinds.Invalid), paths.Of(answer.Field, answer.Row))));
        }
    }

    /// <summary>
    /// Checks one page's answers as the submission would check them (ADR-0089), and stores nothing: the form, the gate,
    /// the revision and the consent wordings, the availability, then every posted answer read, ruled and checked, with
    /// only the errors of the page's fields answered. <c>uniquePerForm</c> is not checked: only a submission checks it
    /// (ADR-0091), so a page's check cannot tell whether an answer is taken.
    /// </summary>
    /// <param name="request">The answers the visitor gave so far, as the transport read them.</param>
    /// <param name="pageKey">The page's key, as the revision the visitor's page showed names it.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> ValidateAsync(SubmissionRequest request, Guid pageKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await ServedAsync(request.Form, cancellationToken) is not { } served)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        if (IsForMembers(served.Form))
        {
            return SubmissionResult.Refused(SubmissionRefusal.MemberRequired);
        }

        var reading = await ReadAsync(request, served, pageKey, cancellationToken);
        return reading.IsRefused ? reading.Refusal : SubmissionResult.Valid();
    }

    /// <summary>
    /// The receipt and the outcome of the form's entry that a submission id names (ADR-0012, ADR-0089), as a retry of its
    /// answers gets them; <see cref="SubmissionRefusal.EntryNotFound"/> when the form has no such entry, only a draft, or
    /// an entry a server key made (ADR-0126).
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the submission posted.</param>
    /// <param name="cancellationToken">Cancelled when the request is, or when its time limit runs out.</param>
    public async Task<SubmissionResult> ReceiptAsync(Guid form, Guid submissionId, CancellationToken cancellationToken = default) =>
        await ReceiptAsync(form, submissionId, serverKey: null, cancellationToken);

    /// <summary>
    /// The receipt of a submission for the server key that made it (ADR-0126): with a key, only an entry that key
    /// submitted answers, and without one only an entry no key made; any other is the same miss as an unknown submission.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission's id.</param>
    /// <param name="serverKey">The name of the server key the request carried, or null.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<SubmissionResult> ReceiptAsync(Guid form, Guid submissionId, string? serverKey, CancellationToken cancellationToken = default)
    {
        if (await ServedAsync(form, cancellationToken) is not { } served)
        {
            return SubmissionResult.Refused(SubmissionRefusal.FormNotFound);
        }

        if (IsForMembers(served.Form))
        {
            return SubmissionResult.Refused(SubmissionRefusal.MemberRequired);
        }

        return await entries.GetBySubmissionAsync(served.Row.Id, submissionId, cancellationToken) is { State: not EntryState.Draft } entry
            && string.Equals(KeyThatSubmitted(entry), serverKey, StringComparison.Ordinal)
            ? await ReplayAsync(entry, served.Form, cancellationToken)
            : SubmissionResult.Refused(SubmissionRefusal.EntryNotFound);
    }

    // The name of the server key the entry records as the one that submitted it, or null when no key did (ADR-0126): a
    // receipt answers that key alone, and a request without a key only an entry no key made (6.8a).
    private static string? KeyThatSubmitted(StoredEntry entry)
    {
        using var context = System.Text.Json.JsonDocument.Parse(entry.Context);
        return context.RootElement.TryGetProperty(SubmissionRequest.ServerKeyContext, out var name) && name.ValueKind == System.Text.Json.JsonValueKind.String
            ? name.GetString()
            : null;
    }

    /// <summary>
    /// What a copy of the form's entry that a submission id names shows (ADR-0123): its stored answers in the scope its
    /// outcome renders in, with what the rules hid for it; null when the form is not served or is for members, and when
    /// it has no such entry, or only a draft.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the submission posted.</param>
    /// <param name="cancellationToken">Cancelled when the request is.</param>
    public async Task<EntryCopy?> CopyAsync(Guid form, Guid submissionId, CancellationToken cancellationToken = default)
    {
        if (await ServedAsync(form, cancellationToken) is not { } served || IsForMembers(served.Form))
        {
            return null;
        }

        return await entries.GetBySubmissionAsync(served.Row.Id, submissionId, cancellationToken) is { State: not EntryState.Draft } entry
            ? await recorder.CopyAsync(entry, served.Form, cancellationToken)
            : null;
    }

    // The form, as the site serves it, and its row read again, so a form trashed a moment ago takes nothing (step 1).
    private async Task<Served?> ServedAsync(Guid key, CancellationToken cancellationToken) =>
        await cache.GetAsync(key, cancellationToken) is { } cached
        && await forms.GetAsync(key, cancellationToken) is { TrashedUtc: null, PublishedVersionId: not null } row
            ? new Served(cached.Published, row)
            : null;

    // The gate: members-only forms wait for 14.3, which binds a request to the signed-in member (ADR-0012).
    private static bool IsForMembers(PublishedForm form) => form.Definition.Settings.Availability.Access != MemberAccess.Anyone;

    // Compatibility, availability and the answers read and checked (section 4.4, steps 4, 5 and 7), the places left on
    // the options they chose among them (ADR-0090) and, for a submission, the answers that must be unique (ADR-0091). For
    // a page, only the errors of its fields count, and a page without an answer is no error.
    private async Task<Reading> ReadAsync(SubmissionRequest request, Served served, Guid? pageKey, CancellationToken cancellationToken)
    {
        var form = served.Form;
        var definition = form.Definition;

        // Compatibility: the aliases of the revision the page showed (ADR-0002).
        if (await AliasesAsync(request.Revision, served.Row.Id, form, cancellationToken) is not { } aliases)
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.FormChanged));
        }

        var unreadable = new List<string>();
        var answers = Answers(request, aliases, definition, unreadable, out var changed);
        var onPage = pageKey is { } key ? aliases.OnPage(key, definition) : null;
        if (pageKey is not null && onPage is null)
        {
            changed |= Unknown(aliases, null, PageKeyPath, unreadable);
        }

        var query = Query(request, definition, unreadable);
        var context = Context(request, definition, unreadable);
        if (request.ServerKey is { } serverKey)
        {
            context[SubmissionRequest.ServerKeyContext] = serverKey;
        }

        if (unreadable.Count > 0)
        {
            return Reading.Refused(SubmissionResult.Unreadable(unreadable));
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
            Member = request.Member,
            Query = query,
            Context = context,
        };
        var wordings = changed ? null : await WordingsAsync(definition, answers, scope, cancellationToken);
        if (wordings is null)
        {
            return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.FormChanged));
        }

        // Availability, by the site's clock and the entries that count toward the limit.
        switch (await availability.CheckAsync(definition.Settings.Availability, served.Row.Id, cancellationToken))
        {
            case FormUnavailable.Closed:
                return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.FormClosed));
            case FormUnavailable.Full:
                return Reading.Refused(SubmissionResult.Refused(SubmissionRefusal.EntryLimitReached));
        }

        // The answers, read by their types before the rules and checked once the rules leave them shown (ADR-0072).
        if (pageKey is null && IsEmpty(request))
        {
            return Reading.Refused(SubmissionResult.Invalid([new SubmissionProblem(SubmissionProblems.Empty, string.Empty)]));
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
                Uploads = await UploadsAsync(request, answers, definition, served.Row.Id, cancellationToken),
            },
            cancellationToken);
        var paths = new FieldPaths(definition);
        var full = await places.CheckAsync(served.Row.Id, read.Places, cancellationToken);
        // A unique answer is checked once nothing else refuses the post: an answer taken by another entry is then told to
        // a post that would be stored, never to one that stores nothing, which could ask it of any address (ADR-0091).
        var taken = pageKey is null && read.Errors.IsEmpty && full.IsEmpty ? await unique.CheckAsync(served.Row.Id, read.Unique, cancellationToken) : [];
        var all = full.IsEmpty && taken.IsEmpty ? read.Errors : [.. read.Errors.Concat(full).Concat(taken).OrderBy(paths.Rank)];
        ImmutableArray<SubmissionError> errors = onPage is null ? all : [.. all.Where(error => onPage.Contains(error.Field))];
        if (!errors.IsEmpty)
        {
            return Reading.Refused(aliases.IsCurrent || errors.All(error => aliases.Shown.Contains(error.Field))
                ? SubmissionResult.Invalid(errors.Select(paths.Problem))
                : SubmissionResult.Refused(SubmissionRefusal.FormChanged));
        }

        return new Reading(null, new ReadSubmission(scope, page, read, wordings));
    }

    // A retry of an entry's submission id: the same answers get the entry's receipt and outcome, others 409.
    private async Task<SubmissionResult> RetryAsync(StoredEntry entry, string digest, PublishedForm form, CancellationToken cancellationToken) =>
        string.Equals(entry.SubmissionHash, digest, StringComparison.Ordinal)
            ? await ReplayAsync(entry, form, cancellationToken)
            : SubmissionResult.Refused(SubmissionRefusal.DuplicateSubmission);

    // An entry's receipt and outcome again, as it was answered.
    private async Task<SubmissionResult> ReplayAsync(StoredEntry entry, PublishedForm form, CancellationToken cancellationToken)
    {
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
        private readonly FormDefinition _shown;

        public Aliases(FormDefinition shown, bool isCurrent)
        {
            _shown = shown;
            IsCurrent = isCurrent;
            foreach (var field in Fields(shown))
            {
                if (field.Alias is { } alias)
                {
                    Top[alias] = field;
                }
            }

            Shown.UnionWith(Keys(Fields(shown)));
        }

        public bool IsCurrent { get; }

        public Dictionary<string, FormField> Top { get; } = new(StringComparer.Ordinal);

        public HashSet<Guid> Shown { get; } = [];

        // The keys of the page with this key in this revision and in the live version, whose errors a page's check
        // answers (ADR-0089); null when neither has the page.
        public HashSet<Guid>? OnPage(Guid key, FormDefinition live)
        {
            var found = new[] { _shown, live }.Select(definition => definition.Pages.FirstOrDefault(page => page.Key == key)).OfType<FormPage>().ToList();
            return found.Count == 0 ? null : new HashSet<Guid>(Keys(found.SelectMany(page => page.Groups).SelectMany(group => group.Fields)));
        }

        // The keys of these fields, of their repeatable groups' children and of their grid rows.
        private static IEnumerable<Guid> Keys(IEnumerable<FormField> fields) =>
            fields.SelectMany(field => field.Fields.Prepend(field)).SelectMany(item => item.Rows.Select(row => row.Key).Prepend(item.Key));
    }

    // A form the site serves: as it serves it, and its row.
    private sealed record Served(PublishedForm Form, StoredForm Row);

    // What a request's reading gave: its refusal, or the answers with what they were read in.
    private sealed record Reading(SubmissionResult? Refusal, ReadSubmission? Read)
    {
        [MemberNotNullWhen(true, nameof(Refusal))]
        [MemberNotNullWhen(false, nameof(Read))]
        public bool IsRefused => Refusal is not null;

        public static Reading Refused(SubmissionResult refusal) => new(refusal, null);
    }

    // Answers that stand, with the scope and the page they were read in and the wording each ticked consent was shown.
    private sealed record ReadSubmission(MergeTagScope Scope, RequestPage Page, SubmissionAnswers Answers, IReadOnlyDictionary<Guid, ConsentWording> Wordings);

    // The answers by the live version's keys.
    private sealed record PostedAnswers(
        IReadOnlyDictionary<Guid, PostedAnswer> Fields,
        IReadOnlyDictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> Rows);

    // Where each field, child and grid row of the live version is, as a problem's path names it, and its place in the
    // order of the form and its rows, the order the reader lists errors in.
    private sealed class FieldPaths
    {
        private readonly Dictionary<Guid, (string Alias, FormField? Group)> _fields = [];
        private readonly Dictionary<Guid, (FormField Grid, FormField? Group)> _rows = [];
        private readonly Dictionary<Guid, (int Field, int Child)> _order = [];

        public FieldPaths(FormDefinition definition)
        {
            foreach (var (field, index) in Fields(definition).Select((field, index) => (field, index)))
            {
                Add(field, null, (index, -1));
                foreach (var (child, position) in field.Fields.Select((child, position) => (child, position)))
                {
                    Add(child, field, (index, position));
                }
            }
        }

        // An error's place: its field's, then its row's, then its place among its group's children. Errors that share a
        // place keep their order (OrderBy is stable), so the reader's come before those added after it.
        public (int Field, int Row, int Child) Rank(SubmissionError error) =>
            _order.TryGetValue(error.Field, out var at) ? (at.Field, error.Row ?? -1, at.Child) : (int.MaxValue, 0, 0);

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

        private void Add(FormField field, FormField? group, (int Field, int Child) order)
        {
            _fields[field.Key] = (field.Alias ?? string.Empty, group);
            _order[field.Key] = order;
            foreach (var row in field.Rows)
            {
                _rows[row.Key] = (field, group);
                _order[row.Key] = order;
            }
        }
    }
}
