namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>Where a delivery is, in the one word the deliveries log says for a mail and a run alike (ADR-0219).</summary>
internal enum DeliveryState
{
    /// <summary>Queued or pending: never tried, or tried without a failure.</summary>
    Waiting,

    /// <summary>An attempt holds it.</summary>
    Sending,

    /// <summary>An attempt failed, and the next one has its time.</summary>
    TryingAgain,

    /// <summary>Sent, or succeeded.</summary>
    Delivered,

    /// <summary>Failed for good.</summary>
    Failed,

    /// <summary>An attempt was cut off: it may have arrived.</summary>
    Uncertain,

    /// <summary>A mail held back by the limits of visitor mail; never sent.</summary>
    Limited,

    /// <summary>A run that never ran.</summary>
    Skipped,
}

/// <summary>Which states the deliveries log lists (ADR-0219).</summary>
internal enum DeliveryStates
{
    /// <summary>Failed, trying again, uncertain or limited.</summary>
    Problems,

    /// <summary>Waiting or sending.</summary>
    Waiting,

    /// <summary>Delivered.</summary>
    Delivered,

    /// <summary>Skipped.</summary>
    Skipped,
}

/// <summary>What the deliveries log lists (ADR-0219).</summary>
internal enum DeliveryKinds
{
    /// <summary>Mail, and the runs of email actions that queued none.</summary>
    Email,

    /// <summary>The runs of webhooks, Slack and Microsoft Teams among them.</summary>
    Webhook,

    /// <summary>The runs of every other action type.</summary>
    Other,
}

/// <summary>Which deliveries to list: every filter that is set must match.</summary>
internal sealed record DeliveryQuery
{
    /// <summary>The longest search.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>Only deliveries in these states.</summary>
    public DeliveryStates? States { get; init; }

    /// <summary>Only deliveries of this kind.</summary>
    public DeliveryKinds? Kind { get; init; }

    /// <summary>Only deliveries of this form's entries.</summary>
    public Guid? FormKey { get; init; }

    /// <summary>Only deliveries queued at or after this moment, which lies in the years 1753 to 9999.</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Only mail whose recipients hold this text, or deliveries of a form whose name holds it.</summary>
    public string? Search { get; init; }
}

/// <summary>One row of the deliveries log: a mail, or a run that queued none (ADR-0219).</summary>
/// <param name="IsEmail">Whether it is a mail; otherwise a run.</param>
/// <param name="Id">The mail's or the run's id.</param>
/// <param name="State">Where it is.</param>
/// <param name="EmailKind">What a mail is for; null for a run.</param>
/// <param name="Recipients">A mail's recipients, comma-separated; null for a run.</param>
/// <param name="ActionRunId">The run that queued a mail; null for a run, or a mail of no action.</param>
/// <param name="ActionType">The action type's alias of the run, or of the run that queued the mail.</param>
/// <param name="ActionKey">The action's key.</param>
/// <param name="FormVersionId">The version whose action ran.</param>
/// <param name="EntryKey">The entry it belongs to; null for a mail of no entry, or one whose entry is gone.</param>
/// <param name="EntryNumber">The entry's number.</param>
/// <param name="FormKey">The entry's form.</param>
/// <param name="FormName">The form's name.</param>
/// <param name="Attempts">The attempts claimed so far.</param>
/// <param name="NextAttemptUtc">When it may be tried next; null once it is final.</param>
/// <param name="Error">Why its last attempt did not succeed, or which limit held a mail back.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="StartedUtc">When a run's first attempt started, since a retry from the log.</param>
/// <param name="LastAttemptUtc">When a run's latest attempt started.</param>
/// <param name="FinishedUtc">When it reached a final state: a mail sent, or a run finished.</param>
internal sealed record DeliveryRow(
    bool IsEmail,
    long Id,
    DeliveryState State,
    EmailKind? EmailKind,
    string? Recipients,
    long? ActionRunId,
    string? ActionType,
    Guid? ActionKey,
    int? FormVersionId,
    Guid? EntryKey,
    long? EntryNumber,
    Guid? FormKey,
    string? FormName,
    int Attempts,
    DateTimeOffset? NextAttemptUtc,
    DeliveryError? Error,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? FinishedUtc);

/// <summary>
/// The deliveries log over <c>jfbEmail</c> and <c>jfbActionRun</c> (section 4.4, ADR-0219): every mail, and every run that
/// queued none, problems first, then the newest. It only reads; a retry goes through the outbox repositories.
/// </summary>
internal interface IDeliveryRepository
{
    /// <summary>
    /// One page of the deliveries that match, problems first (failed, trying again, uncertain, limited), each group the newest
    /// first, with the number that match; under the read budget of lists (ADR-0003).
    /// </summary>
    Task<Page<DeliveryRow>> QueryAsync(DeliveryQuery query, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>A mail as the log shows it, or null when it does not exist.</summary>
    Task<DeliveryRow?> GetEmailAsync(long emailId, CancellationToken cancellationToken = default);

    /// <summary>A run as the log shows it, or null when it does not exist; a run that queued mail is found too.</summary>
    Task<DeliveryRow?> GetRunAsync(long runId, CancellationToken cancellationToken = default);
}
