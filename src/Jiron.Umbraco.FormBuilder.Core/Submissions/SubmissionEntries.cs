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
    IReadOnlyDictionary<string, string> Context);

/// <summary>An entry with its receipt and outcome (ADR-0088).</summary>
/// <param name="Entry">The entry.</param>
/// <param name="Receipt">Its receipt.</param>
/// <param name="Outcome">Its outcome.</param>
internal sealed record SubmittedEntry(StoredEntry Entry, SubmissionReceipt Receipt, SubmissionOutcome Outcome);

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

    // The name of the outcome in the submitted event's details.
    private const string OutcomeDetail = "outcome";

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
        var state = definition.Settings.Submission.Verification is null ? EntryState.Submitted : EntryState.Unverified;
        var queued = state == EntryState.Unverified ? [] : Runs(definition, submission.Answers.State.Actions);
        var kept = queued.Count > 0 || TriggerCanFire(definition, state);
        var answers = kept ? submission.Answers.Answers : [.. submission.Answers.Answers.Where(answer => !KeptUntilActions(definition, answer.Field))];
        var entry = new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = submission.FormId,
            FormVersionId = submission.FormVersionId,
            State = state,
            SubmissionId = submission.SubmissionId,
            SubmissionHash = submission.Digest,
            Culture = submission.Culture,
            ContentKey = submission.Page.Content,
            PageUrl = await PathAsync(submission.Page, submission.Culture, cancellationToken),
            Context = Json(submission.Context),
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
                // word. An entry that does not count holds no place and no answer, and takes none from another.
                if (EntryLimits.Counts(created.State, created.IsTest))
                {
                    var full = await places.CountAsync(created.FormId, submission.Answers.Places, cancellationToken);
                    var taken = await unique.FindAsync(created.Id, submission.Answers.Unique, cancellationToken);
                    if (!full.IsEmpty || !taken.IsEmpty)
                    {
                        throw new SubmissionInvalidException([.. full, .. taken]);
                    }
                }

                await events.AddAsync(new NewEntryEvent(created.Id, SubmittedEvent, Details: Details(outcome)), cancellationToken);
                if (queued.Count > 0)
                {
                    await runs.AddAsync(created.Id, created.FormVersionId, Guid.NewGuid(), queued, cancellationToken: cancellationToken);
                }

                await stats.IncrementAsync(created.FormId, [new DailyStatCount(SubmissionsMetric)], cancellationToken);
            },
            cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var receipt = await ReceiptAsync(stored, definition, submission.Page.Site, cancellationToken);
        var rendered = await outcomes.RenderAsync(outcome, scope with { Entry = EntryOf(stored), Answers = submission.Answers.ForMergeTags }, cancellationToken);
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
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(form);
        var definition = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version
            ? locks.Apply(documents.Readable(version))
            : form.Definition;
        var page = await pages.PageAsync(entry.ContentKey);
        var receipt = await ReceiptAsync(entry, definition, page.Site, cancellationToken);
        var outcome = Outcome(await events.ListForEntryAsync(entry.Id, cancellationToken));
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
            Answers = answers?.ForMergeTags() ?? MergeTagAnswers.None,
        };
        return new SubmittedEntry(entry, receipt, await outcomes.RenderAsync(outcome, scope, cancellationToken));
    }

    // The runs of the enabled actions the rules let run that fire on submission, in the form's order.
    private static List<NewActionRun> Runs(FormDefinition definition, IReadOnlyList<string> running)
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
        if (state == EntryState.Unverified)
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

    // The page's path in the culture, from the content's address and never from the visitor; null without one.
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

    private static MergeTagEntry EntryOf(StoredEntry entry) => new()
    {
        Key = entry.Key,
        Number = entry.Number,
        State = StoredNames.Of(entry.State),
        Submitted = entry.SubmittedUtc,
    };

    // The submitted event's details: the outcome the rules chose, and nothing a visitor typed.
    private static string Details(string? outcome) => outcome is null ? "{}" : Json(new Dictionary<string, string> { [OutcomeDetail] = outcome });

    // The outcome the entry's submitted event names, or null.
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
