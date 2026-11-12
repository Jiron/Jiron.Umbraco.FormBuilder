using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// A page is read in two statements: one <c>UNION ALL</c> of both tables' ids, filtered in each part and ordered by
/// whether it is a problem and when it was queued, then the page's rows with their entry and form. The union carries ids,
/// a number and a time only, so no column has to agree on a type across the two tables, which SQL Server would otherwise
/// decide for a <c>NULL</c> of one part.
/// </remarks>
internal sealed class DeliveryRepository : IDeliveryRepository
{
    private static readonly string Queued = StoredNames.Of(EmailState.Queued);
    private static readonly string Sending = StoredNames.Of(EmailState.Sending);
    private static readonly string Sent = StoredNames.Of(EmailState.Sent);
    private static readonly string MailFailed = StoredNames.Of(EmailState.Failed);
    private static readonly string MailUncertain = StoredNames.Of(EmailState.Uncertain);
    private static readonly string Limited = StoredNames.Of(EmailState.Limited);
    private static readonly string Pending = StoredNames.Of(ActionRunState.Pending);
    private static readonly string Running = StoredNames.Of(ActionRunState.Running);
    private static readonly string Succeeded = StoredNames.Of(ActionRunState.Succeeded);
    private static readonly string RunFailed = StoredNames.Of(ActionRunState.Failed);
    private static readonly string RunUncertain = StoredNames.Of(ActionRunState.Uncertain);
    private static readonly string Skipped = StoredNames.Of(ActionRunState.Skipped);

    private readonly FormBuilderDatabase _database;
    private readonly TimeSpan _budget;

    /// <summary>The repository, with the default budget for its list.</summary>
    public DeliveryRepository(FormBuilderDatabase database)
        : this(database, FormBuilderQueryBudget.Default)
    {
    }

    /// <summary>The repository with another budget for its list, for the tests of the budget itself.</summary>
    internal DeliveryRepository(FormBuilderDatabase database, TimeSpan budget)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);
        _database = database;
        _budget = budget;
    }

    /// <inheritdoc />
    public Task<Page<DeliveryRow>> QueryAsync(DeliveryQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        Check(query);
        return _database.QueryAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var mail = MailPart(n, a, query);
                var runs = RunPart(n, a, query);
                var parts = new List<string>(2);
                if (mail is not null)
                {
                    parts.Add(mail);
                }

                if (runs is not null)
                {
                    parts.Add(runs);
                }

                if (parts.Count == 0)
                {
                    return new Page<DeliveryRow>([], 0);
                }

                var union = string.Join(" UNION ALL ", parts);
                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM ({union}) d", a.Values);
                var ids = await db.FetchListAsync<DeliveryIdDto>(
                    $"SELECT d.{n.C("isEmail")}, d.{n.C("id")} FROM ({union}) d ORDER BY d.{n.C("problem")} DESC, d.{n.C("createdUtc")} DESC, d.{n.C("isEmail")} DESC, d.{n.C("id")} DESC {n.Page(page.Skip, page.Take)}",
                    a.Values);
                var mails = await MailRowsAsync(db, n, ids.Where(id => id.IsEmail == 1).Select(id => id.Id).ToList());
                var runRows = await RunRowsAsync(db, n, ids.Where(id => id.IsEmail == 0).Select(id => id.Id).ToList());
                var rows = ids
                    .Select(id => id.IsEmail == 1 ? mails.GetValueOrDefault(id.Id) : runRows.GetValueOrDefault(id.Id))
                    .OfType<DeliveryRow>()
                    .ToList();
                return new Page<DeliveryRow>(rows, total);
            },
            _budget,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<DeliveryRow?> GetEmailAsync(long emailId, CancellationToken cancellationToken = default) =>
        _database.ReadAsync(
            async db => (await MailRowsAsync(db, new SqlNames(db), [emailId])).GetValueOrDefault(emailId),
            cancellationToken);

    /// <inheritdoc />
    public Task<DeliveryRow?> GetRunAsync(long runId, CancellationToken cancellationToken = default) =>
        _database.ReadAsync(
            async db => (await RunRowsAsync(db, new SqlNames(db), [runId])).GetValueOrDefault(runId),
            cancellationToken);

    /// <summary>The state the log says of a mail.</summary>
    internal static DeliveryState StateOf(EmailState state, int attempts, DeliveryError? error) => state switch
    {
        EmailState.Queued => attempts > 0 && error is not null ? DeliveryState.TryingAgain : DeliveryState.Waiting,
        EmailState.Sending => DeliveryState.Sending,
        EmailState.Sent => DeliveryState.Delivered,
        EmailState.Failed => DeliveryState.Failed,
        EmailState.Uncertain => DeliveryState.Uncertain,
        _ => DeliveryState.Limited,
    };

    /// <summary>The state the log says of a run.</summary>
    internal static DeliveryState StateOf(ActionRunState state, int attempts, DeliveryError? error) => state switch
    {
        ActionRunState.Pending => attempts > 0 && error is not null ? DeliveryState.TryingAgain : DeliveryState.Waiting,
        ActionRunState.Running => DeliveryState.Sending,
        ActionRunState.Succeeded => DeliveryState.Delivered,
        ActionRunState.Failed => DeliveryState.Failed,
        ActionRunState.Uncertain => DeliveryState.Uncertain,
        _ => DeliveryState.Skipped,
    };

    private static void Check(DeliveryQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.FromUtc is { } from && !FormBuilderDbTime.IsStorable(from))
        {
            throw new ArgumentException($"The moment to list from lies in the years {FormBuilderDbTime.MinStorable.Year} to {FormBuilderDbTime.MaxStorable.Year}.", nameof(query));
        }

        if (query.Search is { Length: > DeliveryQuery.MaxSearchLength })
        {
            throw new ArgumentException($"A search is at most {DeliveryQuery.MaxSearchLength} characters.", nameof(query));
        }
    }

    // A mail is a problem when it failed, is uncertain or limited, or waits for another try after a failed one.
    private static string MailProblem(SqlNames n, string e) =>
        $"({e}.{n.C("state")} IN ('{MailFailed}', '{MailUncertain}', '{Limited}') OR ({e}.{n.C("state")} = '{Queued}' AND {e}.{n.C("attempts")} > 0 AND {e}.{n.C("lastErrorCode")} IS NOT NULL))";

    private static string RunProblem(SqlNames n, string r) =>
        $"({r}.{n.C("state")} IN ('{RunFailed}', '{RunUncertain}') OR ({r}.{n.C("state")} = '{Pending}' AND {r}.{n.C("attempts")} > 0 AND {r}.{n.C("lastErrorCode")} IS NOT NULL))";

    // The mail part of the union, or null when the query leaves out mail.
    private static string? MailPart(SqlNames n, SqlArgs a, DeliveryQuery query)
    {
        if (query.Kind is DeliveryKinds.Webhook or DeliveryKinds.Other || query.States is DeliveryStates.Skipped)
        {
            return null;
        }

        var problem = MailProblem(n, "e");
        var where = new List<string>();
        switch (query.States)
        {
            case DeliveryStates.Problems:
                where.Add(problem);
                break;
            case DeliveryStates.Waiting:
                where.Add($"e.{n.C("state")} IN ('{Queued}', '{Sending}') AND NOT {problem}");
                break;
            case DeliveryStates.Delivered:
                where.Add($"e.{n.C("state")} = '{Sent}'");
                break;
        }

        Common(n, a, query, where, "e", e => query.SearchesVisitors
            ? $"e.{n.C("toAddresses")} LIKE {e} {SqlNames.EscapeClause}"
            : $"(e.{n.C("toAddresses")} LIKE {e} {SqlNames.EscapeClause} AND e.{n.C("recipientKey")} IS NULL)");
        return $"SELECT 1 AS {n.C("isEmail")}, e.{n.C("id")} AS {n.C("id")}, CASE WHEN {problem} THEN 1 ELSE 0 END AS {n.C("problem")}, e.{n.C("createdUtc")} AS {n.C("createdUtc")} "
            + $"FROM {n.T(FormBuilderTables.Email)} e LEFT JOIN {n.T(FormBuilderTables.Entry)} en ON en.{n.C("id")} = e.{n.C("entryId")} LEFT JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = en.{n.C("formId")}"
            + (where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where));
    }

    // The run part of the union: runs that queued no mail, or null when the query leaves out runs.
    private static string? RunPart(SqlNames n, SqlArgs a, DeliveryQuery query)
    {
        var problem = RunProblem(n, "r");
        var where = new List<string>
        {
            $"r.{n.C("id")} NOT IN (SELECT m.{n.C("actionRunId")} FROM {n.T(FormBuilderTables.Email)} m WHERE m.{n.C("actionRunId")} IS NOT NULL)",
        };
        switch (query.Kind)
        {
            case DeliveryKinds.Email:
                where.Add($"r.{n.C("actionType")} = {a.Add(EmailActionType.Alias)}");
                break;
            case DeliveryKinds.Webhook:
                where.Add($"r.{n.C("actionType")} = {a.Add(WebhookActionType.Alias)}");
                break;
            case DeliveryKinds.Other:
                where.Add($"r.{n.C("actionType")} NOT IN ({a.Add(EmailActionType.Alias)}, {a.Add(WebhookActionType.Alias)})");
                break;
        }

        switch (query.States)
        {
            case DeliveryStates.Problems:
                where.Add(problem);
                break;
            case DeliveryStates.Waiting:
                where.Add($"r.{n.C("state")} IN ('{Pending}', '{Running}') AND NOT {problem}");
                break;
            case DeliveryStates.Delivered:
                where.Add($"r.{n.C("state")} = '{Succeeded}'");
                break;
            case DeliveryStates.Skipped:
                where.Add($"r.{n.C("state")} = '{Skipped}'");
                break;
        }

        // A run's target lives in its form's version, so the search finds its form only.
        Common(n, a, query, where, "r", null);
        return $"SELECT 0 AS {n.C("isEmail")}, r.{n.C("id")} AS {n.C("id")}, CASE WHEN {problem} THEN 1 ELSE 0 END AS {n.C("problem")}, r.{n.C("createdUtc")} AS {n.C("createdUtc")} "
            + $"FROM {n.T(FormBuilderTables.ActionRun)} r JOIN {n.T(FormBuilderTables.Entry)} en ON en.{n.C("id")} = r.{n.C("entryId")} JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = en.{n.C("formId")}"
            + " WHERE " + string.Join(" AND ", where);
    }

    // The filters both parts share: the form, the time, and the search over the form's name and what else the part searches.
    private static void Common(SqlNames n, SqlArgs a, DeliveryQuery query, List<string> where, string alias, Func<string, string>? search)
    {
        if (query.FormKey is { } form)
        {
            where.Add($"f.{n.C("key")} = {a.Add(form)}");
        }

        if (query.FromUtc is { } from)
        {
            where.Add($"{alias}.{n.C("createdUtc")} >= {a.Add(FormBuilderDbTime.Write(from))}");
        }

        if (query.Search?.Trim() is { Length: > 0 } text)
        {
            var pattern = a.Add("%" + SqlNames.Escape(text) + "%");
            var byName = $"f.{n.C("name")} LIKE {pattern} {SqlNames.EscapeClause}";
            where.Add(search is null ? byName : $"({search(pattern)} OR {byName})");
        }
    }

    private static async Task<Dictionary<long, DeliveryRow>> MailRowsAsync(IUmbracoDatabase db, SqlNames n, List<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var a = new SqlArgs();
        var rows = await db.FetchListAsync<DeliveryMailDto>(
            $"SELECT e.{n.C("id")}, e.{n.C("kind")}, e.{n.C("toAddresses")}, e.{n.C("recipientKey")}, e.{n.C("actionRunId")}, e.{n.C("state")}, e.{n.C("attempts")}, e.{n.C("nextAttemptUtc")}, "
            + $"e.{n.C("lastErrorCode")}, e.{n.C("lastErrorMessage")}, e.{n.C("createdUtc")}, e.{n.C("sentUtc")}, "
            + $"r.{n.C("actionType")}, r.{n.C("actionKey")}, r.{n.C("formVersionId")}, en.{n.C("key")} AS {n.C("entryKey")}, en.{n.C("number")} AS {n.C("entryNumber")}, "
            + $"f.{n.C("key")} AS {n.C("formKey")}, f.{n.C("name")} AS {n.C("formName")} "
            + $"FROM {n.T(FormBuilderTables.Email)} e LEFT JOIN {n.T(FormBuilderTables.ActionRun)} r ON r.{n.C("id")} = e.{n.C("actionRunId")} "
            + $"LEFT JOIN {n.T(FormBuilderTables.Entry)} en ON en.{n.C("id")} = e.{n.C("entryId")} LEFT JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = en.{n.C("formId")} "
            + $"WHERE e.{n.C("id")} IN ({a.List(ids.Cast<object?>())})",
            a.Values);
        return rows.ToDictionary(row => row.Id, row =>
        {
            var state = StoredNames.Parse<EmailState>(row.State);
            var error = row.LastErrorCode is { } code ? new DeliveryError(code, row.LastErrorMessage) : null;
            return new DeliveryRow(
                true,
                row.Id,
                StateOf(state, row.Attempts, error),
                StoredNames.Parse<EmailKind>(row.Kind),
                row.ToAddresses,
                row.ActionRunId,
                row.ActionType,
                row.ActionKey,
                row.FormVersionId,
                row.EntryKey,
                row.EntryNumber,
                row.FormKey,
                row.FormName,
                row.Attempts,
                FormBuilderDbTime.Read(row.NextAttemptUtc),
                error,
                FormBuilderDbTime.Read(row.CreatedUtc),
                null,
                null,
                FormBuilderDbTime.Read(row.SentUtc),
                row.RecipientKey is not null);
        });
    }

    private static async Task<Dictionary<long, DeliveryRow>> RunRowsAsync(IUmbracoDatabase db, SqlNames n, List<long> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var a = new SqlArgs();
        var rows = await db.FetchListAsync<DeliveryRunDto>(
            $"SELECT r.{n.C("id")}, r.{n.C("actionType")}, r.{n.C("actionKey")}, r.{n.C("formVersionId")}, r.{n.C("state")}, r.{n.C("attempts")}, r.{n.C("nextAttemptUtc")}, "
            + $"r.{n.C("lastErrorCode")}, r.{n.C("lastErrorMessage")}, r.{n.C("createdUtc")}, r.{n.C("startedUtc")}, r.{n.C("lastAttemptUtc")}, r.{n.C("finishedUtc")}, "
            + $"en.{n.C("key")} AS {n.C("entryKey")}, en.{n.C("number")} AS {n.C("entryNumber")}, f.{n.C("key")} AS {n.C("formKey")}, f.{n.C("name")} AS {n.C("formName")} "
            + $"FROM {n.T(FormBuilderTables.ActionRun)} r JOIN {n.T(FormBuilderTables.Entry)} en ON en.{n.C("id")} = r.{n.C("entryId")} JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = en.{n.C("formId")} "
            + $"WHERE r.{n.C("id")} IN ({a.List(ids.Cast<object?>())})",
            a.Values);
        return rows.ToDictionary(row => row.Id, row =>
        {
            var state = StoredNames.Parse<ActionRunState>(row.State);
            var error = row.LastErrorCode is { } code ? new DeliveryError(code, row.LastErrorMessage) : null;
            return new DeliveryRow(
                false,
                row.Id,
                StateOf(state, row.Attempts, error),
                null,
                null,
                null,
                row.ActionType,
                row.ActionKey,
                row.FormVersionId,
                row.EntryKey,
                row.EntryNumber,
                row.FormKey,
                row.FormName,
                row.Attempts,
                FormBuilderDbTime.Read(row.NextAttemptUtc),
                error,
                FormBuilderDbTime.Read(row.CreatedUtc),
                FormBuilderDbTime.Read(row.StartedUtc),
                FormBuilderDbTime.Read(row.LastAttemptUtc),
                FormBuilderDbTime.Read(row.FinishedUtc));
        });
    }
}

/// <summary>A row of the deliveries log's union: which table, and the row's id.</summary>
[ExplicitColumns]
internal sealed class DeliveryIdDto
{
    [Column("isEmail")]
    public int IsEmail { get; set; }

    [Column("id")]
    public long Id { get; set; }
}

/// <summary>A mail as the deliveries log reads it, with its run, entry and form; never its subject or body.</summary>
[ExplicitColumns]
internal sealed class DeliveryMailDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("toAddresses")]
    public string ToAddresses { get; set; } = string.Empty;

    [Column("recipientKey")]
    public string? RecipientKey { get; set; }

    [Column("actionRunId")]
    public long? ActionRunId { get; set; }

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("nextAttemptUtc")]
    public DateTime? NextAttemptUtc { get; set; }

    [Column("lastErrorCode")]
    public string? LastErrorCode { get; set; }

    [Column("lastErrorMessage")]
    public string? LastErrorMessage { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("sentUtc")]
    public DateTime? SentUtc { get; set; }

    [Column("actionType")]
    public string? ActionType { get; set; }

    [Column("actionKey")]
    public Guid? ActionKey { get; set; }

    [Column("formVersionId")]
    public int? FormVersionId { get; set; }

    [Column("entryKey")]
    public Guid? EntryKey { get; set; }

    [Column("entryNumber")]
    public int? EntryNumber { get; set; }

    [Column("formKey")]
    public Guid? FormKey { get; set; }

    [Column("formName")]
    public string? FormName { get; set; }
}

/// <summary>A run as the deliveries log reads it, with its entry and form.</summary>
[ExplicitColumns]
internal sealed class DeliveryRunDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [Column("actionKey")]
    public Guid ActionKey { get; set; }

    [Column("formVersionId")]
    public int FormVersionId { get; set; }

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("nextAttemptUtc")]
    public DateTime? NextAttemptUtc { get; set; }

    [Column("lastErrorCode")]
    public string? LastErrorCode { get; set; }

    [Column("lastErrorMessage")]
    public string? LastErrorMessage { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("startedUtc")]
    public DateTime? StartedUtc { get; set; }

    [Column("lastAttemptUtc")]
    public DateTime? LastAttemptUtc { get; set; }

    [Column("finishedUtc")]
    public DateTime? FinishedUtc { get; set; }

    [Column("entryKey")]
    public Guid EntryKey { get; set; }

    [Column("entryNumber")]
    public int EntryNumber { get; set; }

    [Column("formKey")]
    public Guid FormKey { get; set; }

    [Column("formName")]
    public string FormName { get; set; } = string.Empty;
}
