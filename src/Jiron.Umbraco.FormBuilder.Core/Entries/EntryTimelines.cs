using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>A field an edit changed, as the timeline shows it.</summary>
/// <param name="Label">The field's label when it was edited.</param>
/// <param name="Before">Its answer's texts before.</param>
/// <param name="After">Its answer's texts after.</param>
/// <param name="Masked">Whether the field is sensitive and the reader may not see it, so neither is shown.</param>
internal sealed record EntryTimelineChange(string Label, IReadOnlyList<string> Before, IReadOnlyList<string> After, bool Masked);

/// <summary>An event of an entry's timeline, with what its kind says.</summary>
/// <param name="Id">The event.</param>
/// <param name="When">When it happened.</param>
/// <param name="Kind">What happened, as the entry's events name it.</param>
/// <param name="User">The backoffice user who did it, or none.</param>
/// <param name="From">The state a move or an Undo left.</param>
/// <param name="To">The state it reached.</param>
/// <param name="Count">How many actions a re-run queued.</param>
/// <param name="Recipient">Whom a forward went to: a user's name or an address.</param>
/// <param name="Changes">What an edit changed.</param>
internal sealed record EntryTimelineEvent(
    long Id,
    DateTimeOffset When,
    string Kind,
    Guid? User,
    string? From,
    string? To,
    int? Count,
    string? Recipient,
    IReadOnlyList<EntryTimelineChange> Changes);

/// <summary>A mail of an entry.</summary>
/// <param name="Id">The mail.</param>
/// <param name="When">When it went out, or was queued while it has not.</param>
/// <param name="Kind">What sent it.</param>
/// <param name="To">Its recipients, an address the reader may not see masked.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="State">Where it stands.</param>
/// <param name="ActionRun">The action run that sent it, or none.</param>
internal sealed record EntryTimelineMail(long Id, DateTimeOffset When, EmailKind Kind, string To, string Subject, EmailState State, long? ActionRun);

/// <summary>An action run of an entry.</summary>
/// <param name="Id">The run, which its retry names.</param>
/// <param name="When">When it last tried, finished or was queued.</param>
/// <param name="Action">The action.</param>
/// <param name="Name">The action's name in the entry's version, or none.</param>
/// <param name="Type">The action type's alias.</param>
/// <param name="State">Where it stands.</param>
/// <param name="Attempts">How many times it tried.</param>
/// <param name="Error">Why its last try failed, as stored, or none.</param>
internal sealed record EntryTimelineRun(long Id, DateTimeOffset When, Guid Action, string? Name, string Type, ActionRunState State, int Attempts, string? Error);

/// <summary>An entry's events, action runs and mail, newest first.</summary>
/// <param name="Events">Its events.</param>
/// <param name="Runs">Its action runs.</param>
/// <param name="Mail">Its mail.</param>
internal sealed record EntryTimeline(IReadOnlyList<EntryTimelineEvent> Events, IReadOnlyList<EntryTimelineRun> Runs, IReadOnlyList<EntryTimelineMail> Mail);

/// <summary>
/// An entry's timeline for the panel (ADR-0208): its events, its action runs named as its version names their actions,
/// and its mail, newest first. What a reader may not see stays masked: an edit's sensitive values, and a recipient that
/// is a sensitive answer of the entry.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The timelines.</param>
/// <param name="emails">The mail.</param>
/// <param name="store">The stored answers, for the sensitive addresses.</param>
/// <param name="runs">The action runs.</param>
/// <param name="documents">Reads the entry's version, for its actions' names.</param>
internal sealed class EntryTimelines(IFormRepository forms, IEntryRepository entries, IEntryEventRepository events, IEmailRepository emails, EntryAnswerStore store, IActionRunRepository runs, FormDocuments documents)
{
    /// <summary>The timeline of an entry of a form; null when the form has no such entry.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="maySeeSensitive">Whether the reader may see sensitive values.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryTimeline?> ReadAsync(Guid formKey, Guid entryKey, bool maySeeSensitive, CancellationToken cancellationToken = default)
    {
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form
            || await entries.GetAsync(entryKey, cancellationToken) is not { } entry
            || entry.FormId != form.Id
            || entry.State == EntryState.Draft)
        {
            return null;
        }

        var hidden = maySeeSensitive ? [] : await SensitiveTextsAsync(form.Id, entry.Id, cancellationToken);
        var timeline = (await events.ListForEntryAsync(entry.Id, cancellationToken))
            .OrderByDescending(entryEvent => entryEvent.OccurredUtc)
            .ThenByDescending(entryEvent => entryEvent.Id)
            .Select(entryEvent => EventOf(entryEvent, maySeeSensitive))
            .ToList();
        var mail = (await emails.ListForEntryAsync(entry.Id, cancellationToken))
            .Select(email => new EntryTimelineMail(email.Id, email.SentUtc ?? email.CreatedUtc, email.Kind, Masked(email.To, hidden), email.Subject, email.State, email.ActionRunId))
            .OrderByDescending(email => email.When)
            .ThenByDescending(email => email.Id)
            .ToList();
        var names = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version
            ? documents.Readable(version).Actions.GroupBy(action => action.Key).ToDictionary(group => group.Key, group => group.First().Name)
            : [];
        var ran = (await runs.ListForEntryAsync(entry.Id, cancellationToken))
            .Select(run => new EntryTimelineRun(run.Id, run.LastAttemptUtc ?? run.FinishedUtc ?? run.StartedUtc ?? run.CreatedUtc, run.ActionKey, names.GetValueOrDefault(run.ActionKey), run.ActionType, run.State, run.Attempts, run.LastError?.Message ?? run.LastError?.Code))
            .OrderByDescending(run => run.When)
            .ThenByDescending(run => run.Id)
            .ToList();
        return new EntryTimeline(timeline, ran, mail);
    }

    // An event with what its kind's details say.
    private static EntryTimelineEvent EventOf(StoredEntryEvent entryEvent, bool maySeeSensitive)
    {
        string? from = null, to = null, recipient = null;
        int? count = null;
        List<EntryTimelineChange> changes = [];
        try
        {
            using var details = JsonDocument.Parse(entryEvent.Details);
            var root = details.RootElement;
            switch (entryEvent.Kind)
            {
                case EntryStateChanges.ChangedEvent or EntryStateChanges.UndoneEvent:
                    from = Text(root, "from");
                    to = Text(root, "to");
                    break;
                case EntryActionReruns.RerunEvent when root.TryGetProperty("actions", out var actions) && actions.ValueKind == JsonValueKind.Array:
                    count = actions.GetArrayLength();
                    break;
                case EntryForwards.ForwardedEvent:
                    recipient = Text(root, "to");
                    break;
                case EntryEdits.EditedEvent when root.TryGetProperty("changes", out var edited) && edited.ValueKind == JsonValueKind.Array:
                    foreach (var change in edited.EnumerateArray())
                    {
                        var masked = !maySeeSensitive && change.TryGetProperty("sensitive", out var sensitive) && sensitive.ValueKind == JsonValueKind.True;
                        changes.Add(new EntryTimelineChange(Text(change, "label") ?? string.Empty, masked ? [] : Texts(change, "before"), masked ? [] : Texts(change, "after"), masked));
                    }

                    break;
            }
        }
        catch (JsonException)
        {
            // An event whose details cannot be read still says what happened.
        }

        return new EntryTimelineEvent(entryEvent.Id, entryEvent.OccurredUtc, entryEvent.Kind, entryEvent.UserKey, from, to, count, recipient, changes);
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static List<string> Texts(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
            : [];

    // The texts of the entry's sensitive answers, which a recipient may be.
    private async Task<HashSet<string>> SensitiveTextsAsync(int formId, long entryId, CancellationToken cancellationToken)
    {
        var sensitive = (await forms.ListFieldsAsync(formId, cancellationToken)).Where(field => field.Values.IsSensitive).Select(field => field.Values.FieldKey).ToHashSet();
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sensitive.Count > 0 && await store.ReadAsync(entryId, cancellationToken) is { } read)
        {
            foreach (var answer in read.All.Where(answer => sensitive.Contains(answer.Field) || (answer.Repeater is { } repeater && sensitive.Contains(repeater))))
            {
                found.UnionWith(answer.Values.Select(value => value.Text).Where(text => !string.IsNullOrWhiteSpace(text)).Select(text => text!.Trim()));
            }
        }

        return found;
    }

    // A mail's recipients with each address that is a sensitive answer masked.
    private static string Masked(string to, HashSet<string> hidden) =>
        hidden.Count == 0
            ? to
            : string.Join(", ", to.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(address => hidden.Contains(address) ? MergeTags.MergeTagAnswerTexts.Masked : address));
}
