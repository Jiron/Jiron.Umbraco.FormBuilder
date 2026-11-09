using System.Collections.Immutable;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// The action runs of a pass (ADR-0007, ADR-0158): it marks expired leases, then claims and runs up to <see cref="PerPass"/>
/// due runs, <see cref="AtATime"/> at a time. Each claim is one conditional statement, so two servers never run an attempt
/// of the same claim.
/// </summary>
internal sealed partial class ActionRunQueue(IActionRunRepository runs, ActionAttempts attempts, OutboxWake wake, TimeProvider time, ILogger<ActionRunQueue> logger) : IOutboxQueue
{
    /// <summary>How many due runs a pass takes.</summary>
    public const int PerPass = 25;

    /// <summary>How many attempts run at a time on a server.</summary>
    public const int AtATime = 4;

    /// <summary>How long a claim holds a run.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public string Name => "action runs";

    /// <inheritdoc />
    public async Task<bool> PassAsync(string claimant, DateTimeOffset deadline, CancellationToken stopping)
    {
        await runs.ExpireLeasesAsync(stopping);
        var due = await runs.DueAsync(PerPass, stopping);
        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = AtATime },
            async (id, _) =>
            {
                if (time.GetUtcNow() >= deadline || wake.Paused || stopping.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    if (await runs.ClaimAsync(id, claimant, Lease, stopping) is { } claimed)
                    {
                        await attempts.RunAsync(claimed, stopping);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    // Its lease runs out if it was claimed; the other runs go on.
                    LogAttemptFailed(logger, id, LoggedException.Of(exception));
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    // The host stops, and a claim that did not come through leaves the run as it was.
                }
            });
        return !stopping.IsCancellationRequested && (await runs.DueAsync(1, stopping)).Count > 0;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not run action run {Run}; it stays queued, or becomes uncertain when it was claimed")]
    private static partial void LogAttemptFailed(ILogger logger, long run, Exception exception);
}

/// <summary>
/// One attempt of a claimed run (ADR-0007, ADR-0158): it reads the entry and the action as the site runs them, gives the type
/// its time and records what came of it through the token of the claim. An error goes back to pending with
/// <see cref="BackOff"/> and fails for good after <see cref="MaxAttempts"/> attempts; an attempt that was cut off, by its time
/// limit or by the host stopping, becomes uncertain, because it may have been delivered. A type that does not stop when
/// told holds nothing up: after <see cref="Grace"/> the attempt counts as cut off, and when it does end, its result replaces
/// that (the late finisher).
/// </summary>
internal sealed partial class ActionAttempts(
    IActionRunRepository runs,
    IEntryRepository entries,
    IFormRepository forms,
    FormDocuments documents,
    SubmissionEntries submissions,
    ActionTypeRegistry types,
    ActionSettingLocks locks,
    ActionSettlement settlement,
    IEventAggregator events,
    TimeProvider time,
    ILogger<ActionAttempts> logger)
{
    /// <summary>How many attempts a series of a run has.</summary>
    public const int MaxAttempts = 5;

    /// <summary>The code of an attempt whose time ran out.</summary>
    public const string TimedOut = "attempt_timed_out";

    /// <summary>The code of an attempt the host stopped.</summary>
    public const string HostStopped = "host_stopped";

    /// <summary>The code of an attempt that threw; its message is the exception's type.</summary>
    public const string Threw = "exception";

    /// <summary>The code of a run whose action type the site no longer has.</summary>
    public const string UnknownType = "unknown_type";

    /// <summary>The code of a run whose version no longer has its action, or cannot be read.</summary>
    public const string UnknownAction = "unknown_action";

    /// <summary>How long a type that was told to stop gets before the attempt counts as cut off.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>The waits before the second to the fifth attempt.</summary>
    public static readonly ImmutableArray<TimeSpan> BackOff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(27)];

    /// <summary>Runs one attempt of a claimed run, and records what came of it.</summary>
    /// <param name="run">The run as claimed.</param>
    /// <param name="stopping">Cancelled when the host stops.</param>
    public async Task RunAsync(StoredActionRun run, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(run);
        var prepared = await PrepareAsync(run, stopping);
        if (prepared.Refusal is { } refusal)
        {
            // Nothing was sent. Failing to read is no attempt of the action's, so it never fails the run for good.
            await RecordAsync(run, refusal, nothingSent: refusal.Kind == ActionResultKind.TryAgain);
            return;
        }

        if (prepared.Type is null || prepared.Context is null)
        {
            // The entry was deleted with its runs meanwhile.
            return;
        }

        await PublishAsync(new ActionRunningNotification(prepared.Context, run.ActionType));
        var timeout = prepared.Type.Declaration.AttemptTimeout ?? ActionTypeRegistry.LongestAttempt;

        // The sources live as long as the attempt: a type that does not stop may still use its token after this method.
        var limit = new CancellationTokenSource(timeout, time);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, stopping);
        var token = linked.Token;
        var attempt = AttemptAsync(prepared.Type, prepared.Context, stopping, token);
        _ = attempt.ContinueWith(
            _ =>
            {
                linked.Dispose();
                limit.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            await attempt.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Told to stop: it has a moment to say how far it got.
            try
            {
                await attempt.WaitAsync(Grace, time, CancellationToken.None);
            }
            catch (TimeoutException)
            {
                // The type does not stop. The attempt counts as cut off, and its result replaces that when it comes.
                LogDoesNotStop(logger, run.ActionType, run.Id);
                await RecordAsync(run, ActionResult.Uncertain(stopping.IsCancellationRequested ? HostStopped : TimedOut), prepared.Context);
                _ = FinishLateAsync(run, attempt, prepared.Context);
                return;
            }
        }

        await RecordAsync(run, await attempt, prepared.Context);
    }

    // The entry and the action as the site runs them; a refusal when the run cannot work, nothing when the entry is gone.
    private async Task<(IFormBuilderActionType? Type, ActionRunContext? Context, ActionResult? Refusal)> PrepareAsync(StoredActionRun run, CancellationToken stopping)
    {
        try
        {
            if (types.Find(run.ActionType) is not { } type)
            {
                return (null, null, ActionResult.Failed(UnknownType, $"The site has no action type '{run.ActionType}'."));
            }

            if (await entries.GetByIdAsync(run.EntryId, stopping) is not { } entry)
            {
                return (null, null, null);
            }

            if (await forms.GetVersionByIdAsync(run.FormVersionId, stopping) is not { } version)
            {
                return (null, null, ActionResult.Failed(UnknownAction, "The form's version is gone."));
            }

            var definition = documents.Readable(version);
            if (definition.Actions.FirstOrDefault(action => action.Key == run.ActionKey) is not { } action)
            {
                return (null, null, ActionResult.Failed(UnknownAction, "The form's version no longer has this action."));
            }

            if (await submissions.ScopeAsync(entry, stopping) is not { } scope)
            {
                return (null, null, null);
            }

            return (type, new ActionRunContext
            {
                Action = locks.Apply(action),
                Trigger = run.Trigger,
                Scope = scope,
                Attempt = run.Attempts,
                DeliveryKey = $"{entry.Key:N}.{run.ActionKey:N}.{run.ChangeKey:N}",
                RunId = run.Id,
                EntryId = entry.Id,
            }, null);
        }
        catch (FormBuilderValidationException)
        {
            return (null, null, ActionResult.Failed(UnknownAction, "The form's version cannot be read."));
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
        {
            LogPrepareFailed(logger, run.Id, LoggedException.Of(exception));
            return (null, null, ActionResult.TryAgain(Threw, exception.GetType().FullName));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Nothing was sent, so the next attempt may go without a question.
            return (null, null, ActionResult.TryAgain(HostStopped));
        }
    }

    // The type's attempt, on the thread pool so that a type that blocks holds nothing up; it never throws. A cancellation,
    // whoever's it is, and any error once the attempt was told to stop, may have come after the delivery: only a person
    // decides whether such a run goes out again.
    private async Task<ActionResult> AttemptAsync(IFormBuilderActionType type, ActionRunContext context, CancellationToken stopping, CancellationToken token)
    {
        try
        {
            return await Task.Run(() => type.RunAsync(context, token), CancellationToken.None) ?? ActionResult.TryAgain(Threw, "The action type returned no result.");
        }
        catch (Exception exception) when (exception is OperationCanceledException || (token.IsCancellationRequested && exception is not OutOfMemoryException))
        {
            return ActionResult.Uncertain(stopping.IsCancellationRequested ? HostStopped : TimedOut);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Its message may hold what a visitor typed or a secret, so only its type is kept (D13).
            LogAttemptThrew(logger, type.Declaration.Alias, exception.GetType().FullName);
            return ActionResult.TryAgain(Threw, exception.GetType().FullName);
        }
    }

    private async Task FinishLateAsync(StoredActionRun run, Task<ActionResult> attempt, ActionRunContext context)
    {
        try
        {
            var late = await attempt;
            if (late.Kind != ActionResultKind.Uncertain)
            {
                await RecordAsync(run, late, context);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogLateFailed(logger, run.Id, LoggedException.Of(exception));
        }
    }

    // Records the outcome through the claim's token, and what it brings, whatever the host does by now. Only the holder of
    // the claim can change the run, so a late finisher of an attempt that was retried meanwhile records nothing.
    private async Task RecordAsync(StoredActionRun run, ActionResult result, ActionRunContext? context = null, bool nothingSent = false)
    {
        var token = run.LockedBy ?? throw new ArgumentException("A claimed run has its token.", nameof(run));
        var error = result.Code is { } code ? new DeliveryError(code, Clean(result.Message)) : null;
        var kind = result.Kind == ActionResultKind.TryAgain && run.Attempts >= MaxAttempts && !nothingSent ? ActionResultKind.Failed : result.Kind;
        bool recorded;
        try
        {
            recorded = kind switch
            {
                ActionResultKind.Succeeded => await settlement.FinishAsync(run, () => runs.SucceedAsync(run.Id, token, CancellationToken.None)),
                ActionResultKind.Failed => await settlement.FinishAsync(run, () => runs.FailAsync(run.Id, token, error!, CancellationToken.None)),
                ActionResultKind.TryAgain => await runs.RetryAsync(run.Id, token, BackOff[Math.Clamp(run.Attempts, 1, BackOff.Length) - 1], error!, CancellationToken.None),
                _ => await runs.CutOffAsync(run.Id, token, error!, CancellationToken.None),
            };
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The lease runs out, and the run becomes uncertain: nothing delivers it twice.
            LogRecordFailed(logger, run.Id, LoggedException.Of(exception));
            return;
        }

        if (recorded && context is not null)
        {
            await PublishAsync(kind == ActionResultKind.Succeeded
                ? new ActionRunCompletedNotification(context, run.ActionType)
                : new ActionRunFailedNotification(context, run.ActionType, kind == ActionResultKind.TryAgain, result.Code));
        }
    }

    // What a site's handler does never decides an attempt, and one that hangs holds it up for a moment at most.
    private async Task PublishAsync(INotification notification)
    {
        try
        {
            await Task.Run(() => events.PublishAsync(notification, CancellationToken.None), CancellationToken.None).WaitAsync(Grace, time, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogHandlerFailed(logger, notification.GetType().Name, LoggedException.Of(exception));
        }
    }

    // A message for the log, on one line and without control characters; the repository cuts it to its column.
    private static string? Clean(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var cleaned = new StringBuilder(message.Length);
        foreach (var c in message)
        {
            cleaned.Append(char.IsControl(c) ? ' ' : c);
        }

        return cleaned.ToString().Trim();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An attempt of the action type {Type} threw {Exception}; it tries again after the back-off")]
    private static partial void LogAttemptThrew(ILogger logger, string type, string? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The action type {Type} did not stop when told to, in action run {Run}; the run is uncertain until it ends")]
    private static partial void LogDoesNotStop(ILogger logger, string type, long run);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not read what action run {Run} needs; it tries again after the back-off")]
    private static partial void LogPrepareFailed(ILogger logger, long run, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not record the outcome of action run {Run}; it becomes uncertain when its lease runs out")]
    private static partial void LogRecordFailed(ILogger logger, long run, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The late end of action run {Run} failed; it stays uncertain")]
    private static partial void LogLateFailed(ILogger logger, long run, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A handler of {Notification} failed; the action run goes on")]
    private static partial void LogHandlerFailed(ILogger logger, string notification, Exception exception);
}
