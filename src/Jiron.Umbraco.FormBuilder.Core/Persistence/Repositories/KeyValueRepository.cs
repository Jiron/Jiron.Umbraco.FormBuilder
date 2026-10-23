using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// Jiron Form Builder's one-time flags and small values in <c>jfbKeyValue</c> (section 4.5): the section grant, the setup
/// checklist, the file mirror's hashes. Keys are lowercase, so the case-insensitive key column (ADR-0033) never has to
/// decide between two spellings.
/// </summary>
internal interface IKeyValueRepository
{
    /// <summary>A value, or null when the key is not stored (or stores null).</summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Every stored key that starts with <paramref name="prefix"/>, with its value, in key order.</summary>
    Task<IReadOnlyList<KeyValuePair<string, string?>>> ListAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Stores a value, replacing any earlier one.</summary>
    Task SetAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>Stores a value only when the key is new. True when this call stored it: the first writer wins on every server.</summary>
    Task<bool> TryAddAsync(string key, string? value, CancellationToken cancellationToken = default);

    /// <summary>Removes a key. True when it was stored.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class KeyValueRepository(FormBuilderDatabase database) : IKeyValueRepository
{
    /// <summary>The longest key the column holds.</summary>
    public const int MaxKeyLength = 255;

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        CheckKey(key, nameof(key));
        return database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var row = await db.FirstOrNullAsync<KeyValueDto>($"SELECT * FROM {n.T(FormBuilderTables.KeyValue)} WHERE {n.C("key")} = @0", key);
                return row?.Value;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<KeyValuePair<string, string?>>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        CheckKey(prefix, nameof(prefix));
        return database.ReadAsync<IReadOnlyList<KeyValuePair<string, string?>>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<KeyValueDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.KeyValue)} WHERE {n.C("key")} LIKE @0 {SqlNames.EscapeClause} ORDER BY {n.C("key")}",
                    SqlNames.Escape(prefix) + "%");
                return rows.Select(row => KeyValuePair.Create(row.Key, row.Value)).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        CheckKey(key, nameof(key));
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var now = database.UtcNow;
                var update = $"UPDATE {n.T(FormBuilderTables.KeyValue)} SET {n.C("value")} = @0, {n.C("updatedUtc")} = @1 WHERE {n.C("key")} = @2";

                // A writer that adds the key in between makes the insert a no-op; the second update then replaces its value.
                if (await db.ExecAsync(update, value, now, key) == 0 && await InsertIfMissingAsync(db, n, key, value, now) == 0)
                {
                    await db.ExecAsync(update, value, now, key);
                }

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> TryAddAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        CheckKey(key, nameof(key));
        return database.WriteAsync(
            async db => await InsertIfMissingAsync(db, new SqlNames(db), key, value, database.UtcNow) == 1,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        CheckKey(key, nameof(key));
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.KeyValue)} WHERE {n.C("key")} = @0", key) == 1;
            },
            cancellationToken);
    }

    /// <summary>
    /// True for a key of 1 to <see cref="MaxKeyLength"/> characters from <c>a-z</c>, <c>0-9</c> and <c>. _ : / -</c>.
    /// </summary>
    internal static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or ':' or '/' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckKey(string key, string parameter)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException($"A key holds 1 to {MaxKeyLength} characters from a-z, 0-9 and . _ : / -.", parameter);
        }
    }

    // One statement, so a second writer cannot slip in between a check and the insert (ADR-0036).
    private static Task<int> InsertIfMissingAsync(IUmbracoDatabase db, SqlNames n, string key, string? value, DateTime now)
    {
        var table = n.T(FormBuilderTables.KeyValue);
        return db.ExecAsync(
            $"INSERT INTO {table} ({n.C("key")}, {n.C("value")}, {n.C("updatedUtc")}) SELECT @0, @1, @2 WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("key")} = @0)",
            key,
            value,
            now);
    }
}
