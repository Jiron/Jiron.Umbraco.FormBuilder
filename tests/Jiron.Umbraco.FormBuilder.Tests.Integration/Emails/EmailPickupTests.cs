using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>Mail through Umbraco's own sender to the pickup folder (ROADMAP 8.2, ADR-0007, ADR-0159).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EmailPickupTests : EmailTestBase
{
    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    private MemoryFileStore Files => GetRequiredService<MemoryFileStore>();

    [Test]
    public async Task AnHtmlMail_IsOneHtmlPart_WithItsTextVersionStored()
    {
        var stored = await Outbox.QueueAsync(Mail());

        await SendDueAsync();

        var written = Written().Single();
        Assert.Multiple(async () =>
        {
            Assert.That(written.Body, Is.TypeOf<TextPart>(), "one part, no multipart/alternative (ADR-0007)");
            Assert.That(((TextPart)written.Body!).ContentType.MimeType, Is.EqualTo("text/html"));
            Assert.That(((TextPart)written.Body!).Text, Does.Contain("Thank you."));
            Assert.That(stored!.TextBody, Is.EqualTo("Thank you."));
            Assert.That((await Emails.GetAsync(stored.Id))!.State, Is.EqualTo(EmailState.Sent));
            Assert.That((await Emails.GetAsync(stored.Id))!.PurgeAfterUtc, Is.EqualTo(Clock.GetUtcNow().AddDays(30)).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public async Task APlainTextMail_IsOnePlainPart()
    {
        var stored = await Outbox.QueueAsync(Mail() with { PlainText = true, Body = "Just <words>." });

        await SendDueAsync();

        var written = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(written.Body, Is.TypeOf<TextPart>());
            Assert.That(((TextPart)written.Body!).ContentType.MimeType, Is.EqualTo("text/plain"));
            Assert.That(((TextPart)written.Body!).Text.Trim(), Is.EqualTo("Just <words>."));
            Assert.That(stored!.HtmlBody, Is.Null);
            Assert.That(stored.TextBody, Is.EqualTo("Just <words>."));
        });
    }

    [Test]
    public async Task HeaderValuesWithLineBreaks_ArriveWithSpaces_AndAddNoHeaders()
    {
        var current = await Settings.GetAsync<EmailSettings>();
        await Settings.SaveAsync(current.Value with { SenderName = "The\r\nTeam", SenderAddress = "team@jfb.test" }, current.Version, Editor);

        await Outbox.QueueAsync(Mail(subject: "Line one\r\nBcc: ben@jfb.test\nLine three") with { ReplyTo = "reply@jfb.test\r\nBcc: ben@jfb.test" });
        await SendDueAsync();

        var written = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(written.Subject, Is.EqualTo("Line one  Bcc: ben@jfb.test Line three"));
            Assert.That(written.Bcc, Is.Empty);
            Assert.That(written.ReplyTo, Is.Empty, "a reply-to that is not one address is left out");
            Assert.That(written.From.Mailboxes.Single().Name, Is.EqualTo("The  Team"));
            Assert.That(written.From.Mailboxes.Single().Address, Is.EqualTo("team@jfb.test"));
        });
    }

    [Test]
    public async Task ASubjectStoredWithALineBreak_LeavesWithASpace()
    {
        await Emails.AddAsync(new NewEmail(EmailKind.Test, "anna@jfb.test", HostSender, "First\nsecond", "Text", "en"));

        await SendDueAsync();

        Assert.That(Written().Single().Subject, Is.EqualTo("First second"));
    }

    [Test]
    public async Task ABlankSubject_ReadsAsNoSubject_AndABlankPlainBodyAsItsSubject()
    {
        var plain = await Outbox.QueueAsync(Mail(subject: " \r\n ") with { PlainText = true, Body = string.Empty });

        await SendDueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Written().Single().Subject, Is.EqualTo(EmailOutbox.NoSubject));
            Assert.That(plain!.TextBody, Is.EqualTo(EmailOutbox.NoSubject));
        });
    }

    [Test]
    public async Task ALongSubject_IsCutToWhatTheColumnHolds()
    {
        var stored = await Outbox.QueueAsync(Mail(subject: new string('s', EmailRepository.MaxSubjectLength + 5)));

        Assert.That(stored!.Subject, Has.Length.EqualTo(EmailRepository.MaxSubjectLength));
    }

    [Test]
    public async Task TheSender_IsTheSitesOwn_AndOtherwiseUmbracos()
    {
        var site = Guid.NewGuid();
        var global = await Settings.GetAsync<EmailSettings>();
        await Settings.SaveAsync(global.Value with { SenderName = "Everyone" }, global.Version, Editor);
        var own = await Settings.GetAsync<EmailSettings>(site);
        await Settings.SaveSiteAsync(site, own.Value with { SenderName = "A \"quoted\", name", SenderAddress = "site-a@jfb.test" }, ["senderName", "senderAddress"], own.Version, Editor);

        await Outbox.QueueAsync(Mail(subject: "From the site") with { Site = site });
        await Outbox.QueueAsync(Mail(subject: "From the host"));
        await SendDueAsync();

        var written = Written();
        var fromSite = written.Single(mail => mail.Subject == "From the site").From.Mailboxes.Single();
        var fromHost = written.Single(mail => mail.Subject == "From the host").From.Mailboxes.Single();
        Assert.Multiple(() =>
        {
            Assert.That((fromSite.Name, fromSite.Address), Is.EqualTo(("A \"quoted\", name", "site-a@jfb.test")));
            Assert.That((fromHost.Name, fromHost.Address), Is.EqualTo(("Everyone", HostSender)));
        });
    }

    [Test]
    public async Task EveryRecipient_GetsTheMail_AndNotWhatIsNoAddress()
    {
        var mail = Mail() with
        {
            To = ["anna@jfb.test", "ANNA@jfb.test", "nope", "ben@jfb.test"],
            Cc = ["copy@jfb.test", "nope"],
            Bcc = ["hidden@jfb.test"],
            ReplyTo = "reply@jfb.test",
        };

        await Outbox.QueueAsync(mail);
        await SendDueAsync();

        var written = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(written.To.Mailboxes.Select(box => box.Address), Is.EqualTo(new[] { "anna@jfb.test", "ben@jfb.test" }));
            Assert.That(written.Cc.Mailboxes.Select(box => box.Address), Is.EqualTo(new[] { "copy@jfb.test" }));
            Assert.That(written.ReplyTo.Mailboxes.Select(box => box.Address), Is.EqualTo(new[] { "reply@jfb.test" }));
        });
    }

    [Test]
    public async Task AMailWithNoAddress_IsNotQueued()
    {
        Assert.That(await Outbox.QueueAsync(Mail(to: "nope")), Is.Null);

        Assert.That(await Emails.DueAsync(10), Is.Empty);
    }

    [Test]
    public async Task AVisitorsAddress_ThatQuotesOtherAddresses_ReachesNobody()
    {
        Assert.That(await Outbox.QueueAsync(Mail("\"x,victim@jfb.test,y\"@jfb.test", toVisitor: true)), Is.Null);

        await SendDueAsync();

        Assert.That(Written(), Is.Empty);
    }

    [Test]
    public async Task AVisitorsAddress_WhoseKeyCannotBeStored_IsNotSentUncounted()
    {
        // One character whose compatibility form is 18 letters: the key outgrows its column though the address fits.
        var address = new string((char)0xfdfa, 200) + "@jfb.test";

        Assert.That(await Outbox.QueueAsync(Mail(address, toVisitor: true)), Is.Null);
    }

    [Test]
    public async Task AMailToAVisitor_GoesToOneAddress()
    {
        var two = Mail(toVisitor: true) with { To = ["a@jfb.test", "b@jfb.test"] };

        Assert.ThrowsAsync<ArgumentException>(() => Outbox.QueueAsync(two));

        var one = await Outbox.QueueAsync(Mail("Anna+x@jfb.test", toVisitor: true));
        var team = await Outbox.QueueAsync(Mail("Anna+x@jfb.test"));
        Assert.Multiple(() =>
        {
            Assert.That(one!.RecipientKey, Is.EqualTo("anna@jfb.test"));
            Assert.That(team!.RecipientKey, Is.Null, "mail to the team is not counted");
        });
    }

    [Test]
    public async Task NoMailLeaves_WhileSendingIsPaused_AndAllOfItOnceItIsNot()
    {
        Pause(true);
        for (var i = 0; i < 3; i++)
        {
            await Outbox.QueueAsync(Mail(subject: $"Mail {i}"));
        }

        await Pass.RunAsync(CancellationToken.None);
        var queue = GetRequiredService<IEnumerable<Core.Actions.IOutboxQueue>>().OfType<EmailQueue>().Single();
        await queue.PassAsync("server", Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);
        var waiting = await Emails.DueAsync(10);
        var attempts = new List<int>();
        foreach (var id in waiting)
        {
            attempts.Add((await Emails.GetAsync(id))!.Attempts);
        }

        Assert.Multiple(() =>
        {
            Assert.That(Written(), Is.Empty);
            Assert.That(waiting, Has.Count.EqualTo(3));
            Assert.That(attempts, Is.All.EqualTo(0), "a pause uses no attempt");
        });

        Pause(false);
        await SendDueAsync();

        Assert.That(Written().Select(mail => mail.Subject), Is.EquivalentTo(new[] { "Mail 0", "Mail 1", "Mail 2" }));
    }

    [Test]
    public async Task APass_TakesWhatItMay_AndLeavesTheRestForTheNext()
    {
        for (var i = 0; i < EmailQueue.PerPass + 3; i++)
        {
            await Outbox.QueueAsync(Mail(subject: $"Mail {i}"));
        }

        var queue = GetRequiredService<IEnumerable<Core.Actions.IOutboxQueue>>().OfType<EmailQueue>().Single();
        var left = await queue.PassAsync("server", Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.True);
            Assert.That(Written(), Has.Count.EqualTo(EmailQueue.PerPass));
        });

        Assert.That(await queue.PassAsync("server", Clock.GetUtcNow().AddMinutes(1), CancellationToken.None), Is.False);
        Assert.That(Written(), Has.Count.EqualTo(EmailQueue.PerPass + 3));
    }

    [Test]
    public async Task APassPastItsDeadline_ClaimsNothing()
    {
        await Outbox.QueueAsync(Mail());
        var queue = GetRequiredService<IEnumerable<Core.Actions.IOutboxQueue>>().OfType<EmailQueue>().Single();

        var left = await queue.PassAsync("server", Clock.GetUtcNow(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(left, Is.True);
            Assert.That(Written(), Is.Empty);
        });
    }

    [Test]
    public async Task AttachmentsGoAlong_WithinTheLimit_AndWithoutWhatIsHeldBack()
    {
        MailLimits.AttachmentBytes = 24;
        var fine = await FileAsync("fine.pdf", 12, FileScanStates.Clean);
        var tooLarge = await FileAsync("large.pdf", 7, FileScanStates.Clean);
        var fits = await FileAsync("fits\r\n.pdf", 6, FileScanStates.Clean);
        var quarantined = await FileAsync("bad.pdf", 1, FileScanStates.Quarantined);
        var gone = Guid.NewGuid();

        await Outbox.QueueAsync(Mail() with { Attachments = [fine, tooLarge, fits, quarantined, gone, fine] });
        await SendDueAsync();

        var written = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(written.Attachments.OfType<MimePart>().Select(part => part.FileName), Is.EqualTo(new[] { "fine.pdf", "fits  .pdf" }));
            Assert.That(written.Body, Is.TypeOf<Multipart>());
            Assert.That(((Multipart)written.Body!).ContentType.MediaSubtype, Is.EqualTo("mixed"));
        });
    }

    [Test]
    public async Task AFileThatCannotBeRead_UsesNoAttempt_AndTheMailWaits()
    {
        var file = await FileAsync("fine.pdf", 3, FileScanStates.Clean);
        var mail = await Outbox.QueueAsync(Mail() with { Attachments = [file] });
        Files.FailReads = true;

        for (var outage = 0; outage < 7; outage++)
        {
            await Pass.RunAsync(CancellationToken.None);
            Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var waiting = (await Emails.GetAsync(mail!.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(Written(), Is.Empty);
            Assert.That((waiting.State, waiting.Attempts), Is.EqualTo((EmailState.Queued, 0)));
            Assert.That(waiting.LockedBy, Is.Null);
            Assert.That(waiting.LastError, Is.EqualTo(new DeliveryError(Core.Actions.ActionAttempts.Threw, typeof(IOException).FullName)));
        });

        Files.FailReads = false;
        await SendDueAsync();

        Assert.That(Written().Single().Attachments.Count(), Is.EqualTo(1));
    }

    [Test]
    public async Task AFileWithoutItsBytes_IsLeftOut_AndTheMailGoes()
    {
        var file = await FileAsync("gone.pdf", 3, FileScanStates.Clean);
        await Files.DeleteAsync((await Entries.GetFileAsync(file))!.StoredName);

        await Outbox.QueueAsync(Mail() with { Attachments = [file] });
        await SendDueAsync();

        Assert.That(Written().Single().Attachments, Is.Empty);
    }

    [Test]
    public async Task AnUnscannedFile_GoesAlong_OnASiteWithoutAScanner()
    {
        var file = await FileAsync("waits.pdf", 3, FileScanStates.Pending);

        await Outbox.QueueAsync(Mail() with { Attachments = [file] });
        await SendDueAsync();

        Assert.That(Written().Single().Attachments.Count(), Is.EqualTo(1), "nothing is left to wait for (ADR-0144)");
    }

    [Test]
    public async Task ALayoutThatIsGone_OrCannotBeRead_GivesTheCard()
    {
        var layouts = GetRequiredService<IEmailLayoutRepository>();
        var broken = await layouts.CreateAsync(new NewEmailLayout(Guid.NewGuid(), "broken", "Broken", """{"look":"nonsense"}"""));
        var letter = await layouts.CreateAsync(new NewEmailLayout(Guid.NewGuid(), "mine", "Mine", """{"look":"letter","footer":{"":"<p>Our own footer</p>"}}"""));

        var gone = await Outbox.QueueAsync(Mail() with { Layout = Guid.NewGuid() });
        var unread = await Outbox.QueueAsync(Mail() with { Layout = broken.Key });
        var builtIn = await Outbox.QueueAsync(Mail() with { Layout = EmailLayouts.LetterKey });
        var mine = await Outbox.QueueAsync(Mail() with { Layout = letter.Key });

        Assert.Multiple(() =>
        {
            Assert.That(gone!.HtmlBody, Does.Contain("border:1px solid"));
            Assert.That(unread!.HtmlBody, Does.Contain("border:1px solid"));
            Assert.That(builtIn!.HtmlBody, Does.Not.Contain("border:1px solid"));
            Assert.That(mine!.HtmlBody, Does.Contain("Our own footer").And.Not.Contain("border:1px solid"));
            Assert.That(mine.TextBody, Does.EndWith("Our own footer"));
        });
    }

    [Test]
    public async Task TheSitesDefaultLayout_IsWhatAMailUses_UnlessItNamesOne()
    {
        var current = await Settings.GetAsync<EmailSettings>();
        await Settings.SaveAsync(current.Value with { DefaultLayout = EmailLayouts.LetterKey }, current.Version, Editor);

        var site = await Outbox.QueueAsync(Mail());
        var named = await Outbox.QueueAsync(Mail() with { Layout = EmailLayouts.CardKey });

        Assert.Multiple(() =>
        {
            Assert.That(site!.HtmlBody, Does.Not.Contain("border:1px solid"));
            Assert.That(named!.HtmlBody, Does.Contain("border:1px solid"));
        });
    }

    [Test]
    public async Task ALinkInAMail_StartsWithTheSitesAddress()
    {
        var current = await Settings.GetAsync<GeneralSettings>();
        await Settings.SaveAsync(current.Value with { BaseUrl = "https://forms.jfb.test" }, current.Version, Editor);

        var stored = await Outbox.QueueAsync(Mail() with { Body = "<p><a href=\"/resume\">Go on</a></p>" });

        Assert.That(stored!.HtmlBody, Does.Contain("href=\"https://forms.jfb.test/resume\""));
    }

    [Test]
    public async Task ATestMail_GoesToWhoAsks_ThroughTheQueue()
    {
        var tests = GetRequiredService<TestEmails>();

        var german = await tests.SendAsync("ada@jfb.test", "de-DE", null, EmailLayouts.LetterKey, CancellationToken.None);
        var other = await tests.SendAsync("ben@jfb.test", "fr", null, null, CancellationToken.None);
        var none = await tests.SendAsync(null, "en", null, null, CancellationToken.None);
        await SendDueAsync();

        var written = Written();
        Assert.Multiple(() =>
        {
            Assert.That((german, other, none), Is.EqualTo((TestEmailStatus.Queued, TestEmailStatus.Queued, TestEmailStatus.NoAddress)));
            Assert.That(written, Has.Count.EqualTo(2));
            Assert.That(written.Single(mail => mail.To.Mailboxes.Single().Address == "ada@jfb.test").Subject, Is.EqualTo("Test-E-Mail von Jiron Form Builder"));
            Assert.That(written.Single(mail => mail.To.Mailboxes.Single().Address == "ben@jfb.test").Subject, Is.EqualTo("Test email from Jiron Form Builder"));
        });
    }

    [Test]
    public async Task TheSettings_MeanTheSameAfterAReload()
    {
        var current = await Settings.GetAsync<EmailSettings>();
        var saved = current.Value with { SenderName = "Team", SenderAddress = "team@jfb.test", DefaultLayout = EmailLayouts.LetterKey, TeamRecipients = ["a@jfb.test"], ForwardAddresses = ["b@jfb.test"] };

        await Settings.SaveAsync(saved, current.Version, Editor);

        var read = (await Settings.GetAsync<EmailSettings>()).Value;
        Assert.Multiple(() =>
        {
            Assert.That((read.SenderName, read.SenderAddress, read.DefaultLayout), Is.EqualTo(("Team", "team@jfb.test", (Guid?)EmailLayouts.LetterKey)));
            Assert.That(read.TeamRecipients, Is.EqualTo(new[] { "a@jfb.test" }));
            Assert.That(read.ForwardAddresses, Is.EqualTo(new[] { "b@jfb.test" }));
        });
    }

    [Test]
    public async Task BadSettings_AreRefused()
    {
        var current = await Settings.GetAsync<EmailSettings>();

        var refused = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => Settings.SaveAsync(current.Value with { SenderAddress = "Anna <anna@jfb.test>" }, current.Version, Editor));

        Assert.That(refused!.Errors.Single().Field, Is.EqualTo("email.senderAddress"));
    }

    [Test]
    public async Task ThePurge_DeletesMailWhoseTimeHasCome()
    {
        var sent = await Outbox.QueueAsync(Mail());
        var waiting = await Outbox.QueueAsync(Mail(subject: "Held"));
        var token = (await Emails.ClaimAsync(sent!.Id, "server", EmailQueue.Lease))!.LockedBy!;
        await Emails.SentAsync(sent.Id, token, EmailOutbox.Keep);
        var part = GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().OfType<EmailPurge>().Single();

        Clock.Advance(EmailOutbox.Keep - TimeSpan.FromMinutes(1));
        await part.RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);
        Assert.That(await Emails.GetAsync(sent.Id), Is.Not.Null, "not yet");

        Clock.Advance(TimeSpan.FromMinutes(2));
        await part.RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);
        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.GetAsync(sent.Id), Is.Null);
            Assert.That(await Emails.GetAsync(waiting!.Id), Is.Not.Null, "what has not left stays");
        });
    }

    [Test]
    public async Task ThePurge_GoesOnInBatches_UntilItsDeadline()
    {
        var part = GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().OfType<EmailPurge>().Single();
        var limits = new EmailLimits(0, 0, TimeSpan.FromDays(1));
        for (var i = 0; i < EmailPurge.Batch + 2; i++)
        {
            await Emails.AddWithinLimitsAsync(new NewEmail(EmailKind.Action, "a@jfb.test", HostSender, "S", "T", "en", RecipientKey: "a@jfb.test"), limits);
        }

        Clock.Advance(TimeSpan.FromDays(2));
        await part.RunAsync(Clock.GetUtcNow(), CancellationToken.None);
        var ids = await CountLimitedAsync();
        Assert.That(ids, Is.EqualTo(EmailPurge.Batch + 2), "past its deadline it deletes nothing");

        await part.RunAsync(Clock.GetUtcNow().AddMinutes(1), CancellationToken.None);
        Assert.That(await CountLimitedAsync(), Is.Zero);
    }

    [Test]
    public async Task AMailHeldBackByTheLimits_KeepsNoLinkCode_WhileOneThatWaitsToBeSentDoes()
    {
        var limits = new EmailLimits(1, 200, TimeSpan.FromDays(1));
        NewEmail Confirm(string code) => new(
            EmailKind.Verification,
            "ada@jfb.test",
            HostSender,
            "Confirm",
            $"Confirm: https://site.test/jfb/api/v1/verify?t={code}",
            "en",
            HtmlBody: $"<a href=\"https://site.test/jfb/api/v1/verify?t={code}\">Confirm</a>",
            RecipientKey: "ada@jfb.test");

        var first = (await Emails.AddWithinLimitsAsync(Confirm("FIRSTCODE123"), limits))!;
        var held = (await Emails.AddWithinLimitsAsync(Confirm("SECONDCODE456"), limits))!;
        var stored = (await Emails.GetAsync(held.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That((first.State, held.State), Is.EqualTo((EmailState.Queued, EmailState.Limited)));
            Assert.That(first.TextBody, Does.Contain("FIRSTCODE123"), "a mail that waits is sent with its code");
            Assert.That(stored.HtmlBody + stored.TextBody, Does.Not.Contain("SECONDCODE456"), "nobody who reads the database can use a held-back code");
            Assert.That(stored.TextBody, Does.Contain($"verify?t={LinkSecrets.Removed}"), "the link still names its page");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
    }

    private async Task<int> CountLimitedAsync()
    {
        var database = GetRequiredService<Core.Persistence.FormBuilderDatabase>();
        return await database.ReadAsync(db => db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM jfbEmail"), CancellationToken.None);
    }

    private async Task<Guid> FileAsync(string name, int bytes, string scanState)
    {
        var form = await FormRows.GetAsync(await PublishAsync(name: Guid.NewGuid().ToString("N")));
        var id = FormBuilderFileId.New();
        await Files.SaveAsync(id, new MemoryStream(new byte[bytes]));
        var key = Guid.NewGuid();
        await Entries.AddFileAsync(new NewEntryFile(key, form!.Id, Guid.NewGuid(), name, id, "application/pdf", bytes, new string('a', 64), scanState));
        return key;
    }
}
