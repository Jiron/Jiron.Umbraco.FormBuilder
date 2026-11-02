using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The mail outbox (ADR-0007, ADR-0040): mail queued as it will be sent, claimed once with a token of each claim's own,
/// finished by the claim's holder only, uncertain when a lease runs out and still recorded by the late finisher, kept
/// until its purge, retried from the log, and gone with its entry unless it was sent. Every change with a stale token or state changes nothing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EmailRepositoryTests : RepositoryTestBase
{
    private const string Server = "server-a JFB_TEST";
    private const string Stale = Server + "/00000000000000000000000000000000";
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Keep = TimeSpan.FromDays(30);
    private static readonly DeliveryError Refused = new("smtp.550", "Mailbox unavailable.");

    [Test]
    public async Task AClaimGivenBack_CountsNoAttempt_AndNeedsItsToken()
    {
        var entryId = await EntryAsync();
        var stored = await Emails.AddAsync(Mail(entryId));
        var token = (await Emails.ClaimAsync(stored!.Id, Server, TimeSpan.FromMinutes(5)))!.LockedBy!;

        var wrong = await Emails.ReleaseAsync(stored.Id, Stale, TimeSpan.FromMinutes(1), Refused);
        var right = await Emails.ReleaseAsync(stored.Id, token, TimeSpan.FromMinutes(1), Refused);

        var released = (await Emails.GetAsync(stored.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That((wrong, right), Is.EqualTo((false, true)));
            Assert.That((released.State, released.Attempts, released.LockedBy, released.LockedUntilUtc), Is.EqualTo((EmailState.Queued, 0, (string?)null, (DateTimeOffset?)null)));
            Assert.That(released.LastError, Is.EqualTo(Refused));
            Assert.That(released.NextAttemptUtc, Is.GreaterThan(released.CreatedUtc));
        });
    }

    [Test]
    public async Task AMail_IsQueued_AsItWillBeSent()
    {
        var entryId = await EntryAsync();
        var attachments = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var mail = Mail(entryId) with
        {
            HtmlBody = "<p>Thank you, Anna.</p>",
            Cc = "copy@jfb.test",
            Bcc = "hidden@jfb.test, archive@jfb.test",
            ReplyTo = "anna@jfb.test",
            ActionRunId = 7,
            Attachments = attachments,
            RecipientKey = "anna@jfb.test",
        };

        var stored = await Emails.AddAsync(mail);

        Assert.That(stored, Is.Not.Null);
        Assert.Multiple(async () =>
        {
            Assert.That(stored!.Kind, Is.EqualTo(EmailKind.Action));
            Assert.That((stored.EntryId, stored.ActionRunId), Is.EqualTo(((long?)entryId, (long?)7)));
            Assert.That((stored.To, stored.Cc, stored.Bcc, stored.ReplyTo, stored.From), Is.EqualTo((mail.To, mail.Cc, mail.Bcc, mail.ReplyTo, mail.From)));
            Assert.That((stored.Subject, stored.HtmlBody, stored.TextBody, stored.Culture, stored.RecipientKey), Is.EqualTo((mail.Subject, mail.HtmlBody, mail.TextBody, mail.Culture, mail.RecipientKey)));
            Assert.That(stored.Attachments, Is.EqualTo(attachments));
            Assert.That(stored, Is.EqualTo(stored with { State = EmailState.Queued, Attempts = 0, NextAttemptUtc = Now, CreatedUtc = Now, LockedBy = null, LockedUntilUtc = null, LastError = null, SentUtc = null, PurgeAfterUtc = null }));
            AssertSame(await Emails.GetAsync(stored.Id), stored);
            Assert.That(await Emails.GetAsync(stored.Id + 100), Is.Null);
            Assert.That(Query(db => db.ExecuteScalar<string>($"SELECT {Q("attachments")} FROM {T(FormBuilderTables.Email)}")), Is.EqualTo($"[\"{attachments[0]:D}\",\"{attachments[1]:D}\"]"), "a JSON array of file keys");
        });
    }

    [Test]
    public async Task AMail_WithoutAnEntry_IsQueued_AndOneForAnEntryThatDoesNotExistIsNot()
    {
        var entryId = await EntryAsync();

        var test = await Emails.AddAsync(Mail(null) with { Kind = EmailKind.Test });

        Assert.Multiple(async () =>
        {
            Assert.That(test, Is.Not.Null);
            Assert.That(test!.Attachments, Is.Empty);
            Assert.That(test.HtmlBody, Is.Null, "a plain-text mail");
            Assert.That(await Emails.AddAsync(Mail(entryId + 100)), Is.Null);
            Assert.That(Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnEntrysMail_IsListedOldestFirst()
    {
        var entryId = await EntryAsync();
        var first = await Emails.AddAsync(Mail(entryId));
        Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await Emails.AddAsync(Mail(entryId) with { Kind = EmailKind.Verification });
        await Emails.AddAsync(Mail(await EntryAsync()));

        Assert.That((await Emails.ListForEntryAsync(entryId)).Select(mail => mail.Id), Is.EqualTo(new[] { first!.Id, second!.Id }));
    }

    [Test]
    public async Task AClaim_TakesADueMail_ForOneClaimOnly_TheLongestDueFirst()
    {
        var first = await Emails.AddAsync(Mail(null));
        var second = await Emails.AddAsync(Mail(null));
        await Emails.RetryAsync(first!.Id, await TakeAsync(first), TimeSpan.FromMinutes(1), Refused);
        var third = await Emails.AddAsync(Mail(null));

        Assert.That(await Emails.DueAsync(10), Is.EqualTo(new[] { second!.Id, third!.Id }), "the retry is not due yet");
        Assert.That(await Emails.ClaimAsync(first.Id, Server, Lease), Is.Null, "nor can it be claimed");
        Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.That(await Emails.DueAsync(10), Is.EqualTo(new[] { second.Id, third.Id, first.Id }), "the longest due first");
        Assert.That(await Emails.DueAsync(2), Is.EqualTo(new[] { second.Id, third.Id }));

        var claimed = await Emails.ClaimAsync(second!.Id, Server, Lease);
        var later = Now.AddMinutes(2);
        Assert.Multiple(async () =>
        {
            AssertSame(claimed, second with { State = EmailState.Sending, Attempts = 1, LockedBy = claimed?.LockedBy, LockedUntilUtc = later + Lease });
            Assert.That(claimed?.LockedBy, Does.Match("^server-a JFB_TEST/[0-9a-f]{32}$"), "the claimant's name and 128 random bits");
            Assert.That(await Emails.ClaimAsync(second.Id, "server-b JFB_TEST", Lease), Is.Null, "claimed once");
            AssertSame(await Emails.GetAsync(second.Id), claimed!);
            Assert.That(await Emails.DueAsync(10), Is.EqualTo(new[] { third.Id, first.Id }));
        });
    }

    [Test]
    public async Task ParallelClaims_LetExactlyOneThrough()
    {
        var mail = await Emails.AddAsync(Mail(null));

        var claims = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() => Emails.ClaimAsync(mail!.Id, $"server-{i} JFB_TEST", Lease))));

        Assert.That(claims.Count(claim => claim is not null), Is.EqualTo(1));
    }

    [Test]
    public async Task Outcomes_AreRecorded_OnlyByTheClaimsToken_AndKeepTheMailUntilItsPurge()
    {
        var held = new List<StoredEmail>();
        for (var i = 0; i < 4; i++)
        {
            var mail = await Emails.AddAsync(Mail(null));
            held.Add((await Emails.ClaimAsync(mail!.Id, Server, Lease))!);
            await Emails.AddAsync(Mail(null));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.SentAsync(held[0].Id, held[1].LockedBy!, Keep), Is.False, "another claim's token");
            Assert.That(await Emails.RetryAsync(held[1].Id, held[2].LockedBy!, TimeSpan.Zero, Refused), Is.False);
            Assert.That(await Emails.FailAsync(held[2].Id, held[3].LockedBy!, Refused, Keep), Is.False);
            Assert.That(await Emails.CutOffAsync(held[3].Id, held[0].LockedBy!, Refused, Keep), Is.False);
            Assert.That(await Emails.SentAsync(held[0].Id, Stale, Keep), Is.False);
            Assert.That(await Emails.RetryAsync(held[1].Id, Stale, TimeSpan.Zero, Refused), Is.False);
            Assert.That(await Emails.FailAsync(held[2].Id, Stale, Refused, Keep), Is.False);
            Assert.That(await Emails.CutOffAsync(held[3].Id, Stale, Refused, Keep), Is.False);
            foreach (var mail in held)
            {
                AssertSame(await Emails.GetAsync(mail.Id), mail, "a stale token changes nothing");
            }
        });

        Clock.Advance(TimeSpan.FromSeconds(30));
        var later = Now.AddSeconds(30);
        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.SentAsync(held[0].Id, held[0].LockedBy!, Keep), Is.True);
            Assert.That(await Emails.RetryAsync(held[1].Id, held[1].LockedBy!, TimeSpan.FromMinutes(9), Refused), Is.True);
            Assert.That(await Emails.FailAsync(held[2].Id, held[2].LockedBy!, Refused, Keep), Is.True);
            Assert.That(await Emails.CutOffAsync(held[3].Id, held[3].LockedBy!, Refused, Keep), Is.True);
        });

        var after = await Task.WhenAll(held.Select(mail => Emails.GetAsync(mail.Id)));
        Assert.Multiple(async () =>
        {
            AssertSame(after[0], held[0] with { State = EmailState.Sent, SentUtc = later, PurgeAfterUtc = later + Keep, NextAttemptUtc = null, LockedUntilUtc = null });
            AssertSame(after[1], held[1] with { State = EmailState.Queued, NextAttemptUtc = later.AddMinutes(9), LockedBy = null, LockedUntilUtc = null, LastError = Refused });
            AssertSame(after[2], held[2] with { State = EmailState.Failed, PurgeAfterUtc = later + Keep, NextAttemptUtc = null, LockedUntilUtc = null, LastError = Refused });
            AssertSame(after[3], held[3] with { State = EmailState.Uncertain, PurgeAfterUtc = later + Keep, LastError = Refused }, "cut off: the token stays for the late finisher");
            Assert.That(await Emails.SentAsync(held[0].Id, held[0].LockedBy!, Keep), Is.False, "a sent mail takes no outcome");
            Assert.That(await Emails.FailAsync(held[1].Id, held[1].LockedBy!, Refused, Keep), Is.False, "a queued mail is no attempt's");
            AssertSame(await Emails.GetAsync(held[0].Id), after[0]!);
            AssertSame(await Emails.GetAsync(held[1].Id), after[1]!);
        });
    }

    [Test]
    public async Task AnError_IsStoredAsItsCodeAndMessage_InTheirOwnColumns_TheMessageCutToItsColumn()
    {
        var mail = await Emails.AddAsync(Mail(null));
        var wordy = new DeliveryError("smtp.timeout", new string('m', EmailRepository.MaxErrorMessageLength + 10));

        await Emails.RetryAsync(mail!.Id, await TakeAsync(mail), TimeSpan.Zero, wordy);
        var retried = await Emails.GetAsync(mail.Id);
        var row = await Database.ReadAsync(db => db.FirstOrNullAsync<EmailDto>($"SELECT * FROM {new SqlNames(db).T(FormBuilderTables.Email)} WHERE {new SqlNames(db).C("id")} = @0", mail.Id));
        await Emails.RetryAsync(mail.Id, await TakeAsync(mail), TimeSpan.Zero, new DeliveryError("smtp.busy"));
        var bare = await Emails.GetAsync(mail.Id);
        await Emails.SentAsync(mail.Id, await TakeAsync(mail), Keep);

        Assert.Multiple(async () =>
        {
            Assert.That(retried!.LastError, Is.EqualTo(new DeliveryError("smtp.timeout", new string('m', EmailRepository.MaxErrorMessageLength))), "the whole code, the message cut");
            Assert.That((row!.LastErrorCode, row.LastErrorMessage), Is.EqualTo(("smtp.timeout", new string('m', EmailRepository.MaxErrorMessageLength))), "two columns, no separator to parse");
            Assert.That(bare!.LastError, Is.EqualTo(new DeliveryError("smtp.busy")));
            Assert.That((await Emails.GetAsync(mail.Id))!.LastError, Is.Null, "a success clears it");
            Assert.That((await Emails.GetAsync(mail.Id))!.Attempts, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ALeaseThatRanOut_ReplacesTheWholeLastError_ItsMessageToo()
    {
        var mail = (await Emails.AddAsync(Mail(null)))!;
        await Emails.RetryAsync(mail.Id, await TakeAsync(mail), TimeSpan.Zero, new DeliveryError("smtp.busy", "Try again later."));
        await TakeAsync(mail);
        Clock.Advance(Lease + TimeSpan.FromSeconds(1));

        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.ExpireLeasesAsync(Keep), Is.EqualTo(1));
            Assert.That((await Emails.GetAsync(mail.Id))!.LastError, Is.EqualTo(new DeliveryError(DeliveryError.LeaseExpired)), "no message of the attempt before");
        });
    }

    [Test]
    public async Task ALeaseThatRanOut_MakesTheMailUncertain_AndTheLateFinisherStillRecordsItsResult()
    {
        var late = (await Emails.AddAsync(Mail(null)))!;
        var retried = (await Emails.AddAsync(Mail(null)))!;
        var again = (await Emails.AddAsync(Mail(null)))!;
        var lateToken = await TakeAsync(late);
        var retriedToken = await TakeAsync(retried);
        var againToken = await TakeAsync(again);
        var waiting = await Emails.AddAsync(Mail(null));

        Clock.Advance(Lease);
        Assert.That(await Emails.ExpireLeasesAsync(Keep), Is.Zero, "a lease that ends now still holds");

        Clock.Advance(TimeSpan.FromSeconds(1));
        var expiredAt = Now + Lease + TimeSpan.FromSeconds(1);
        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.ExpireLeasesAsync(Keep), Is.EqualTo(3));
            Assert.That(await Emails.ExpireLeasesAsync(Keep), Is.Zero, "once");
            var expired = await Emails.GetAsync(late.Id);
            Assert.That(expired, Is.Not.Null);
            Assert.That((expired!.State, expired.LockedBy, expired.LastError, expired.PurgeAfterUtc), Is.EqualTo((EmailState.Uncertain, lateToken, new DeliveryError(DeliveryError.LeaseExpired), (DateTimeOffset?)(expiredAt + Keep))));
            Assert.That(await Emails.DueAsync(10), Is.EqualTo(new[] { waiting!.Id }), "nothing claims an uncertain mail");
            Assert.That(await Emails.ClaimAsync(late.Id, "server-b JFB_TEST", Lease), Is.Null);
        });

        await Emails.RequeueAsync(retried.Id, EmailState.Uncertain);
        Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.SentAsync(late.Id, lateToken, Keep), Is.True, "the late finisher");
            var sent = await Emails.GetAsync(late.Id);
            Assert.That((sent!.State, sent.PurgeAfterUtc), Is.EqualTo((EmailState.Sent, (DateTimeOffset?)(expiredAt.AddSeconds(1) + Keep))), "kept from when it was sent");
            Assert.That(await Emails.SentAsync(retried.Id, retriedToken, Keep), Is.False, "its token was cleared");
            Assert.That((await Emails.GetAsync(retried.Id))!.State, Is.EqualTo(EmailState.Queued));
            Assert.That(await Emails.RetryAsync(again.Id, againToken, TimeSpan.Zero, Refused), Is.True, "a late finisher that failed");
            var queued = await Emails.GetAsync(again.Id);
            Assert.That((queued!.State, queued.PurgeAfterUtc), Is.EqualTo((EmailState.Queued, (DateTimeOffset?)null)), "a queued mail is never purged");
        });
    }

    [Test]
    public async Task ARetryFromTheLog_StartsOver_FromTheStateTheUserSaw()
    {
        var mail = await Emails.AddAsync(Mail(null));
        await Emails.FailAsync(mail!.Id, await TakeAsync(mail), Refused, Keep);

        Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.That(async () => await Emails.RequeueAsync(mail.Id, EmailState.Uncertain), Throws.InstanceOf<FormBuilderConcurrencyException>(), "not in the state the user saw");
        var retried = await Emails.RequeueAsync(mail.Id, EmailState.Failed);

        Assert.Multiple(async () =>
        {
            Assert.That(retried, Is.Not.Null);
            Assert.That((retried!.State, retried.Attempts, retried.NextAttemptUtc, retried.LockedBy, retried.LockedUntilUtc, retried.PurgeAfterUtc, retried.LastError), Is.EqualTo((EmailState.Queued, 0, (DateTimeOffset?)Now.AddMinutes(10), (string?)null, (DateTimeOffset?)null, (DateTimeOffset?)null, Refused)));
            Assert.That(await Emails.DueAsync(10), Is.EqualTo(new[] { mail.Id }));
            Assert.That(async () => await Emails.RequeueAsync(mail.Id, EmailState.Failed), Throws.InstanceOf<FormBuilderConcurrencyException>(), "twice");
            Assert.That(await Emails.RequeueAsync(mail.Id + 100, EmailState.Failed), Is.Null);
        });
    }

    [Test]
    public async Task ALimitedMail_IsNeverClaimedOrRetried()
    {
        var mail = await Emails.AddAsync(Mail(null));
        Write(db => db.Execute($"UPDATE {T(FormBuilderTables.Email)} SET {Q("state")} = @0 WHERE {Q("id")} = @1", StoredNames.Of(EmailState.Limited), mail!.Id));

        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.DueAsync(10), Is.Empty);
            Assert.That(await Emails.ClaimAsync(mail!.Id, Server, Lease), Is.Null);
            Assert.That(async () => await Emails.RequeueAsync(mail.Id, EmailState.Failed), Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That((await Emails.GetAsync(mail.Id))!.State, Is.EqualTo(EmailState.Limited));
        });
    }

    [Test]
    public async Task ThePurge_DeletesMailPastItsTime_TheOldestFirst()
    {
        var keptLonger = await SentAsync(TimeSpan.FromDays(2));
        var second = await SentAsync(TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));
        var first = await SentAsync(TimeSpan.FromDays(1));
        var queued = await Emails.AddAsync(Mail(null));

        Clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromSeconds(1));
        Assert.That(await Emails.PurgeAsync(10), Is.Zero, "not yet");
        Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.PurgeAsync(1), Is.EqualTo(1));
            Assert.That(await Emails.GetAsync(first), Is.Null, "the earliest purge time first, though its id is later");
            Assert.That(await Emails.GetAsync(second), Is.Not.Null);
            Assert.That(await Emails.PurgeAsync(10), Is.EqualTo(1), "a purge time that has come");
            Assert.That(await Emails.GetAsync(second), Is.Null);
            Assert.That(await Emails.GetAsync(keptLonger), Is.Not.Null, "kept for two days");
            Assert.That(await Emails.GetAsync(queued!.Id), Is.Not.Null, "unsent mail has no purge time");
        });
    }

    [Test]
    public async Task DeletingAnEntry_DeletesItsUnsentMail_AndSentMailStaysInTheLog()
    {
        var entryId = await EntryAsync();
        var other = await Emails.AddAsync(Mail(await EntryAsync()));
        var sent = await Emails.AddAsync(Mail(entryId));
        await Emails.SentAsync(sent!.Id, await TakeAsync(sent), Keep);
        var failed = await Emails.AddAsync(Mail(entryId));
        await Emails.FailAsync(failed!.Id, await TakeAsync(failed), Refused, Keep);
        var sending = (await Emails.AddAsync(Mail(entryId)))!;
        var sendingToken = await TakeAsync(sending);
        await Emails.AddAsync(Mail(entryId));

        Assert.That(await Emails.DeleteUnsentAsync(entryId + 100), Is.Zero, "no such entry");
        Assert.That(await Emails.DeleteUnsentAsync(entryId), Is.EqualTo(3));
        await Entries.DeleteAsync(entryId);

        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.GetAsync(sent.Id))!.EntryId, Is.Null, "sent mail stays without its entry");
            Assert.That(await Emails.GetAsync(failed.Id), Is.Null);
            Assert.That(await Emails.GetAsync(sending.Id), Is.Null);
            Assert.That(await Emails.GetAsync(other!.Id), Is.Not.Null, "another entry's mail");
            Assert.That(Count(), Is.EqualTo(2));
            Assert.That(await Emails.SentAsync(sending.Id, sendingToken, Keep), Is.False, "an attempt of a deleted mail records nothing");
        });
    }

    private static NewEmail Mail(long? entryId) =>
        new(EmailKind.Action, "sales@jfb.test", "Jiron Form Builder <forms@jfb.test>", "A new entry", "Thank you, Anna.", "en-US", EntryId: entryId);

    // Stored mail compared field by field: its attachments are a list.
    private static void AssertSame(StoredEmail? actual, StoredEmail expected, string? why = null)
    {
        Assert.That(actual, Is.Not.Null, why);
        Assert.That(actual! with { Attachments = [] }, Is.EqualTo(expected with { Attachments = [] }), why);
        Assert.That(actual.Attachments, Is.EqualTo(expected.Attachments), why);
    }

    private int Count() => Query(db => db.ExecuteScalar<int>($"SELECT COUNT(*) FROM {T(FormBuilderTables.Email)}"));

    private async Task<long> EntryAsync()
    {
        var form = await CreateFormAsync();
        return await AddEntryAsync(form.Id, form.DraftVersionId!.Value);
    }

    // A mail sent now and kept for the given time; its id.
    private async Task<long> SentAsync(TimeSpan keep)
    {
        var mail = await Emails.AddAsync(Mail(null));
        await Emails.SentAsync(mail!.Id, await TakeAsync(mail), keep);
        return mail.Id;
    }

    // Claims a mail that must be claimable, and gives the claim's token.
    private async Task<string> TakeAsync(StoredEmail mail)
    {
        var claimed = await Emails.ClaimAsync(mail.Id, Server, Lease);
        Assert.That(claimed, Is.Not.Null, "the mail can be claimed");
        return claimed!.LockedBy!;
    }
}
