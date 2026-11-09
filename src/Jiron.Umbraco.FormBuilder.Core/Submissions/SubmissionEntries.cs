using System.Buffers;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>A submission whose answers stand, as <see cref="SubmissionEntries.CreateAsync"/> stores it (ADR-0088).</summary>
/// <param name="FormId">The form's row.</param>
/// <param name="FormVersionId">The row of the published version the answers were read with.</param>
/// <param name="Form">The form as the site serves it, with that version.</param>
/// <param name="Answers">The answers as the reader read them.</param>
/// <param name="Wordings">The wording each ticked consent was shown, by the field's key.</param>
/// <param name="SubmissionId">The submission id, the visitor's or the one the server made.</param>
/// <param name="Digest">The submission's digest (<see cref="SubmissionDigest"/>).</param>
/// <param name="Culture">The culture, one of the site's languages.</param>
/// <param name="Page">The page the form was on.</param>
/// <param name="Context">The context values the form lists, as posted.</param>
/// <param name="Redirect">The page the placement sends the visitor to after the send (ADR-0129), or null for the outcome.</param>
/// <param name="ChallengeId">The id of the challenge that made it, which makes one entry (ADR-0012); null for a request without one.</param>
/// <param name="Verdict">Its spam score and why (ADR-0012); null for none.</param>
/// <param name="Draft">The draft it finishes, which goes in its unit (ADR-0163); null for none.</param>
/// <param name="Started">When its challenge was first issued, from which its time to complete counts (ADR-0211); null for a request without one.</param>
/// <remarks>
/// A preview's submission is a test entry (<see cref="IsTest"/>), which sets off nothing unless <see cref="RunsActions"/>
/// (ADR-0204).
/// </remarks>
internal sealed record NewSubmission(
    int FormId,
    int FormVersionId,
    PublishedForm Form,
    SubmissionAnswers Answers,
    IReadOnlyDictionary<Guid, ConsentWording> Wordings,
    Guid SubmissionId,
    string Digest,
    string Culture,
    RequestPage Page,
    IReadOnlyDictionary<string, string> Context,
    Guid? Redirect = null,
    string? ChallengeId = null,
    SpamVerdict? Verdict = null,
    long? Draft = null,
    DateTimeOffset? Started = null)
{
    /// <summary>Whether it is a test entry, sent from the preview (ADR-0204).</summary>
    public bool IsTest { get; init; }

    /// <summary>What the entry keeps of the visitor's IP address, as the form's mode keeps it (ADR-0224); null for nothing.</summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Whether it sets off what an entry sets off as it arrives: its actions, its confirmation mail and the notification
    /// Umbraco's webhooks hear. Always for a visitor's; for a test entry only when its ticket says so (ADR-0204).
    /// </summary>
    public bool RunsActions { get; init; } = true;
}

/// <summary>An entry with its receipt and outcome (ADR-0088).</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Receipt">Its receipt.</param>
/// <param name="Outcome">Its outcome.</param>
internal sealed record SubmittedEntry(StoredEntry Entry, SubmissionReceipt Receipt, SubmissionOutcome Outcome);

/// <summary>
/// What a copy of an entry's answers shows (ADR-0123): the scope its outcome renders in, with the version the entry was
/// submitted with and its stored answers, the pages, groups and fields the rules hid for it, and when it was submitted.
/// </summary>
/// <param name="Scope">The scope: the entry's version as its form, its culture, its page and its stored answers.</param>
/// <param name="Hidden">The keys of the pages, groups and fields the rules hid; null for an entry whose event does not record them.</param>
/// <param name="Submitted">When the entry was submitted.</param>
internal sealed record EntryCopy(MergeTagScope Scope, IReadOnlySet<Guid>? Hidden, DateTimeOffset Submitted);

/// <summary>An entry as the entry panel reads it (ADR-0208).</summary>
/// <param name="Scope">The scope: the entry's version as its form, its culture, its page and its stored answers.</param>
/// <param name="Answers">The answers as stored, with their files and consent wordings; null when none could be read.</param>
/// <param name="Timeline">Its events, oldest first.</param>
internal sealed record EntryRead(MergeTagScope Scope, EntryAnswers? Answers, IReadOnlyList<StoredEntryEvent> Timeline);

/// <summary>
/// The entries of submissions (ADR-0088): stores a submission's entry in one transaction with its <c>submitted</c>
/// event, its action runs and its daily counts (ADR-0211), once its choices are held against the places left (ADR-0090) and its
/// answers that must be unique against the other entries' (ADR-0091). A test entry (ADR-0204) holds no place or answer,
/// counts nowhere, and sets off nothing unless its ticket runs actions; its event keeps the actions its rules chose, which
/// Run actions... runs later. It gives
/// an entry's receipt and outcome again for a retry of the same answers, rendered with the version it was submitted
/// with, its stored answers and the outcome its event names.
/// </summary>
internal sealed class SubmissionEntries(
    EntryAnswerStore store,
    OptionPlaces places,
    UniqueAnswers unique,
    IEntryEventRepository events,
    IActionRunRepository runs,
    IDailyStatRepository stats,
    IFormRepository forms,
    FormDocuments documents,
    FieldSettingLocks locks,
    SubmissionOutcomes outcomes,
    FormPages pages,
    SiteTimeZones timeZones,
    MergeTagUmbraco umbraco,
    SiteBaseUrl baseUrls,
    ActionSettlement settlement,
    EntryEvents entryEvents,
    OutboxWake wake,
    IEntryRepository entries,
    IEmailRepository emails,
    FieldTypeRegistry types,
    FormInsights insights,
    TimeProvider time)
{
    /// <summary>The kind of the event that records a submission.</summary>
    public const string SubmittedEvent = "submitted";

    /// <summary>The daily metric that counts submissions (section 4.5).</summary>
    public const string SubmissionsMetric = "submissions";

    /// <summary>The daily metric that counts the submissions the protection put into Spam (section 4.5).</summary>
    public const string SpamMetric = "spam";

    // The name of the outcome in the submitted event's details.
    private const string OutcomeDetail = "outcome";

    // The name of what the rules hid in the submitted event's details: the keys of pages, groups and fields (ADR-0123).
    private const string HiddenDetail = "hidden";

    // The name of the actions the rules chose in the details of an entry that went to Spam, which marking it as no spam queues (ADR-0140).
    private const string ActionsDetail = "actions";

    // The origin a page's address is built on when the site has no base URL: only its path is kept.
    private const string NoOrigin = "https://jfb.invalid";

    /// <summary>
    /// Stores a submission's entry, with its event, its action runs and its count in the same unit, and renders its
    /// outcome; null when the form no longer exists. Refused as <see cref="EntryAnswerStore.CreateAsync"/> refuses, and
    /// with <see cref="SubmissionInvalidException"/> when an option it chose has no place left for it (ADR-0090).
    /// </summary>
    /// <param name="submission">The submission.</param>
    /// <param name="scope">The scope its answers were read in, which the outcome renders in with the entry.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<SubmittedEntry?> CreateAsync(NewSubmission submission, MergeTagScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(scope);
        var definition = submission.Form.Definition;
        var verdict = submission.Verdict ?? SpamVerdict.None;
        // A test entry that sets off nothing arrives as confirmed, with no mail asking for it (ADR-0204).
        var state = verdict.IsSpam ? EntryState.Spam : definition.Settings.Submission.Verification is null || !submission.RunsActions ? ArrivalState(definition) : EntryState.Unverified;
        var queued = !submission.RunsActions || state is EntryState.Unverified or EntryState.Spam ? [] : Runs(definition, submission.Answers.State.Actions, ArrivalTriggers(definition, verified: false));
        // A test entry whose actions wait for Run actions... keeps what they may use until they ran (ADR-0204); one whose
        // rules chose none that its arrival could run has nothing to wait for.
        var waits = !submission.RunsActions
            && Runs(definition, submission.Answers.State.Actions, ArrivalTriggers(definition, verified: definition.Settings.Submission.Verification is not null)).Count > 0;
        var kept = queued.Count > 0 || waits || ActionSettlement.TriggerCanFire(definition, state);
        var answers = kept ? submission.Answers.Answers : [.. submission.Answers.Answers.Where(answer => !ActionSettlement.KeptUntilActions(definition, answer.Field))];

        // The answers kept only until the actions have run that no action will use are never stored; the timeline says so, as
        // it does once such answers go (ADR-0225).
        var neverKept = kept ? [] : submission.Answers.Answers.Select(answer => answer.Field).Where(field => ActionSettlement.KeptUntilActions(definition, field)).Distinct().ToList();
        var now = time.GetUtcNow();
        var duration = submission.Started is { } started ? now - started : (TimeSpan?)null;
        var counts = await CountsAsync(submission, verdict, now, duration, cancellationToken);
        var entry = new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = submission.FormId,
            FormVersionId = submission.FormVersionId,
            State = state,
            SubmissionId = submission.SubmissionId,
            ChallengeId = submission.ChallengeId,
            SubmissionHash = submission.Digest,
            Culture = submission.Culture,
            ContentKey = submission.Page.Content,
            PageUrl = await PathAsync(submission.Page, submission.Culture, cancellationToken),

            // The member signed in when it was sent (ADR-0009), by whom the privacy tools find it (ADR-0223); a test
            // entry has none.
            MemberKey = submission.IsTest ? null : scope.Member,
            IpAddress = submission.IsTest ? null : submission.IpAddress,
            Context = Json(submission.Context),
            SpamScore = verdict.Score,
            SpamReasons = SpamReasons.Stored(verdict.Reasons),
            EntryLimit = definition.Settings.Availability.MaxEntries,
            IsTest = submission.IsTest,
            DurationSeconds = duration is { } took ? (int)Math.Clamp(Math.Floor(took.TotalSeconds), 0, int.MaxValue) : null,
        };

        // A field only the draft has gets its catalogue row before its first value; a second test entry, or one sent at
        // the same moment, adds none. A field the draft stores otherwise than the row its published versions keep, or
        // under another parent, keeps no value in a test entry: the row stays theirs until a publish changes it
        // (ADR-0003, ADR-0204).
        if (submission.IsTest)
        {
            var known = await forms.ListFieldsAsync(submission.FormId, cancellationToken);
            var drafted = FieldCatalogue.Of(definition, types);
            var raced = false;
            foreach (var row in FieldCatalogue.DraftOnly(drafted, known))
            {
                try
                {
                    await forms.AddFieldAsync(submission.FormId, row, cancellationToken);
                }
                catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.DuplicateKey)
                {
                    // Another submission, or a publish, added the row a moment ago: the catalogue is read again.
                    raced = true;
                }
            }

            if (raced)
            {
                known = await forms.ListFieldsAsync(submission.FormId, cancellationToken);
            }

            var unlike = FieldCatalogue.Unlike(drafted, known);
            answers = [.. answers.Where(answer => !unlike.Contains(answer.Field))];
        }

        var outcome = submission.Answers.State.Outcome;
        var stored = await store.CreateAsync(
            entry,
            definition,
            answers,
            submission.Wordings,
            async created =>
            {
                // Its values are counted now, and the unit holds the form's row: these counts and answers are the last
                // word. An entry that does not count holds no place and no answer, and takes none from another. As
                // before the unit, a taken answer is told only to an entry the places let through (ADR-0091).
                // A challenge whose entry is gone, on a form that stored none then, is spent too (ADR-0158). The unit holds the
                // form's row, as the unit that lets such an entry go does, so one of the two always sees the other.
                if (created.ChallengeId is { } challenge && await settlement.ChallengeUsedAsync(challenge, cancellationToken))
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.ChallengeUsed, "The challenge made an entry that is gone since.");
                }

                if (EntryLimits.Counts(created.State, created.IsTest))
                {
                    var full = await places.CountAsync(created.FormId, submission.Answers.Places, cancellationToken);
                    var taken = full.IsEmpty ? await unique.FindAsync(created.Id, submission.Answers.Unique, cancellationToken) : [];
                    if (!full.IsEmpty || !taken.IsEmpty)
                    {
                        throw new SubmissionInvalidException([.. full, .. taken]);
                    }
                }

                await events.AddAsync(new NewEntryEvent(created.Id, SubmittedEvent, Details: Details(outcome, submission.Answers.State.Hidden, waits || KeepsChosenActions(definition, state) ? submission.Answers.State.Actions : null)), cancellationToken);
                if (neverKept.Count > 0)
                {
                    await events.AddAsync(new NewEntryEvent(created.Id, ActionSettlement.ValuesRemovedEvent, Details: JsonSerializer.Serialize(new { fields = neverKept.Select(key => key.ToString("D")).Order(StringComparer.Ordinal) })), cancellationToken);
                }

                if (queued.Count > 0)
                {
                    await runs.AddAsync(created.Id, created.FormVersionId, Guid.NewGuid(), queued, cancellationToken: cancellationToken);
                }

                // A test entry adds nothing to the form's counts, which insights read (section 4.4).
                if (counts.Count > 0)
                {
                    await stats.IncrementAsync(created.FormId, counts, cancellationToken);
                }

                if (submission.Draft is { } draft)
                {
                    // The unit holds the form's row, so of two submits that name one draft only the first finds it.
                    await emails.DeleteUnsentAsync(draft, cancellationToken);
                    if (!await entries.DeleteAsync(draft, cancellationToken))
                    {
                        throw new FormBuilderConflictException(FormBuilderConflict.DuplicateSubmission, "Another submission finished this draft a moment ago.");
                    }
                }

                if (state == EntryState.Unverified)
                {
                    await events.AddAsync(new NewEntryEvent(created.Id, EntryVerification.RequestedEvent, DueIn: TimeSpan.Zero), cancellationToken);
                    await events.AddAsync(new NewEntryEvent(created.Id, EntryVerification.ExpiresEvent, DueIn: EntryVerification.TimeOf(definition)), cancellationToken);
                }
            },
            cancellationToken);
        if (stored is null)
        {
            return null;
        }

        if (stored.State == EntryState.Unverified)
        {
            wake.Wake();
        }

        // An entry that waits for a confirmation or sits in Spam is told of when it gets here (8.5, EntrySpamFolder); a
        // test entry that sets off nothing is told of to nobody (ADR-0204).
        if (submission.RunsActions && stored.State is EntryState.Submitted or EntryState.Approved)
        {
            await entryEvents.TellArrivedAsync(stored, scope.FormAlias);
        }

        try
        {
            var receipt = await ReceiptAsync(stored, definition, submission.Page.Site, cancellationToken);
            var rendered = await outcomes.RenderAsync(outcome, scope with { Entry = EntryOf(stored, definition), Answers = submission.Answers.ForMergeTags }, submission.Redirect, cancellationToken);
            return new SubmittedEntry(stored, receipt, rendered);
        }
        finally
        {
            // A form that stores no entries lets it go at once when no action can run for it (section 4.4, ADR-0158), with the
            // receipt and the outcome made or not, and whether the visitor is still there or not.
            if (queued.Count == 0 && !waits && definition.Settings.Submission.Store == EntryStorage.Never)
            {
                await settlement.SettleAsync(stored.Id, definition, CancellationToken.None);
            }
        }
    }

    // What the entry adds to the form's counts in its unit: none for a test entry; spam for one in Spam; otherwise a
    // submission and, while the form's insights are on, its page, its hour in the time zone every site shares and its
    // time to complete (ADR-0211), so a rollback counts none of them.
    private async Task<IReadOnlyList<DailyStatCount>> CountsAsync(NewSubmission submission, SpamVerdict verdict, DateTimeOffset now, TimeSpan? duration, CancellationToken cancellationToken)
    {
        if (submission.IsTest)
        {
            return [];
        }

        if (verdict.IsSpam)
        {
            return [new DailyStatCount(SpamMetric)];
        }

        if (!await insights.IsOnAsync(submission.Form.Definition, cancellationToken))
        {
            return [new DailyStatCount(SubmissionsMetric)];
        }

        // The hour by the clock every site shares, which the report names: one form's hours take one clock (ADR-0211).
        var zone = await timeZones.GetAsync(null, cancellationToken);
        return [new DailyStatCount(SubmissionsMetric), .. InsightMetrics.OfEntry(submission.Page.Content, TimeZoneInfo.ConvertTime(now, zone), duration)];
    }

    // Whether a later change of the entry's state still needs what the rules chose as it came in: its runs do not all queue now.
    private static bool KeepsChosenActions(FormDefinition definition, EntryState state) =>
        state is EntryState.Spam or EntryState.Unverified
        || (state == EntryState.Submitted && definition.Actions.Any(action => action.Enabled && action.Trigger != ActionTrigger.Submitted))
        || (state == EntryState.Approved && definition.Actions.Any(action => action.Enabled && action.Trigger == ActionTrigger.Rejected));

    /// <summary>
    /// An entry's receipt and outcome again, for a retry of its answers: rendered with the version it was submitted
    /// with, its stored answers, its context values and the outcome its <c>submitted</c> event names. A replay has no
    /// query values, which no entry keeps.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="form">The form as the site serves it now, whose name and alias the outcome renders with.</param>
    /// <param name="placement">The page the retry's placement names (ADR-0129), which no entry keeps, or null.</param>
    /// <param name="cancellationToken">Cancels the reads and the rendering.</param>
    public async Task<SubmittedEntry> ReplayAsync(StoredEntry entry, PublishedForm form, Guid? placement = null, CancellationToken cancellationToken = default)
    {
        var (scope, timeline) = await ReadAsync(entry, form, cancellationToken);
        var receipt = await ReceiptAsync(entry, scope.Form, scope.Site, cancellationToken);
        return new SubmittedEntry(entry, receipt, await outcomes.RenderAsync(Outcome(timeline), scope, placement, cancellationToken));
    }

    /// <summary>
    /// What a copy of an entry's answers shows (ADR-0123): the scope its replay renders in, with what the rules hid for
    /// it as its <c>submitted</c> event records it.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="form">The form as the site serves it now, as for <see cref="ReplayAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryCopy> CopyAsync(StoredEntry entry, PublishedForm form, CancellationToken cancellationToken = default)
    {
        var (scope, timeline) = await ReadAsync(entry, form, cancellationToken);
        return new EntryCopy(scope, Hidden(timeline), entry.SubmittedUtc);
    }

    /// <summary>
    /// What an action runs for (ADR-0158): the scope its replay renders in, with the version the entry was submitted with
    /// and the form's name and alias now. Null when the form or the version is gone.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<MergeTagScope?> ScopeAsync(StoredEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form
            || await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is not { } version)
        {
            return null;
        }

        return (await ReadAsync(entry, locks.Apply(documents.Readable(version)), form.Name, form.Alias, cancellationToken)).Scope;
    }

    /// <summary>
    /// What the entry panel shows of an entry (ADR-0208): its scope as <see cref="ScopeAsync"/> makes it, with the
    /// answers as stored (their files and consent wordings) and its whole timeline, each read once. Null when the
    /// version is gone.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="form">Its form.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryRead?> ReadWholeAsync(StoredEntry entry, StoredForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(form);
        if (await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is not { } version)
        {
            return null;
        }

        var definition = locks.Apply(documents.Readable(version));
        var page = await pages.PageAsync(entry.ContentKey);
        var timeline = await events.ListForEntryAsync(entry.Id, cancellationToken);
        var answers = await store.ReadAsync(entry.Id, cancellationToken);
        return new EntryRead(ScopeOf(entry, definition, form.Name, form.Alias, page, timeline, answers), answers, timeline);
    }

    /// <summary>
    /// What a list shows of a page of a form's entries (ADR-0207): the scope of each, as <see cref="ScopeAsync"/> makes
    /// it, from one read of their answers and one of their <c>submitted</c> events, each version and page they name read
    /// once. An entry whose version is gone is left out.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <param name="page">Its entries.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyDictionary<long, MergeTagScope>> ScopesAsync(StoredForm form, IReadOnlyList<StoredEntry> page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(page);
        var scopes = new Dictionary<long, MergeTagScope>();
        if (page.Count == 0)
        {
            return scopes;
        }

        var ids = page.Select(entry => entry.Id).ToArray();
        var answers = await store.ReadManyAsync(form.Id, ids, cancellationToken);
        var submitted = (await events.ListOfKindAsync(ids, SubmittedEvent, cancellationToken)).ToLookup(entryEvent => entryEvent.EntryId);
        var definitions = new Dictionary<int, FormDefinition?>();
        var places = new Dictionary<Guid, RequestPage>();
        RequestPage? nowhere = null;
        foreach (var entry in page)
        {
            if (!definitions.TryGetValue(entry.FormVersionId, out var definition))
            {
                definition = definitions[entry.FormVersionId] = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version
                    ? locks.Apply(documents.Readable(version))
                    : null;
            }

            if (definition is null)
            {
                continue;
            }

            RequestPage place;
            if (entry.ContentKey is { } content)
            {
                if (!places.TryGetValue(content, out place!))
                {
                    place = places[content] = await pages.PageAsync(content);
                }
            }
            else
            {
                place = nowhere ??= await pages.PageAsync(null);
            }

            scopes[entry.Id] = ScopeOf(entry, definition, form.Name, form.Alias, place, [.. submitted[entry.Id]], answers.GetValueOrDefault(entry.Id));
        }

        return scopes;
    }

    // An entry as its replay and its copy read it.
    private async Task<(MergeTagScope Scope, IReadOnlyList<StoredEntryEvent> Timeline)> ReadAsync(StoredEntry entry, PublishedForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(form);
        var definition = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version
            ? locks.Apply(documents.Readable(version))
            : form.Definition;
        return await ReadAsync(entry, definition, form.Form.Name, form.Form.Alias, cancellationToken);
    }

    // The scope with the version it was submitted with, its page, its context values and its stored answers, with the
    // fields the rules hid for it, and its timeline.
    private async Task<(MergeTagScope Scope, IReadOnlyList<StoredEntryEvent> Timeline)> ReadAsync(StoredEntry entry, FormDefinition definition, string formName, string formAlias, CancellationToken cancellationToken)
    {
        var page = await pages.PageAsync(entry.ContentKey);
        var timeline = await events.ListForEntryAsync(entry.Id, cancellationToken);
        var answers = await store.ReadAsync(entry.Id, cancellationToken);
        return (ScopeOf(entry, definition, formName, formAlias, page, timeline, answers), timeline);
    }

    // The scope of an entry from what was read for it: the events need hold only its submitted one.
    private static MergeTagScope ScopeOf(StoredEntry entry, FormDefinition definition, string formName, string formAlias, RequestPage page, IReadOnlyList<StoredEntryEvent> timeline, EntryAnswers? answers) => new()
    {
        Form = definition,
        FormName = formName,
        FormAlias = formAlias,
        Culture = entry.Culture,
        Site = page.Site,
        Page = page.Content,
        Entry = EntryOf(entry, definition),
        Context = Context(entry.Context),
        Answers = answers?.ForMergeTags(Hidden(timeline)) ?? MergeTagAnswers.None,
    };

    // The runs of the enabled actions the rules let run that fire on these triggers, in the form's order.
    internal static List<NewActionRun> Runs(FormDefinition definition, IReadOnlyList<string> running, params ActionTrigger[] triggers)
    {
        var chosen = running.ToHashSet(StringComparer.Ordinal);
        return [.. definition.Actions
            .Select((action, index) => (Action: action, Index: index))
            .Where(item => item.Action.Enabled && triggers.Contains(item.Action.Trigger) && chosen.Contains(DefinitionLogic.Id(item.Action.Key)))
            .Select(item => new NewActionRun(item.Action.Key, item.Action.Type, item.Action.Trigger, item.Index, item.Action.StopOnFailure))];
    }

    // The triggers an entry fires as it arrives: approved too, on a form that does not wait for a person to approve (section 4.4).
    internal static ActionTrigger[] ArrivalTriggers(FormDefinition definition, bool verified)
    {
        var automatic = definition.Settings.Submission.Approval == ApprovalMode.Automatic;
        return (verified, automatic) switch
        {
            (true, true) => [ActionTrigger.Submitted, ActionTrigger.Verified, ActionTrigger.Approved],
            (true, false) => [ActionTrigger.Submitted, ActionTrigger.Verified],
            (false, true) => [ActionTrigger.Submitted, ActionTrigger.Approved],
            _ => [ActionTrigger.Submitted],
        };
    }

    // The state an entry reaches as it arrives.
    internal static EntryState ArrivalState(FormDefinition definition) =>
        definition.Settings.Submission.Approval == ApprovalMode.Automatic ? EntryState.Approved : EntryState.Submitted;

    // The receipt: the submission id, the reference in the version's format and the year in the site's time zone.
    private async Task<SubmissionReceipt> ReceiptAsync(StoredEntry entry, FormDefinition definition, Guid? site, CancellationToken cancellationToken)
    {
        var zone = await timeZones.GetAsync(site, cancellationToken);
        var year = TimeZoneInfo.ConvertTime(entry.SubmittedUtc, zone).Year;
        return new SubmissionReceipt(entry.SubmissionId, EntryReferences.Format(definition.Settings.Submission.EntryReference, entry.Number, year), entry.SubmittedUtc);
    }

    // The page's path in the culture, from the content's own address and not from an address the visitor sends, though a request
    // names which page it is on; null without one.
    internal async Task<string?> PathAsync(RequestPage page, string culture, CancellationToken cancellationToken)
    {
        if (page.Content is not { } key || await umbraco.ContentAsync(key) is not { } content)
        {
            return null;
        }

        var baseUrl = await baseUrls.GetAsync(page.Site, cancellationToken) ?? NoOrigin;
        if (umbraco.Url(content, await umbraco.CultureAsync(culture), baseUrl) is not { } address || !Uri.TryCreate(address, UriKind.Absolute, out var absolute))
        {
            return null;
        }

        var path = absolute.AbsolutePath;
        return path.Length <= EntryRepository.MaxPageUrlLength ? path : null;
    }

    // What a visitor's texts may say of an entry; one in Spam reads as any other, so a bot learns nothing (ADR-0012).
    private static MergeTagEntry EntryOf(StoredEntry entry, FormDefinition definition) => new()
    {
        Key = entry.Key,
        Number = entry.Number,
        State = StoredNames.Of(entry.State != EntryState.Spam ? entry.State : definition.Settings.Submission.Verification is null ? ArrivalState(definition) : EntryState.Unverified),
        Submitted = entry.SubmittedUtc,
    };

    // The submitted event's details: the outcome the rules chose and the keys of what they hid, in the form's order,
    // and nothing a visitor typed.
    private static string Details(string? outcome, IReadOnlyList<string> hidden, IReadOnlyList<string>? actions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            if (outcome is not null)
            {
                writer.WriteString(OutcomeDetail, outcome);
            }

            writer.WriteStartArray(HiddenDetail);
            foreach (var id in hidden)
            {
                writer.WriteStringValue(id);
            }

            writer.WriteEndArray();
            if (actions is not null)
            {
                writer.WriteStartArray(ActionsDetail);
                foreach (var id in actions)
                {
                    writer.WriteStringValue(id);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        return StrictText.Utf8.GetString(buffer.WrittenSpan);
    }

    // The keys of what the rules hid, as the entry's submitted event records them; null when it records none.
    private static HashSet<Guid>? Hidden(IReadOnlyList<StoredEntryEvent> timeline)
    {
        if (timeline.FirstOrDefault(item => item.Kind == SubmittedEvent) is not { } submitted)
        {
            return null;
        }

        using var details = JsonDocument.Parse(submitted.Details);
        if (details.RootElement.ValueKind != JsonValueKind.Object || !details.RootElement.TryGetProperty(HiddenDetail, out var hidden) || hidden.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var keys = new HashSet<Guid>();
        foreach (var item in hidden.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParseExact(item.GetString(), "D", out var key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    // The outcome the entry's submitted event names, or null.
    /// <summary>The actions the rules chose for an entry that went to Spam, as its <c>submitted</c> event records them; none for any other.</summary>
    /// <param name="timeline">The entry's timeline.</param>
    internal static IReadOnlyList<string> ChosenActions(IReadOnlyList<StoredEntryEvent> timeline)
    {
        if (timeline.FirstOrDefault(entryEvent => entryEvent.Kind == SubmittedEvent) is not { } submitted)
        {
            return [];
        }

        using var details = JsonDocument.Parse(submitted.Details);
        return details.RootElement.ValueKind == JsonValueKind.Object && details.RootElement.TryGetProperty(ActionsDetail, out var actions) && actions.ValueKind == JsonValueKind.Array
            ? [.. actions.EnumerateArray().Select(action => action.GetString()).OfType<string>()]
            : [];
    }

    private static string? Outcome(IReadOnlyList<StoredEntryEvent> timeline)
    {
        if (timeline.FirstOrDefault(item => item.Kind == SubmittedEvent) is not { } submitted)
        {
            return null;
        }

        using var details = JsonDocument.Parse(submitted.Details);
        return details.RootElement.ValueKind == JsonValueKind.Object && details.RootElement.TryGetProperty(OutcomeDetail, out var key) && key.ValueKind == JsonValueKind.String
            ? key.GetString()
            : null;
    }

    // An entry's stored context values.
    private static Dictionary<string, string> Context(string json)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var context = JsonDocument.Parse(json);
        foreach (var property in context.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                values[property.Name] = property.Value.GetString()!;
            }
        }

        return values;
    }

    // Names and texts as a JSON object, in their order.
    private static string Json(IReadOnlyDictionary<string, string> values)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in values)
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return StrictText.Utf8.GetString(buffer.WrittenSpan);
    }
}
