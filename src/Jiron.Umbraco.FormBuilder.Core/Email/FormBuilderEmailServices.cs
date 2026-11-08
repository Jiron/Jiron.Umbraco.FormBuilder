using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Mail;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>The registrations of mail (ADR-0007, ADR-0159), which the registrations of the action framework make, whose worker sends it.</summary>
internal static class FormBuilderEmailServices
{
    /// <summary>Registers the renderer, the layouts, the outbox, the test mail, the previews, the queue after the action runs, and the purge.</summary>
    public static IUmbracoBuilder AddFormBuilderEmail(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<EmailRenderer>();
        builder.Services.TryAddSingleton<EmailLayouts>();
        builder.Services.TryAddSingleton<EmailOutbox>();
        builder.Services.TryAddSingleton<EmailAttempts>();
        builder.Services.TryAddSingleton<DeliveryNotices>();
        builder.Services.TryAddSingleton<TestEmails>();
        builder.Services.TryAddSingleton<EmailLayoutLibrary>();
        builder.Services.TryAddSingleton<EmailPreviews>();
        builder.Services.TryAddSingleton<PrivacyTeam>();
        builder.JironFormBuilder().ActionTypes().Append<EmailActionType>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxQueue, EmailQueue>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IFormBuilderRetentionPart, EmailPurge>());
        return builder;
    }
}

/// <summary>Deletes mail whose time has come, in bounded batches (section 4.5): sent, failed, uncertain and limited mail alike.</summary>
/// <param name="emails">The mail.</param>
/// <param name="time">The clock.</param>
internal sealed class EmailPurge(IEmailRepository emails, TimeProvider time) : IFormBuilderRetentionPart
{
    /// <summary>How many mails one batch deletes.</summary>
    public const int Batch = 200;

    /// <inheritdoc />
    public string Name => "mail";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        while (time.GetUtcNow() < deadline && await emails.PurgeAsync(Batch, cancellationToken) == Batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}

/// <summary>What became of a request for a test mail.</summary>
internal enum TestEmailStatus
{
    /// <summary>It is queued.</summary>
    Queued,

    /// <summary>Umbraco could not send it: no mail server or pickup folder is set up.</summary>
    NotConfigured,

    /// <summary>The user has no address to send it to.</summary>
    NoAddress,
}

/// <summary>
/// Test mail from the backoffice (section 4.4, ADR-0159): only ever to the user who asks, so it can reach nobody else, through
/// the queue, so it shows what a real mail would do.
/// </summary>
/// <param name="outbox">The queue.</param>
/// <param name="sender">Umbraco's sender, which says whether mail can leave.</param>
internal sealed class TestEmails(EmailOutbox outbox, IEmailSender sender)
{
    private static readonly FrozenDictionary<string, (string Subject, string Body)> Words = new Dictionary<string, (string Subject, string Body)>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ("Test email from Jiron Form Builder", "<p>This is a test email. Your site can send mail, and this is how it looks.</p>"),
        ["de"] = ("Test-E-Mail von Jiron Form Builder", "<p>Dies ist eine Test-E-Mail. Ihre Website kann E-Mails senden, und so sehen sie aus.</p>"),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Queues a test mail to this address.</summary>
    /// <param name="address">The asking user's address.</param>
    /// <param name="culture">The asking user's language.</param>
    /// <param name="site">The key of the site's root, whose sender and layout it shows; null for the global settings.</param>
    /// <param name="layout">The key of the layout, or null for the site's.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<TestEmailStatus> SendAsync(string? address, string? culture, Guid? site, Guid? layout, CancellationToken cancellationToken)
    {
        if (!EmailAddresses.IsOne(address))
        {
            return TestEmailStatus.NoAddress;
        }

        if (!sender.CanSendRequiredEmail())
        {
            return TestEmailStatus.NotConfigured;
        }

        var language = culture?.Split('-')[0] ?? "en";
        var (subject, body) = Words.TryGetValue(language, out var words) ? words : Words["en"];
        await outbox.QueueAsync(
            new OutgoingEmail
            {
                Kind = EmailKind.Test,
                To = [address!],
                Subject = subject,
                Body = body,
                Culture = Words.ContainsKey(language) ? language : "en",
                Site = site,
                Layout = layout,
            },
            cancellationToken);
        return TestEmailStatus.Queued;
    }
}
