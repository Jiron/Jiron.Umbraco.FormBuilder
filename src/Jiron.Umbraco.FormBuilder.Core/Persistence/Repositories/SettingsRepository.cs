using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>The scopes of the settings rows (ADR-0008): the values every site shares, and a site's own.</summary>
internal static class SettingsScopes
{
    /// <summary>The scope of the values every site shares; its rows exist from the migration on (ADR-0033).</summary>
    public const string Global = "global";

    /// <summary>The scope of a site, by the key of its root content.</summary>
    public static string ForSite(Guid rootKey) => rootKey.ToString("D");

    /// <summary>True for <see cref="Global"/> and for a site's scope as <see cref="ForSite"/> writes it.</summary>
    public static bool IsValid(string? scope) =>
        scope == Global || (scope is { Length: 36 } && Guid.TryParseExact(scope, "D", out var key) && ForSite(key) == scope);
}

/// <summary>A stored settings section.</summary>
/// <param name="Scope">"global", or a site's scope.</param>
/// <param name="Section">The section, for example "email".</param>
/// <param name="SchemaVersion">The schema version of its JSON.</param>
/// <param name="Version">The optimistic version a save sends back.</param>
/// <param name="Json">Its values as camelCase JSON; a missing property means the default.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
/// <param name="UpdatedByUserKey">Who changed it last, or null when Jiron Form Builder did.</param>
internal sealed record StoredSettings(string Scope, string Section, int SchemaVersion, int Version, string Json, DateTimeOffset UpdatedUtc, Guid? UpdatedByUserKey);

/// <summary>A section to save.</summary>
/// <param name="Scope">"global", or a site's scope.</param>
/// <param name="Section">The section: a lowercase letter, then up to 63 lowercase letters and digits.</param>
/// <param name="SchemaVersion">The schema version of its JSON.</param>
/// <param name="ExpectedVersion">The version the caller read, or 0 for a section the scope has never saved.</param>
/// <param name="Json">Its values as camelCase JSON.</param>
/// <param name="UserKey">Who saves it, or null when Jiron Form Builder does.</param>
internal sealed record SettingsSave(string Scope, string Section, int SchemaVersion, int ExpectedVersion, string Json, Guid? UserKey);

/// <summary>
/// The settings sections in <c>jfbSetting</c> (ADR-0008), one row per scope and section, with optimistic concurrency
/// on <see cref="StoredSettings.Version"/>: a stale save throws <see cref="FormBuilderConcurrencyException"/> and
/// writes nothing.
/// </summary>
internal interface ISettingsRepository
{
    /// <summary>A section of a scope, or null when the scope has not saved it.</summary>
    Task<StoredSettings?> GetAsync(string scope, string section, CancellationToken cancellationToken = default);

    /// <summary>Every stored section of a scope, by section.</summary>
    Task<IReadOnlyList<StoredSettings>> ListAsync(string scope, CancellationToken cancellationToken = default);

    /// <summary>A section's row of every scope that stores it, the global one first.</summary>
    Task<IReadOnlyList<StoredSettings>> ListSectionAsync(string section, CancellationToken cancellationToken = default);

    /// <summary>Saves a section and returns it with its next version: 1 for the first save of a scope.</summary>
    Task<StoredSettings> SaveAsync(SettingsSave save, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a site's section, so the site follows the global values again. False when it is not stored. The global
    /// rows always stay.
    /// </summary>
    Task<bool> DeleteAsync(string scope, string section, int expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every section a site stores, once its root is deleted for good (ADR-0043, ADR-0232), whatever their
    /// versions; the sections removed, by name, none when the scope stored none. The global rows always stay.
    /// </summary>
    Task<IReadOnlyList<string>> DeleteScopeAsync(string scope, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SettingsRepository(FormBuilderDatabase database) : ISettingsRepository
{
    /// <summary>The longest section name the column holds.</summary>
    public const int MaxSectionLength = 64;

    /// <inheritdoc />
    public Task<StoredSettings?> GetAsync(string scope, string section, CancellationToken cancellationToken = default)
    {
        CheckScope(scope);
        CheckSection(section);
        return database.ReadAsync(db => FindAsync(db, scope, section), cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSettings>> ListAsync(string scope, CancellationToken cancellationToken = default)
    {
        CheckScope(scope);
        return database.ReadAsync<IReadOnlyList<StoredSettings>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<SettingDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Setting)} WHERE {n.C("scope")} = @0 ORDER BY {n.C("section")}",
                    scope);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredSettings>> ListSectionAsync(string section, CancellationToken cancellationToken = default)
    {
        CheckSection(section);
        return database.ReadAsync<IReadOnlyList<StoredSettings>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<SettingDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Setting)} WHERE {n.C("section")} = @0 ORDER BY CASE WHEN {n.C("scope")} = @1 THEN 0 ELSE 1 END, {n.C("scope")}",
                    section,
                    SettingsScopes.Global);
                return rows.Select(FromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredSettings> SaveAsync(SettingsSave save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        CheckScope(save.Scope);
        CheckSection(save.Section);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(save.SchemaVersion, nameof(save));
        ArgumentOutOfRangeException.ThrowIfNegative(save.ExpectedVersion, nameof(save));
        ArgumentException.ThrowIfNullOrEmpty(save.Json, nameof(save));
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Setting);
                var now = database.UtcNow;

                // One statement each: a first save inserts once, a later one updates the version it read (ADR-0036).
                var written = save.ExpectedVersion == 0
                    ? await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("scope")}, {n.C("section")}, {n.C("schemaVersion")}, {n.C("version")}, {n.C("json")}, {n.C("updatedUtc")}, {n.C("updatedByUserKey")})
                        SELECT @0, @1, @2, 1, @3, @4, @5
                        WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("scope")} = @0 AND {n.C("section")} = @1)
                        """,
                        save.Scope,
                        save.Section,
                        save.SchemaVersion,
                        save.Json,
                        now,
                        save.UserKey)
                    : await db.ExecAsync(
                        $"UPDATE {table} SET {n.C("schemaVersion")} = @0, {n.C("version")} = {n.C("version")} + 1, {n.C("json")} = @1, {n.C("updatedUtc")} = @2, {n.C("updatedByUserKey")} = @3 WHERE {n.C("scope")} = @4 AND {n.C("section")} = @5 AND {n.C("version")} = @6",
                        save.SchemaVersion,
                        save.Json,
                        now,
                        save.UserKey,
                        save.Scope,
                        save.Section,
                        save.ExpectedVersion);
                if (written != 1)
                {
                    throw new FormBuilderConcurrencyException($"The settings section '{save.Section}' was saved by someone else meanwhile.");
                }

                return (await FindAsync(db, save.Scope, save.Section))!;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string scope, string section, int expectedVersion, CancellationToken cancellationToken = default)
    {
        CheckScope(scope);
        CheckSection(section);
        if (scope == SettingsScopes.Global)
        {
            throw new ArgumentException("The global settings rows always stay.", nameof(scope));
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var deleted = await db.ExecAsync(
                    $"DELETE FROM {n.T(FormBuilderTables.Setting)} WHERE {n.C("scope")} = @0 AND {n.C("section")} = @1 AND {n.C("version")} = @2",
                    scope,
                    section,
                    expectedVersion);
                if (deleted == 1)
                {
                    return true;
                }

                return await FindAsync(db, scope, section) is null
                    ? false
                    : throw new FormBuilderConcurrencyException($"The settings section '{section}' was saved by someone else meanwhile.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> DeleteScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        CheckScope(scope);
        if (scope == SettingsScopes.Global)
        {
            throw new ArgumentException("The global settings rows always stay.", nameof(scope));
        }

        return database.WriteAsync<IReadOnlyList<string>>(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Setting);
                var sections = await db.FetchListAsync<string>($"SELECT {n.C("section")} FROM {table} WHERE {n.C("scope")} = @0 ORDER BY {n.C("section")}", scope);
                if (sections.Count > 0)
                {
                    await db.ExecAsync($"DELETE FROM {table} WHERE {n.C("scope")} = @0", scope);
                }

                return sections;
            },
            cancellationToken);
    }

    /// <summary>True for a lowercase letter followed by up to 63 lowercase letters and digits.</summary>
    internal static bool IsValidSection(string? section) =>
        section is { Length: > 0 and <= MaxSectionLength }
        && section[0] is >= 'a' and <= 'z'
        && section.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9'));

    private static async Task<StoredSettings?> FindAsync(IUmbracoDatabase db, string scope, string section)
    {
        var n = new SqlNames(db);
        var row = await db.FirstOrNullAsync<SettingDto>(
            $"SELECT * FROM {n.T(FormBuilderTables.Setting)} WHERE {n.C("scope")} = @0 AND {n.C("section")} = @1",
            scope,
            section);
        return row is null ? null : FromDto(row);
    }

    private static StoredSettings FromDto(SettingDto d) =>
        new(d.Scope, d.Section, d.SchemaVersion, d.Version, d.Json, FormBuilderDbTime.Read(d.UpdatedUtc), d.UpdatedByUserKey);

    private static void CheckScope(string scope)
    {
        if (!SettingsScopes.IsValid(scope))
        {
            throw new ArgumentException("A settings scope is \"global\" or a site's root key in lowercase.", nameof(scope));
        }
    }

    private static void CheckSection(string section)
    {
        if (!IsValidSection(section))
        {
            throw new ArgumentException("A settings section is a lowercase letter followed by up to 63 lowercase letters and digits.", nameof(section));
        }
    }
}
