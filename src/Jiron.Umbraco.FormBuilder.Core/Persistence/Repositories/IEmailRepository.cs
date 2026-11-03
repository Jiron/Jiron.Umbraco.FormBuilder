namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>What a mail is for (section 4.5).</summary>
internal enum EmailKind
{
    /// <summary>Sent by an email action.</summary>
    Action,

    /// <summary>A link to resume a saved form.</summary>
    Resume,

    /// <summary>The address check of a form with verification.</summary>
    Verification,

    /// <summary>A test mail from the backoffice.</summary>
    Test,

    /// <summary>A scheduled report.</summary>
    Report,

    /// <summary>A digest of entries.</summary>
    Digest,

    /// <summary>An entry forwarded by a user.</summary>
    Forward,

    /// <summary>A notice to the team of a delivery that failed for good.</summary>
    Notice,
}

/// <summary>Where a mail is (ADR-0007).</summary>
internal enum EmailState
{
    /// <summary>Waiting for its next attempt, or for its first one.</summary>
    Queued,

    /// <summary>Claimed: an attempt holds its lease.</summary>
    Sending,

    /// <summary>Handed to the mail server; final.</summary>
    Sent,

    /// <summary>Failed for good; final until a retry from the log.</summary>
    Failed,

    /// <summary>An attempt was cut off or its lease ran out: it may have been sent. Only a retry from the log sends it again.</summary>
    Uncertain,

    /// <summary>Held back by the limits for visitor mail (ADR-0012); final, never sent or retried.</summary>
    Limited,
}

/// <summary>A mail to queue, as it will be sent (ADR-0007): one body, HTML or plain text, with the text always there.</summary>
/// <param name="Kind">What it is for.</param>
/// <param name="To">The recipients, comma-separated, 1 to 4,000 characters.</param>
/// <param name="From">The sender's name and address, never a visitor's (section 4.10); empty for Umbraco's own when it is sent.</param>
/// <param name="Subject">The subject, at most 1,000 characters; line breaks are replaced when it is sent.</param>
/// <param name="TextBody">The text version, always there (ADR-0007).</param>
/// <param name="Culture">The culture it was written in, 1 to 32 characters.</param>
/// <param name="HtmlBody">The HTML body, or null for a plain-text mail.</param>
/// <param name="Cc">The copy recipients, or null.</param>
/// <param name="Bcc">The blind copy recipients, or null.</param>
/// <param name="ReplyTo">The reply-to address, or null.</param>
/// <param name="EntryId">The entry it belongs to, or null.</param>
/// <param name="ActionRunId">The action run that queued it, or null.</param>
/// <param name="Attachments">The keys of the files to attach, or null for none.</param>
/// <param name="RecipientKey">The visitor-typed address, folded for the limits of visitor mail (ADR-0012), or null.</param>
internal sealed record NewEmail(
    EmailKind Kind,
    string To,
    string From,
    string Subject,
    string TextBody,
    string Culture,
    string? HtmlBody = null,
    string? Cc = null,
    string? Bcc = null,
    string? ReplyTo = null,
    long? EntryId = null,
    long? ActionRunId = null,
    IReadOnlyList<Guid>? Attachments = null,
    string? RecipientKey = null);

/// <summary>A stored mail.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Kind">What it is for.</param>
/// <param name="EntryId">The entry it belongs to; null once that entry is deleted.</param>
/// <param name="ActionRunId">The action run that queued it.</param>
/// <param name="To">The recipients.</param>
/// <param name="Cc">The copy recipients.</param>
/// <param name="Bcc">The blind copy recipients.</param>
/// <param name="ReplyTo">The reply-to address.</param>
/// <param name="From">The sender.</param>
/// <param name="Subject">The subject.</param>
/// <param name="HtmlBody">The HTML body, or null for a plain-text mail.</param>
/// <param name="TextBody">The text version.</param>
/// <param name="Attachments">The keys of the files to attach.</param>
/// <param name="Culture">The culture it was written in.</param>
/// <param name="RecipientKey">The folded visitor address.</param>
/// <param name="State">Where it is.</param>
/// <param name="Attempts">The attempts claimed so far.</param>
/// <param name="NextAttemptUtc">When it may be claimed; null once it is final.</param>
/// <param name="LockedUntilUtc">When the lease of its claim ends, while an attempt holds it or it is uncertain.</param>
/// <param name="LockedBy">The token of its last claim, kept in a final state; null while it waits for a claim.</param>
/// <param name="LastError">Why its last attempt did not succeed.</param>
/// <param name="CreatedUtc">When it was queued.</param>
/// <param name="SentUtc">When it was handed to the mail server.</param>
/// <param name="PurgeAfterUtc">When the purge deletes it (section 4.5).</param>
internal sealed record StoredEmail(
    long Id,
    EmailKind Kind,
    long? EntryId,
    long? ActionRunId,
    string To,
    string? Cc,
    string? Bcc,
    string? ReplyTo,
    string From,
    string Subject,
    string? HtmlBody,
    string TextBody,
    IReadOnlyList<Guid> Attachments,
    string Culture,
    string? RecipientKey,
    EmailState State,
    int Attempts,
    DateTimeOffset? NextAttemptUtc,
    DateTimeOffset? LockedUntilUtc,
    string? LockedBy,
    DeliveryError? LastError,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? SentUtc,
    DateTimeOffset? PurgeAfterUtc);

/// <summary>The limits of mail to an address a visitor typed (ADR-0012).</summary>
/// <param name="PerRecipientPerDay">How many one recipient key may get in 24 hours.</param>
/// <param name="PerSitePerHour">How many may leave for such addresses in an hour.</param>
/// <param name="Keep">How long a mail held back is kept.</param>
internal sealed record EmailLimits(int PerRecipientPerDay, int PerSitePerHour, TimeSpan Keep);

/// <summary>
/// The mail outbox in <c>jfbEmail</c> (ADR-0007, ADR-0040): every state change is one conditional statement that names
/// the mail and its state, and for an attempt's outcome the token of its claim. The sender (8.2) owns the policy: the
/// attempts, the back-off, the lease and how long mail is kept.
/// </summary>
internal interface IEmailRepository
{
    /// <summary>
    /// Queues a mail, due at once, in the caller's unit of work when there is one; the mail as stored, or null when the
    /// entry it belongs to does not exist.
    /// </summary>
    Task<StoredEmail?> AddAsync(NewEmail email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a mail to an address a visitor typed, within the limits (ADR-0012): the count and the insert are one statement,
    /// holding a range lock on SQL Server, so parallel requests cannot pass them together. A mail over a limit is stored
    /// <see cref="EmailState.Limited"/>, with the limit as its error, and never leaves. The mail as stored, or null when the
    /// entry it belongs to does not exist. It needs its <see cref="NewEmail.RecipientKey"/>.
    /// </summary>
    Task<StoredEmail?> AddWithinLimitsAsync(NewEmail email, EmailLimits limits, CancellationToken cancellationToken = default);

    /// <summary>The mail with this id, or null.</summary>
    Task<StoredEmail?> GetAsync(long emailId, CancellationToken cancellationToken = default);

    /// <summary>An entry's mail, oldest first.</summary>
    Task<IReadOnlyList<StoredEmail>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>The ids of up to <paramref name="take"/> queued mails that are due, the longest due first.</summary>
    Task<IReadOnlyList<long>> DueAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims a mail for an attempt for <paramref name="claimant"/> (8.2: the server and its worker): one statement that
    /// finds it queued and due, and writes a new token of the claimant's name and 128 random bits, the lease's end and one
    /// more attempt. The mail as claimed, its token in <see cref="StoredEmail.LockedBy"/>, or null when the claim is not
    /// the caller's.
    /// </summary>
    Task<StoredEmail?> ClaimAsync(long emailId, string claimant, TimeSpan lease, CancellationToken cancellationToken = default);

    /// <summary>Records a mail handed to the mail server, kept for <paramref name="keep"/>. False when the token no longer holds it.</summary>
    Task<bool> SentAsync(long emailId, string token, TimeSpan keep, CancellationToken cancellationToken = default);

    /// <summary>Records a failed attempt to be tried again after <paramref name="delay"/>. False when the token no longer holds the mail.</summary>
    Task<bool> RetryAsync(long emailId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives back a claim that sent nothing, to be claimed again after <paramref name="delay"/>, without counting as an attempt.
    /// False when the token no longer holds the mail.
    /// </summary>
    Task<bool> ReleaseAsync(long emailId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default);

    /// <summary>Records a failure for good, kept for <paramref name="keep"/>. False when the token no longer holds the mail.</summary>
    Task<bool> FailAsync(long emailId, string token, DeliveryError error, TimeSpan keep, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an attempt that was cut off and may have been sent: <see cref="EmailState.Uncertain"/>, keeping the token,
    /// kept for <paramref name="keep"/>. False when the token no longer holds the mail.
    /// </summary>
    Task<bool> CutOffAsync(long emailId, string token, DeliveryError error, TimeSpan keep, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns the mail being sent whose lease ran out into <see cref="EmailState.Uncertain"/>, keeping the tokens, each kept
    /// for <paramref name="keep"/>; how many.
    /// </summary>
    Task<int> ExpireLeasesAsync(TimeSpan keep, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a mail from the deliveries log (section 4.4): from the state the user saw, <see cref="EmailState.Failed"/>
    /// or <see cref="EmailState.Uncertain"/>, back to queued and due now, with its attempts at 0, its token and its purge
    /// time cleared. The mail as it is then, or null when it does not exist; throws
    /// <see cref="FormBuilderConcurrencyException"/> when it was no longer in that state.
    /// </summary>
    Task<StoredEmail?> RequeueAsync(long emailId, EmailState expected, CancellationToken cancellationToken = default);

    /// <summary>Deletes up to <paramref name="take"/> mails whose purge time has come, the earliest purge time first; how many.</summary>
    Task<int> PurgeAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entry's mail that was not sent, as the first part of the unit that deletes the entry (section 4.4); how
    /// many. It holds the entry's row first, so no mail is queued for it until that unit ends. Sent mail stays in the
    /// log, and deleting the entry clears its link.
    /// </summary>
    Task<int> DeleteUnsentAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the mail of these entries that was not sent, as the first part of the unit that deletes them (ADR-0047);
    /// how many. It holds the entries' rows first, so no mail is queued for them until that unit ends.
    /// </summary>
    Task<int> DeleteUnsentAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);
}
