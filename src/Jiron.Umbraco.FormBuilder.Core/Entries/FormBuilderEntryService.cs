using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>The entries for a site's own code (ADR-0233), over the pipeline, the entry queries and the state changes.</summary>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="queries">Lists a form's entries.</param>
/// <param name="answers">Reads the answers.</param>
/// <param name="documents">Reads a version's definition.</param>
/// <param name="cache">The published forms.</param>
/// <param name="submissions">The submission pipeline.</param>
/// <param name="changes">The state changes.</param>
/// <param name="timeZones">The year of a reference.</param>
internal sealed class FormBuilderEntryService(
    IFormRepository forms,
    IEntryRepository entries,
    IEntryQueryRepository queries,
    EntryAnswerStore answers,
    FormDocuments documents,
    PublishedFormCache cache,
    FormSubmissions submissions,
    EntryStateChanges changes,
    SiteTimeZones timeZones) : IFormBuilderEntryService
{
    /// <summary>
    /// The server key an entry a site's code creates records as its context value <c>jfbServerKey</c>: no configured
    /// key can have this name, whose characters a key's name never holds.
    /// </summary>
    public const string CodeKey = "@code";

    /// <inheritdoc />
    public async Task<FormBuilderEntry?> GetAsync(Guid entryKey, CancellationToken cancellationToken = default)
    {
        if (await entries.GetAsync(entryKey, cancellationToken) is not { State: not EntryState.Draft } entry
            || await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form)
        {
            return null;
        }

        return (await ReadAsync(form, [entry], cancellationToken))[0];
    }

    /// <inheritdoc />
    public async Task<FormBuilderEntryPage?> ListAsync(Guid formKey, FormBuilderEntryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.Take, 1, nameof(query));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Take, FormBuilderEntryQuery.MaxTake, nameof(query));
        var after = query.After is null ? null : Decode(query.After);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var page = await queries.ListAsync(
            new EntryQuery(form.Id)
            {
                States = query.States.Count == 0 ? [.. Enum.GetValues<EntryState>().Where(state => state != EntryState.Draft)] : [.. query.States.Distinct().Select(StateOf)],
                Tests = query.IncludeTest ? EntryTestFilter.Include : EntryTestFilter.Exclude,
                SubmittedFrom = query.SubmittedFrom?.UtcDateTime,
                SubmittedBefore = query.SubmittedBefore?.UtcDateTime,
            },
            after,
            query.Take,
            count: false,
            cancellationToken);
        return new FormBuilderEntryPage(await ReadAsync(form, page.Entries, cancellationToken), page.Next is { } next ? Encode(next) : null);
    }

    /// <inheritdoc />
    public async Task<FormBuilderEntryResult> CreateAsync(FormBuilderNewEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (await cache.GetAsync(entry.Form, cancellationToken) is not { } served)
        {
            return new FormBuilderEntryResult { Refusal = CodeOf(SubmissionRefusal.FormNotFound) };
        }

        var result = await submissions.SubmitAsync(
            new SubmissionRequest
            {
                Form = entry.Form,
                Revision = served.Published.Form.PublicRevision,
                SubmissionId = entry.SubmissionId,
                Culture = entry.Culture,
                ContentKey = entry.Page,
                Fields = entry.Answers.ToDictionary(pair => pair.Key, pair => Posted(pair.Value), StringComparer.Ordinal),
                Rows = entry.Rows.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>)[.. pair.Value.Select(row => (IReadOnlyDictionary<string, PostedAnswer>)row.ToDictionary(child => child.Key, child => Posted(child.Value), StringComparer.Ordinal))],
                    StringComparer.Ordinal),
                Context = entry.Context,
                Member = entry.Member,
                ServerKey = CodeKey,
            },
            cancellationToken);
        if (result.Receipt is not { } receipt)
        {
            return new FormBuilderEntryResult
            {
                Refusal = CodeOf(result.Refusal ?? SubmissionRefusal.FormNotFound),
                Problems = [.. result.Problems.Select(problem => new FormBuilderEntryProblem(problem.Code, problem.Path, problem.Text))],
            };
        }

        var stored = await forms.GetAsync(entry.Form, cancellationToken) is { } form
            ? await entries.GetBySubmissionAsync(form.Id, receipt.SubmissionId, cancellationToken)
            : null;
        return stored is null || await GetAsync(stored.Key, cancellationToken) is not { } created
            ? new FormBuilderEntryResult { Refusal = CodeOf(SubmissionRefusal.EntryNotFound) }
            : new FormBuilderEntryResult { Entry = created, Replayed = result.Replayed };
    }

    /// <inheritdoc />
    public async Task<bool> ChangeStateAsync(Guid entryKey, FormBuilderEntryState state, CancellationToken cancellationToken = default)
    {
        if (state is FormBuilderEntryState.Unverified || !Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "An entry moves to submitted, approved, rejected or Spam.");
        }

        if (await entries.GetAsync(entryKey, cancellationToken) is not { State: not EntryState.Draft and not EntryState.Unverified } entry
            || await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form)
        {
            return false;
        }

        var change = await changes.ChangeAsync(form.Key, [entryKey], StateOf(state), userKey: null, cancellationToken);
        return change.Moved.Count > 0;
    }

    /// <summary>The public state of a stored one; a draft has none.</summary>
    internal static FormBuilderEntryState StateOf(EntryState state) => state switch
    {
        EntryState.Unverified => FormBuilderEntryState.Unverified,
        EntryState.Submitted => FormBuilderEntryState.Submitted,
        EntryState.Approved => FormBuilderEntryState.Approved,
        EntryState.Rejected => FormBuilderEntryState.Rejected,
        EntryState.Spam => FormBuilderEntryState.Spam,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "A draft is no entry here."),
    };

    /// <summary>The public API's code of a refusal (section 4.6).</summary>
    internal static string CodeOf(SubmissionRefusal refusal) => refusal switch
    {
        SubmissionRefusal.ResumeUnavailable => "resume_unavailable",
        SubmissionRefusal.FormNotFound => "form_not_found",
        SubmissionRefusal.ChallengeRequired => "challenge_required",
        SubmissionRefusal.CaptchaFailed => "captcha_failed",
        SubmissionRefusal.MemberRequired => "member_required",
        SubmissionRefusal.DuplicateSubmission => "duplicate_submission",
        SubmissionRefusal.FormChanged => "form_changed",
        SubmissionRefusal.FormClosed => "form_closed",
        SubmissionRefusal.EntryLimitReached => "entry_limit_reached",
        SubmissionRefusal.AlreadySubmitted => "already_submitted",
        SubmissionRefusal.InvalidRequest => "invalid_request",
        SubmissionRefusal.ValidationFailed => "validation_failed",
        _ => "not_found",
    };

    private static EntryState StateOf(FormBuilderEntryState state) => state switch
    {
        FormBuilderEntryState.Unverified => EntryState.Unverified,
        FormBuilderEntryState.Submitted => EntryState.Submitted,
        FormBuilderEntryState.Approved => EntryState.Approved,
        FormBuilderEntryState.Rejected => EntryState.Rejected,
        FormBuilderEntryState.Spam => EntryState.Spam,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Not a state."),
    };

    private static PostedAnswer Posted(FormBuilderNewAnswer answer) => new(
        [.. answer.Values],
        answer.Parts?.ToDictionary(part => part.Key, part => (IReadOnlyList<string?>)[part.Value], StringComparer.Ordinal));

    // The cursor of the newest-first list: the submission time and the entry's id.
    internal static string Encode(EntryCursor cursor) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{cursor.SubmittedUtc.Ticks}.{cursor.EntryId}")));

    internal static EntryCursor Decode(string text)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(text)).Split('.');
            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks is >= 0 and <= 3_155_378_975_999_999_999
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                return new EntryCursor(EntrySort.Newest, id, new DateTime(ticks, DateTimeKind.Utc), null);
            }
        }
        catch (FormatException)
        {
        }

        throw new ArgumentException("The cursor is not one a page of entries gave.", nameof(text));
    }

    // The entries of one form with their answers, each with the reference of the version it was submitted with.
    private async Task<IReadOnlyList<FormBuilderEntry>> ReadAsync(StoredForm form, IReadOnlyList<StoredEntry> found, CancellationToken cancellationToken)
    {
        if (found.Count == 0)
        {
            return [];
        }

        var read = await answers.ReadManyAsync(form.Id, [.. found.Select(entry => entry.Id)], cancellationToken);
        var catalogue = await forms.ListFieldsAsync(form.Id, cancellationToken);
        var sensitive = catalogue.Where(field => field.Values.IsSensitive).Select(field => field.Values.FieldKey).ToHashSet();
        var formats = new Dictionary<int, EntryReferenceFormat?>();
        var listed = new List<FormBuilderEntry>(found.Count);
        foreach (var entry in found)
        {
            if (!formats.TryGetValue(entry.FormVersionId, out var format))
            {
                formats[entry.FormVersionId] = format = await FormatAsync(entry.FormVersionId, cancellationToken);
            }

            var zone = await timeZones.GetAsync(entry.SiteKey, cancellationToken);
            listed.Add(new FormBuilderEntry
            {
                Key = entry.Key,
                FormKey = form.Key,
                FormAlias = form.Alias,
                Number = entry.Number,
                Reference = EntryReferences.Format(format, entry.Number, TimeZoneInfo.ConvertTime(entry.SubmittedUtc, zone).Year),
                State = StateOf(entry.State),
                SubmittedUtc = entry.SubmittedUtc,
                Culture = entry.Culture,
                IsTest = entry.IsTest,
                Member = entry.MemberKey,
                Site = entry.SiteKey,
                Page = entry.ContentKey,
                Answers = read.TryGetValue(entry.Id, out var typed) ? [.. typed.All.Select(answer => AnswerOf(answer, sensitive))] : [],
            });
        }

        return listed;
    }

    private async Task<EntryReferenceFormat?> FormatAsync(int versionId, CancellationToken cancellationToken)
    {
        try
        {
            return await forms.GetVersionByIdAsync(versionId, cancellationToken) is { } version ? documents.Readable(version).Settings.Submission.EntryReference : null;
        }
        catch (FormBuilderValidationException)
        {
            return null;
        }
    }

    private static FormBuilderEntryAnswer AnswerOf(EntryAnswer answer, HashSet<Guid> sensitive) => new()
    {
        FieldKey = answer.Field,
        Field = answer.Alias,
        Type = answer.Type,
        Row = answer.Repeater is null ? null : answer.Row,
        Values = [.. answer.Values.Select(value => value.Text)],
        GridRows = answer.Values.All(value => value.Value.Row is not null) && answer.Values.Length > 0 ? [.. answer.Values.Select(value => value.Value.Row!.Value)] : [],
        Files = answer.Storage == FieldStorageKind.File
            ? [.. answer.Values.Where(value => value.File is not null).Select(value => new FormBuilderEntryFile(value.File!.Key, value.File.OriginalName, value.File.SizeBytes))]
            : [],
        IsSensitive = sensitive.Contains(answer.Field),
    };
}
