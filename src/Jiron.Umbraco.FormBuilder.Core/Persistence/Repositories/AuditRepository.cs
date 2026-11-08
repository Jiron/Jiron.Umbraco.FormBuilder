using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
internal sealed class AuditRepository : IAuditRepository
{
    /// <summary>The longest action or entity type the columns hold.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The most entries one purge deletes.</summary>
    public const int MaxPurgeTake = 1000;

    /// <summary>The shortest window of <see cref="AddOnceAsync"/>: SQL Server stores times to about 3 ms.</summary>
    public static readonly TimeSpan MinWindow = TimeSpan.FromSeconds(1);

    /// <summary>The longest window of <see cref="AddOnceAsync"/>.</summary>
    public static readonly TimeSpan MaxWindow = TimeSpan.FromDays(1);

    /// <summary>The longest time the log can be kept: ten years.</summary>
    public static readonly TimeSpan MaxKeep = TimeSpan.FromDays(3653);

    private readonly FormBuilderDatabase _database;
    private readonly TimeSpan _budget;

    /// <summary>The repository, with the default budget for its list.</summary>
    public AuditRepository(FormBuilderDatabase database)
        : this(database, FormBuilderQueryBudget.Default)
    {
    }

    /// <summary>The repository with another budget for its list, for the tests of the budget itself.</summary>
    internal AuditRepository(FormBuilderDatabase database, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);
        _database = database;
        _budget = budget;
    }

    /// <inheritdoc />
    public Task<StoredAuditEntry> AddAsync(NewAuditEntry entry, CancellationToken cancellationToken = default)
    {
        CheckEntry(entry);
        return _database.WriteAsync(
            async db =>
            {
                var row = new AuditEntryDto
                {
                    OccurredUtc = _database.UtcNow,
                    UserKey = entry.UserKey,
                    Action = entry.Action,
                    EntityType = entry.EntityType,
                    EntityKey = entry.EntityKey,
                    Details = Json(entry),
                };
                await db.InsertRowAsync(row);

                // Read back, so the time is the one stored: SQL Server rounds it.
                return FromDto((await ReadAsync(db, new SqlNames(db), row.Id))!);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredAuditEntry?> AddOnceAsync(NewAuditEntry entry, TimeSpan window, CancellationToken cancellationToken = default)
    {
        CheckEntry(entry);
        if (entry.EntityKey is null || entry.UserKey is null)
        {
            throw new ArgumentException("An action recorded once per window names its item and its user.", nameof(entry));
        }

        if (window < MinWindow || window > MaxWindow)
        {
            throw new ArgumentOutOfRangeException(nameof(window), window, $"A window is at least {MinWindow.TotalSeconds:0} second and at most {MaxWindow.TotalHours:0} hours.");
        }

        return _database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.AuditEntry);
                var now = _database.UtcNow;
                var since = now - window;
                var added = await db.ExecAsync(
                    $"""
                    INSERT INTO {table} ({n.C("occurredUtc")}, {n.C("userKey")}, {n.C("action")}, {n.C("entityType")}, {n.C("entityKey")}, {n.C("details")})
                    SELECT @0, @1, @2, @3, @4, @5
                    WHERE NOT EXISTS (SELECT 1 FROM {table} a{n.LockingRead} WHERE a.{n.C("entityKey")} = @4 AND a.{n.C("userKey")} = @1 AND a.{n.C("action")} = @2 AND a.{n.C("occurredUtc")} > @6)
                    """,
                    now,
                    entry.UserKey,
                    entry.Action,
                    entry.EntityType,
                    entry.EntityKey,
                    Json(entry),
                    since);
                if (added == 0)
                {
                    return null;
                }

                // The unit holds the check's lock, so the window's one entry is the one just added.
                var row = await db.FirstOrNullAsync<AuditEntryDto>(
                    $"SELECT * FROM {table} WHERE {n.C("entityKey")} = @0 AND {n.C("userKey")} = @1 AND {n.C("action")} = @2 AND {n.C("occurredUtc")} > @3 ORDER BY {n.C("id")} DESC",
                    entry.EntityKey,
                    entry.UserKey,
                    entry.Action,
                    since);
                return row is null ? null : FromDto(row);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredAuditEntry?> GetAsync(long id, CancellationToken cancellationToken = default) =>
        _database.ReadAsync(
            async db =>
            {
                var row = await ReadAsync(db, new SqlNames(db), id);
                return row is null ? null : FromDto(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<Page<StoredAuditEntry>> QueryAsync(AuditQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        CheckQuery(query);
        return _database.QueryAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.AuditEntry);
                var (where, args) = Where(n, query);
                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table}{where}", args);
                var rows = await db.FetchListAsync<AuditEntryDto>(
                    $"SELECT * FROM {table}{where} ORDER BY {n.C("occurredUtc")} DESC, {n.C("id")} DESC {n.Page(page.Skip, page.Take)}",
                    args);
                return new Page<StoredAuditEntry>(rows.Select(FromDto).ToList(), total);
            },
            _budget,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(TimeSpan keep, int take, CancellationToken cancellationToken = default)
    {
        if (keep <= TimeSpan.Zero || keep > MaxKeep)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, $"The log is kept longer than 0 and at most {MaxKeep.TotalDays:0} days.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxPurgeTake);
        return _database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.AuditEntry);
                return db.ExecAsync(
                    $"DELETE FROM {table} WHERE {n.C("occurredUtc")} < @0 AND {n.C("id")} IN (SELECT o.{n.C("id")} FROM {table} o WHERE o.{n.C("occurredUtc")} < @0 ORDER BY o.{n.C("occurredUtc")}, o.{n.C("id")} {n.Page(0, take)})",
                    _database.UtcNow - keep);
            },
            cancellationToken);
    }

    /// <summary>Whether <paramref name="action"/> is <c>entity.verb</c>: two camelCase words joined by a dot, at most 64 characters.</summary>
    internal static bool IsAction(string? action) =>
        action is { Length: <= MaxNameLength } && action.Split('.') is [var entity, var verb] && IsWord(entity) && IsWord(verb);

    /// <summary>Whether <paramref name="entityType"/> is one camelCase word of at most 64 characters.</summary>
    internal static bool IsEntityType(string? entityType) => entityType is { Length: <= MaxNameLength } && IsWord(entityType);

    private static bool IsWord(string word) => word.Length > 0 && char.IsAsciiLetterLower(word[0]) && word.All(char.IsAsciiLetterOrDigit);

    private static Task<AuditEntryDto?> ReadAsync(IUmbracoDatabase db, SqlNames n, long id) =>
        db.FirstOrNullAsync<AuditEntryDto>($"SELECT * FROM {n.T(FormBuilderTables.AuditEntry)} WHERE {n.C("id")} = @0", id);

    private static void CheckEntry(NewAuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Require(IsAction(entry.Action), $"An action is entity.verb: two camelCase words of ASCII letters and digits joined by a dot, at most {MaxNameLength} characters.");
        Require(IsEntityType(entry.EntityType), $"An entity type is one camelCase word of ASCII letters and digits, at most {MaxNameLength} characters.");
        Require(entry.EntityKey != Guid.Empty && entry.UserKey != Guid.Empty, "An entry names its item and user by key, or by null for none.");

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(entry));
            }
        }
    }

    private static void CheckQuery(AuditQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Require(query.Action is null || IsAction(query.Action), "The action to list is entity.verb.");
        Require(query.EntityType is null || IsEntityType(query.EntityType), "The entity type to list is one camelCase word.");
        Require(query.FromUtc is not { } from || FormBuilderDbTime.IsStorable(from), $"The moment to list from lies in the years {Years}.");
        Require(query.BeforeUtc is not { } before || FormBuilderDbTime.IsStorable(before), $"The moment to list up to lies in the years {Years}.");
        Require(query.Entities is null || (query.Entities.Count is > 0 and <= SqlArgs.MaxListLength && query.Entities.All(IsEntityType)), "The kinds of item to list are camelCase words, at least one.");
        Require(
            query.Search is null || (query.Search.Users.Count <= AuditSearch.MaxMatches && query.Search.Items.Count <= AuditSearch.MaxMatches && query.Search.Forms.Count <= AuditSearch.MaxForms),
            $"A search names at most {AuditSearch.MaxMatches} users, {AuditSearch.MaxMatches} items and {AuditSearch.MaxForms} forms.");

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(query));
            }
        }
    }

    private static string Years => $"{FormBuilderDbTime.MinStorable.Year} to {FormBuilderDbTime.MaxStorable.Year}";

    // The query's filters as one WHERE clause, its parameters numbered in order.
    private static (string Where, object?[] Args) Where(SqlNames n, AuditQuery query)
    {
        var parts = new List<string>();
        var args = new SqlArgs();
        if (query.Action is { } action)
        {
            Add("action", "=", action);
        }

        if (query.EntityType is { } entityType)
        {
            Add("entityType", "=", entityType);
        }

        if (query.EntityKey is { } entityKey)
        {
            Add("entityKey", "=", entityKey);
        }

        if (query.UserKey is { } userKey)
        {
            Add("userKey", "=", userKey);
        }

        if (query.FromUtc is { } from)
        {
            Add("occurredUtc", ">=", FormBuilderDbTime.Write(from));
        }

        if (query.BeforeUtc is { } before)
        {
            Add("occurredUtc", "<", FormBuilderDbTime.Write(before));
        }

        // An action's first word is its kind of item; the words hold no wildcard, as IsEntityType checked.
        if (query.Entities is { } entities)
        {
            parts.Add("(" + string.Join(" OR ", entities.Select(entity => $"{n.C("action")} LIKE {args.Add(entity + ".%")}")) + ")");
        }

        if (query.FormKey is { } form)
        {
            parts.Add($"({n.C("entityKey")} = {args.Add(form)} OR {Naming(form)})");
        }

        if (query.Search is { } search)
        {
            var matches = new List<string>();
            if (search.Users.Count > 0)
            {
                matches.Add($"{n.C("userKey")} IN ({args.List(search.Users.Cast<object?>())})");
            }

            if (search.Items.Count > 0)
            {
                matches.Add($"{n.C("entityKey")} IN ({args.List(search.Items.Cast<object?>())})");
            }

            matches.AddRange(search.Forms.Select(Naming));
            parts.Add(matches.Count == 0 ? "1 = 0" : "(" + string.Join(" OR ", matches) + ")");
        }

        return (parts.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", parts), args.Values);

        void Add(string column, string comparison, object value) => parts.Add($"{n.C(column)} {comparison} {args.Add(value)}");

        // The details as AuditDetails writes them name a form as "form":"<key>", which holds no wildcard of LIKE.
        string Naming(Guid form) => $"{n.C("details")} LIKE {args.Add("%\"form\":\"" + form.ToString("D") + "\"%")}";
    }

    private static string Json(NewAuditEntry entry) => entry.Details?.ToJson() ?? "{}";

    private static StoredAuditEntry FromDto(AuditEntryDto d) => new(
        d.Id,
        FormBuilderDbTime.Read(d.OccurredUtc),
        d.UserKey,
        d.Action,
        d.EntityType,
        d.EntityKey,
        d.Details);
}
