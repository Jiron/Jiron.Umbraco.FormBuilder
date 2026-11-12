using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// The mail of a pass (ADR-0007, ADR-0159): it marks expired leases, then claims and sends up to <see cref="PerPass"/> due mails,
/// <see cref="AtATime"/> at a time. While Umbraco could not send, or this server waits after the mail server could not be
/// reached (<see cref="MailServerReach"/>), it claims nothing, so no attempt is used and nothing is dropped; the job and the
/// next wake look again.
/// </summary>
internal sealed partial class EmailQueue(IEmailRepository emails, IEmailSender sender, EmailAttempts attempts, MailServerReach reach, OutboxWake wake, TimeProvider time, ILogger<EmailQueue> logger) : IOutboxQueue
{
    /// <summary>How many due mails a pass takes.</summary>
    public const int PerPass = 25;

    /// <summary>How many mails are sent at a time on a server.</summary>
    public const int AtATime = 4;

    /// <summary>How long a claim holds a mail.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public string Name => "mail";

    /// <inheritdoc />
    public async Task<bool> PassAsync(string claimant, DateTimeOffset deadline, CancellationToken stopping)
    {
        await emails.ExpireLeasesAsync(EmailOutbox.Keep, stopping);
        if (!sender.CanSendRequiredEmail() || reach.Paused)
        {
            return false;
        }

        var due = await emails.DueAsync(PerPass, stopping);
        await Parallel.ForEachAsync(
            due,
            new ParallelOptions { MaxDegreeOfParallelism = AtATime },
            async (id, _) =>
            {
                if (time.GetUtcNow() >= deadline || wake.Paused || reach.Paused || stopping.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    if (await emails.ClaimAsync(id, claimant, Lease, stopping) is { } claimed)
                    {
                        await attempts.SendAsync(claimed, stopping);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
                {
                    // Its lease runs out if it was claimed; the other mail goes on.
                    LogAttemptFailed(logger, id, LoggedException.Of(exception));
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    // The host stops, and a claim that did not come through leaves the mail as it was.
                }
            });
        return !stopping.IsCancellationRequested && (await emails.DueAsync(1, stopping)).Count > 0;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not send mail {Email}; it stays queued, or becomes uncertain when it was claimed")]
    private static partial void LogAttemptFailed(ILogger logger, long email, LoggedException exception);
}

/// <summary>
/// One attempt of a claimed mail (ADR-0007): through <see cref="IEmailSender"/>, so a site's own sender and its handlers see
/// every mail. An error goes back to the queue with the back-off of action runs and fails for good after as many attempts; an
/// attempt that was cut off, by its time limit or by the host stopping, becomes uncertain, because it may have left, and its
/// result replaces that when it does end (the late finisher). An attempt that found no mail server sent nothing: the mail goes
/// back with its attempt, and this server waits (<see cref="MailServerReach"/>, ADR-0237).
/// </summary>
internal sealed partial class EmailAttempts(
    IEmailRepository emails,
    IEmailSender sender,
    IEntryRepository entries,
    IFormBuilderFileStore files,
    IEnumerable<IFormBuilderFileScanner> scanners,
    ActionSettlement settlement,
    DeliveryNotices notices,
    MailServerReach reach,
    IOptionsMonitor<FormBuilderOptions> options,
    TimeProvider time,
    ILogger<EmailAttempts> logger)
{
    /// <summary>What Umbraco's sender and a site's handlers know this mail by.</summary>
    public const string EmailType = "JironFormBuilder";

    /// <summary>The code of an attempt that found no mail server; its message is the exception's type.</summary>
    public const string NoConnection = "no_connection";

    /// <summary>How long Umbraco's sender gets (ADR-0007).</summary>
    public static readonly TimeSpan Expires = TimeSpan.FromSeconds(45);

    private readonly bool _scanned = scanners.Any();

    /// <summary>Sends one claimed mail, and records what came of it.</summary>
    /// <param name="mail">The mail as claimed.</param>
    /// <param name="stopping">Cancelled when the host stops.</param>
    public async Task SendAsync(StoredEmail mail, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(mail);
        EmailMessage message;
        List<Stream> opened = [];
        try
        {
            message = await MessageAsync(mail, opened, stopping);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Nothing left, so the next attempt may go without a question, and failing to read uses no attempt.
            await CloseAsync(opened);
            await ReleaseAsync(mail, exception is OperationCanceledException ? new DeliveryError(ActionAttempts.HostStopped) : new DeliveryError(ActionAttempts.Threw, exception.GetType().FullName), ActionAttempts.BackOff[0]);
            return;
        }

        var attempt = Task.Run(() => sender.SendAsync(message, EmailType, enableNotification: true, Expires), CancellationToken.None);
        _ = attempt.ContinueWith(_ => CloseAsync(opened), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        try
        {
            await attempt.WaitAsync(Expires + ActionAttempts.Grace, time, stopping);
            reach.Reached();
            await RecordAsync(mail, Outcome.Sent);
        }
        catch (Exception exception) when (exception is TimeoutException || (exception is OperationCanceledException && !attempt.IsFaulted))
        {
            // It may have left: only a person decides whether it goes out again.
            await RecordAsync(mail, Outcome.CutOff(stopping.IsCancellationRequested ? ActionAttempts.HostStopped : ActionAttempts.TimedOut));

            // Also when it has ended by now: its result may have come while the cut-off was written.
            _ = FinishLateAsync(mail, attempt);
        }
        catch (Exception exception) when (MailServerReach.Refused(exception))
        {
            // No mail server answered, so nothing left: the mail keeps its attempt, and this server waits before it tries again.
            var pause = reach.Refusal();
            LogUnreached(logger, mail.Id, exception.GetType().FullName, pause);
            await ReleaseAsync(mail, new DeliveryError(NoConnection, exception.GetType().FullName), pause);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Its message may hold an address, so only its type is kept (D13).
            LogSendFailed(logger, mail.Id, exception.GetType().FullName);
            await RecordAsync(mail, Outcome.TryAgain(exception.GetType().FullName!));
        }
    }

    private async Task<EmailMessage> MessageAsync(StoredEmail mail, List<Stream> opened, CancellationToken stopping)
    {
        var attachments = new List<EmailMessageAttachment>();
        var left = options.CurrentValue.Limits.Mail.AttachmentBytes;
        foreach (var key in mail.Attachments)
        {
            if (await entries.GetFileAsync(key, stopping) is not { } file || IsHeldBack(file))
            {
                LogLeftOut(logger, key, mail.Id, "it is gone, quarantined or not scanned yet");
                continue;
            }

            var travels = EncodedBytes(file.SizeBytes);
            if (travels > left)
            {
                LogLeftOut(logger, key, mail.Id, "it does not fit the attachment limit");
                continue;
            }

            if (await files.OpenReadAsync(file.StoredName, stopping) is not { } content)
            {
                LogLeftOut(logger, key, mail.Id, "its bytes are gone");
                continue;
            }

            opened.Add(content);
            left -= travels;
            attachments.Add(new EmailMessageAttachment(content, EmailHeaders.Clean(Path.GetFileName(file.OriginalName)) is { Length: > 0 } name ? name : key.ToString("N")));
        }

        return new EmailMessage(
            mail.From.Length == 0 ? null : mail.From,
            Split(mail.To),
            mail.Cc is null ? null : Split(mail.Cc),
            mail.Bcc is null ? null : Split(mail.Bcc),
            mail.ReplyTo is null ? null : [mail.ReplyTo],
            EmailHeaders.Clean(mail.Subject),
            mail.HtmlBody ?? mail.TextBody,
            mail.HtmlBody is not null,
            attachments.Count > 0 ? attachments : null);
    }

    // What a file comes to in the mail: attachments travel as base64, and it is that a mail server's limit counts.
    private static long EncodedBytes(long bytes) => (bytes + 2) / 3 * 4;

    // A site that took its scanner away has nothing left to wait for (ADR-0144).
    private bool IsHeldBack(StoredEntryFile file) => file.ScanState switch
    {
        FileScanStates.Quarantined => true,
        FileScanStates.Pending => _scanned,
        _ => false,
    };

    private static string[] Split(string addresses) => addresses.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private async Task CloseAsync(List<Stream> opened)
    {
        foreach (var stream in opened)
        {
            try
            {
                await stream.DisposeAsync();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogCloseFailed(logger, LoggedException.Of(exception));
            }
        }
    }

    private async Task FinishLateAsync(StoredEmail mail, Task attempt)
    {
        try
        {
            await attempt;
            await RecordAsync(mail, Outcome.Sent);
        }
        catch (Exception exception) when (exception is OperationCanceledException or TimeoutException)
        {
            // It stays uncertain: nobody knows how far it got.
            LogLateFailed(logger, mail.Id, exception.GetType().FullName);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogLateFailed(logger, mail.Id, exception.GetType().FullName);
            await RecordAsync(mail, Outcome.TryAgain(exception.GetType().FullName!));
        }
    }

    // Records the outcome through the claim's token, whatever the host does by now. Only the holder of the claim can change
    // the mail, so a late finisher of an attempt that was retried meanwhile records nothing.
    private async Task RecordAsync(StoredEmail mail, Outcome outcome)
    {
        var token = mail.LockedBy ?? throw new ArgumentException("A claimed mail has its token.", nameof(mail));
        var settle = false;
        try
        {
            if (outcome.Kind == OutcomeKind.Sent)
            {
                if (await emails.SentAsync(mail.Id, token, EmailOutbox.Keep, CancellationToken.None) && LinkSecrets.Holds(mail.Kind))
                {
                    // Its copy keeps no code that would confirm an address or open a draft (ADR-0226).
                    await emails.ReplaceSentBodiesAsync(mail.Id, mail.HtmlBody is { } html ? LinkSecrets.Mask(html) : null, LinkSecrets.Mask(mail.TextBody), CancellationToken.None);
                }

                settle = true;
            }
            else if (outcome.Kind == OutcomeKind.CutOff)
            {
                await emails.CutOffAsync(mail.Id, token, new DeliveryError(outcome.Code!, null), EmailOutbox.Keep, CancellationToken.None);
            }
            else if (mail.Attempts >= ActionAttempts.MaxAttempts)
            {
                await emails.FailAsync(mail.Id, token, new DeliveryError(ActionAttempts.Threw, outcome.Code), EmailOutbox.Keep, CancellationToken.None);
                await notices.TellOfMailAsync(mail with { LastError = new DeliveryError(ActionAttempts.Threw, outcome.Code) });
                settle = true;
            }
            else
            {
                var back = ActionAttempts.BackOff[Math.Clamp(mail.Attempts, 1, ActionAttempts.BackOff.Length) - 1];
                await emails.RetryAsync(mail.Id, token, back, new DeliveryError(ActionAttempts.Threw, outcome.Code), CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The lease runs out, and the mail becomes uncertain: nothing sends it twice.
            LogRecordFailed(logger, mail.Id, LoggedException.Of(exception));
        }

        if (settle && mail.EntryId is { } entry)
        {
            await settlement.AfterMailAsync(entry);
        }
    }

    // Back to the queue with its attempt: nothing left.
    private async Task ReleaseAsync(StoredEmail mail, DeliveryError error, TimeSpan delay)
    {
        var token = mail.LockedBy ?? throw new ArgumentException("A claimed mail has its token.", nameof(mail));
        try
        {
            await emails.ReleaseAsync(mail.Id, token, delay, error, CancellationToken.None);
        }
        catch (Exception recording) when (recording is not OutOfMemoryException)
        {
            LogRecordFailed(logger, mail.Id, LoggedException.Of(recording));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder left file {File} out of mail {Email}: {Reason}")]
    private static partial void LogLeftOut(ILogger logger, Guid file, long email, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not send mail {Email}: {Exception}")]
    private static partial void LogSendFailed(ILogger logger, long email, string? exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not reach the mail server for mail {Email} ({Exception}); it stays queued with its attempt, and this server waits {Pause} before it sends again")]
    private static partial void LogUnreached(ILogger logger, long email, string? exception, TimeSpan pause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mail {Email} of Jiron Form Builder ended after it was cut off: {Exception}")]
    private static partial void LogLateFailed(ILogger logger, long email, string? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not record what came of mail {Email}; its lease runs out and it becomes uncertain")]
    private static partial void LogRecordFailed(ILogger logger, long email, LoggedException exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not close a mail's attachment")]
    private static partial void LogCloseFailed(ILogger logger, LoggedException exception);

    private enum OutcomeKind
    {
        Sent,
        TryAgain,
        CutOff,
    }

    private readonly record struct Outcome(OutcomeKind Kind, string? Code)
    {
        public static readonly Outcome Sent = new(OutcomeKind.Sent, null);

        public static Outcome TryAgain(string code) => new(OutcomeKind.TryAgain, code);

        public static Outcome CutOff(string code) => new(OutcomeKind.CutOff, code);
    }
}
