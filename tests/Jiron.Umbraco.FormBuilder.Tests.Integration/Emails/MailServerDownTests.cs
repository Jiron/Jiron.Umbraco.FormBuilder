using System.Net;
using System.Net.Sockets;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>
/// Umbraco's own sender, set up for a mail server on this machine that is not running (ROADMAP 16.4, ADR-0237): what it
/// throws when nobody answers is read as no mail server, so the mail keeps its attempt.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class MailServerDownTests : EmailTestBase
{
    [Test]
    public async Task AMailServerThatIsNotRunning_UsesNoAttempt_AndThisServerWaits()
    {
        var mail = await Outbox.QueueAsync(Mail());

        await Pass.RunAsync(CancellationToken.None);

        var waiting = (await Emails.GetAsync(mail!.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That((waiting.State, waiting.Attempts), Is.EqualTo((EmailState.Queued, 0)));
            Assert.That(waiting.LastError?.Code, Is.EqualTo(EmailAttempts.NoConnection), waiting.LastError?.Message);
            Assert.That(GetRequiredService<MailServerReach>().Paused, Is.True);
            Assert.That(Written(), Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var port = ClosedPort();
        builder.Services.Configure<GlobalSettings>(settings => settings.Smtp = new SmtpSettings
        {
            From = HostSender,
            Host = "127.0.0.1",
            Port = port,
            DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.Network,
        });
    }

    // A port the system just handed out and took back, so nothing listens on it.
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
