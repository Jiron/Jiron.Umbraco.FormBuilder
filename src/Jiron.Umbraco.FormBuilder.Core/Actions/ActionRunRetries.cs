using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>How a request about an action run came out.</summary>
internal enum ActionRunRetryStatus
{
    /// <summary>The run is queued again.</summary>
    Queued,

    /// <summary>The form, the entry or the run does not exist, or they do not belong together.</summary>
    NotFound,

    /// <summary>The run is uncertain and the request did not confirm it: its first attempt may have been delivered.</summary>
    NeedsConfirmation,

    /// <summary>The run did not fail and is not uncertain, so there is nothing to retry.</summary>
    NotRetryable,
}

/// <summary>What a retry of an action run came to.</summary>
/// <param name="Status">How it came out.</param>
/// <param name="Run">The run as it is then; null when it was not found.</param>
internal sealed record ActionRunRetry(ActionRunRetryStatus Status, StoredActionRun? Run);

/// <summary>
/// The action runs of an entry, and the retry from the deliveries log (section 4.4, ADR-0040, ADR-0158): a run that failed
/// or is uncertain starts its attempts over, and an uncertain one only with a confirmation, because its first attempt may
/// already have been delivered. Committing the retry wakes the worker.
/// </summary>
internal sealed class ActionRunRetries(FormBuilderDatabase database, IFormRepository forms, IEntryRepository entries, IActionRunRepository runs, IAuditRepository audit)
{
    /// <summary>The audit action of a retry; the details name the run and whether it was uncertain.</summary>
    public const string RetriedAction = "entry.actionRetried";

    /// <summary>An entry's runs in the order they were queued; null when the form has no such entry.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyList<StoredActionRun>?> ListAsync(Guid formKey, Guid entryKey, CancellationToken cancellationToken = default) =>
        await EntryAsync(formKey, entryKey, cancellationToken) is { } entry ? await runs.ListForEntryAsync(entry.Id, cancellationToken) : null;

    /// <summary>Queues a run again.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="entryKey">The entry.</param>
    /// <param name="runId">The run.</param>
    /// <param name="confirmed">Whether the user confirmed that an uncertain run may go out a second time.</param>
    /// <param name="userKey">Who asks.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <exception cref="FormBuilderConcurrencyException">The run's state changed between the read and the retry.</exception>
    public async Task<ActionRunRetry> RetryAsync(Guid formKey, Guid entryKey, long runId, bool confirmed, Guid userKey, CancellationToken cancellationToken = default)
    {
        if (await EntryAsync(formKey, entryKey, cancellationToken) is not { } entry
            || await runs.GetAsync(runId, cancellationToken) is not { } run
            || run.EntryId != entry.Id)
        {
            return new ActionRunRetry(ActionRunRetryStatus.NotFound, null);
        }

        if (run.State == ActionRunState.Uncertain && !confirmed)
        {
            return new ActionRunRetry(ActionRunRetryStatus.NeedsConfirmation, run);
        }

        if (run.State is not (ActionRunState.Failed or ActionRunState.Uncertain))
        {
            return new ActionRunRetry(ActionRunRetryStatus.NotRetryable, run);
        }

        return await database.WriteAsync(
            async _ =>
            {
                var requeued = await runs.RequeueAsync(runId, run.State, cancellationToken);
                if (requeued is null)
                {
                    return new ActionRunRetry(ActionRunRetryStatus.NotFound, null);
                }

                var details = new AuditDetails().Add("entry", entry.Key).Add("run", runId).Add("uncertain", run.State == ActionRunState.Uncertain);
                await audit.AddAsync(new NewAuditEntry(RetriedAction, FormAudit.EntityType, formKey, userKey, details), cancellationToken);
                return new ActionRunRetry(ActionRunRetryStatus.Queued, requeued);
            },
            cancellationToken);
    }

    private async Task<StoredEntry?> EntryAsync(Guid formKey, Guid entryKey, CancellationToken cancellationToken)
    {
        if (await entries.GetAsync(entryKey, cancellationToken) is not { } entry || await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form)
        {
            return null;
        }

        return form.Key == formKey ? entry : null;
    }
}

/// <summary>How a test run of an action came out.</summary>
internal enum ActionTestStatus
{
    /// <summary>It ran; see the result.</summary>
    Done,

    /// <summary>The form's draft does not exist, or has no such action.</summary>
    NotFound,

    /// <summary>The site has no such action type.</summary>
    UnknownType,

    /// <summary>The type reaches what the action is set up to reach, and the request did not confirm that.</summary>
    NeedsConfirmation,

    /// <summary>The action's settings do not fit its type, as the form check says; there is no test until they do.</summary>
    SettingsInvalid,
}

/// <summary>What a request for a test run of an action came to.</summary>
/// <param name="Status">How it came out.</param>
/// <param name="Result">The type's result; null unless it ran.</param>
internal sealed record ActionTest(ActionTestStatus Status, ActionTestResult? Result = null);

/// <summary>
/// Test runs of a draft's actions against the sample entry (section 4.4, ADR-0158, ADR-0187): nothing is stored and no run changes. What
/// a test run reaches is the type's, by its <see cref="ActionTypeDeclaration.Test"/>: a type that reaches what the action is
/// set up to reach runs only after a confirmation. The draft is the one the designer holds, saved or not, or the stored one
/// (ADR-0190); settings the type cannot read are refused before anything runs, and while actions and email are paused only a
/// dry run runs.
/// </summary>
internal sealed class ActionTestRuns(IFormBuilderFormService forms, ActionTypeRegistry types, ActionSettingLocks locks, SampleEntries samples, OutboxWake wake, TimeProvider time)
{
    /// <summary>The message a type's failure stands under when it threw.</summary>
    public const string FailedMessage = "The test could not finish.";

    /// <summary>The message of a test refused while actions and email are paused.</summary>
    public const string PausedMessage = "Actions and email are paused on this site, so the test cannot go out.";

    /// <summary>Runs an action of the form's draft against a sample entry.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="actionKey">The action in its draft.</param>
    /// <param name="user">Who asks.</param>
    /// <param name="confirmed">Whether the user confirmed that the test reaches what the action is set up to reach.</param>
    /// <param name="held">The draft as the designer holds it, saved or not; null for the stored draft.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<ActionTest> TestAsync(Guid formKey, Guid actionKey, ActionTestUser user, bool confirmed, FormDefinition? held = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        FormDefinition? definition;
        FormInfo? form;
        if (held is null)
        {
            var draft = await forms.GetDraftAsync(formKey, cancellationToken);
            (definition, form) = (draft?.Definition, draft?.Form);
        }
        else
        {
            (definition, form) = (held, await forms.GetAsync(formKey, cancellationToken));
        }

        if (definition is null || form is null || definition.Actions.FirstOrDefault(each => each.Key == actionKey) is not { } action)
        {
            return new ActionTest(ActionTestStatus.NotFound);
        }

        if (types.Find(action.Type) is not { } type)
        {
            return new ActionTest(ActionTestStatus.UnknownType);
        }

        // Settings the type cannot read, such as a draft written before its editor, are the form check's to say.
        var locked = locks.Apply(action);
        if (!FieldSettingsChecks.Check(type.SettingsSchema, locked.Settings, "$.settings", "An action", new DefinitionProblems()))
        {
            return new ActionTest(ActionTestStatus.SettingsInvalid);
        }

        // While paused, what a test reaches would not go out, or would go out later unasked (ADR-0190); a dry run reaches
        // nothing, so it still runs. Before the confirmation: nobody is asked about a test that cannot happen.
        if (type.Declaration.Test != ActionTestKind.DryRun && wake.Paused)
        {
            return new ActionTest(ActionTestStatus.Done, new ActionTestResult(ActionTestOutcome.Failed, PausedMessage) { Code = ActionTestCodes.Paused });
        }

        if (type.Declaration.Test == ActionTestKind.AfterConfirmation && !confirmed)
        {
            return new ActionTest(ActionTestStatus.NeedsConfirmation);
        }

        var context = new ActionTestContext
        {
            Action = locked,
            User = user,
            Scope = await samples.ScopeAsync(definition, form.Name, form.Alias, string.Empty, cancellationToken),
        };
        using var limit = new CancellationTokenSource(type.Declaration.AttemptTimeout ?? ActionTypeRegistry.LongestAttempt, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, cancellationToken);
        try
        {
            return new ActionTest(ActionTestStatus.Done, await type.TestAsync(context, linked.Token).WaitAsync(linked.Token));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && !cancellationToken.IsCancellationRequested)
        {
            // Its message may hold a secret, so the editor reads only that it failed; the log of the type has the rest.
            return new ActionTest(ActionTestStatus.Done, new ActionTestResult(ActionTestOutcome.Failed, FailedMessage) { Code = ActionTestCodes.Failed });
        }
    }
}
