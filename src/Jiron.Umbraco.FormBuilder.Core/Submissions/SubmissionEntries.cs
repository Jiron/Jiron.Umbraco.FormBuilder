using System.Buffers;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;

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
    SpamVerdict? Verdict = null);

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

/// <summary>
/// The entries of submissions (ADR-0088): stores a submission's entry in one transaction with its <c>submitted</c>
/// event, its action runs and its daily count, once its choices are held against the places left (ADR-0090) and its
/// answers that must be unique against the other entries' (ADR-0091), and gives
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
    SiteBaseUrl baseUrls)
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
        var state = verdict.IsSpam ? EntryState.Spam : definition.Settings.Submission.Verification is null ? EntryState.Submitted : EntryState.Unverified;
        var queued = state is EntryState.Unverified or EntryState.Spam ? [] : Runs(definition, submission.Answers.State.Actions);
        var kept = queued.Count > 0 || TriggerCanFire(definition, state);
        var answers = kept ? submission.Answers.Answers : [.. submission.Answers.Answers.Where(answer => !KeptUntilActions(definition, answer.Field))];
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
            Context = Json(submission.Context),
            SpamScore = verdict.Score,
            SpamReasons = SpamReasons.Stored(verdict.Reasons),
            EntryLimit = definition.Settings.Availability.MaxEntries,
        };

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
                if (EntryLimits.Counts(created.State, created.IsTest))
                {
                    var full = await places.CountAsync(created.FormId, submission.Answers.Places, cancellationToken);
                    var taken = full.IsEmpty ? await unique.FindAsync(created.Id, submission.Answers.Unique, cancellationToken) : [];
                    if (!full.IsEmpty || !taken.IsEmpty)
                    {
                        throw new SubmissionInvalidException([.. full, .. taken]);
                    }
                }

                await events.AddAsync(new NewEntryEvent(created.Id, SubmittedEvent, Details: Details(outcome, submission.Answers.State.Hidden, verdict.IsSpam ? submission.Answers.State.Actions : null)), cancellationToken);
                if (queued.Count > 0)
                {
                    await runs.AddAsync(created.Id, created.FormVersionId, Guid.NewGuid(), queued, cancellationToken: cancellationToken);
                }

                await stats.IncrementAsync(created.FormId, [new DailyStatCount(verdict.IsSpam ? SpamMetric : SubmissionsMetric)], cancellationToken);
            },
            cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var receipt = await ReceiptAsync(stored, definition, submission.Page.Site, cancellationToken);
        var rendered = await outcomes.RenderAsync(outcome, scope with { Entry = EntryOf(stored), Answers = submission.Answers.ForMergeTags }, submission.Redirect, cancellationToken);
        return new SubmittedEntry(stored, receipt, rendered);
    }

    /// <summary>
    /// An entry's receipt and outcome again, for a retry of its answers: rendered with the version it was submitted
    /// with, its stored answers, its context values and the outcome its <c>submitted</c> event names. A replay has no
    /// query values, which no entry keeps.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="form">The form as the site serves it now, whose name and alias the outcome renders with.</param>
    /// <param name="cancellationToken">Cancels the reads and the rendering.</param>
    public async Task<SubmittedEntry> ReplayAsync(StoredEntry entry, PublishedForm form, CancellationToken cancellationToken = default)
    {
        var (scope, timeline) = await ReadAsync(entry, form, cancellationToken);
        var receipt = await ReceiptAsync(entry, scope.Form, scope.Site, cancellationToken);
        return new SubmittedEntry(entry, receipt, await outcomes.RenderAsync(Outcome(timeline), scope, cancellationToken: cancellationToken));
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

    // An entry as its replay and its copy read it: the scope with the version it was submitted with, its page, its
    // context values and its stored answers, with the fields the rules hid for it, and its timeline.
    private async Task<(MergeTagScope Scope, IReadOnlyList<StoredEntryEvent> Timeline)> ReadAsync(StoredEntry entry, PublishedForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(form);
        var definition = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version
            ? locks.Apply(documents.Readable(version))
            : form.Definition;
        var page = await pages.PageAsync(entry.ContentKey);
        var timeline = await events.ListForEntryAsync(entry.Id, cancellationToken);
        var answers = await store.ReadAsync(entry.Id, cancellationToken);
        var scope = new MergeTagScope
        {
            Form = definition,
            FormName = form.Form.Name,
            FormAlias = form.Form.Alias,
            Culture = entry.Culture,
            Site = page.Site,
            Page = page.Content,
            Entry = EntryOf(entry),
            Context = Context(entry.Context),
            Answers = answers?.ForMergeTags(Hidden(timeline)) ?? MergeTagAnswers.None,
        };
        return (scope, timeline);
    }

    // The runs of the enabled actions the rules let run that fire on submission, in the form's order.
    internal static List<NewActionRun> Runs(FormDefinition definition, IReadOnlyList<string> running)
    {
        var chosen = running.ToHashSet(StringComparer.Ordinal);
        return [.. definition.Actions
            .Select((action, index) => (Action: action, Index: index))
            .Where(item => item.Action.Enabled && item.Action.Trigger == ActionTrigger.Submitted && chosen.Contains(DefinitionLogic.Id(item.Action.Key)))
            .Select(item => new NewActionRun(item.Action.Key, item.Action.Type, ActionTrigger.Submitted, item.Index, item.Action.StopOnFailure))];
    }

    // Whether an action of the form may still run for an entry that queued none (section 4.4): any while it waits for
    // its address to be confirmed, an approved or rejected one while it waits for approval, and an approved one when it
    // is approved at once, which 8.5 queues.
    private static bool TriggerCanFire(FormDefinition definition, EntryState state)
    {
        var enabled = definition.Actions.Where(action => action.Enabled).ToList();
        if (state is EntryState.Unverified or EntryState.Spam)
        {
            return enabled.Count > 0;
        }

        return definition.Settings.Submission.Approval == ApprovalMode.Manual
            ? enabled.Any(action => action.Trigger is ActionTrigger.Approved or ActionTrigger.Rejected)
            : enabled.Any(action => action.Trigger == ActionTrigger.Approved);
    }

    // Whether the field keeps its value only until the actions have run.
    private static bool KeptUntilActions(FormDefinition definition, Guid field) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(item => item.Fields.Prepend(item))
            .FirstOrDefault(item => item.Key == field)?.Keep == FieldKeep.UntilActionsRun;

    // The receipt: the submission id, the reference in the version's format and the year in the site's time zone.
    private async Task<SubmissionReceipt> ReceiptAsync(StoredEntry entry, FormDefinition definition, Guid? site, CancellationToken cancellationToken)
    {
        var zone = await timeZones.GetAsync(site, cancellationToken);
        var year = TimeZoneInfo.ConvertTime(entry.SubmittedUtc, zone).Year;
        return new SubmissionReceipt(entry.SubmissionId, EntryReferences.Format(definition.Settings.Submission.EntryReference, entry.Number, year), entry.SubmittedUtc);
    }

    // The page's path in the culture, from the content's own address and not from an address the visitor sends, though a request
    // names which page it is on; null without one.
    private async Task<string?> PathAsync(RequestPage page, string culture, CancellationToken cancellationToken)
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
    private static MergeTagEntry EntryOf(StoredEntry entry) => new()
    {
        Key = entry.Key,
        Number = entry.Number,
        State = StoredNames.Of(entry.State == EntryState.Spam ? EntryState.Submitted : entry.State),
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
