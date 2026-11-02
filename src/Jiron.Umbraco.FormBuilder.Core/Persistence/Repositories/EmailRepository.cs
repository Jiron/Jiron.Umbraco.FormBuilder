using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// A mail's one error column holds <c>code: message</c>, cut to its length; the code always fits (ADR-0040). What is queued
/// wakes this server's worker once it is committed (ADR-0007).
/// </remarks>
internal sealed class EmailRepository(FormBuilderDatabase database) : IEmailRepository
{
    /// <summary>The longest list of recipients a column holds.</summary>
    public const int MaxAddressesLength = 4000;

    /// <summary>The longest sender or reply-to address the columns hold.</summary>
    public const int MaxAddressLength = 500;

    /// <summary>The longest subject the column holds.</summary>
    public const int MaxSubjectLength = 1000;

    /// <summary>The longest culture the column holds.</summary>
    public const int MaxCultureLength = 32;

    /// <summary>The longest recipient key the column holds.</summary>
    public const int MaxRecipientKeyLength = 320;

    /// <summary>The longest error message the column holds; longer ones are cut.</summary>
    public const int MaxErrorMessageLength = 1000;

    /// <summary>The error of a mail held back by the limit of its recipient.</summary>
    public const string RecipientLimit = "recipient_limit";

    /// <summary>The error of a mail held back by the limit of the site.</summary>
    public const string SiteLimit = "site_limit";

    private static readonly string Queued = StoredNames.Of(EmailState.Queued);
    private static readonly string Sending = StoredNames.Of(EmailState.Sending);
    private static readonly string Sent = StoredNames.Of(EmailState.Sent);
    private static readonly string Failed = StoredNames.Of(EmailState.Failed);
    private static readonly string Uncertain = StoredNames.Of(EmailState.Uncertain);
    private static readonly string Limited = StoredNames.Of(EmailState.Limited);

    /// <inheritdoc />
    public Task<StoredEmail?> AddAsync(NewEmail email, CancellationToken cancellationToken = default)
    {
        CheckEmail(email);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                if (email.EntryId is { } entryId && !await RowHolds.HoldEntryAsync(db, n, entryId))
                {
                    return null;
                }

                var now = database.UtcNow;
                var row = new EmailDto
                {
                    Kind = StoredNames.Of(email.Kind),
                    EntryId = email.EntryId,
                    ActionRunId = email.ActionRunId,
                    ToAddresses = email.To,
                    CcAddresses = email.Cc,
                    BccAddresses = email.Bcc,
                    ReplyToAddress = email.ReplyTo,
                    FromAddress = email.From,
                    Subject = email.Subject,
                    HtmlBody = email.HtmlBody,
                    TextBody = email.TextBody,
                    Attachments = AttachmentsText(email.Attachments ?? []),
                    Culture = email.Culture,
                    RecipientKey = email.RecipientKey,
                    State = Queued,
                    NextAttemptUtc = now,
                    CreatedUtc = now,
                };
                await db.InsertRowAsync(row);
                database.PublishAfterCommit(new OutboxQueuedNotification());
                return await FindAsync(db, n, row.Id);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEmail?> AddWithinLimitsAsync(NewEmail email, EmailLimits limits, CancellationToken cancellationToken = default)
    {
        CheckEmail(email);
        ArgumentNullException.ThrowIfNull(limits);
        Outbox.CheckKeep(limits.Keep);
        if (email.RecipientKey is null)
        {
            throw new ArgumentException("A mail to an address a visitor typed has its recipient key.", nameof(email));
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                if (email.EntryId is { } entryId && !await RowHolds.HoldEntryAsync(db, n, entryId))
                {
                    return null;
                }

                var now = database.UtcNow;
                var emails = n.T(FormBuilderTables.Email);

                // The columns that have a value: a null parameter in a select list has no type of its own on SQL Server.
                (string Column, object? Value)[] values =
                [
                    ("kind", StoredNames.Of(email.Kind)),
                    ("entryId", email.EntryId),
                    ("actionRunId", email.ActionRunId),
                    ("toAddresses", email.To),
                    ("ccAddresses", email.Cc),
                    ("bccAddresses", email.Bcc),
                    ("replyToAddress", email.ReplyTo),
                    ("fromAddress", email.From),
                    ("subject", email.Subject),
                    ("htmlBody", email.HtmlBody),
                    ("textBody", email.TextBody),
                    ("attachments", AttachmentsText(email.Attachments ?? [])),
                    ("culture", email.Culture),
                    ("recipientKey", email.RecipientKey),
                    ("createdUtc", now),
                ];
                var given = values.Where(value => value.Value is not null).ToArray();
                object?[] args =
                [
                    email.RecipientKey,
                    now.AddDays(-1),
                    now.AddHours(-1),
                    limits.PerRecipientPerDay,
                    limits.PerSitePerHour,
                    now,
                    RecipientLimit,
                    SiteLimit,
                    now + limits.Keep,
                    .. given.Select(value => value.Value),
                ];
                const int FirstValue = 9;
                var columns = string.Join(", ", given.Select(value => n.C(value.Column)));
                var parameters = SqlArgs.Placeholders(FirstValue, given.Length);
                var over = $"(mine.{n.C("sent")} >= @3 OR site.{n.C("sent")} >= @4)";
                await db.ExecAsync(
                    $"INSERT INTO {emails} ({columns}, {n.C("state")}, {n.C("nextAttemptUtc")}, {n.C("lastErrorCode")}, {n.C("purgeAfterUtc")}, {n.C("attempts")}) "
                    + $"SELECT {parameters}, CASE WHEN {over} THEN '{Limited}' ELSE '{Queued}' END, CASE WHEN {over} THEN NULL ELSE @5 END, "
                    + $"CASE WHEN mine.{n.C("sent")} >= @3 THEN @6 WHEN site.{n.C("sent")} >= @4 THEN @7 END, CASE WHEN {over} THEN @8 END, 0 "
                    + $"FROM (SELECT COUNT(*) AS {n.C("sent")} FROM {emails}{n.LockingRead} WHERE {n.C("recipientKey")} = @0 AND {n.C("createdUtc")} > @1 AND {n.C("state")} <> '{Limited}') mine "
                    + $"CROSS JOIN (SELECT COUNT(*) AS {n.C("sent")} FROM {emails}{n.LockingRead} WHERE {n.C("recipientKey")} IS NOT NULL AND {n.C("createdUtc")} > @2 AND {n.C("state")} <> '{Limited}') site",
                    args);

                // The range stays held until the unit ends, so the newest mail of this key is the one just added.
                var row = await db.FirstOrNullAsync<EmailDto>(
                    $"SELECT * FROM {emails} WHERE {n.C("recipientKey")} = @0 ORDER BY {n.C("id")} DESC {n.Page(0, 1)}",
                    email.RecipientKey);
                var stored = row is null ? null : FromDto(row);
                if (stored?.State == EmailState.Queued)
                {
                    database.PublishAfterCommit(new OutboxQueuedNotification());
                }

                return stored;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEmail?> GetAsync(long emailId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, new SqlNames(db), emailId), cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmail>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<StoredEmail>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<EmailDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("entryId")} = @0 ORDER BY {n.C("createdUtc")}, {n.C("id")}",
                    entryId);
                return [.. rows.Select(FromDto)];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> DueAsync(int take, CancellationToken cancellationToken = default)
    {
        Outbox.CheckTake(take);
        return database.ReadAsync<IReadOnlyList<long>>(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.FetchListAsync<long>(
                    $"SELECT {n.C("id")} FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("state")} = '{Queued}' AND {n.C("nextAttemptUtc")} <= @0 ORDER BY {n.C("nextAttemptUtc")}, {n.C("id")} {n.Page(0, take)}",
                    database.UtcNow);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEmail?> ClaimAsync(long emailId, string claimant, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        Outbox.CheckClaimant(claimant);
        Outbox.CheckLease(lease);
        var token = Outbox.NewToken(claimant);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var now = database.UtcNow;
                var claimed = await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Email)} SET {n.C("state")} = '{Sending}', {n.C("lockedBy")} = @1, {n.C("lockedUntilUtc")} = @2, {n.C("attempts")} = {n.C("attempts")} + 1 WHERE {n.C("id")} = @0 AND {n.C("state")} = '{Queued}' AND {n.C("nextAttemptUtc")} <= @3",
                    emailId,
                    token,
                    now + lease,
                    now);
                return claimed == 1 ? await FindAsync(db, n, emailId) : null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> SentAsync(long emailId, string token, TimeSpan keep, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckKeep(keep);
        return OutcomeAsync(
            emailId,
            token,
            (n, now) => ($"{n.C("state")} = '{Sent}', {n.C("sentUtc")} = @2, {n.C("purgeAfterUtc")} = @3, {n.C("nextAttemptUtc")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = NULL, {n.C("lastErrorMessage")} = NULL", [now, now + keep]),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> RetryAsync(long emailId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckDelay(delay);
        Outbox.CheckError(error);
        return OutcomeAsync(
            emailId,
            token,
            (n, now) => ($"{n.C("state")} = '{Queued}', {n.C("nextAttemptUtc")} = @2, {n.C("lockedBy")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = @3, {n.C("lastErrorMessage")} = @4, {n.C("purgeAfterUtc")} = NULL", [now + delay, error.Code, Message(error)]),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ReleaseAsync(long emailId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckDelay(delay);
        Outbox.CheckError(error);
        return OutcomeAsync(
            emailId,
            token,
            (n, now) => ($"{n.C("state")} = '{Queued}', {n.C("attempts")} = {n.C("attempts")} - 1, {n.C("nextAttemptUtc")} = @2, {n.C("lockedBy")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = @3, {n.C("lastErrorMessage")} = @4, {n.C("purgeAfterUtc")} = NULL", [now + delay, error.Code, Message(error)]),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> FailAsync(long emailId, string token, DeliveryError error, TimeSpan keep, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckError(error);
        Outbox.CheckKeep(keep);
        return OutcomeAsync(
            emailId,
            token,
            (n, now) => ($"{n.C("state")} = '{Failed}', {n.C("nextAttemptUtc")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("lastErrorCode")} = @2, {n.C("lastErrorMessage")} = @3, {n.C("purgeAfterUtc")} = @4", [error.Code, Message(error), now + keep]),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> CutOffAsync(long emailId, string token, DeliveryError error, TimeSpan keep, CancellationToken cancellationToken = default)
    {
        Outbox.CheckToken(token);
        Outbox.CheckError(error);
        Outbox.CheckKeep(keep);
        return OutcomeAsync(
            emailId,
            token,
            (n, now) => ($"{n.C("state")} = '{Uncertain}', {n.C("lastErrorCode")} = @2, {n.C("lastErrorMessage")} = @3, {n.C("purgeAfterUtc")} = @4", [error.Code, Message(error), now + keep]),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ExpireLeasesAsync(TimeSpan keep, CancellationToken cancellationToken = default)
    {
        Outbox.CheckKeep(keep);
        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var now = database.UtcNow;
                return db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Email)} SET {n.C("state")} = '{Uncertain}', {n.C("lastErrorCode")} = @1, {n.C("lastErrorMessage")} = NULL, {n.C("purgeAfterUtc")} = @2 WHERE {n.C("state")} = '{Sending}' AND {n.C("lockedUntilUtc")} < @0",
                    now,
                    DeliveryError.LeaseExpired,
                    now + keep);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEmail?> RequeueAsync(long emailId, EmailState expected, CancellationToken cancellationToken = default)
    {
        if (expected is not (EmailState.Failed or EmailState.Uncertain))
        {
            throw new ArgumentOutOfRangeException(nameof(expected), StoredNames.Describe(expected), "Only a failed or an uncertain mail is retried from the log.");
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var changed = await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Email)} SET {n.C("state")} = '{Queued}', {n.C("attempts")} = 0, {n.C("nextAttemptUtc")} = @2, {n.C("lockedBy")} = NULL, {n.C("lockedUntilUtc")} = NULL, {n.C("purgeAfterUtc")} = NULL WHERE {n.C("id")} = @0 AND {n.C("state")} = @1",
                    emailId,
                    StoredNames.Of(expected),
                    database.UtcNow);
                var current = await FindAsync(db, n, emailId);
                if (changed == 0 && current is not null)
                {
                    throw new FormBuilderConcurrencyException($"The mail was changed meanwhile (it was no longer {StoredNames.Describe(expected)}).");
                }

                if (changed == 1)
                {
                    database.PublishAfterCommit(new OutboxQueuedNotification());
                }

                return current;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> PurgeAsync(int take, CancellationToken cancellationToken = default)
    {
        Outbox.CheckTake(take);
        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var emails = n.T(FormBuilderTables.Email);
                // The delete names the purge time again for the row itself: under read-committed snapshot the subquery may
                // read an older version of a mail that a retry has just put back.
                return db.ExecAsync(
                    $"DELETE FROM {emails} WHERE {n.C("purgeAfterUtc")} <= @0 AND {n.C("id")} IN (SELECT o.{n.C("id")} FROM {emails} o WHERE o.{n.C("purgeAfterUtc")} <= @0 ORDER BY o.{n.C("purgeAfterUtc")}, o.{n.C("id")} {n.Page(0, take)})",
                    database.UtcNow);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteUnsentAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                // Held first, so no mail can be queued for the entry until the unit that deletes it ends.
                var n = new SqlNames(db);
                return await RowHolds.HoldEntryAsync(db, n, entryId)
                    ? await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("entryId")} = @0 AND {n.C("state")} <> '{Sent}'", entryId)
                    : 0;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> DeleteUnsentAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        var ids = entryIds.Distinct().Order().ToArray();
        if (ids.Length == 0)
        {
            return Task.FromResult(0);
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var deleted = 0;
                foreach (var chunk in ids.Chunk(SqlArgs.MaxListLength))
                {
                    // Held first, so no mail can be queued for the entries until the unit that deletes them ends.
                    object?[] args = [.. chunk.Cast<object?>()];
                    var list = SqlArgs.Placeholders(0, chunk.Length);
                    await RowHolds.HoldAsync(db, n, FormBuilderTables.Entry, $"{n.C("id")} IN ({list})", args);
                    deleted += await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("entryId")} IN ({list}) AND {n.C("state")} <> '{Sent}'", args);
                }

                return deleted;
            },
            cancellationToken);
    }

    internal static StoredEmail FromDto(EmailDto d) => new(
        d.Id,
        StoredNames.Parse<EmailKind>(d.Kind),
        d.EntryId,
        d.ActionRunId,
        d.ToAddresses,
        d.CcAddresses,
        d.BccAddresses,
        d.ReplyToAddress,
        d.FromAddress,
        d.Subject,
        d.HtmlBody,
        d.TextBody,
        Attachments(d.Attachments),
        d.Culture,
        d.RecipientKey,
        StoredNames.Parse<EmailState>(d.State),
        d.Attempts,
        FormBuilderDbTime.Read(d.NextAttemptUtc),
        FormBuilderDbTime.Read(d.LockedUntilUtc),
        d.LockedBy,
        d.LastErrorCode is { } code ? new DeliveryError(code, d.LastErrorMessage) : null,
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.SentUtc),
        FormBuilderDbTime.Read(d.PurgeAfterUtc));

    // The message cut to its column; the code is checked and short already.
    private static string? Message(DeliveryError error) => Outbox.Cut(error.Message, MaxErrorMessageLength);

    /// <summary>The attachments as the column holds them: a JSON array of file keys.</summary>
    internal static string AttachmentsText(IReadOnlyList<Guid> keys) =>
        "[" + string.Join(",", keys.Select(key => "\"" + key.ToString("D") + "\"")) + "]";

    private static Guid[] Attachments(string text)
    {
        using var json = JsonDocument.Parse(text);
        return [.. json.RootElement.EnumerateArray().Select(key => key.GetGuid())];
    }

    private static void CheckEmail(NewEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);
        Require(Enum.IsDefined(email.Kind), "A mail's kind is one of section 4.5.");
        Require(Plain(email.To, MaxAddressesLength), $"A mail's recipients are 1 to {MaxAddressesLength} characters without control characters.");
        Require(email.Cc is null || Plain(email.Cc, MaxAddressesLength), $"A mail's copy recipients are 1 to {MaxAddressesLength} characters without control characters.");
        Require(email.Bcc is null || Plain(email.Bcc, MaxAddressesLength), $"A mail's blind copy recipients are 1 to {MaxAddressesLength} characters without control characters.");
        Require(email.ReplyTo is null || Plain(email.ReplyTo, MaxAddressLength), $"A mail's reply-to address is 1 to {MaxAddressLength} characters without control characters.");
        Require(email.From is { Length: 0 } || Plain(email.From, MaxAddressLength), $"A mail's sender is empty, for Umbraco's own when it is sent, or at most {MaxAddressLength} characters without control characters.");
        Require(email.Subject is not null && email.Subject.Length <= MaxSubjectLength, $"A mail's subject holds at most {MaxSubjectLength} characters.");
        Require(email.TextBody is { Length: > 0 }, "A mail's text version is not empty (ADR-0007).");
        Require(email.HtmlBody is null || email.HtmlBody.Length > 0, "A mail's HTML body is null or not empty.");
        Require(Plain(email.Culture, MaxCultureLength), $"A mail's culture is 1 to {MaxCultureLength} characters without control characters.");
        Require(email.RecipientKey is null || Plain(email.RecipientKey, MaxRecipientKeyLength), $"A mail's recipient key is 1 to {MaxRecipientKeyLength} characters without control characters.");
        Require(email.EntryId is null or > 0 && (email.ActionRunId is null or > 0), "A mail's entry and action run ids are 1 or more.");
        Require(email.Attachments is null || (!email.Attachments.Contains(Guid.Empty) && email.Attachments.Distinct().Count() == email.Attachments.Count), "A mail's attachments are distinct file keys.");

        static bool Plain(string? text, int length) => text is not null && text.Length > 0 && text.Length <= length && !text.Any(char.IsControl);

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(email));
            }
        }
    }

    // An attempt's outcome: one statement for a mail the token still holds, sending or uncertain (the late finisher).
    private Task<bool> OutcomeAsync(long emailId, string token, Func<SqlNames, DateTime, (string Set, object?[] Args)> outcome, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var (set, args) = outcome(n, database.UtcNow);
                return await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Email)} SET {set} WHERE {n.C("id")} = @0 AND {n.C("lockedBy")} = @1 AND {n.C("state")} IN ('{Sending}', '{Uncertain}')",
                    [emailId, token, .. args]) == 1;
            },
            cancellationToken);

    private static async Task<StoredEmail?> FindAsync(IUmbracoDatabase db, SqlNames n, long emailId)
    {
        var row = await db.FirstOrNullAsync<EmailDto>($"SELECT * FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("id")} = @0", emailId);
        return row is null ? null : FromDto(row);
    }
}
