using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MimeKit;
using Umbraco.Cms.Core.Configuration.Models;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>
/// A host for the tests of mail (ROADMAP 8.2, ADR-0159): sending not paused, a hand-moved clock, each pass run by the test, and
/// Umbraco's own sender writing to a pickup folder of the test's.
/// </summary>
public abstract class EmailTestBase : ActionTestBase
{
    /// <summary>Umbraco's sender address on this host.</summary>
    private protected const string HostSender = "site@jfb.test";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "jfb-mail-" + Guid.NewGuid().ToString("N"));

    private protected IEmailRepository Emails => GetRequiredService<IEmailRepository>();

    private protected EmailOutbox Outbox => GetRequiredService<EmailOutbox>();

    /// <summary>The limits of mail, as a change of the configuration would set them.</summary>
    private protected FormBuilderMailLimitsOptions MailLimits => GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Limits.Mail;

    /// <summary>A rich-text mail to these addresses.</summary>
    private protected static OutgoingEmail Mail(string to = "anna@jfb.test", string subject = "Your answers", bool toVisitor = false) => new()
    {
        Kind = EmailKind.Action,
        To = [to],
        Subject = subject,
        Body = "<p>Thank you.</p>",
        Culture = "en",
        ToVisitor = toVisitor,
    };

    /// <summary>Every mail the sender wrote, read as a mail client would.</summary>
    private protected List<MimeMessage> Written()
    {
        if (!Directory.Exists(_folder))
        {
            return [];
        }

        return [.. Directory.GetFiles(_folder, "*.eml").Order(StringComparer.Ordinal).Select(file => MimeMessage.Load(file))];
    }

    /// <summary>Runs passes until none leaves anything that is due now.</summary>
    private protected async Task SendDueAsync()
    {
        for (var pass = 0; pass < 20; pass++)
        {
            await Pass.RunAsync(CancellationToken.None);
            if ((await Emails.DueAsync(1)).Count == 0)
            {
                return;
            }
        }
    }

    [TearDown]
    public void RemoveTheFolder()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        Directory.CreateDirectory(_folder);
        builder.Services.Configure<GlobalSettings>(settings => settings.Smtp = new SmtpSettings
        {
            From = HostSender,
            DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = _folder,
        });
    }
}
