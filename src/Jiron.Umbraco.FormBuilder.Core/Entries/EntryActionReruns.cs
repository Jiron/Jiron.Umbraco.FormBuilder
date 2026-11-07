using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>An action a re-run runs for one entry at least.</summary>
/// <param name="Key">The action.</param>
/// <param name="Type">Its type's alias.</param>
/// <param name="Name">Its name, or none.</param>
/// <param name="Trigger">What starts it.</param>
internal sealed record EntryRerunAction(Guid Key, string Type, string? Name, ActionTrigger Trigger);

/// <summary>What a re-run of entries' actions does, or would do.</summary>
/// <param name="Actions">The actions it runs for one entry at least, in the form's order.</param>
/// <param name="Entries">How many entries it runs actions for.</param>
/// <param name="LeftOut">How many it leaves out: gone, in Spam, waiting for their address, a test, or with no action to run.</param>
internal sealed record EntryRerun(IReadOnlyList<EntryRerunAction> Actions, int Entries, int LeftOut);

/// <summary>
/// Runs entries' actions again by a user's hand (ADR-0207): for each entry that was sent and is not in Spam or waiting
/// for its address, the enabled actions of its version that the rules chose for it, of the triggers it has passed
/// (Submitted; Verified once its address was confirmed; Approved or Rejected as it stands), in the form's order, as one
/// new change that is due at once. Its timeline says so, and the audit log names the entries.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The timelines.</param>
/// <param name="runs">The action outbox.</param>
/// <param name="audit">The audit log.</param>
/// <param name="documents">Reads stored versions.</param>
internal sealed class EntryActionReruns(
    FormBuilderDatabase database,
    IFormRepository forms,
    IEntryRepository entries,
    IEntryEventRepository events,
    IActionRunRepository runs,
    IAuditRepository audit,
    FormDocuments documents)
{
    /// <summary>The kind of the event that records a re-run; its details name the actions.</summary>
    public const string RerunEvent = "actionsRerun";

    /// <summary>The audit action of a re-run; its details name the entries' numbers.</summary>
    public const string RerunAction = "entry.actionsRerun";

    /// <summary>The most entries one re-run takes.</summary>
    public const int MaxEntries = EntryStateChanges.MaxEntries;

    /// <summary>What a re-run would do, without doing it, for the user to confirm.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries, 1 to <see cref="MaxEntries"/>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public Task<EntryRerun> PlanAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, CancellationToken cancellationToken = default) =>
        RunAsync(formKey, entryKeys, null, cancellationToken);

    /// <summary>Runs the entries' actions again.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKeys">The entries, 1 to <see cref="MaxEntries"/>.</param>
    /// <param name="userKey">The user who asks.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public Task<EntryRerun> RerunAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, Guid userKey, CancellationToken cancellationToken = default) =>
        RunAsync(formKey, entryKeys, userKey, cancellationToken);

    // The triggers an entry has passed.
    private static ActionTrigger[] Passed(StoredEntry entry, FormDefinition definition)
    {
        var passed = new List<ActionTrigger> { ActionTrigger.Submitted };
        if (entry.VerifiedUtc is not null && definition.Settings.Submission.Verification is not null)
        {
            passed.Add(ActionTrigger.Verified);
        }

        if (entry.State == EntryState.Approved)
        {
            passed.Add(ActionTrigger.Approved);
        }
        else if (entry.State == EntryState.Rejected)
        {
            passed.Add(ActionTrigger.Rejected);
        }

        return [.. passed];
    }

    private async Task<EntryRerun> RunAsync(Guid formKey, IReadOnlyCollection<Guid> entryKeys, Guid? userKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entryKeys);
        if (entryKeys.Count > MaxEntries)
        {
            throw new ArgumentException($"One re-run takes at most {MaxEntries} entries.", nameof(entryKeys));
        }

        var keys = entryKeys.Distinct().ToList();
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return new EntryRerun([], 0, keys.Count);
        }

        var found = new List<StoredEntry>();
        foreach (var key in keys)
        {
            if (await entries.GetAsync(key, cancellationToken) is { } entry && entry.FormId == form.Id && !entry.IsTest
                && entry.State is EntryState.Submitted or EntryState.Approved or EntryState.Rejected)
            {
                found.Add(entry);
            }
        }

        var submitted = (await events.ListOfKindAsync([.. found.Select(entry => entry.Id)], SubmissionEntries.SubmittedEvent, cancellationToken)).ToLookup(entryEvent => entryEvent.EntryId);
        var definitions = new Dictionary<int, FormDefinition?>();
        var planned = new List<(StoredEntry Entry, IReadOnlyList<NewActionRun> Runs)>();
        var actions = new Dictionary<Guid, (FormAction Action, int Order)>();
        foreach (var entry in found)
        {
            if (!definitions.TryGetValue(entry.FormVersionId, out var definition))
            {
                definition = definitions[entry.FormVersionId] = await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version ? documents.Readable(version) : null;
            }

            if (definition is null)
            {
                continue;
            }

            // The rules' choice, as the submitted event keeps it when a later trigger needs it, or as the runs it queued show it.
            IReadOnlyList<string> chosen = SubmissionEntries.ChosenActions([.. submitted[entry.Id]]);
            if (chosen.Count == 0)
            {
                chosen = [.. (await runs.ListForEntryAsync(entry.Id, cancellationToken)).Select(run => DefinitionLogic.Id(run.ActionKey)).Distinct(StringComparer.Ordinal)];
            }

            var queued = SubmissionEntries.Runs(definition, chosen, Passed(entry, definition));
            if (queued.Count == 0)
            {
                continue;
            }

            planned.Add((entry, queued));
            foreach (var run in queued)
            {
                actions.TryAdd(run.ActionKey, (definition.Actions[run.SortOrder], run.SortOrder));
            }
        }

        if (userKey is { } user && planned.Count > 0)
        {
            var change = Guid.NewGuid();
            await database.WriteAsync(
                async _ =>
                {
                    foreach (var (entry, queued) in planned)
                    {
                        var details = JsonSerializer.Serialize(new { actions = queued.Select(run => DefinitionLogic.Id(run.ActionKey)) });
                        await events.AddAsync(new NewEntryEvent(entry.Id, RerunEvent, user, change, details), cancellationToken);
                        await runs.AddAsync(entry.Id, entry.FormVersionId, change, queued, cancellationToken: cancellationToken);
                    }

                    foreach (var chunk in planned.Chunk(AuditDetails.MaxListLength))
                    {
                        await audit.AddAsync(FormAudit.Entry(RerunAction, form, user, new AuditDetails().Add("entries", chunk.Length).AddNumbers("numbers", chunk.Select(item => (long)item.Entry.Number))), cancellationToken);
                    }

                    return true;
                },
                cancellationToken);
        }

        return new EntryRerun(
            [.. actions.Values.OrderBy(item => item.Order).Select(item => new EntryRerunAction(item.Action.Key, item.Action.Type, item.Action.Name, item.Action.Trigger))],
            planned.Count,
            keys.Count - planned.Count);
    }
}
