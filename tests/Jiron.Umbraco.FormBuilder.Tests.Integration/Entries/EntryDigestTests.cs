using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>
/// The summary email (ROADMAP 11.3, IA 6.4, ADR-0209), with a hand-moved clock: one summary per user and period, sent once
/// the site's 07:00 has passed in its time zone, nothing for a user who has not asked or whose period had no entries, no
/// form the user may not view entries of, and no answer in the mail; the weekly one on Mondays for the week before, and
/// the test mail at once, marked as a test, in the user's language.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryDigestTests : ActionTestBase
{
    private Personas _people = null!;

    private EntryDigests Digests => GetRequiredService<EntryDigests>();

    private IEmailRepository Emails => GetRequiredService<IEmailRepository>();

    /// <summary>
    /// Each test's story starts on Monday, 2 November 2026, 09:00 UTC, whatever the test before left on the fixture's clock,
    /// with the personas of ADR-0013, whose addresses are their roles at jfb.test.
    /// </summary>
    [SetUp]
    public async Task CreateThePersonasAsync()
    {
        Clock.SetUtcNow(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        await using var scope = Services.CreateAsyncScope();
        _people = await ActivatorUtilities.CreateInstance<AccessPersonas>(scope.ServiceProvider).CreateAsync();
    }

    [Test]
    public async Task TheDailySummary_GoesOnceAfterSevenInTheSitesZone_WithTheVisibleFormsCountsAndNoAnswer()
    {
        // Monday, 2 November 2026, 10:00 in Berlin, on a site whose address is known, so the mail links the backoffice.
        await GeneralAsync("Europe/Berlin", "https://www.jfb.test");
        var open = await PublishAsync(name: "Contact us");
        var hidden = await PublishAsync(name: "Salaries");
        await RestrictAsync(hidden);
        await Digests.SaveAsync(_people["viewer"].Key, EntryDigestFrequency.Daily);
        await Digests.SaveAsync(_people["noAccess"].Key, EntryDigestFrequency.Daily);

        await SubmitAsync(open, """{"name":"Ada Lovelace"}""");
        await SubmitAsync(open, """{"name":"Grace Hopper"}""");
        await StoreAsync(open, EntryState.Spam);
        await StoreAsync(open, EntryState.Submitted, test: true);
        await StoreAsync(hidden, EntryState.Submitted);

        // 06:30 in Berlin: not yet. 07:30: once, however often the job runs, and also when the user saved the same choice
        // again just before it ran.
        MoveTo(new DateTimeOffset(2026, 11, 3, 5, 30, 0, TimeSpan.Zero));
        await RunAsync();
        var early = await DigestsSentAsync();
        MoveTo(new DateTimeOffset(2026, 11, 3, 6, 30, 0, TimeSpan.Zero));
        await Digests.SaveAsync(_people["viewer"].Key, EntryDigestFrequency.Daily);
        await RunAsync();
        await RunAsync();
        var sent = await DigestsSentAsync();

        // The next morning: no entry came on Tuesday, so nothing goes, though the day counts as done.
        MoveTo(new DateTimeOffset(2026, 11, 4, 6, 30, 0, TimeSpan.Zero));
        await RunAsync();
        var later = await DigestsSentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.Empty, "before 07:00 in the site's time zone");
            Assert.That(sent.Select(mail => mail.To), Is.EqualTo(new[] { "viewer@jfb.test" }), "one summary, to the user who asked and may view a form; none to who did not ask");
            var mail = sent.Single();
            Assert.That(mail.Subject, Is.EqualTo("Summary of entries, Monday, November 2, 2026"), "dates as the user's language writes them");
            Assert.That(mail.HtmlBody, Does.Contain("2 entries arrived on Monday, November 2, 2026."), "neither spam nor the test entry");
            Assert.That(mail.HtmlBody, Does.Contain($"href=\"https://www.jfb.test/umbraco/section/jfb-forms/workspace/jfb-form/edit/{open:D}/view/entries\""));
            Assert.That(mail.HtmlBody, Does.Contain("Contact us"));
            Assert.That(mail.HtmlBody, Does.Contain("href=\"https://www.jfb.test/umbraco/section/jfb-forms/workspace/jfb-inbox\""));
            Assert.That(mail.HtmlBody, Does.Not.Contain("Salaries"), "a form the user may not view entries of");
            Assert.That(mail.HtmlBody + mail.TextBody, Does.Not.Contain("Ada").And.Not.Contain("Grace"), "no answer");
            Assert.That(later, Has.Count.EqualTo(1), "a day without entries sends nothing");
        });
    }

    [Test]
    public async Task TheWeeklySummary_GoesOnMondaysForTheWeekBefore_WithWhatWaitsForApproval()
    {
        // Monday, 2 November 2026, 09:00 UTC, the site's zone.
        var open = await PublishAsync(name: "Contact us");
        var hidden = await PublishAsync(name: "Applications");
        await RestrictAsync(hidden);
        await GetRequiredService<IFormBuilderAccessService>().ChangeGrantAsync(
            new AccessGrantChange(_people.GroupKeys["hr"], PermissionTarget.Form, hidden, FormBuilderPermissions.None, FormBuilderPermissions.ViewEntries, null));
        await Digests.SaveAsync(_people["hr"].Key, EntryDigestFrequency.Weekly);
        for (var i = 0; i < 3; i++)
        {
            await StoreAsync(hidden, EntryState.Submitted);
            await StoreAsync(open, EntryState.Submitted);
        }

        MoveTo(new DateTimeOffset(2026, 11, 9, 6, 59, 0, TimeSpan.Zero));
        await RunAsync();
        var early = await DigestsSentAsync();
        MoveTo(new DateTimeOffset(2026, 11, 9, 7, 1, 0, TimeSpan.Zero));
        await RunAsync();
        var sent = await DigestsSentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(early, Is.Empty, "before Monday's 07:00");
            Assert.That(sent.Select(mail => mail.To), Is.EqualTo(new[] { "hr@jfb.test" }));
            var mail = sent.Single();
            Assert.That(mail.Subject, Is.EqualTo("Summary of entries, Monday, November 2, 2026 to Sunday, November 8, 2026"));
            Assert.That(mail.HtmlBody, Does.Contain("3 entries arrived from Monday, November 2, 2026 to Sunday, November 8, 2026."));
            Assert.That(mail.HtmlBody, Does.Contain("3 entries are waiting for approval."), "only the forms the user may view");
            Assert.That(mail.HtmlBody, Does.Contain("Applications").And.Not.Contain("Contact us"));
        });
    }

    [Test]
    public async Task ChangingHowOften_SendsNothingForWhatPassedBefore_AndNeverAgainWhatWasSent()
    {
        // Monday, 2 November 2026, 09:00 UTC: a weekly user has the week before covered.
        var open = await PublishAsync(name: "Contact us");
        var viewer = _people["viewer"].Key;
        await Digests.SaveAsync(viewer, EntryDigestFrequency.Weekly);
        await StoreAsync(open, EntryState.Submitted);

        // Wednesday, 07:30: daily from now; Tuesday passed before the choice, so nothing goes for it.
        MoveTo(new DateTimeOffset(2026, 11, 4, 7, 30, 0, TimeSpan.Zero));
        await Digests.SaveAsync(viewer, EntryDigestFrequency.Daily);
        await RunAsync();
        var afterChange = await DigestsSentAsync();

        // Thursday, 07:30: Wednesday's summary, the first period that ended after the choice.
        await StoreAsync(open, EntryState.Submitted);
        MoveTo(new DateTimeOffset(2026, 11, 5, 7, 30, 0, TimeSpan.Zero));
        await RunAsync();
        var next = await DigestsSentAsync();

        // Back to weekly on Thursday: the job covered Wednesday, and the record never goes back to the week before.
        await Digests.SaveAsync(viewer, EntryDigestFrequency.Weekly);
        var recorded = await GetRequiredService<IUserSettingRepository>().GetAsync(viewer, EntryDigests.SentSetting);

        Assert.Multiple(() =>
        {
            Assert.That(afterChange, Is.Empty, "nothing for the days before the change");
            Assert.That(next.Select(mail => mail.Subject), Is.EqualTo(new[] { "Summary of entries, Wednesday, November 4, 2026" }));
            Assert.That(recorded, Does.Contain("2026-11-04"), "the covered day stays covered");
        });
    }

    [Test]
    public async Task TheTestMail_GoesAtOnce_MarkedAsATest_InTheUsersLanguage_AndRecordsNothing()
    {
        var viewer = (await GetRequiredService<IUserService>().GetAsync(_people["viewer"].Key))!;
        var outcome = await Digests.SendTestAsync(viewer, EntryDigestFrequency.Daily);
        await RunAsync();
        var sent = await DigestsSentAsync();

        // A user writing in German gets the words of German, also in the preview, which sends nothing.
        var globals = GetRequiredService<IOptions<GlobalSettings>>().Value;
        var german = new User(globals) { Key = Guid.NewGuid(), Email = "de@jfb.test", Language = "de-DE" };
        var preview = await Digests.PreviewAsync(german, EntryDigestFrequency.Daily, test: true);

        // No test goes to a user without an address, or to one who may not sign in.
        var noAddress = await Digests.SendTestAsync(new User(globals) { Key = Guid.NewGuid(), Email = " " }, EntryDigestFrequency.Daily);
        var disabled = await Digests.SendTestAsync(new User(globals) { Key = Guid.NewGuid(), Email = "off@jfb.test", IsApproved = false }, EntryDigestFrequency.Daily);

        Assert.Multiple(async () =>
        {
            Assert.That(outcome, Is.EqualTo(EntryDigestOutcome.Queued));
            Assert.That(sent.Select(mail => (mail.To, mail.Subject)), Is.EqualTo(new[] { ("viewer@jfb.test", "Test: Summary of entries, Sunday, November 1, 2026") }), "even without entries; the job sends nothing to who has not asked");
            Assert.That(sent.Single().HtmlBody, Does.Contain("No entries arrived on Sunday, November 1, 2026."));
            Assert.That(sent.Single().HtmlBody, Does.Not.Contain("/umbraco/section/"), "no link without the site's address");
            Assert.That(preview.Subject, Does.StartWith("Test: Zusammenfassung der Eintr"));
            Assert.That(preview.Html, Does.Contain("keine Eintr"));
            Assert.That((noAddress, disabled), Is.EqualTo((EntryDigestOutcome.NoAddress, EntryDigestOutcome.NoAddress)));
            Assert.That(await DigestsSentAsync(), Has.Count.EqualTo(1), "a preview queues nothing, nor a test that goes nowhere");
        });
    }

    [Test]
    public async Task TheJob_SendsWhatIsDue_AndARunStopsAtItsDeadline_TheNextGoingOn()
    {
        var form = await PublishAsync(name: "Contact us");
        await Digests.SaveAsync(_people["viewer"].Key, EntryDigestFrequency.Daily);
        await Digests.SaveAsync(_people["admin"].Key, EntryDigestFrequency.Daily);
        await SubmitAsync(form, """{"name":"Ada Lovelace"}""");
        MoveTo(new DateTimeOffset(2026, 11, 3, 8, 0, 0, TimeSpan.Zero));

        // A run whose deadline has come sends nothing; the job's own run, within its budget, sends what waits.
        await Digests.RunAsync(Clock.GetUtcNow());
        var stopped = await DigestsSentAsync();
        await Services.GetServices<IDistributedBackgroundJob>().OfType<FormBuilderDigestJob>().Single().ExecuteAsync(CancellationToken.None);
        var sent = await DigestsSentAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.Empty, "a run stops at its deadline");
            Assert.That(sent.Select(mail => mail.To), Is.EquivalentTo(new[] { "viewer@jfb.test", "admin@jfb.test" }), "the next goes on");
            Assert.That(FormBuilderDigestJob.Budget, Is.EqualTo(TimeSpan.FromSeconds(8)));
        });
    }

    private Task RunAsync() => Digests.RunAsync(DateTimeOffset.MaxValue);

    // The clock moves on to a moment; it never goes back.
    private void MoveTo(DateTimeOffset utc) => Clock.Advance(utc - Clock.GetUtcNow());

    private async Task<List<StoredEmail>> DigestsSentAsync()
    {
        var found = new List<StoredEmail>();
        foreach (var id in await Emails.DueAsync(500))
        {
            if (await Emails.GetAsync(id) is { Kind: EmailKind.Digest } mail)
            {
                found.Add(mail);
            }
        }

        return found;
    }

    private async Task GeneralAsync(string zone, string baseUrl)
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(general.Value with { TimeZone = zone, BaseUrl = baseUrl }, general.Version, null);
    }

    // Gives the form a restriction of its own, so only grants on it count.
    private async Task RestrictAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        await GetRequiredService<IFormBuilderAccessService>().RestrictFormAsync(form, row.ConcurrencyVersion, true, null);
    }

    // An entry of the form in a state, a minute after the one before.
    private async Task StoreAsync(Guid form, EntryState state, bool test = false)
    {
        var row = (await FormRows.GetAsync(form))!;
        Clock.Advance(TimeSpan.FromMinutes(1));
        await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            IsTest = test,
        });
    }
}
