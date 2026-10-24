using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>An internal note on an entry.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="EntryId">The entry.</param>
/// <param name="UserKey">The backoffice user who wrote it.</param>
/// <param name="Body">The note.</param>
/// <param name="CreatedUtc">When it was written.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
internal sealed record StoredEntryNote(int Id, long EntryId, Guid UserKey, string Body, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc);

/// <summary>
/// The internal notes on entries in <c>jfbEntryNote</c> (section 4.4, ADR-0038), never sent anywhere. Who may change or
/// delete a note is the entry service's to decide (10.x); a change is the last writer's.
/// </summary>
internal interface IEntryNoteRepository
{
    /// <summary>Adds a note; null when the entry does not exist.</summary>
    Task<StoredEntryNote?> AddAsync(long entryId, Guid userKey, string body, CancellationToken cancellationToken = default);

    /// <summary>The note with this id, or null.</summary>
    Task<StoredEntryNote?> GetAsync(int noteId, CancellationToken cancellationToken = default);

    /// <summary>An entry's notes, oldest first.</summary>
    Task<IReadOnlyList<StoredEntryNote>> ListAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>Changes a note's text; null when the note does not exist.</summary>
    Task<StoredEntryNote?> UpdateAsync(int noteId, string body, CancellationToken cancellationToken = default);

    /// <summary>Deletes a note. True when it existed.</summary>
    Task<bool> DeleteAsync(int noteId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class EntryNoteRepository(FormBuilderDatabase database) : IEntryNoteRepository
{
    /// <summary>The longest note, in characters.</summary>
    public const int MaxBodyLength = 10_000;

    /// <inheritdoc />
    public Task<StoredEntryNote?> AddAsync(long entryId, Guid userKey, string body, CancellationToken cancellationToken = default)
    {
        CheckBody(body);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await EntryRepository.HoldAsync(db, n, entryId))
                {
                    return null;
                }

                var now = database.UtcNow;
                var row = new EntryNoteDto { EntryId = entryId, UserKey = userKey, Body = body, CreatedUtc = now, UpdatedUtc = now };
                await db.InsertRowAsync(row);
                return await FindAsync(db, n, $"{n.C("id")} = @0", row.Id);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEntryNote?> GetAsync(int noteId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return FindAsync(db, n, $"{n.C("id")} = @0", noteId);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEntryNote>> ListAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredEntryNote>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EntryNoteDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.EntryNote)} WHERE {n.C("entryId")} = @0 ORDER BY {n.C("createdUtc")}, {n.C("id")}",
                    entryId);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredEntryNote?> UpdateAsync(int noteId, string body, CancellationToken cancellationToken = default)
    {
        CheckBody(body);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.EntryNote)} SET {n.C("body")} = @0, {n.C("updatedUtc")} = @1 WHERE {n.C("id")} = @2",
                    body,
                    database.UtcNow,
                    noteId);
                return await FindAsync(db, n, $"{n.C("id")} = @0", noteId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int noteId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.EntryNote)} WHERE {n.C("id")} = @0", noteId) == 1;
            },
            cancellationToken);

    internal static StoredEntryNote FromDto(EntryNoteDto d) =>
        new(d.Id, d.EntryId, d.UserKey, d.Body, FormBuilderDbTime.Read(d.CreatedUtc), FormBuilderDbTime.Read(d.UpdatedUtc));

    private static async Task<StoredEntryNote?> FindAsync(IUmbracoDatabase db, SqlNames n, string where, params object?[] args)
    {
        var row = await db.FirstOrNullAsync<EntryNoteDto>($"SELECT * FROM {n.T(FormBuilderTables.EntryNote)} WHERE {where}", args);
        return row is null ? null : FromDto(row);
    }

    private static void CheckBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyLength)
        {
            throw new ArgumentException($"A note holds 1 to {MaxBodyLength} characters, not only white space.", nameof(body));
        }
    }
}
