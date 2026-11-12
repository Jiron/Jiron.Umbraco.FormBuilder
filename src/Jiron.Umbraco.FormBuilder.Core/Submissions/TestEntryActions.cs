using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>A form's test entry as the preview's Run actions... sees it (ADR-0204).</summary>
/// <param name="Key">The entry's key.</param>
/// <param name="Number">Its number in the form.</param>
/// <param name="ActionsRan">
/// Whether its actions ran, as its ticket said they should, or wait for its address to be confirmed, so Run actions...
/// has nothing to run.
/// </param>
/// <param name="Actions">The keys of the actions Run actions... would run, in the order shown; none once they ran.</param>
/// <param name="AwaitsConfirmation">Whether its actions wait for its address to be confirmed, as a visitor's entry's do.</param>
internal sealed record TestEntry(Guid Key, int Number, bool ActionsRan, IReadOnlyList<Guid> Actions, bool AwaitsConfirmation = false);

/// <summary>What Run actions... did for a test entry (ADR-0204).</summary>
internal enum TestEntryRun
{
    /// <summary>Its actions are queued, and go out as a visitor's entry's would.</summary>
    Queued,

    /// <summary>Its actions ran before, or wait for its address to be confirmed: nothing more is queued.</summary>
    AlreadyRan,

    /// <summary>The form has no such test entry, or it is gone.</summary>
    NotFound,
}

/// <summary>
/// A test entry's actions (ADR-0204). An entry sent from the preview runs none unless its ticket said so; Run actions...
/// runs them later, once: the actions its rules chose as it came in, which its submitted event keeps, on the triggers it
/// fires as it arrives, a confirmation's among them on a form with email verification, as the version it was sent with
/// holds them. Only a test entry of the form is found, and a second run queues nothing.
/// </summary>
/// <param name="forms">The forms and their versions.</param>
/// <param name="entries">The entries.</param>
/// <param name="events">The entries' timelines.</param>
/// <param name="runs">The action runs.</param>
/// <param name="documents">Reads a version's definition.</param>
internal sealed class TestEntryActions(
    IFormRepository forms,
    IEntryRepository entries,
    IEntryEventRepository events,
    IActionRunRepository runs,
    FormDocuments documents)
{
    /// <summary>The form's test entry a submission id names, with the actions Run actions... would run; null for none.</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the preview sent it with.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<TestEntry?> FindAsync(Guid form, Guid submissionId, CancellationToken cancellationToken = default) =>
        await ReadAsync(form, submissionId, cancellationToken) is { } read
            ? new TestEntry(read.Entry.Key, read.Entry.Number, read.Ran, read.Ran ? [] : [.. read.Queued.Select(run => run.ActionKey)], read.Entry.State == EntryState.Unverified)
            : null;

    /// <summary>The definition of the version the form's test entry was sent with, whose actions Run actions... runs; null for none.</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the preview sent it with.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<Definitions.FormDefinition?> DefinitionOfAsync(Guid form, Guid submissionId, CancellationToken cancellationToken = default) =>
        await forms.GetAsync(form, cancellationToken) is { TrashedUtc: null } row
        && await entries.GetBySubmissionAsync(row.Id, submissionId, cancellationToken) is { IsTest: true } entry
        && await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is { } version
            ? documents.Readable(version)
            : null;

    /// <summary>Queues the test entry's actions, once (ADR-0204).</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="submissionId">The submission id the preview sent it with.</param>
    /// <param name="cancellationToken">Cancels the reads and the unit of work.</param>
    public async Task<TestEntryRun> RunAsync(Guid form, Guid submissionId, CancellationToken cancellationToken = default)
    {
        if (await ReadAsync(form, submissionId, cancellationToken) is not { } read)
        {
            return TestEntryRun.NotFound;
        }

        if (read.Ran)
        {
            return TestEntryRun.AlreadyRan;
        }

        try
        {
            // The entry's own key is the change: a second run of the same entry, even at the same moment, queues nothing.
            return read.Queued.Count == 0 || await runs.AddAsync(read.Entry.Id, read.Entry.FormVersionId, read.Entry.Key, read.Queued, cancellationToken: cancellationToken) is not null
                ? TestEntryRun.Queued
                : TestEntryRun.NotFound;
        }
        catch (FormBuilderConflictException conflict) when (conflict.Conflict == FormBuilderConflict.DuplicateKey)
        {
            return TestEntryRun.AlreadyRan;
        }
    }

    // The test entry, whether its actions ran or wait for a confirmation, and the runs Run actions... would queue.
    private async Task<(StoredEntry Entry, bool Ran, List<NewActionRun> Queued)?> ReadAsync(Guid form, Guid submissionId, CancellationToken cancellationToken)
    {
        if (await forms.GetAsync(form, cancellationToken) is not { TrashedUtc: null } row
            || await entries.GetBySubmissionAsync(row.Id, submissionId, cancellationToken) is not { IsTest: true, State: not (EntryState.Draft or EntryState.Spam) } entry
            || await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is not { } version)
        {
            return null;
        }

        var definition = documents.Readable(version);
        var ran = entry.State == EntryState.Unverified || (await runs.ListForEntryAsync(entry.Id, cancellationToken)).Count > 0;
        var queued = SubmissionEntries.Runs(
            definition,
            SubmissionEntries.ChosenActions(await events.ListForEntryAsync(entry.Id, cancellationToken)),
            SubmissionEntries.ArrivalTriggers(definition, verified: definition.Settings.Submission.Verification is not null));
        return (entry, ran, queued);
    }
}
