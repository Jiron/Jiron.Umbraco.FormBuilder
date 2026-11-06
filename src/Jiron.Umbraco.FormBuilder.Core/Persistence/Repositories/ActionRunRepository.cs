using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// The order of a batch (section 4.4, ADR-0040), the runs one change queued for an entry: an earlier run of the batch, by
/// position and then id, holds a run back while it is running, while it is pending without an attempt, or while it stops
/// the rest and is not final. The due list and the claim share that condition. The claim reads it with locks on SQL
/// Server; the due list only proposes, so it reads without, and holds no lock between the passes of the servers.
/// </remarks>
internal sealed class ActionRunRepository(FormBuilderDatabase database) : IActionRunRepository
{
    /// <summary>The most runs one call queues.</summary>
    public const int MaxRunsPerCall = 500;

    /// <summary>The longest action type alias the column holds.</summary>
    public const int MaxActionTypeLength = 128;

    /// <summary>The longest error message the column holds; longer ones are cut.</summary>
    public const int MaxErrorMessageLength = 1000;

    private static readonly string Pending = StoredNames.Of(ActionRunState.Pending);
    private static readonly string Running = StoredNames.Of(ActionRunState.Running);
    private static readonly string Succeeded = StoredNames.Of(ActionRunState.Succeeded);
    private static readonly string Failed = StoredNames.Of(ActionRunState.Failed);
    private static readonly string Skipped = StoredNames.Of(ActionRunState.Skipped);
    private static readonly string Uncertain = StoredNames.Of(ActionRunState.Uncertain);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredActionRun>?> AddAsync(long entryId, int formVersionId, Guid changeKey, IReadOnlyList<NewActionRun> runs, TimeSpan wait = default, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(formVersionId, 1);
        if (changeKey == Guid.Empty)
        {
            throw new ArgumentException("A batch names the change that queues it.", nameof(changeKey));
        }

        Outbox.CheckDelay(wait);
        CheckRuns(runs);
        return database.WriteAsync<IReadOnlyList<StoredActionRun>?>(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await RowHolds.HoldEntryAsync(db, n, entryId))
                {
                    return null;
                }

                // A change queues one batch for an entry. The hold keeps every other batch of the entry out until the unit
                // ends, so the key is checked once, here.
                if (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {n.T(FormBuilderTables.ActionRun)}{n.LockingRead} WHERE {n.C("entryId")} = @0 AND {n.C("changeKey")} = @1", entryId, changeKey) > 0)
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "The entry already has the runs of this change.");
                }

                // Its commit wakes this server's worker (ADR-0007).
                database.PublishAfterCommit(new OutboxQueuedNotification());
                var now = database.UtcNow;
                var ids = new HashSet<long>();
                // By position, so the ids follow the order too and the entry's list reads as the runs run.
                foreach (var run in runs.OrderBy(run => run.SortOrder))
                {
                    var row = new ActionRunDto
                    {
                        EntryId = entryId,
                        FormVersionId = formVersionId,
                        ActionKey = run.ActionKey,
                        ActionType = run.ActionType,
                        TriggerKind = StoredNames.Of(run.Trigger),
                        SortOrder = run.SortOrder,
                        StopOnFailure = run.StopOnFailure,
                        State = Pending,
                        NextAttemptUtc = now + wait,
                        ChangeKey = changeKey,
                        CreatedUtc = now,
                    };
                    await db.InsertRowAsync(row);
                    ids.Add(row.Id);
                }

                return [.. (await ListAsync(db, n, entryId)).Where(run => ids.Contains(run.Id))];
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredActionRun?> GetAsync(long runId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, new SqlNames(db), runId), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredActionRun>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => ListAsync(db, new SqlNames(db), entryId), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> DueAsync(int take, CancellationToken cancellationToken = default)
    {
        Outbox.CheckTake(take);
        return database.ReadAsync<IReadOnlyList<long>>(
            async db =>
            {
                return await db.FetchListAsync<long>(DueSql(new SqlNames(db), take), database.UtcNow);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredActionRun?> ClaimAsync(long runId, string claimant, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        Outbox.CheckClaimant(claimant);
        Outbox.CheckLease(lease);
        var token = Outbox.NewToken(claimant);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var now = database.UtcNow;
                var claimed = await db.ExecAsync(
                    ClaimSql(n),
                    runId,
                    token,
                    now + lease,
                    now);
                return claimed == 1 ? await FindAsync(db, n, runId) : null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SucceedAsync(long runId, string token, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        return OutcomeAsync(
            runId,
            token,
            (n, now) => ($"{n.C("state")} = '{Succeeded}', {n.C("finishedUtc")} = @2, {n.C("nextAttemptUtc")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = NULL, {n.C("lastErrorMessage")} = NULL", [now]),
            stopsTheRest: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RetryAsync(long runId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckDelay(delay);
        Outbox.CheckError(error);
        return OutcomeAsync(
            runId,
            token,
            (n, now) => ($"{n.C("state")} = '{Pending}', {n.C("nextAttemptUtc")} = @2, {n.C("lockedBy")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = @3, {n.C("lastErrorMessage")} = @4", [now + delay, error.Code, Message(error)]),
            stopsTheRest: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> FailAsync(long runId, string token, DeliveryError error, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckError(error);
        return OutcomeAsync(
            runId,
            token,
            (n, now) => ($"{n.C("state")} = '{Failed}', {n.C("finishedUtc")} = @2, {n.C("nextAttemptUtc")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = @3, {n.C("lastErrorMessage")} = @4", [now, error.Code, Message(error)]),
            stopsTheRest: true,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CutOffAsync(long runId, string token, DeliveryError error, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckError(error);
        return OutcomeAsync(
            runId,
            token,
            (n, _) => ($"{n.C("state")} = '{Uncertain}', {n.C("lastErrorCode")} = @2, {n.C("lastErrorMessage")} = @3", [error.Code, Message(error)]),
            stopsTheRest: false,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountFailedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.ActionRun)} WHERE {n.C("state")} = @0 AND {n.C("finishedUtc")} >= @1",
                    StoredNames.Of(ActionRunState.Failed),
                    FormBuilderDbTime.Write(since));
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> ExpireLeasesAsync(CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.ActionRun)} SET {n.C("state")} = '{Uncertain}', {n.C("lastErrorCode")} = @1, {n.C("lastErrorMessage")} = NULL WHERE {n.C("state")} = '{Running}' AND {n.C("lockedUntilUtc")} < @0",
                    database.UtcNow,
                    DeliveryError.LeaseExpired);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredActionRun?> RequeueAsync(long runId, ActionRunState expected, CancellationToken cancellationToken = default)
    {
        if (expected is not (ActionRunState.Failed or ActionRunState.Uncertain))
        {
            throw new ArgumentOutOfRangeException(nameof(expected), StoredNames.Describe(expected), "Only a failed or an uncertain run is retried from the log.");
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var runs = n.T(FormBuilderTables.ActionRun);
                var now = database.UtcNow;
                var changed = await db.ExecAsync(
                    $"UPDATE {runs} SET {n.C("state")} = '{Pending}', {n.C("attempts")} = 0, {n.C("nextAttemptUtc")} = @2, {n.C("lockedBy")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("startedUtc")} = NULL, {n.C("lastAttemptUtc")} = NULL, {n.C("finishedUtc")} = NULL WHERE {n.C("id")} = @0 AND {n.C("state")} = @1",
                    runId,
                    StoredNames.Of(expected),
                    now);
                if (changed == 1)
                {
                    database.PublishAfterCommit(new OutboxQueuedNotification());

                    // The runs this one skipped when it failed for good wait for it again: the rest of its batch comes back with it.
                    await db.ExecAsync(
                        $"UPDATE {runs} SET {n.C("state")} = '{Pending}', {n.C("nextAttemptUtc")} = @2, {n.C("finishedUtc")} = NULL, {n.C("lastErrorCode")} = NULL WHERE {n.C("state")} = '{Skipped}' AND {n.C("lastErrorCode")} = @1 AND {n.C("entryId")} = (SELECT s.{n.C("entryId")} FROM {runs} s WHERE s.{n.C("id")} = @0) AND {Stopper(n, runs, "@0", Pending)}",
                        runId,
                        DeliveryError.Stopped,
                        now);
                }

                var current = await FindAsync(db, n, runId);
                if (changed == 0 && current is not null)
                {
                    throw new FormBuilderConcurrencyException($"The run was changed meanwhile (it was no longer {StoredNames.Describe(expected)}).");
                }

                return current;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> SkipChangeAsync(Guid changeKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.ActionRun)} SET {n.C("state")} = '{Skipped}', {n.C("finishedUtc")} = @1, {n.C("nextAttemptUtc")} = NULL, {n.C("lastErrorCode")} = @2, {n.C("lastErrorMessage")} = NULL WHERE {n.C("changeKey")} = @0 AND {n.C("state")} = '{Pending}' AND {n.C("attempts")} = 0",
                    changeKey,
                    database.UtcNow,
                    DeliveryError.Undone);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> SkipChangeOfEntryAsync(Guid changeKey, long entryId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.ActionRun)} SET {n.C("state")} = '{Skipped}', {n.C("finishedUtc")} = @2, {n.C("nextAttemptUtc")} = NULL, {n.C("lastErrorCode")} = @3, {n.C("lastErrorMessage")} = NULL WHERE {n.C("changeKey")} = @0 AND {n.C("entryId")} = @1 AND {n.C("state")} = '{Pending}' AND {n.C("attempts")} = 0",
                    changeKey,
                    entryId,
                    database.UtcNow,
                    DeliveryError.Superseded);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> SkipUnstartedOfEntryAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.ActionRun)} SET {n.C("state")} = '{Skipped}', {n.C("finishedUtc")} = @1, {n.C("nextAttemptUtc")} = NULL, {n.C("lastErrorCode")} = @2, {n.C("lastErrorMessage")} = NULL WHERE {n.C("entryId")} = @0 AND {n.C("state")} = '{Pending}' AND {n.C("attempts")} = 0",
                    entryId,
                    database.UtcNow,
                    DeliveryError.Superseded);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> CountOfChangeAsync(Guid changeKey, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.ActionRun)} WHERE {n.C("changeKey")} = @0",
                    changeKey);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredActionRun>> LastOfActionsAsync(int formId, IReadOnlyCollection<Guid> actionKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actionKeys);
        if (actionKeys.Count > Outbox.MaxTake)
        {
            throw new ArgumentOutOfRangeException(nameof(actionKeys), $"At most {Outbox.MaxTake} actions at once.");
        }

        return database.ReadAsync<IReadOnlyList<StoredActionRun>>(
            async db =>
            {
                var n = new SqlNames(db);
                var sql = LastSql(n);
                var found = new List<StoredActionRun>();
                // One seek an action, by its index, which stops at the newest run that counts.
                foreach (var key in actionKeys.Distinct())
                {
                    if (await db.FirstOrNullAsync<ActionRunDto>(sql, key, formId) is { } row)
                    {
                        found.Add(FromDto(row));
                    }
                }

                return found;
            },
            cancellationToken);
    }

    /// <summary>
    /// The last run of an action (<c>@0</c> the action, <c>@1</c> the form): the newest by the action's index that has been
    /// tried or has settled, and that belongs to an entry of the form.
    /// </summary>
    internal static string LastSql(SqlNames n)
    {
        var runs = n.T(FormBuilderTables.ActionRun);
        var entries = n.T(FormBuilderTables.Entry);
        return $"SELECT r.* FROM {runs} r WHERE r.{n.C("actionKey")} = @0 AND (r.{n.C("state")} <> '{Pending}' OR r.{n.C("attempts")} > 0) AND EXISTS (SELECT 1 FROM {entries} e WHERE e.{n.C("id")} = r.{n.C("entryId")} AND e.{n.C("formId")} = @1) ORDER BY r.{n.C("id")} DESC {n.Page(0, 1)}";
    }

    internal static StoredActionRun FromDto(ActionRunDto d) => new(
        d.Id,
        d.EntryId,
        d.FormVersionId,
        d.ActionKey,
        d.ActionType,
        StoredNames.Parse<ActionTrigger>(d.TriggerKind),
        d.SortOrder,
        d.StopOnFailure,
        StoredNames.Parse<ActionRunState>(d.State),
        d.Attempts,
        FormBuilderDbTime.Read(d.NextAttemptUtc),
        d.ChangeKey,
        FormBuilderDbTime.Read(d.LockedUntilUtc),
        d.LockedBy,
        d.LastErrorCode is { } code ? new DeliveryError(code, d.LastErrorMessage) : null,
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.StartedUtc),
        FormBuilderDbTime.Read(d.FinishedUtc))
    {
        LastAttemptUtc = FormBuilderDbTime.Read(d.LastAttemptUtc),
    };

    /// <summary>
    /// The due list (<c>@0</c> the time): the ids of the pending runs whose time came and that nothing holds back, the
    /// longest due first. It reads without locks: it only proposes, and the claim checks again under its own.
    /// </summary>
    internal static string DueSql(SqlNames n, int take)
    {
        var runs = n.T(FormBuilderTables.ActionRun);
        return $"SELECT r.{n.C("id")} FROM {runs} r WHERE r.{n.C("state")} = '{Pending}' AND r.{n.C("nextAttemptUtc")} <= @0 AND {Unblocked(n, runs, "r", locking: false)} ORDER BY r.{n.C("nextAttemptUtc")}, r.{n.C("id")} {n.Page(0, take)}";
    }

    /// <summary>
    /// The claim (<c>@0</c> the run, <c>@1</c> the token, <c>@2</c> the lease's end, <c>@3</c> the time, which is when the
    /// attempt starts): one update of a run that is pending, due and not held back, whose check of the earlier runs holds
    /// their rows on SQL Server until the claim commits.
    /// </summary>
    internal static string ClaimSql(SqlNames n)
    {
        var runs = n.T(FormBuilderTables.ActionRun);
        return $"UPDATE {runs} SET {n.C("state")} = '{Running}', {n.C("lockedBy")} = @1, {n.C("lockedUntilUtc")} = @2, {n.C("attempts")} = {n.C("attempts")} + 1, {n.C("startedUtc")} = COALESCE({n.C("startedUtc")}, @3), {n.C("lastAttemptUtc")} = @3 WHERE {n.C("id")} = @0 AND {n.C("state")} = '{Pending}' AND {n.C("nextAttemptUtc")} <= @3 AND {Unblocked(n, runs, runs, locking: true)}";
    }

    private static string? Message(DeliveryError error) => Outbox.Cut(error.Message, MaxErrorMessageLength);

    // No earlier run of the same batch (entry and change) holds the run back: none is running, none waits for its first
    // attempt, and none that stops the rest is still to be decided. The outer reference names the run being checked;
    // a locking check holds the earlier runs' rows on SQL Server.
    private static string Unblocked(SqlNames n, string runs, string outer, bool locking) =>
        $"NOT EXISTS (SELECT 1 FROM {runs} p{(locking ? n.LockingRead : string.Empty)} WHERE p.{n.C("entryId")} = {outer}.{n.C("entryId")} AND p.{n.C("changeKey")} = {outer}.{n.C("changeKey")} AND {Before(n, "p", outer)} AND (p.{n.C("state")} = '{Running}' OR (p.{n.C("state")} = '{Pending}' AND p.{n.C("attempts")} = 0) OR (p.{n.C("stopOnFailure")} = 1 AND p.{n.C("state")} NOT IN ('{Succeeded}', '{Failed}', '{Skipped}'))))";

    // The run with the given id stops the rest, is in the given state, and comes before the run being changed in its batch.
    private static string Stopper(SqlNames n, string runs, string id, string state) =>
        $"EXISTS (SELECT 1 FROM {runs} f{n.LockingRead} WHERE f.{n.C("id")} = {id} AND f.{n.C("stopOnFailure")} = 1 AND f.{n.C("state")} = '{state}' AND f.{n.C("entryId")} = {runs}.{n.C("entryId")} AND f.{n.C("changeKey")} = {runs}.{n.C("changeKey")} AND {Before(n, "f", runs)})";

    // The run of the first alias comes before the run of the second, by position and then by id.
    private static string Before(SqlNames n, string earlier, string later) =>
        $"({earlier}.{n.C("sortOrder")} < {later}.{n.C("sortOrder")} OR ({earlier}.{n.C("sortOrder")} = {later}.{n.C("sortOrder")} AND {earlier}.{n.C("id")} < {later}.{n.C("id")}))";

    private static void CheckRuns(IReadOnlyList<NewActionRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        Require(runs.Count is > 0 and <= MaxRunsPerCall, $"A call queues 1 to {MaxRunsPerCall} runs.");
        foreach (var run in runs)
        {
            Require(run is not null, "A run is not null.");
            Require(run!.ActionKey != Guid.Empty, "A run names its action.");
            Require(run.ActionType is { Length: > 0 and <= MaxActionTypeLength } && !run.ActionType.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)), $"An action type is 1 to {MaxActionTypeLength} characters without spaces or control characters.");
            Require(Enum.IsDefined(run.Trigger), "A run's trigger is one of the definition's.");
            Require(run.SortOrder >= 0, "A run's position is 0 or more.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(runs));
            }
        }
    }

    // An attempt's outcome: one statement for a run the token still holds, running or uncertain (the late finisher), and
    // for a failure for good of a run that stops the rest, its entry's later pending runs skipped in the same unit.
    private Task<bool> OutcomeAsync(long runId, string token, Func<SqlNames, DateTime, (string Set, object?[] Args)> outcome, bool stopsTheRest, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var runs = n.T(FormBuilderTables.ActionRun);
                var now = database.UtcNow;
                var (set, args) = outcome(n, now);
                var changed = await db.ExecAsync(
                    $"UPDATE {runs} SET {set} WHERE {n.C("id")} = @0 AND {n.C("lockedBy")} = @1 AND {n.C("state")} IN ('{Running}', '{Uncertain}')",
                    [runId, token, .. args]);
                if (changed == 1 && stopsTheRest)
                {
                    await db.ExecAsync(
                        $"UPDATE {runs} SET {n.C("state")} = '{Skipped}', {n.C("finishedUtc")} = @1, {n.C("nextAttemptUtc")} = NULL, {n.C("lastErrorCode")} = @2, {n.C("lastErrorMessage")} = NULL WHERE {n.C("state")} = '{Pending}' AND {n.C("entryId")} = (SELECT s.{n.C("entryId")} FROM {runs} s WHERE s.{n.C("id")} = @0) AND {Stopper(n, runs, "@0", Failed)}",
                        runId,
                        now,
                        DeliveryError.Stopped);
                }

                return changed == 1;
            },
            cancellationToken);

    private static async Task<StoredActionRun?> FindAsync(IUmbracoDatabase db, SqlNames n, long runId)
    {
        var row = await db.FirstOrNullAsync<ActionRunDto>($"SELECT * FROM {n.T(FormBuilderTables.ActionRun)} WHERE {n.C("id")} = @0", runId);
        return row is null ? null : FromDto(row);
    }

    private static async Task<IReadOnlyList<StoredActionRun>> ListAsync(IUmbracoDatabase db, SqlNames n, long entryId)
    {
        var rows = await db.FetchListAsync<ActionRunDto>(
            $"SELECT * FROM {n.T(FormBuilderTables.ActionRun)} WHERE {n.C("entryId")} = @0 ORDER BY {n.C("id")}",
            entryId);
        return [.. rows.Select(FromDto)];
    }
}
