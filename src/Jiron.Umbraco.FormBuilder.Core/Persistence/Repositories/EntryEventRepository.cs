using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>An event to add to an entry's timeline.</summary>
/// <param name="EntryId">The entry.</param>
/// <param name="Kind">What happened, such as <c>submitted</c> or <c>stateChanged</c>: 1 to 64 ASCII letters, digits, dots or hyphens, starting with a letter.</param>
/// <param name="UserKey">The backoffice user who did it, or null for the visitor or the system.</param>
/// <param name="ChangeKey">The change it belongs to, so Undo finds every event of that change (ADR-0023).</param>
/// <param name="Details">What it changed, as a JSON object; old and new values only of fields that are not sensitive.</param>
internal sealed record NewEntryEvent(long EntryId, string Kind, Guid? UserKey = null, Guid? ChangeKey = null, string Details = "{}");

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
}

/// <inheritdoc />
internal sealed class EntryEventRepository(FormBuilderDatabase database) : IEntryEventRepository
{
    /// <summary>The longest kind the column holds.</summary>
    public const int MaxKindLength = 64;

    /// <inheritdoc />
    public Task<StoredEntryEvent?> AddAsync(NewEntryEvent entryEvent, CancellationToken cancellationToken = default)
    {
        Check(entryEvent);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await EntryRepository.HoldAsync(db, n, entryEvent.EntryId))
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
