using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>An event to add to an entry's timeline.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="Kind">What happened, such as <c>submitted</c> or <c>stateChanged</c>: 1 to 64 ASCII letters, digits, dots or hyphens, starting with a letter.</param>
/// <param name="UserKey">The backoffice user who did it, or null for the visitor or the system.</param>
/// <param name="ChangeKey">The change it belongs to, so Undo finds every event of that change (ADR-0023).</param>
/// <param name="Details">What it changed, as a JSON object; old and new values only of fields that are not sensitive.</param>
/// <param name="DueIn">How long until something it leaves to do falls due, as at the end of an undo window; null for nothing (ADR-0162).</param>
internal sealed record NewEntryEvent(long EntryId, string Kind, Guid? UserKey = null, Guid? ChangeKey = null, string Details = "{}", TimeSpan? DueIn = null);

/// <summary>An event of an entry's timeline.</summary>
/// <param name="Id">The row's id, which orders events of the same moment.</param>
/// <param name="EntryId">The entry.</param>
/// <param name="OccurredUtc">When it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="UserKey">The backoffice user who did it.</param>
/// <param name="ChangeKey">The change it belongs to.</param>
/// <param name="Details">What it changed, as JSON.</param>
internal sealed record StoredEntryEvent(long Id, long EntryId, DateTimeOffset OccurredUtc, string Kind, Guid? UserKey, Guid? ChangeKey, string Details);

/// <summary>
/// The timeline of entries in <c>jfbEntryEvent</c> (section 4.4, ADR-0038). It is append-only: an event is a record of
/// what happened, so a correction or an undo is a new event (ADR-0023), and the events go only with their entry.
/// </summary>
internal interface IEntryEventRepository
{
    /// <summary>Adds an event, stamped with the current time; null when the entry does not exist.</summary>
    Task<StoredEntryEvent?> AddAsync(NewEntryEvent entryEvent, CancellationToken cancellationToken = default);

    /// <summary>An entry's events, oldest first.</summary>
    Task<IReadOnlyList<StoredEntryEvent>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>The events of one change, a bulk change over many entries included, in the order they were added.</summary>
    Task<IReadOnlyList<StoredEntryEvent>> ListForChangeAsync(Guid changeKey, CancellationToken cancellationToken = default);

    /// <summary>The events of one kind of these entries, by entry and oldest first: a page of a list reads its entries' <c>submitted</c> events at once.</summary>
    /// <param name="entryIds">The entries.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoredEntryEvent>> ListOfKindAsync(IReadOnlyCollection<long> entryIds, string kind, CancellationToken cancellationToken = default);

    /// <summary>The events whose work has fallen due, oldest first (ADR-0162).</summary>
    Task<IReadOnlyList<StoredEntryEvent>> DueAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>Claims an event's work; false when another server or an Undo took it first.</summary>
    Task<bool> ClaimDueAsync(long eventId, CancellationToken cancellationToken = default);

    /// <summary>Calls back the work a change's events left, by an Undo; how many were still waiting.</summary>
    Task<int> CancelDueAsync(Guid changeKey, CancellationToken cancellationToken = default);

    /// <summary>Calls back what an entry's earlier moves still have waiting, as a later move replaces them; the changes they belong to.</summary>
    Task<IReadOnlyList<Guid>> CancelMovesDueAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>Whether a move of the entry still waits for its undo window, so nothing may let the entry go yet.</summary>
    Task<bool> HasMoveDueAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>Gives an event's work a later time, as it could not be done now.</summary>
    Task DelayDueAsync(long eventId, TimeSpan wait, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class EntryEventRepository(FormBuilderDatabase database) : IEntryEventRepository
{
    /// <summary>The longest kind the column holds.</summary>
    public const int MaxKindLength = 64;

    /// <summary>The kind of the event of a move between states.</summary>
    public const string StateChangedKind = "stateChanged";

    /// <summary>The kind of the event of a move out of Spam.</summary>
    public const string NotSpamKind = "notSpam";

    /// <inheritdoc />
    public Task<StoredEntryEvent?> AddAsync(NewEntryEvent entryEvent, CancellationToken cancellationToken = default)
    {
        Check(entryEvent);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await RowHolds.HoldEntryAsync(db, n, entryEvent.EntryId))
                {
                    return null;
                }

                var row = new EntryEventDto
                {
                    EntryId = entryEvent.EntryId,
                    OccurredUtc = database.UtcNow,
                    Kind = entryEvent.Kind,
                    UserKey = entryEvent.UserKey,
                    ChangeKey = entryEvent.ChangeKey,
                    Details = entryEvent.Details,
                    DueUtc = entryEvent.DueIn is { } dueIn ? database.UtcNow + dueIn : null,
                };
                await db.InsertRowAsync(row);

                // Read back, so the time is the one the database keeps (SQL Server's DATETIME rounds it).
                var stored = await db.FirstOrNullAsync<EntryEventDto>($"SELECT * FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {n.C("id")} = @0", row.Id);
                return FromDto(stored!);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryEvent>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredEntryEvent>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EntryEventDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {n.C("entryId")} = @0 ORDER BY {n.C("occurredUtc")}, {n.C("id")}",
                    entryId);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEntryEvent>> ListOfKindAsync(IReadOnlyCollection<long> entryIds, string kind, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var found = new List<StoredEntryEvent>();

        // The ids are in order and each statement sorts its own, so the statements' rows follow one another.
        foreach (var chunk in entryIds.Distinct().Order().Chunk(SqlArgs.MaxListLength - 1))
        {
            var rows = await database.ReadAsync(
                db =>
                {
                    var n = new SqlNames(db);
                    var a = new SqlArgs();
                    return db.FetchListAsync<EntryEventDto>(
                        $"SELECT * FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {n.C("entryId")} IN ({a.List(chunk.Cast<object?>())}) AND {n.C("kind")} = {a.Add(kind)} ORDER BY {n.C("entryId")}, {n.C("occurredUtc")}, {n.C("id")}",
                        a.Values);
                },
                cancellationToken);
            found.AddRange(rows.Select(FromDto));
        }

        return found;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryEvent>> ListForChangeAsync(Guid changeKey, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredEntryEvent>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EntryEventDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {n.C("changeKey")} = @0 ORDER BY {n.C("id")}",
                    changeKey);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryEvent>> DueAsync(int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        return database.ReadAsync<IReadOnlyList<StoredEntryEvent>>(
            async db =>
            {
                var n = new SqlNames(db);
                var sql = db.SqlContext.SqlSyntax.SelectTop(
                    db.SqlContext.Sql($"SELECT * FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {n.C("dueUtc")} <= @0 ORDER BY {n.C("dueUtc")}, {n.C("id")}", database.UtcNow),
                    take);
                var rows = await db.FetchListAsync<EntryEventDto>(sql.SQL, sql.Arguments);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ClaimDueAsync(long eventId, CancellationToken cancellationToken = default) =>
        await database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryEvent)} SET {n.C("dueUtc")} = NULL WHERE {n.C("id")} = @0 AND {n.C("dueUtc")} IS NOT NULL", eventId);
            },
            cancellationToken) == 1;

    /// <inheritdoc />
    public Task<int> CancelDueAsync(Guid changeKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryEvent)} SET {n.C("dueUtc")} = NULL WHERE {n.C("changeKey")} = @0 AND {n.C("dueUtc")} IS NOT NULL", changeKey);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> CancelMovesDueAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.WriteAsync<IReadOnlyList<Guid>>(
            async db =>
            {
                var n = new SqlNames(db);
                var moves = $"{n.C("entryId")} = @0 AND {n.C("dueUtc")} IS NOT NULL AND {n.C("kind")} IN ('{StateChangedKind}', '{NotSpamKind}')";
                var rows = await db.FetchListAsync<EntryEventDto>($"SELECT * FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {moves}", entryId);
                if (rows.Count > 0)
                {
                    await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryEvent)} SET {n.C("dueUtc")} = NULL WHERE {moves}", entryId);
                }

                return [.. rows.Select(row => row.ChangeKey).OfType<Guid>().Distinct()];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasMoveDueAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.EntryEvent)} WHERE {n.C("entryId")} = @0 AND {n.C("dueUtc")} IS NOT NULL AND {n.C("kind")} IN ('{StateChangedKind}', '{NotSpamKind}')",
                    entryId) > 0;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task DelayDueAsync(long eventId, TimeSpan wait, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryEvent)} SET {n.C("dueUtc")} = @1 WHERE {n.C("id")} = @0 AND {n.C("dueUtc")} IS NOT NULL", eventId, database.UtcNow + wait);
            },
            cancellationToken);

    internal static StoredEntryEvent FromDto(EntryEventDto d) =>
        new(d.Id, d.EntryId, FormBuilderDbTime.Read(d.OccurredUtc), d.Kind, d.UserKey, d.ChangeKey, d.Details);

    private static void Check(NewEntryEvent entryEvent)
    {
        ArgumentNullException.ThrowIfNull(entryEvent);
        var kind = entryEvent.Kind;
        if (kind is null || kind.Length is 0 or > MaxKindLength || !char.IsAsciiLetter(kind[0]) || !kind.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
        {
            throw new ArgumentException($"An event's kind is 1 to {MaxKindLength} ASCII letters, digits, dots or hyphens, starting with a letter.", nameof(entryEvent));
        }

        if (entryEvent.Details is null || !JsonObjectText.IsObject(entryEvent.Details))
        {
            throw new ArgumentException("An event's details are a JSON object.", nameof(entryEvent));
        }
    }

}
