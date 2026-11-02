using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>The limits of mail to an address a visitor typed (ROADMAP 8.2, section 4.10, ADR-0012), on SQLite and SQL Server.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EmailLimitsTests : EmailTestBase
{
    [Test]
    public async Task EverySpellingOfAMailbox_CountsAsOne()
    {
        string[] spellings = ["Anna@jfb.test", "anna+news@jfb.test", "ANNA@JFB.TEST"];
        foreach (var spelling in spellings)
        {
            Assert.That((await Outbox.QueueAsync(Mail(spelling, toVisitor: true)))!.State, Is.EqualTo(EmailState.Queued), spelling);
        }

        var fourth = await Outbox.QueueAsync(Mail("anna+other@jfb.test", toVisitor: true));

        Assert.Multiple(() =>
        {
            Assert.That(fourth!.State, Is.EqualTo(EmailState.Limited));
            Assert.That(fourth.RecipientKey, Is.EqualTo("anna@jfb.test"));
            Assert.That(fourth.LastError!.Code, Is.EqualTo(EmailRepository.RecipientLimit));
            Assert.That(fourth.NextAttemptUtc, Is.Null);
            Assert.That(fourth.PurgeAfterUtc, Is.EqualTo(Clock.GetUtcNow() + EmailOutbox.Keep).Within(TimeSpan.FromSeconds(1)));
        });
    }

    [Test]
    public async Task TwoSpellings_ThatDifferOnlyInNonAsciiCapitals_CountAsOne()
    {
        MailLimits.PerRecipientPerDay = 1;

        var first = await Outbox.QueueAsync(Mail("\u00c4NNE@jfb.test", toVisitor: true));
        var second = await Outbox.QueueAsync(Mail("\u00e4nne@jfb.test", toVisitor: true));
        var other = await Outbox.QueueAsync(Mail("\u00f6zge@jfb.test", toVisitor: true));

        Assert.Multiple(() =>
        {
            Assert.That(first!.State, Is.EqualTo(EmailState.Queued));
            Assert.That(second!.State, Is.EqualTo(EmailState.Limited));
            Assert.That(second.RecipientKey, Is.EqualTo(first.RecipientKey));
            Assert.That(other!.State, Is.EqualTo(EmailState.Queued), "another mailbox has its own count");
        });
    }

    [Test]
    public async Task TheRecipientsDay_Passes_AndWhatWasHeldBackDoesNotCount()
    {
        MailLimits.PerRecipientPerDay = 1;
        await Outbox.QueueAsync(Mail(toVisitor: true));
        Clock.Advance(TimeSpan.FromHours(23));
        var within = await Outbox.QueueAsync(Mail(toVisitor: true));

        Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        var after = await Outbox.QueueAsync(Mail(toVisitor: true));

        Assert.Multiple(() =>
        {
            Assert.That(within!.State, Is.EqualTo(EmailState.Limited));
            Assert.That(after!.State, Is.EqualTo(EmailState.Queued), "a held-back mail does not keep the limit closed");
        });
    }

    [Test]
    public async Task TheSitesHour_LimitsAllVisitorMail_AndMailToTheTeamIsNotCounted()
    {
        MailLimits.PerSitePerHour = 2;
        for (var i = 0; i < 3; i++)
        {
            await Outbox.QueueAsync(Mail($"team{i}@jfb.test"));
        }

        var first = await Outbox.QueueAsync(Mail("a@jfb.test", toVisitor: true));
        var second = await Outbox.QueueAsync(Mail("b@jfb.test", toVisitor: true));
        var third = await Outbox.QueueAsync(Mail("c@jfb.test", toVisitor: true));
        var team = await Outbox.QueueAsync(Mail("team@jfb.test"));

        Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        var later = await Outbox.QueueAsync(Mail("c@jfb.test", toVisitor: true));

        Assert.Multiple(() =>
        {
            Assert.That((first!.State, second!.State), Is.EqualTo((EmailState.Queued, EmailState.Queued)));
            Assert.That(third!.State, Is.EqualTo(EmailState.Limited));
            Assert.That(third.LastError!.Code, Is.EqualTo(EmailRepository.SiteLimit));
            Assert.That(team!.State, Is.EqualTo(EmailState.Queued));
            Assert.That(later!.State, Is.EqualTo(EmailState.Queued));
        });
    }

    [Test]
    public async Task WhatTheSitesHourHeldBack_DoesNotCountAgainstIt()
    {
        MailLimits.PerSitePerHour = 1;
        var sent = await Outbox.QueueAsync(Mail("a@jfb.test", toVisitor: true));
        Clock.Advance(TimeSpan.FromMinutes(59));
        var held = await Outbox.QueueAsync(Mail("b@jfb.test", toVisitor: true));

        // The sent one is past its hour by now; the held one is not, and it never went.
        Clock.Advance(TimeSpan.FromMinutes(2));
        var next = await Outbox.QueueAsync(Mail("c@jfb.test", toVisitor: true));

        Assert.Multiple(() =>
        {
            Assert.That(sent!.State, Is.EqualTo(EmailState.Queued));
            Assert.That(held!.State, Is.EqualTo(EmailState.Limited));
            Assert.That(next!.State, Is.EqualTo(EmailState.Queued));
        });
    }

    [Test]
    public async Task ParallelRequests_CannotPassTheLimitTogether()
    {
        MailLimits.PerRecipientPerDay = 3;

        var mails = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => Outbox.QueueAsync(Mail("Anna@jfb.test", toVisitor: true)))));

        Assert.Multiple(() =>
        {
            Assert.That(mails.Count(mail => mail!.State == EmailState.Queued), Is.EqualTo(3));
            Assert.That(mails.Count(mail => mail!.State == EmailState.Limited), Is.EqualTo(9));
            Assert.That(mails.Select(mail => mail!.Id), Is.Unique, "each statement reads back its own mail");
        });
    }

    [Test]
    public async Task ALimitedMail_IsNeverSentOrRetried_AndIsPurgedOnTime()
    {
        MailLimits.PerRecipientPerDay = 1;
        await Outbox.QueueAsync(Mail(subject: "Allowed", toVisitor: true));
        var limited = await Outbox.QueueAsync(Mail(subject: "Held back", toVisitor: true));

        await SendDueAsync();
        Clock.Advance(TimeSpan.FromHours(2));
        await SendDueAsync();

        var held = (await Emails.GetAsync(limited!.Id))!;
        Assert.Multiple(async () =>
        {
            Assert.That(Written().Select(mail => mail.Subject), Is.EqualTo(new[] { "Allowed" }));
            Assert.That((held.State, held.Attempts), Is.EqualTo((EmailState.Limited, 0)));
            Assert.That(await Emails.ClaimAsync(limited.Id, "server", EmailQueue.Lease), Is.Null);
        });
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Emails.RequeueAsync(limited.Id, EmailState.Limited));

        Clock.Advance(EmailOutbox.Keep - TimeSpan.FromHours(3));
        await Emails.PurgeAsync(10);
        Assert.That(await Emails.GetAsync(limited.Id), Is.Not.Null, "not yet");

        Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1));
        await Emails.PurgeAsync(10);
        Assert.That(await Emails.GetAsync(limited.Id), Is.Null);
    }

    [Test]
    public async Task TheDeliveryLog_ShowsALimitedMail_WithTheLimitThatHeldItBack()
    {
        MailLimits.PerRecipientPerDay = 1;
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"));
        var entry = await SubmitAsync(form);

        await Outbox.QueueAsync(Mail(subject: "Allowed", toVisitor: true) with { EntryId = entry.Id });
        await Outbox.QueueAsync(Mail(subject: "Held back", toVisitor: true) with { EntryId = entry.Id });

        var log = await Emails.ListForEntryAsync(entry.Id);
        Assert.Multiple(() =>
        {
            Assert.That(log.Select(mail => mail.State), Is.EqualTo(new[] { EmailState.Queued, EmailState.Limited }));
            Assert.That(log[1].Subject, Is.EqualTo("Held back"));
            Assert.That(log[1].LastError!.Code, Is.EqualTo(EmailRepository.RecipientLimit));
        });
    }

    [Test]
    public async Task AMailForAnEntryThatIsGone_IsNotQueued()
    {
        Assert.That(await Outbox.QueueAsync(Mail(toVisitor: true) with { EntryId = 987654 }), Is.Null);
    }

    [Test]
    public async Task EveryValueOfALimitedInsert_IsStoredAsGiven()
    {
        var attachments = new[] { Guid.NewGuid() };
        var stored = await Outbox.QueueAsync(Mail(toVisitor: true) with
        {
            Kind = EmailKind.Verification,
            Cc = ["copy@jfb.test"],
            Bcc = ["hidden@jfb.test"],
            ReplyTo = "reply@jfb.test",
            ActionRunId = 7,
            Attachments = attachments,
            Culture = "de",
            Subject = "S",
        });

        var read = (await Emails.GetAsync(stored!.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(read.Kind, Is.EqualTo(EmailKind.Verification));
            Assert.That((read.To, read.Cc, read.Bcc, read.ReplyTo), Is.EqualTo(("anna@jfb.test", "copy@jfb.test", "hidden@jfb.test", "reply@jfb.test")));
            Assert.That((read.From, read.Subject, read.Culture), Is.EqualTo((HostSender, "S", "de")));
            Assert.That(read.ActionRunId, Is.EqualTo(7));
            Assert.That(read.Attachments, Is.EqualTo(attachments));
            Assert.That(read.HtmlBody, Does.Contain("Thank you."));
            Assert.That(read.TextBody, Is.EqualTo("Thank you."));
            Assert.That(read.CreatedUtc, Is.EqualTo(Clock.GetUtcNow()).Within(TimeSpan.FromSeconds(1)));
            Assert.That(read.NextAttemptUtc, Is.EqualTo(Clock.GetUtcNow()).Within(TimeSpan.FromSeconds(1)));
            Assert.That((read.Attempts, read.LastError, read.PurgeAfterUtc), Is.EqualTo((0, (DeliveryError?)null, (DateTimeOffset?)null)));
        });
    }
}
