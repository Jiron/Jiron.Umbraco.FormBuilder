using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>Email verification, from a submission through its mail and page to the actions (ROADMAP 8.5, ADR-0162).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed partial class EntryVerificationTests : EmailTestBase
{
    private const string Ada = """{"name":"Ada","email":"ada@jfb.test"}""";

    private static readonly FormField EmailField = new() { Key = Guid.Parse("c3000000-0000-4000-8000-000000000001"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    private static readonly FormField Kept = new()
    {
        Key = Guid.Parse("c3000000-0000-4000-8000-000000000002"),
        Type = "text",
        Alias = "kept",
        Label = new LocalizedText("Kept"),
        Keep = FieldKeep.UntilActionsRun,
    };

    private EntrySpamFolder Folder => GetRequiredService<EntrySpamFolder>();

    private IFormBuilderSettingsService SiteSettings => GetRequiredService<IFormBuilderSettingsService>();

    private HeardEntries Heard => GetRequiredService<HeardEntries>();

    private static Func<FormDefinition, FormDefinition> Verifying(ApprovalMode approval, params FormAction[] actions) => definition => definition with
    {
        Actions = actions,
        Settings = definition.Settings with
        {
            Submission = definition.Settings.Submission with { Approval = approval, Verification = new EmailVerification { EmailField = EmailField.Key, Days = 7 } },
        },
    };

    private Task<Guid> FormAsync(params FormAction[] actions) =>
        PublishAsync(name: "Newsletter", change: Verifying(ApprovalMode.Automatic, actions), fields: [NameField, EmailField, Kept]);

    [GeneratedRegex("href=\"(?<link>[^\"]*/verify[^\"]*)\"")]
    private static partial Regex LinkPattern();

    private static string LinkOf(MimeMessage mail)
    {
        var link = LinkPattern().Match(((TextPart)mail.Body!).Text).Groups["link"].Value;
        Assert.That(link, Is.Not.Empty, "the mail holds its link");
        var decoded = WebUtility.HtmlDecode(link);
        return decoded.StartsWith("http", StringComparison.Ordinal) ? new Uri(decoded).PathAndQuery : decoded;
    }

    private static string TokenOf(string link) => Uri.UnescapeDataString(link[(link.IndexOf("?t=", StringComparison.Ordinal) + 3)..]);

    private async Task<(int Status, string Html)> GetAsync(string link)
    {
        using var answer = await SendAsync(HttpMethod.Get, link);
        return ((int)answer.StatusCode, await answer.Content.ReadAsStringAsync());
    }

    private async Task<(int Status, string Html)> PostAsync(string link, string action = "confirm")
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["t"] = TokenOf(link), ["action"] = action });
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/verify", request => request.Content = content);
        return ((int)answer.StatusCode, await answer.Content.ReadAsStringAsync());
    }

    private async Task<string> SentLinkAsync(int mails = 1)
    {
        await SendDueAsync();
        var written = Written();
        Assert.That(written, Has.Count.EqualTo(mails));
        return LinkOf(written[^1]);
    }

    [SetUp]
    public async Task GiveTheSiteItsAddress()
    {
        var current = await SiteSettings.GetAsync<GeneralSettings>();
        await SiteSettings.SaveAsync(current.Value with { BaseUrl = "https://forms.jfb.test" }, current.Version, Editor);
    }

    [Test]
    public async Task ASiteThatDoesNotKnowItsAddress_SendsNoMailWithoutItsLink()
    {
        var current = await SiteSettings.GetAsync<GeneralSettings>();
        await SiteSettings.SaveAsync(current.Value with { BaseUrl = null }, current.Version, Editor);
        var form = await FormAsync();

        var entry = await SubmitAsync(form, Ada);
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(Written(), Is.Empty);
            Assert.That((await Events.ListForEntryAsync(entry.Id)).Select(item => item.Kind), Does.Not.Contain(EntryVerification.SentEvent));
        });
        Clock.Advance(TimeSpan.FromDays(7) + EntryVerification.Grace);
        await SendDueAsync();
        Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null, "it goes like any other nobody confirms");
    }

    [Test]
    public async Task AVerifiedField_KeptOnlyUntilTheActionsRun_IsRefused_AsTheMailNeedsIt()
    {
        var address = new FormField { Key = EmailField.Key, Type = "email", Alias = "email", Label = new LocalizedText("Email"), Keep = FieldKeep.UntilActionsRun };

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(async () => await PublishAsync(name: "Keeps no address", change: Verifying(ApprovalMode.Automatic), fields: [NameField, address]));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Does.Contain("$.settings.submission.verification.emailField"), string.Join(", ", refused.Problems));
    }

    [Test]
    public async Task ASentLinksCode_StaysInNoStoredCopyOfTheMail_AndTheMailedLinkStillConfirms()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();
        var code = link[(link.IndexOf("?t=", StringComparison.Ordinal) + 3)..];
        var stored = (await GetRequiredService<IEmailRepository>().ListForEntryAsync(entry.Id)).Single();

        await PostAsync(link);

        Assert.Multiple(async () =>
        {
            Assert.That(stored.State, Is.EqualTo(EmailState.Sent));
            Assert.That(stored.HtmlBody + stored.TextBody, Does.Not.Contain(code).And.Not.Contain(TokenOf(link)), "nobody who reads the database or a backup can confirm the address (ADR-0226)");
            Assert.That(stored.HtmlBody, Does.Contain($"verify?t={LinkSecrets.Removed}"), "the link still names its page");
            Assert.That(stored.TextBody, Does.Contain($"verify?t={LinkSecrets.Removed}"));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.Not.EqualTo(EntryState.Unverified), "the code the visitor got still works");
        });
    }

    [Test]
    public async Task AConfirmedEntry_IsToldOfOnce()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();
        var whileWaiting = Heard.Heard.OfType<EntrySubmittedNotification>().Count();

        await PostAsync(link);
        await PostAsync(link);

        Assert.Multiple(() =>
        {
            Assert.That(whileWaiting, Is.Zero);
            Assert.That(Heard.Heard.OfType<EntrySubmittedNotification>().Single().EntryKey, Is.EqualTo(entry.Key));
        });
    }

    [Test]
    public async Task AnEntryNobodyConfirms_TakesItsWaitingMailAlong_AndIsInTheAuditLog()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        var changes = GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<EntryChangeQueue>().Single();

        // Only this queue runs, so the mail it queues is still waiting when the entry's time is over.
        await changes.PassAsync("test", DateTimeOffset.MaxValue, CancellationToken.None);
        var waiting = (await Emails.ListForEntryAsync(entry.Id)).Single().State;
        Clock.Advance(TimeSpan.FromDays(7) + EntryVerification.Grace);
        await changes.PassAsync("test", DateTimeOffset.MaxValue, CancellationToken.None);
        await SendDueAsync();

        var audited = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = EntryVerification.ExpiredAction }, new PageRequest(0, 10));
        Assert.Multiple(async () =>
        {
            Assert.That(waiting, Is.Not.EqualTo(EmailState.Sent));
            Assert.That(Written(), Is.Empty, "the mail that was waiting went with it");
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
            Assert.That(await Emails.ListForEntryAsync(entry.Id), Is.Empty);
            Assert.That(audited.Items.Single().Details, Does.Contain(entry.Key.ToString()));
        });
    }

    [Test]
    public async Task ANewLink_GoesOnlyPastItsTime_AndOncePerHour()
    {
        var form = await FormAsync();
        await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        var early = await PostAsync(link, "resend");
        await SendDueAsync();
        var afterEarly = Written().Count;
        Clock.Advance(TimeSpan.FromDays(7));
        await PostAsync(link, "resend");
        await PostAsync(link, "resend");
        await SendDueAsync();
        var afterTwo = Written().Count;
        Clock.Advance(EntryVerification.ResendPause);
        await PostAsync(link, "resend");
        await SendDueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(early.Html, Does.Contain("Confirm my address"));
            Assert.That(afterEarly, Is.EqualTo(1));
            Assert.That(afterTwo, Is.EqualTo(2));
            Assert.That(Written(), Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task ThePage_SpeaksTheVisitorsLanguage()
    {
        var row = (await FormRows.GetAsync(await FormAsync()))!;
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Unverified,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "de-DE",
        }))!;
        var token = GetRequiredService<IVerificationTokens>().Issue(entry.Key, Clock.GetUtcNow() + TimeSpan.FromDays(7));

        var page = await GetAsync($"{Root}/verify?t={Uri.EscapeDataString(token)}");

        Assert.That(page.Html, Does.Contain("lang=\"de").And.Contain("Adresse"));
    }

    [Test]
    public async Task AnEntryWaits_RunsNoAction_AndItsMailGoesToTheAddress()
    {
        var form = await FormAsync(Action("first"));

        var entry = await SubmitAsync(form, Ada);
        await SendDueAsync();

        var mail = Written().Single();
        Assert.Multiple(async () =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Unverified));
            Assert.That(entry.VerifiedUtc, Is.Null);
            Assert.That(await Runs.ListForEntryAsync(entry.Id), Is.Empty);
            Assert.That(Scripted.Started, Is.Empty);
            Assert.That(mail.To.Mailboxes.Single().Address, Is.EqualTo("ada@jfb.test"));
            Assert.That(mail.Subject, Is.EqualTo("Please confirm your email address"));
            Assert.That(((TextPart)mail.Body!).Text, Does.Contain("Newsletter").And.Contain("Confirm my address"));
            Assert.That((await Emails.ListForEntryAsync(entry.Id)).Single().Kind, Is.EqualTo(EmailKind.Verification));
        });
    }

    [Test]
    public async Task TheLinksPage_ChangesNothing_HoweverOftenAScannerFollowsIt()
    {
        var form = await FormAsync(Action("first"));
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        var first = await GetAsync(link);
        var second = await GetAsync(link);
        await Pass.RunAsync(CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(first.Status, Is.EqualTo(200));
            Assert.That(first.Html, Does.Contain("<form method=\"post\"").And.Contain("Confirm my address"));
            Assert.That(second.Html, Is.EqualTo(first.Html));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Unverified));
            Assert.That(Scripted.Started, Is.Empty);
        });
    }

    [Test]
    public async Task TheButton_ConfirmsOnce_AndTheSubmittedAndVerifiedActionsRunTogether_InTheOrderShown()
    {
        var form = await FormAsync(Action("verified first", ActionTrigger.Verified), Action("submitted"), Action("verified last", ActionTrigger.Verified));
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        var confirmed = await PostAsync(link);
        var again = await PostAsync(link);
        var runs = await DrainAsync(entry.Id);

        var stored = (await Entries.GetByIdAsync(entry.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(confirmed.Status, Is.EqualTo(200));
            Assert.That(confirmed.Html, Does.Contain("Your address is confirmed"));
            Assert.That(again.Html, Does.Contain("Your address is confirmed"), "a second press changes nothing");
            Assert.That(stored.State, Is.EqualTo(EntryState.Approved));
            Assert.That(stored.VerifiedUtc, Is.Not.Null);
            Assert.That(runs, Has.Count.EqualTo(3));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "verified first", "submitted", "verified last" }));
        });
    }

    [Test]
    public async Task OnAFormThatWaitsForApproval_AConfirmedEntryWaitsNext()
    {
        var form = await PublishAsync(name: "Newsletter", change: Verifying(ApprovalMode.Manual, Action("on approval", ActionTrigger.Approved), Action("verified", ActionTrigger.Verified)), fields: [NameField, EmailField]);
        var entry = await SubmitAsync(form, Ada);

        await PostAsync(await SentLinkAsync());
        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Submitted));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "verified" }));
        });
    }

    [Test]
    public async Task AConfirmation_RunsAtOnce_WithNoUndoWindow()
    {
        var form = await FormAsync(Action("first"));
        var entry = await SubmitAsync(form, Ada);

        await PostAsync(await SentLinkAsync());
        await Pass.RunAsync(CancellationToken.None);

        Assert.That((await Runs.ListForEntryAsync(entry.Id)).Single().State, Is.EqualTo(ActionRunState.Succeeded));
    }

    [Test]
    public async Task ALinkThatIsNoLink_ConfirmsNothing()
    {
        var form = await FormAsync(Action("first"));
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        var changed = await GetAsync(link[..^4] + "AAAA");
        var empty = await GetAsync($"{Root}/verify");
        var posted = await PostAsync($"{Root}/verify?t=nonsense");

        Assert.Multiple(async () =>
        {
            Assert.That(changed.Status, Is.EqualTo(404));
            Assert.That(empty.Status, Is.EqualTo(404));
            Assert.That(posted.Status, Is.EqualTo(404));
            Assert.That(WebUtility.HtmlDecode(posted.Html), Does.Contain("This link doesn't work").And.Not.Contain("<form"));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Unverified));
        });
    }

    [Test]
    public async Task ThePage_SpeaksTheEntrysLanguage_AndKeepsItsTokenToItself()
    {
        var form = await FormAsync();
        await SubmitAsync(form, Ada);
        using var answer = await SendAsync(HttpMethod.Get, await SentLinkAsync());

        Assert.Multiple(() =>
        {
            Assert.That(answer.Headers.GetValues("Referrer-Policy").Single(), Is.EqualTo("same-origin"));
            Assert.That(answer.Headers.GetValues("X-Robots-Tag").Single(), Does.Contain("noindex"));
            Assert.That(answer.Headers.GetValues("Content-Security-Policy").Single(), Does.Contain("default-src 'none'").And.Contain("form-action 'self'"));
            Assert.That(answer.Headers.CacheControl?.NoStore, Is.True);
        });
    }

    [Test]
    public async Task TheVerificationMail_ObeysTheVisitorAddressLimits()
    {
        MailLimits.PerRecipientPerDay = 1;
        var form = await FormAsync();

        var first = await SubmitAsync(form, Ada);
        var second = await SubmitAsync(form, Ada);
        await SendDueAsync();

        var limited = (await Emails.ListForEntryAsync(second.Id)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(Written(), Has.Count.EqualTo(1));
            Assert.That((await Emails.ListForEntryAsync(first.Id)).Single().State, Is.EqualTo(EmailState.Sent));
            Assert.That(limited.State, Is.EqualTo(EmailState.Limited), "shown as limited, never silently dropped");
        });
    }

    [Test]
    public async Task AnEntryInSpam_GetsNoMail()
    {
        var form = await FormAsync();

        using var content = new StringContent("""{"revision":1,"values":{"name":"Ada","email":"ada@jfb.test"},"honeypot":"Buy now"}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        await SendDueAsync();

        Assert.That(Written(), Is.Empty);
    }

    [Test]
    public async Task NotSpam_OnAVerificationForm_SendsTheEntryToUnverified_WithItsMail()
    {
        var form = await FormAsync(Action("first"));
        var entry = await SpamAsync(form);

        var moved = await Folder.MarkAsNotSpamAsync(entry.Key, null);
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(moved!.State, Is.EqualTo(EntryState.Unverified));
            Assert.That(Written().Single().To.Mailboxes.Single().Address, Is.EqualTo("ada@jfb.test"));
            Assert.That(await Runs.ListForEntryAsync(entry.Id), Is.Empty);
        });
    }

    [Test]
    public async Task AUsersNotSpam_SendsItsMail_OnceTheWindowIsOver_AndNotAfterAnUndo()
    {
        var user = Guid.NewGuid();
        var form = await FormAsync(Action("first"));
        var kept = await SpamAsync(form);
        var undone = await SpamAsync(form);
        var changes = GetRequiredService<EntryStateChanges>();

        await changes.ChangeAsync(form, [kept.Key], EntryState.Submitted, user);
        var undoing = await changes.ChangeAsync(form, [undone.Key], EntryState.Submitted, user);
        await SendDueAsync();
        var inside = Written().Count;
        await changes.UndoAsync(form, undoing.ChangeKey, user);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await SendDueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(inside, Is.Zero);
            Assert.That(Written(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AUsersNotSpam_AsksForItsMailAndItsExpiry_AsTheQueueClaimsIt()
    {
        var form = await FormAsync();
        var entry = await SpamAsync(form);

        await GetRequiredService<EntryStateChanges>().ChangeAsync(form, [entry.Key], EntryState.Submitted, Guid.NewGuid());
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<EntryChangeQueue>().Single().PassAsync("test", DateTimeOffset.MaxValue, CancellationToken.None);

        var due = (await Events.ListForEntryAsync(entry.Id)).Select(item => item.Kind).ToList();
        Assert.That(due, Does.Contain(EntryVerification.ExpiresEvent).And.Contain(EntryVerification.RequestedEvent), "both come in the claim's own unit");
    }

    [Test]
    public async Task ALinkPastItsTime_ConfirmsNothing_AndANewOneDoes()
    {
        var form = await FormAsync(Action("first"));
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        Clock.Advance(TimeSpan.FromDays(7));
        var page = await GetAsync(link);
        var pressed = await PostAsync(link);
        var state = (await Entries.GetByIdAsync(entry.Id))!.State;
        var resent = await PostAsync(link, "resend");
        await SendDueAsync();
        var fresh = Written().Select(LinkOf).Single(each => each != link);
        var confirmed = await PostAsync(fresh);

        Assert.Multiple(async () =>
        {
            Assert.That(page.Html, Does.Contain("This link is too old").And.Contain("name=\"action\" value=\"resend\""));
            Assert.That(pressed.Html, Does.Contain("This link is too old"));
            Assert.That(state, Is.EqualTo(EntryState.Unverified));
            Assert.That(resent.Html, Does.Contain("A new link is on its way"));
            Assert.That(confirmed.Html, Does.Contain("Your address is confirmed"));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Approved));
        });
    }

    [Test]
    public async Task ANewLink_ObeysTheLimitsToo()
    {
        MailLimits.PerRecipientPerDay = 1;
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        Clock.Advance(TimeSpan.FromDays(7));
        await PostAsync(link, "resend");
        await PostAsync(link, "resend");
        Clock.Advance(TimeSpan.FromSeconds(1));
        await SendDueAsync();

        Assert.That((await Emails.ListForEntryAsync(entry.Id)).Count(mail => mail.State == EmailState.Sent), Is.LessThanOrEqualTo(2));
    }

    [Test]
    public async Task AnEntryNobodyConfirms_IsDeleted_AWeekAfterItsLinksTime()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        await SendDueAsync();

        Clock.Advance(TimeSpan.FromDays(7) + EntryVerification.Grace - TimeSpan.FromSeconds(1));
        await SendDueAsync();
        var before = await Entries.GetByIdAsync(entry.Id);
        Clock.Advance(TimeSpan.FromSeconds(2));
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.Not.Null);
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
        });
    }

    [Test]
    public async Task ANewLink_GivesTheEntryMoreTime()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        var link = await SentLinkAsync();

        Clock.Advance(TimeSpan.FromDays(10));
        await PostAsync(link, "resend");
        await SendDueAsync();
        Clock.Advance(TimeSpan.FromDays(5));
        await SendDueAsync();

        Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Not.Null, "past the first mail's time, within the second's");
    }

    [Test]
    public async Task AConfirmedEntry_IsNotDeleted_WhenItsTimeComes()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Ada);
        await PostAsync(await SentLinkAsync());

        Clock.Advance(TimeSpan.FromDays(30));
        await SendDueAsync();

        Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Not.Null);
    }

    [Test]
    public async Task AnEntryWithoutAnAddress_SendsNothing_AndStillExpires()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");
        await SendDueAsync();
        var sent = Written().Count;

        Clock.Advance(TimeSpan.FromDays(15));
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(sent, Is.Zero);
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
        });
    }

    [Test]
    public async Task AValueKeptUntilTheActionsRun_SurvivesTheWait_ServesTheRuns_AndGoesAfterTheLast()
    {
        string? seen = null;
        Scripted.Scripts["verified"] = (context, _) =>
        {
            seen = context.Scope.Answers.Find(Kept.Key)?.Values[0].Text;
            return Task.FromResult(ActionResult.Succeeded);
        };
        var form = await FormAsync(Action("verified", ActionTrigger.Verified));
        var entry = await SubmitAsync(form, """{"name":"Ada","email":"ada@jfb.test","kept":"for a moment"}""");
        var link = await SentLinkAsync();
        var fieldId = (await FormRows.ListFieldsAsync((await FormRows.GetAsync(form))!.Id)).Single(field => field.Values.FieldKey == Kept.Key).Id;
        async Task<bool> HeldAsync() => (await Entries.ListValuesAsync([entry.Id])).Any(value => value.FieldId == fieldId);

        var whileWaiting = await HeldAsync();
        await PostAsync(link);
        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(whileWaiting, Is.True);
            Assert.That(seen, Is.EqualTo("for a moment"));
            Assert.That(await HeldAsync(), Is.False);
        });
    }

    [Test]
    public async Task AValueKeptUntilTheActionsRun_SurvivesSpam_AndServesTheRunsThatFollow()
    {
        string? seen = null;
        Scripted.Scripts["first"] = (context, _) =>
        {
            seen = context.Scope.Answers.Find(Kept.Key)?.Values[0].Text;
            return Task.FromResult(ActionResult.Succeeded);
        };
        var form = await FormAsync(Action("first"));
        var entry = await SpamAsync(form, """{"revision":1,"values":{"name":"Ada","email":"ada@jfb.test","kept":"for a moment"},"honeypot":"Buy now"}""");

        var fieldId = (await FormRows.ListFieldsAsync((await FormRows.GetAsync(form))!.Id)).Single(field => field.Values.FieldKey == Kept.Key).Id;
        async Task<bool> HeldAsync() => (await Entries.ListValuesAsync([entry.Id])).Any(value => value.FieldId == fieldId);

        var inSpam = await HeldAsync();
        await Folder.MarkAsNotSpamAsync(entry.Key, null);
        await PostAsync(await SentLinkAsync());
        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(inSpam, Is.True);
            Assert.That(seen, Is.EqualTo("for a moment"));
            Assert.That(await HeldAsync(), Is.False);
        });
    }

    [TestCase(ActionTrigger.Submitted)]
    [TestCase(ActionTrigger.Verified)]
    [TestCase(ActionTrigger.Approved)]
    public async Task AValueKeptUntilTheActionsRun_ServesAFormsOnlyTrigger_AndGoesAfterIt(ActionTrigger trigger)
    {
        string? seen = null;
        Scripted.Scripts["only"] = (context, _) =>
        {
            seen = context.Scope.Answers.Find(Kept.Key)?.Values[0].Text;
            return Task.FromResult(ActionResult.Succeeded);
        };
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: Verifying(ApprovalMode.Manual, Action("only", trigger)), fields: [NameField, EmailField, Kept]);
        var entry = await SubmitAsync(form, """{"name":"Ada","email":"ada@jfb.test","kept":"for a moment"}""");
        var fieldId = (await FormRows.ListFieldsAsync((await FormRows.GetAsync(form))!.Id)).Single(field => field.Values.FieldKey == Kept.Key).Id;
        async Task<bool> HeldAsync() => (await Entries.ListValuesAsync([entry.Id])).Any(value => value.FieldId == fieldId);

        var whileUnverified = await HeldAsync();
        await PostAsync(await SentLinkAsync());
        var whileWaiting = trigger != ActionTrigger.Approved || await HeldAsync();
        if (trigger == ActionTrigger.Approved)
        {
            await GetRequiredService<EntryStateChanges>().ChangeAsync(form, [entry.Key], EntryState.Approved, null);
        }

        await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(whileUnverified, Is.True);
            Assert.That(whileWaiting, Is.True, "an entry waiting for approval still has its run to come");
            Assert.That(seen, Is.EqualTo("for a moment"));
            Assert.That(await HeldAsync(), Is.False);
        });
    }

    [TestCase("en", "Please confirm your email address")]
    [TestCase("de-CH", "Bitte best")]
    [TestCase("fr", "Veuillez confirmer")]
    [TestCase("it", "Conferma")]
    [TestCase("nl-BE", "Bevestig")]
    [TestCase("es", "Confirme")]
    [TestCase("da", "Bekr")]
    [TestCase("ja", "Please confirm your email address")]
    public void TheWords_FollowTheLanguage_OrEnglish(string culture, string start) =>
        Assert.That(VerificationWords.Of(culture).Subject, Does.StartWith(start));

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<HeardEntries>();
        builder.AddNotificationAsyncHandler<EntrySubmittedNotification, HeardEntriesHandler>();
    }

    private async Task<StoredEntry> SpamAsync(Guid form, string body = """{"revision":1,"values":{"name":"Ada","email":"ada@jfb.test"},"honeypot":"Buy now"}""")
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var row = (await FormRows.GetAsync(form))!;
        return (await Entries.GetByIdAsync((await Entries.ListIdsAsync(row.Id, 10))[^1]))!;
    }
}
