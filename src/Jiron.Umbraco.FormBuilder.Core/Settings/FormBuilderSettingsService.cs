using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>A section's effective values in a scope (ADR-0008, ADR-0043).</summary>
/// <typeparam name="T">The section.</typeparam>
/// <param name="Value">The code defaults, then the global row, then the site row, then the host rules.</param>
/// <param name="Version">The version of the scope's own row, 0 when it has none; a save sends it back.</param>
/// <param name="Overrides">The top-level fields the site overrides; empty for the global scope.</param>
/// <param name="Locks">The host's rules by field path, <c>*</c> for the whole section.</param>
internal sealed record SettingsSnapshot<T>(T Value, int Version, IReadOnlySet<string> Overrides, IReadOnlyDictionary<string, SettingsLock> Locks)
    where T : ISettingsSection;

/// <summary>Reads and saves the backoffice settings sections (ADR-0008, ADR-0043).</summary>
internal interface IFormBuilderSettingsService
{
    /// <summary>
    /// A section's effective values for a site, by the key of its root content, or the global values when
    /// <paramref name="siteKey"/> is null. A section every site shares gives its global values for any site.
    /// </summary>
    Task<SettingsSnapshot<T>> GetAsync<T>(Guid? siteKey = null, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new();

    /// <summary>The keys of the sites that set something of a per-site section of their own.</summary>
    Task<IReadOnlyList<Guid>> SitesOfAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new();

    /// <summary>
    /// Validates and saves the global values of a section; <paramref name="expectedVersion"/> is the version the editor
    /// read. The save is a unit of work of its own.
    /// </summary>
    /// <exception cref="FormBuilderSettingLockedException">The save changes what the host configuration locks.</exception>
    /// <exception cref="FormBuilderSettingsValidationException">A value is not allowed.</exception>
    /// <exception cref="FormBuilderConcurrencyException">Someone saved the section meanwhile.</exception>
    Task<SettingsSnapshot<T>> SaveAsync<T>(T values, int expectedVersion, Guid? userKey, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new();

    /// <summary>
    /// Validates and saves the global values of a section as <paramref name="change"/> makes them from the stored ones,
    /// read in the save, not from a cached copy, so a field the change leaves keeps its stored value;
    /// <paramref name="expectedVersion"/> is the version the editor read. The save is a unit of work of its own.
    /// </summary>
    /// <exception cref="FormBuilderSettingLockedException">The save changes what the host configuration locks.</exception>
    /// <exception cref="FormBuilderSettingsValidationException">A value is not allowed.</exception>
    /// <exception cref="FormBuilderConcurrencyException">Someone saved the section meanwhile.</exception>
    Task<SettingsSnapshot<T>> UpdateAsync<T>(Func<T, T> change, int expectedVersion, Guid? userKey, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new();

    /// <summary>
    /// Validates and saves what a site overrides of a per-site section: the top-level fields in
    /// <paramref name="overrides"/> take their values from <paramref name="values"/>, and every other field follows the
    /// global values. Without fields, the site follows the global values again and its row goes.
    /// </summary>
    /// <exception cref="ArgumentException">Every site shares the section's global values.</exception>
    /// <exception cref="FormBuilderSettingLockedException">The save changes what the host configuration locks.</exception>
    /// <exception cref="FormBuilderSettingsValidationException">A value is not allowed, or a field is not one a site can set.</exception>
    /// <exception cref="FormBuilderConcurrencyException">Someone saved the site's section meanwhile.</exception>
    Task<SettingsSnapshot<T>> SaveSiteAsync<T>(Guid siteKey, T values, IReadOnlyCollection<string> overrides, int expectedVersion, Guid? userKey, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new();

    /// <summary>
    /// Removes everything a site set of its own, once its root is deleted for good (ADR-0043, ADR-0232): its rows go with an
    /// audit entry each, and every server forgets them, while the other sites' rows and the global ones stay. The removal
    /// is a unit of work of its own; the sections removed, none for content that was no site or set nothing.
    /// </summary>
    /// <param name="siteKey">The key of the deleted root.</param>
    /// <param name="userKey">Who deleted it, or null when Umbraco did.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    Task<IReadOnlyList<string>> RemoveSiteAsync(Guid siteKey, Guid? userKey, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed partial class FormBuilderSettingsService(
    ISettingsRepository repository,
    IServiceProvider services,
    IEnumerable<ISettingsSectionMigration> migrations,
    IOptions<FormBuilderOptions> options,
    AppCaches appCaches,
    DistributedCache distributedCache,
    FormBuilderChanges<FormBuilderSettingsService> changes,
    ILogger<FormBuilderSettingsService> logger)
    : IFormBuilderSettingsService
{
    /// <summary>The audit action of a save.</summary>
    public const string SavedAction = "settings.saved";

    /// <summary>The audit action of a site's section removed with its root (ADR-0232).</summary>
    public const string RemovedAction = "settings.removed";

    /// <summary>The audit entity type of a section; the item is the site's key, none for the global values.</summary>
    public const string EntityType = "settings";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    private readonly IReadOnlyList<ISettingsSectionMigration> _migrations = migrations.ToList();

    /// <inheritdoc />
    public Task<SettingsSnapshot<T>> GetAsync<T>(Guid? siteKey = null, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new()
    {
        var scope = siteKey is { } key && T.PerSite ? SettingsScopes.ForSite(key) : SettingsScopes.Global;
        var cacheKey = SettingsCacheKeys.Of(scope, T.Alias);

        // One read for every miss while it runs. A save clears the key and the read under way: a read that began before
        // the save answers its callers but is not kept. It runs without a caller's cancellation, as others may wait for it.
        return RuntimeCacheLoads.GetAsync(
            appCaches.RuntimeCache,
            cacheKey,
            cacheKey + SettingsCacheKeys.LoadingSuffix,
            async () => Snapshot<T>(await ReadStoredAsync<T>(scope, CancellationToken.None)),
            CacheDuration,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> SitesOfAsync<T>(CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new()
    {
        var cacheKey = SettingsCacheKeys.Of(SettingsCacheKeys.Sites, T.Alias);
        return RuntimeCacheLoads.GetAsync<IReadOnlyList<Guid>>(
            appCaches.RuntimeCache,
            cacheKey,
            cacheKey + SettingsCacheKeys.LoadingSuffix,
            async () => [.. (await repository.ListSectionAsync(T.Alias, CancellationToken.None)).Where(row => row.Scope != SettingsScopes.Global).Select(row => Guid.ParseExact(row.Scope, "D"))],
            CacheDuration,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SettingsSnapshot<T>> SaveAsync<T>(T values, int expectedVersion, Guid? userKey, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new()
    {
        ArgumentNullException.ThrowIfNull(values);
        return SaveAsync<T>(null, _ => values, overrides: null, expectedVersion, userKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SettingsSnapshot<T>> UpdateAsync<T>(Func<T, T> change, int expectedVersion, Guid? userKey, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new()
    {
        ArgumentNullException.ThrowIfNull(change);
        return SaveAsync<T>(null, stored => change(SettingsSectionJson.Combine<T>(stored.Global, null)), overrides: null, expectedVersion, userKey, cancellationToken);
    }

    /// <inheritdoc />
    public Task<SettingsSnapshot<T>> SaveSiteAsync<T>(Guid siteKey, T values, IReadOnlyCollection<string> overrides, int expectedVersion, Guid? userKey, CancellationToken cancellationToken = default)
        where T : class, ISettingsSection, new()
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(overrides);
        if (!T.PerSite)
        {
            throw new ArgumentException($"Every site has the global values of the settings section '{T.Alias}'.", nameof(siteKey));
        }

        return SaveAsync<T>(siteKey, _ => values, overrides, expectedVersion, userKey, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> RemoveSiteAsync(Guid siteKey, Guid? userKey, CancellationToken cancellationToken = default)
    {
        changes.EnsureOwnUnit("Removing a site's settings is a unit of work of its own: it clears caches once it has committed (ADR-0043).");
        var scope = SettingsScopes.ForSite(siteKey);
        var removed = await changes.WriteAsync(
            () => repository.DeleteScopeAsync(scope, cancellationToken),
            sections => sections.Select(section => new NewAuditEntry(RemovedAction, EntityType, siteKey, userKey, new AuditDetails().AddIdentifier("section", section))),
            cancellationToken);
        if (removed.Count == 0)
        {
            return removed;
        }

        // Committed: this server forgets the site's copies and the lists of the sites that set each section at once, the others
        // when the cache instruction reaches them.
        SettingsCacheChange[] gone = [.. removed.Select(section => new SettingsCacheChange(scope, section))];
        foreach (var change in gone)
        {
            FormBuilderSettingsCacheRefresher.Forget(appCaches.RuntimeCache, change);
        }

        distributedCache.RefreshByPayload(FormBuilderSettingsCacheRefresher.UniqueId, gone);
        return removed;
    }

    // The values to save come from the stored scope, as read here: a whole section saved, or a change of the stored one.
    private async Task<SettingsSnapshot<T>> SaveAsync<T>(Guid? siteKey, Func<StoredScope, T> valuesOf, IReadOnlyCollection<string>? overrides, int expectedVersion, Guid? userKey, CancellationToken cancellationToken)
        where T : class, ISettingsSection, new()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);
        changes.EnsureOwnUnit("A settings save is a unit of work of its own: it clears caches and tells handlers once it has committed (ADR-0043).");

        var scope = siteKey is { } key ? SettingsScopes.ForSite(key) : SettingsScopes.Global;
        var errors = new SettingsErrors(T.Alias);
        var written = overrides is null ? SettingsSectionJson.FieldNames<T>() : SiteFields<T>(overrides, errors);
        var stored = await ReadStoredAsync<T>(scope, cancellationToken);
        var normalized = Validators<T>().Aggregate(valuesOf(stored), (section, validator) => validator.Normalize(section));
        var row = overrides is null ? SettingsSectionJson.ToNode(normalized) : SettingsSectionJson.SiteRow(normalized, written);
        var proposed = overrides is null ? normalized : SettingsSectionJson.Combine<T>(stored.Global, row);

        // 1. What the host locks, 2. what the section allows, all errors at once (ADR-0008).
        SettingsHostRules.CheckSave(proposed, written, options.Value, Rules<T>(), errors);
        var checks = new SettingsErrors(T.Alias);
        foreach (var validator in Validators<T>())
        {
            validator.Validate(proposed, checks);
        }

        var refused = errors.Errors.Concat(checks.Errors.Where(error => written.Contains(SettingsSectionJson.TopField(error.Field[(T.Alias.Length + 1)..])))).ToList();
        if (refused.Count > 0)
        {
            throw new FormBuilderSettingsValidationException(refused);
        }

        var change = new SettingsCacheChange(scope, T.Alias);
        var before = overrides is null
            ? SettingsSectionJson.ToNode(SettingsSectionJson.Combine<T>(stored.Global, null))
            : SettingsSectionJson.SiteRow(SettingsSectionJson.Combine<T>(stored.Global, stored.Site), SettingsSectionJson.Overrides<T>(stored.Site));
        var changed = SettingsSectionJson.ChangedFields(before, row);
        int? version;
        try
        {
            version = overrides is { Count: 0 }
                ? await RemoveSiteRowAsync<T>(scope, siteKey!.Value, expectedVersion, userKey, changed, cancellationToken)
                : await WriteAsync<T>(scope, siteKey, row, expectedVersion, userKey, changed, cancellationToken);
        }
        catch (FormBuilderConcurrencyException)
        {
            // Saved elsewhere meanwhile: this server's copy may be older than the row, so the next read gets the
            // current values, also while the other server's cache instruction is on its way.
            FormBuilderSettingsCacheRefresher.Forget(appCaches.RuntimeCache, change);
            throw;
        }

        var saved = Snapshot<T>(overrides is null ? new StoredScope(row, null, version ?? 0) : new StoredScope(stored.Global, version is null or 0 ? null : row, version ?? 0));
        if (version is null)
        {
            return saved;
        }

        // Committed: this server forgets its copy at once, whatever the server messenger delivers locally, and the
        // others when the cache instruction reaches them.
        FormBuilderSettingsCacheRefresher.Forget(appCaches.RuntimeCache, change);
        distributedCache.RefreshByPayload(FormBuilderSettingsCacheRefresher.UniqueId, [change]);
        await changes.PublishedAsync(new SettingsSavedNotification(T.Alias, siteKey, version.Value, userKey, changed), $"the settings section {T.Alias}");

        return saved;
    }

    // Writes the row with its audit entry in one unit of work; the row's new version.
    private async Task<int?> WriteAsync<T>(string scope, Guid? siteKey, JsonObject row, int expectedVersion, Guid? userKey, IReadOnlyList<string> changed, CancellationToken cancellationToken)
        where T : class, ISettingsSection, new() =>
        await changes.WriteAsync(
            async () => (await repository.SaveAsync(new SettingsSave(scope, T.Alias, T.SchemaVersion, expectedVersion, row.ToJsonString(SettingsSectionJson.Options), userKey), cancellationToken)).Version,
            version => [Entry<T>(siteKey, version, userKey, changed)],
            cancellationToken);

    // Removes a site's row, so it follows the global values again: 0, the version of a scope without a row, or null
    // when the site had no row to remove. A row that another save created or changed meanwhile is a conflict.
    private Task<int?> RemoveSiteRowAsync<T>(string scope, Guid siteKey, int expectedVersion, Guid? userKey, IReadOnlyList<string> changed, CancellationToken cancellationToken)
        where T : class, ISettingsSection, new() =>
        changes.WriteAsync<int?>(
            async () =>
            {
                if (!await repository.DeleteAsync(scope, T.Alias, expectedVersion, cancellationToken))
                {
                    return expectedVersion == 0
                        ? null
                        : throw new FormBuilderConcurrencyException($"The settings section '{T.Alias}' of the site was removed by someone else meanwhile.");
                }

                return 0;
            },
            version => version is null ? [] : [Entry<T>(siteKey, 0, userKey, changed)],
            cancellationToken);

    // The audit entry of a save: the section, the version and the changed paths, never a value (ADR-0041).
    private static NewAuditEntry Entry<T>(Guid? siteKey, int version, Guid? userKey, IReadOnlyList<string> changed)
        where T : ISettingsSection =>
        new(
            SavedAction,
            EntityType,
            siteKey,
            userKey,
            new AuditDetails()
                .AddIdentifier("section", T.Alias)
                .Add("version", version)
                .AddIdentifiers("fields", changed.Where(AuditDetails.IsIdentifier).Take(AuditDetails.MaxListLength))
                .Add("fieldCount", changed.Count));

    // The fields a site save names, each one a site can set; the others are errors.
    private static HashSet<string> SiteFields<T>(IReadOnlyCollection<string> overrides, SettingsErrors errors)
        where T : ISettingsSection
    {
        var allowed = SettingsSectionJson.SiteFieldNames<T>();
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in overrides.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (allowed.Contains(field))
            {
                fields.Add(field);
            }
            else
            {
                errors.Add(field, SettingsErrors.Scope, "Is not a setting each site can set.");
            }
        }

        return fields;
    }

    private async Task<StoredScope> ReadStoredAsync<T>(string scope, CancellationToken cancellationToken)
        where T : class, ISettingsSection, new()
    {
        var globalRow = await repository.GetAsync(SettingsScopes.Global, T.Alias, cancellationToken);
        var global = ReadRow<T>(globalRow, site: false) ?? [];
        if (scope == SettingsScopes.Global)
        {
            return new StoredScope(global, null, globalRow?.Version ?? 0);
        }

        var siteRow = await repository.GetAsync(scope, T.Alias, cancellationToken);
        return new StoredScope(global, ReadRow<T>(siteRow, site: true), siteRow?.Version ?? 0);
    }

    // A row's JSON, migrated and known to read; a row that cannot be read is logged and holds nothing, so the defaults
    // (or the global values) apply until the next save repairs it (ADR-0008).
    private JsonObject? ReadRow<T>(StoredSettings? row, bool site)
        where T : class, ISettingsSection, new()
    {
        if (row is null)
        {
            return null;
        }

        try
        {
            var node = SettingsSectionJson.Migrate<T>(row.Json, row.SchemaVersion, _migrations);
            _ = site ? SettingsSectionJson.Combine<T>(null, node) : SettingsSectionJson.Read<T>(node);
            return node;
        }
        catch (JsonException exception)
        {
            LogUnreadable(logger, T.Alias, row.Scope, LoggedException.Of(exception));
            return [];
        }
    }

    private SettingsSnapshot<T> Snapshot<T>(StoredScope stored)
        where T : class, ISettingsSection, new()
    {
        var (value, locks) = SettingsHostRules.Apply(SettingsSectionJson.Combine<T>(stored.Global, stored.Site), options.Value, Rules<T>());
        return new SettingsSnapshot<T>(value, stored.Version, SettingsSectionJson.Overrides<T>(stored.Site), locks);
    }

    private IEnumerable<ISettingsValidator<T>> Validators<T>()
        where T : ISettingsSection => services.GetServices<ISettingsValidator<T>>();

    private IEnumerable<ISettingsHostRule<T>> Rules<T>()
        where T : ISettingsSection => services.GetServices<ISettingsHostRule<T>>();

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not read the settings section {Section} of scope {Scope}; the defaults apply until it is saved again")]
    private static partial void LogUnreadable(ILogger logger, string section, string scope, LoggedException exception);

    // The rows a scope reads: the global row's JSON, the site row's, and the version of the scope's own row.
    private sealed record StoredScope(JsonObject Global, JsonObject? Site, int Version);
}
