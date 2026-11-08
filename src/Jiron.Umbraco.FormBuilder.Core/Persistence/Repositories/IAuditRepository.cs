using Jiron.Umbraco.FormBuilder.Core.Audit;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>An action to record in the audit log (section 4.10, ADR-0041).</summary>
/// <param name="Action">
/// What happened: <c>entity.verb</c>, two camelCase words of ASCII letters and digits joined by a dot, at most 64
/// characters, such as <c>entry.deleted</c>. SQL Server compares actions and entity types ignoring case, so no two of
/// them may differ in case only.
/// </param>
/// <param name="EntityType">The kind of item it happened to: one camelCase word of at most 64 characters, such as <c>entry</c>.</param>
/// <param name="EntityKey">The item's key, or null when the action is about no single item.</param>
/// <param name="UserKey">The backoffice user who did it, or null for Jiron Form Builder's own actions (jobs, retention).</param>
/// <param name="Details">What else identifies the action, never content; null for nothing.</param>
internal sealed record NewAuditEntry(string Action, string EntityType, Guid? EntityKey, Guid? UserKey, AuditDetails? Details = null);

/// <summary>A recorded action.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="OccurredUtc">When it was recorded.</param>
/// <param name="UserKey">Who did it; null for Jiron Form Builder itself.</param>
/// <param name="Action">What happened.</param>
/// <param name="EntityType">The kind of item it happened to.</param>
/// <param name="EntityKey">The item's key, if any.</param>
/// <param name="Details">The details as a JSON object; <c>{}</c> for none.</param>
internal sealed record StoredAuditEntry(
    long Id,
    DateTimeOffset OccurredUtc,
    Guid? UserKey,
    string Action,
    string EntityType,
    Guid? EntityKey,
    string Details);

/// <summary>Which audit entries to list: every filter that is set must match.</summary>
internal sealed record AuditQuery
{
    /// <summary>Only this action.</summary>
    public string? Action { get; init; }

    /// <summary>Only items of this kind.</summary>
    public string? EntityType { get; init; }

    /// <summary>Only this item.</summary>
    public Guid? EntityKey { get; init; }

    /// <summary>Only this user's actions.</summary>
    public Guid? UserKey { get; init; }

    /// <summary>Only actions recorded at or after this moment, which lies in the years 1753 to 9999.</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Only actions recorded before this moment, which lies in the years 1753 to 9999.</summary>
    public DateTimeOffset? BeforeUtc { get; init; }

    /// <summary>
    /// Only actions of these kinds of item, each the first word of an action (<c>form</c> for <c>form.published</c>), one
    /// camelCase word; null for every kind (ADR-0219).
    /// </summary>
    public IReadOnlyList<string>? Entities { get; init; }

    /// <summary>
    /// Only actions on this form, or that name it in their details as their <c>form</c>, as an entry's actions do (ADR-0219).
    /// </summary>
    public Guid? FormKey { get; init; }

    /// <summary>Only actions of these users, on these items, or naming these forms: a search's matches (ADR-0219); null for no search.</summary>
    public AuditSearch? Search { get; init; }
}

/// <summary>
/// What a search of the audit log found: an action matches when its user or its item is among them, or it names one of the
/// forms in its details.
/// </summary>
/// <param name="Users">The users whose name or email matched.</param>
/// <param name="Items">The items whose name matched, forms among them.</param>
/// <param name="Forms">The forms whose name matched.</param>
internal sealed record AuditSearch(IReadOnlyCollection<Guid> Users, IReadOnlyCollection<Guid> Items, IReadOnlyCollection<Guid> Forms)
{
    /// <summary>The most users or items one search names.</summary>
    public const int MaxMatches = 200;

    /// <summary>The most forms one search looks for in the details, each a pattern every row is matched against.</summary>
    public const int MaxForms = 40;
}

/// <summary>
/// The audit log in <c>jfbAuditEntry</c> (section 4.10, ADR-0041): who did what to which item and when, never content.
/// An entry is added in the unit of work of the change it records, so it commits or rolls back with it. Entries never
/// change, and outlive the items they name until retention purges them.
/// </summary>
internal interface IAuditRepository
{
    /// <summary>
    /// Records an action, in the caller's unit of work when there is one; the entry as stored, with the time as the
    /// database keeps it (SQL Server rounds it to about 3 ms).
    /// </summary>
    Task<StoredAuditEntry> AddAsync(NewAuditEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an action unless the same user recorded the same action on the same item within <paramref name="window"/>
    /// (a view of an entry, at most every 15 minutes): one statement, so two servers record one entry between them. The
    /// entry as stored, or null when one was recorded within the window, which lasts from 1 second to 24 hours.
    /// </summary>
    /// <exception cref="ArgumentException">The entry names no item or no user.</exception>
    Task<StoredAuditEntry?> AddOnceAsync(NewAuditEntry entry, TimeSpan window, CancellationToken cancellationToken = default);

    /// <summary>The entry with this id, or null.</summary>
    Task<StoredAuditEntry?> GetAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// A page of the entries that match <paramref name="query"/>, newest first, with how many match in all, within a time
    /// budget: the filters may match few rows of a long log, and no index serves them all.
    /// </summary>
    /// <exception cref="FormBuilderQueryTimeoutException">The read ran past its time budget (<c>query_timeout</c>).</exception>
    Task<Page<StoredAuditEntry>> QueryAsync(AuditQuery query, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes up to <paramref name="take"/> of the entries recorded longer than <paramref name="keep"/> ago, the oldest
    /// first (retention, 13.1); how many went. The job repeats it until it returns 0.
    /// </summary>
    Task<int> PurgeAsync(TimeSpan keep, int take, CancellationToken cancellationToken = default);
}
