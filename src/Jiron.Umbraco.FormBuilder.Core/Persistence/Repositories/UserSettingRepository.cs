namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// A backoffice user's own choices in <c>jfbUserSetting</c> (section 4.5, ADR-0008): column sets, saved filters, export
/// choices and the digest, the same on every device. Each is one JSON document under a name; the service that owns a
/// name checks what it holds.
/// </summary>
internal interface IUserSettingRepository
{
    /// <summary>The JSON stored under a name for the user, or none.</summary>
    /// <param name="userKey">The backoffice user.</param>
    /// <param name="name">The setting's name: 1 to <see cref="UserSettingRepository.MaxNameLength"/> characters.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    Task<string?> GetAsync(Guid userKey, string name, CancellationToken cancellationToken = default);

    /// <summary>Stores the JSON under a name for the user, replacing any earlier one.</summary>
    /// <param name="userKey">The backoffice user.</param>
    /// <param name="name">The setting's name.</param>
    /// <param name="json">The setting, as JSON.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    Task SetAsync(Guid userKey, string name, string json, CancellationToken cancellationToken = default);

    /// <summary>Removes a setting. True when it was stored.</summary>
    /// <param name="userKey">The backoffice user.</param>
    /// <param name="name">The setting's name.</param>
    /// <param name="cancellationToken">Stops the write.</param>
    Task<bool> DeleteAsync(Guid userKey, string name, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class UserSettingRepository(FormBuilderDatabase database) : IUserSettingRepository
{
    /// <summary>The longest name the column holds.</summary>
    public const int MaxNameLength = 64;

    /// <inheritdoc />
    public Task<string?> GetAsync(Guid userKey, string name, CancellationToken cancellationToken = default)
    {
        Check(userKey, name);
        return database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ScalarAsync<string?>(
                    $"SELECT {n.C("json")} FROM {n.T(FormBuilderTables.UserSetting)} WHERE {n.C("userKey")} = @0 AND {n.C("name")} = @1",
                    userKey,
                    name);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task SetAsync(Guid userKey, string name, string json, CancellationToken cancellationToken = default)
    {
        Check(userKey, name);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.UserSetting);
                var now = database.UtcNow;
                var update = $"UPDATE {table} SET {n.C("json")} = @0, {n.C("updatedUtc")} = @1 WHERE {n.C("userKey")} = @2 AND {n.C("name")} = @3";

                // A writer that adds the row in between makes the insert a no-op; the second update then replaces its value.
                if (await db.ExecAsync(update, json, now, userKey, name) == 0
                    && await db.ExecAsync(
                        $"INSERT INTO {table} ({n.C("userKey")}, {n.C("name")}, {n.C("json")}, {n.C("updatedUtc")}) SELECT @0, @1, @2, @3 WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("userKey")} = @0 AND {n.C("name")} = @1)",
                        userKey,
                        name,
                        json,
                        now) == 0)
                {
                    await db.ExecAsync(update, json, now, userKey, name);
                }

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid userKey, string name, CancellationToken cancellationToken = default)
    {
        Check(userKey, name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecAsync(
                    $"DELETE FROM {n.T(FormBuilderTables.UserSetting)} WHERE {n.C("userKey")} = @0 AND {n.C("name")} = @1",
                    userKey,
                    name) == 1;
            },
            cancellationToken);
    }

    private static void Check(Guid userKey, string name)
    {
        if (userKey == Guid.Empty)
        {
            throw new ArgumentException("A user setting belongs to a user.", nameof(userKey));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, MaxNameLength, nameof(name));
    }
}
