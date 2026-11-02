using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>A mail to queue: what it says and who gets it; who it comes from and how it looks are the site's.</summary>
internal sealed record OutgoingEmail
{
    /// <summary>What it is for.</summary>
    public required EmailKind Kind { get; init; }

    /// <summary>The recipients; exactly one when <see cref="ToVisitor"/>. An address that is not one is left out.</summary>
    public required IReadOnlyList<string> To { get; init; }

    /// <summary>The subject, which becomes one line; a blank one reads <see cref="EmailOutbox.NoSubject"/>.</summary>
    public required string Subject { get; init; }

    /// <summary>The body: rich text, or plain text when <see cref="PlainText"/>.</summary>
    public required string Body { get; init; }

    /// <summary>The culture it is written in.</summary>
    public required string Culture { get; init; }

    /// <summary>Whether the mail is plain text, as one <c>text/plain</c> body (ADR-0007).</summary>
    public bool PlainText { get; init; }

    /// <summary>Whether the recipient is an address a visitor typed, which the limits count (ADR-0012).</summary>
    public bool ToVisitor { get; init; }

    /// <summary>The copy recipients: fixed addresses, never one a visitor typed, which gets a mail of its own, so the limits count it.</summary>
    public IReadOnlyList<string> Cc { get; init; } = [];

    /// <summary>The blind copy recipients: fixed addresses, never one a visitor typed.</summary>
    public IReadOnlyList<string> Bcc { get; init; } = [];

    /// <summary>The address it comes from instead of the site's, left out when it is not one; never one a visitor typed.</summary>
    public string? From { get; init; }

    /// <summary>The reply-to address, left out when it is not one.</summary>
    public string? ReplyTo { get; init; }

    /// <summary>The key of the site's root, whose sender and layout it takes; null for the global settings.</summary>
    public Guid? Site { get; init; }

    /// <summary>The key of the layout, or null for the site's.</summary>
    public Guid? Layout { get; init; }

    /// <summary>The entry it belongs to.</summary>
    public long? EntryId { get; init; }

    /// <summary>The action run that queued it.</summary>
    public long? ActionRunId { get; init; }

    /// <summary>The keys of the files to attach.</summary>
    public IReadOnlyList<Guid> Attachments { get; init; } = [];
}

/// <summary>
/// Puts mail in the queue (ADR-0007, ADR-0159): it takes the site's sender and layout, renders the one body with its text
/// version, cleans the header values and counts mail to visitors against the limits. Sending is <see cref="EmailQueue"/>'s.
/// </summary>
internal sealed partial class EmailOutbox(
    IEmailRepository emails,
    FormBuilderDatabase database,
    IFormBuilderSettingsService settings,
    EmailLayouts layouts,
    EmailRenderer renderer,
    SiteBaseUrl baseUrls,
    IOptionsMonitor<GlobalSettings> global,
    IOptionsMonitor<FormBuilderOptions> options,
    ILogger<EmailOutbox> logger)
{
    /// <summary>The subject of a mail whose own came out blank, since a mail server needs one to pass it on.</summary>
    public const string NoSubject = "(no subject)";

    /// <summary>How long mail in a final state is kept (section 4.5).</summary>
    public static readonly TimeSpan Keep = TimeSpan.FromDays(30);

    /// <summary>
    /// Queues a mail. The mail as stored; null when it has no recipient that is an address, or the entry it belongs to is gone. A
    /// mail over the limits comes back <see cref="EmailState.Limited"/>. A caller inside a unit of work prepares the mail before
    /// the unit starts, with <see cref="PrepareAsync"/>.
    /// </summary>
    public async Task<StoredEmail?> QueueAsync(OutgoingEmail email, CancellationToken cancellationToken = default) =>
        await PrepareAsync(email, cancellationToken) is { } prepared ? await QueueAsync(prepared, cancellationToken) : null;

    /// <summary>Queues a prepared mail, in the caller's unit of work when there is one, so it leaves only if that unit commits.</summary>
    public Task<StoredEmail?> QueueAsync(NewEmail prepared, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        if (prepared.RecipientKey is null)
        {
            return emails.AddAsync(prepared, cancellationToken);
        }

        var limits = options.CurrentValue.Limits.Mail;
        return emails.AddWithinLimitsAsync(prepared, new EmailLimits(limits.PerRecipientPerDay, limits.PerSitePerHour, Keep), cancellationToken);
    }

    /// <summary>
    /// The mail as it will be stored: the site's sender and layout, the one body with its text version, and clean header values;
    /// null when it has no recipient that is an address. It reads settings and layouts, which on SQLite wait for a unit of work
    /// that is open, so it is refused inside one.
    /// </summary>
    /// <exception cref="InvalidOperationException">A unit of work runs.</exception>
    public async Task<NewEmail?> PrepareAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);
        if (database.InUnitOfWork)
        {
            throw new InvalidOperationException("A mail is prepared before the unit of work that queues it starts: its reads would wait for that unit.");
        }

        if (email.ToVisitor && email.To.Count != 1)
        {
            throw new ArgumentException("A mail to an address a visitor typed goes to that address alone.", nameof(email));
        }

        var to = Addresses(email.To);
        var key = email.ToVisitor && to.Count == 1 ? EmailAddresses.KeyOf(to[0]) : null;
        if (to.Count == 0 || (email.ToVisitor && key is null))
        {
            LogNoRecipient(logger, email.Kind.ToString());
            return null;
        }

        var site = (await settings.GetAsync<EmailSettings>(email.Site, cancellationToken)).Value;
        var subject = Subject(email.Subject);
        var (html, text) = await BodyAsync(email, site, subject, cancellationToken);
        return new NewEmail(
            email.Kind,
            string.Join(", ", to),
            Sender(site, email.From),
            subject,
            text,
            Culture(email.Culture),
            html,
            List(email.Cc),
            List(email.Bcc),
            EmailAddresses.IsOne(email.ReplyTo) ? email.ReplyTo : null,
            email.EntryId,
            email.ActionRunId,
            email.Attachments.Distinct().ToArray(),
            key);
    }

    private static string Subject(string subject)
    {
        var line = EmailHeaders.Clean(subject) ?? string.Empty;
        if (line.Length == 0)
        {
            return NoSubject;
        }

        return line.Length <= EmailRepository.MaxSubjectLength ? line : line[..EmailRepository.MaxSubjectLength];
    }

    private static string Culture(string culture) =>
        !string.IsNullOrWhiteSpace(culture) && culture.Length <= EmailRepository.MaxCultureLength && !culture.Any(char.IsControl) ? culture : "en";

    private static List<string> Addresses(IReadOnlyList<string> addresses) =>
        [.. addresses.Select(address => address?.Trim()).Where(EmailAddresses.IsOne).Select(address => address!).Distinct(StringComparer.OrdinalIgnoreCase)];

    private static string? List(IReadOnlyList<string> addresses) => Addresses(addresses) is { Count: > 0 } list ? string.Join(", ", list) : null;

    // The site's sender, with its name on Umbraco's address when it has no address of its own; empty when there is neither,
    // and Umbraco then says so when it is sent: the health check names it, and the mail waits in the log for a retry.
    private string Sender(EmailSettings site, string? own)
    {
        var address = (EmailAddresses.IsOne(own) ? own : null) ?? site.SenderAddress ?? (EmailAddresses.IsOne(global.CurrentValue.Smtp?.From) ? global.CurrentValue.Smtp!.From : null);
        return address is null ? string.Empty : EmailHeaders.Sender(site.SenderName, address);
    }

    // The mail's one body and its text version, which is never empty.
    private async Task<(string? Html, string Text)> BodyAsync(OutgoingEmail email, EmailSettings site, string subject, CancellationToken cancellationToken)
    {
        if (email.PlainText)
        {
            return (null, email.Body.Length > 0 ? email.Body : subject);
        }

        var layout = await layouts.FindAsync(email.Layout ?? site.DefaultLayout, cancellationToken);
        var rendered = renderer.Render(new EmailPage(subject, email.Body, Culture(email.Culture), layout, site.SenderName, await baseUrls.GetAsync(email.Site, cancellationToken)));
        return (rendered.Html, rendered.Text.Length > 0 ? rendered.Text : subject);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder queued no {Kind} mail: none of its recipients is an address")]
    private static partial void LogNoRecipient(ILogger logger, string kind);
}
