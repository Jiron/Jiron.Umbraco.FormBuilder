using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Runs Jiron Form Builder's database work (D7, ADR-0007, ADR-0036): each unit of work in its own Umbraco scope, behind
/// the gate, on the connection Umbraco's scope gives it. A write commits only when its work returns; an exception rolls
/// it back. A unit called inside another one joins its transaction, so a service can combine several repository writes
/// into one unit of work.
/// </summary>
/// <remarks>
/// A unit of work that the database turns away with a transient error runs again, up to <see cref="Attempts"/> times:
/// a lock conflict (SQLite busy, a SQL Server deadlock) always, a lost connection or a timeout only for reads
/// (<see cref="FormBuilderDatabaseErrors"/>). Only where that is safe: the failed attempt rolled back, so the outermost
/// unit starts over from its first statement. A unit inside another one, or inside a scope that Umbraco opened, shares
/// a transaction only its owner can start over; and an error while the commit is on its way leaves its outcome unknown.
/// Neither runs again. The work runs again whole, so it must keep its effects inside the transaction (notifications
/// are published after the commit).
/// </remarks>
internal sealed partial class FormBuilderDatabase(
    IScopeProvider scopeProvider,
    IScopeAccessor scopeAccessor,
    IFormBuilderDatabaseGate gate,
    TimeProvider timeProvider,
    ILogger<FormBuilderDatabase> logger)
{
    /// <summary>How many times a unit of work runs at most when the database turns it away with a transient error.</summary>
    internal const int Attempts = 3;

    /// <summary>The waits before the second and the third attempt: short, as lock conflicts clear quickly.</summary>
    internal static readonly TimeSpan[] Waits = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(250)];

    // Set inside each attempt, so it flows into the work and everything it awaits, but not back to the caller.
    private readonly AsyncLocal<bool> _inside = new();

    // Set inside a budgeted read, so a budgeted read inside it keeps the outer budget.
    private readonly AsyncLocal<bool> _budgeted = new();

    /// <summary>The clock that stamps every write.</summary>
    public TimeProvider Time => timeProvider;

    /// <summary>The current time as the UTC value to store.</summary>
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// True inside a unit of work or a scope Umbraco opened: work started now joins a transaction its owner commits, so
    /// its effects outside the database must wait for that owner.
    /// </summary>
    public bool InUnitOfWork => _inside.Value || scopeAccessor.AmbientScope is not null;

    /// <summary>Runs read-only work.</summary>
    public Task<T> ReadAsync<T>(Func<IUmbracoDatabase, Task<T>> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        return AttemptsAsync(
            write: false,
            _ => gate.RunAsync(
                async () =>
                {
                    FormBuilderDbExtensions.CurrentToken = cancellationToken;
                    using var scope = scopeProvider.CreateScope(autoComplete: true);
                    return await read(scope.Database);
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs read-only work under a time budget (ADR-0003, ADR-0039): past <paramref name="budget"/> the running
    /// statement stops and the read fails with <see cref="FormBuilderQueryTimeoutException"/>, which never runs again.
    /// The budget starts once the gate lets the read through. A budgeted read inside another one keeps the outer
    /// budget.
    /// </summary>
    public Task<T> QueryAsync<T>(Func<IUmbracoDatabase, Task<T>> read, TimeSpan budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);
        return AttemptsAsync(
            write: false,
            _ => gate.RunAsync(
                async () =>
                {
                    using var scope = scopeProvider.CreateScope(autoComplete: true);
                    var db = scope.Database;
                    if (_budgeted.Value)
                    {
                        return await read(db);
                    }

                    FormBuilderDbExtensions.CurrentToken = cancellationToken;
                    var sqlite = new SqlNames(db).IsSqlite;
                    if (sqlite && db.Connection is null)
                    {
                        // The scope opens its connection with the first statement; the budget needs it open.
                        await db.ScalarAsync<int>("SELECT 1");
                    }

                    using var deadline = FormBuilderQueryBudget.Start(db.Connection, sqlite, budget, timeProvider, cancellationToken);
                    if (sqlite && !deadline.Armed)
                    {
                        LogUnbudgeted(logger);
                    }

                    _budgeted.Value = true;
                    FormBuilderDbExtensions.CurrentToken = deadline.Token;
                    try
                    {
                        return await read(db);
                    }
                    // A refusal stays a refusal; any other failure past the deadline is the stopped statement's.
                    catch (Exception exception) when (deadline.Expired && !cancellationToken.IsCancellationRequested && exception is not ArgumentException)
                    {
                        throw new FormBuilderQueryTimeoutException($"The query ran past its time budget of {budget.TotalSeconds:0.###} s and was stopped.", exception);
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested && exception is not OperationCanceledException and not ArgumentException)
                    {
                        // On SQLite the progress handler interrupted the statement for the caller: say so as .NET does.
                        throw new OperationCanceledException("The query was cancelled.", exception, cancellationToken);
                    }
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Runs a write in one transaction. Its first statement must be a write, so the transaction holds the write lock
    /// before it reads anything and never upgrades a stale read (ADR-0007).
    /// </summary>
    public Task<T> WriteAsync<T>(Func<IUmbracoDatabase, Task<T>> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        return AttemptsAsync(
            write: true,
            committing => gate.RunAsync(
                async () =>
                {
                    FormBuilderDbExtensions.CurrentToken = cancellationToken;
                    using var scope = scopeProvider.CreateScope();
                    var result = await write(scope.Database);
                    scope.Complete();

                    // From here the scope commits as it ends: a failure now may have committed, so it never runs again.
                    committing.Value = true;
                    return result;
                },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>Runs a unit of work, and again after a transient error where that is safe (see the remarks).</summary>
    private async Task<T> AttemptsAsync<T>(bool write, Func<StrongBox<bool>, Task<T>> unit, CancellationToken cancellationToken)
    {
        if (InUnitOfWork)
        {
            return await unit(new StrongBox<bool>());
        }

        for (var attempt = 1; ; attempt++)
        {
            var committing = new StrongBox<bool>();
            try
            {
                return await AttemptAsync(unit, committing);
            }
            catch (Exception exception) when (attempt < Attempts
                && !committing.Value
                && !cancellationToken.IsCancellationRequested
                && FormBuilderDatabaseErrors.IsTransient(exception, write))
            {
                LogRetrying(logger, attempt, FormBuilderDatabaseErrors.Describe(exception));
                await Task.Delay(Waits[attempt - 1], timeProvider, cancellationToken);
            }
        }
    }

    private async Task<T> AttemptAsync<T>(Func<StrongBox<bool>, Task<T>> unit, StrongBox<bool> committing)
    {
        _inside.Value = true;
        return await unit(committing);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder runs a unit of work again after attempt {Attempt} met a transient database error: {Failure}")]
    private static partial void LogRetrying(ILogger logger, int attempt, string failure);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder found no SQLite connection to set its query budget on: this read runs without a time limit")]
    private static partial void LogUnbudgeted(ILogger logger);
}
