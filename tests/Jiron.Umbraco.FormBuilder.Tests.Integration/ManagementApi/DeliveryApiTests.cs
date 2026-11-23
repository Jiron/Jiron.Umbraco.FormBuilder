using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Deliveries;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Activity > Deliveries through the management API (ROADMAP 12.4b, ADR-0219): every mail and every run that queued none,
/// problems first, with its action's name and a webhook's host from the version; the filters and the search; a limited
/// mail's limit with no retry; a retry of a failed mail, and of an uncertain mail or run only when confirmed, each audited;
/// a retry refused without <c>manageActions</c> on the form, while a mail of no entry needs <c>manageSettings</c> alone;
/// and the paused flag.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class DeliveryApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IActionRunRepository Runs => GetRequiredService<IActionRunRepository>();

    private IEmailRepository Mail => GetRequiredService<IEmailRepository>();

    private static readonly FormAction EmailTheTeam = new()
    {
        Key = Guid.Parse("40000000-0000-4000-8000-0000000000e1"),
        Type = EmailActionType.Alias,
        Name = "Email the team",
    };

    private static readonly FormAction SendToTheCrm = new()
    {
        Key = Guid.Parse("40000000-0000-4000-8000-0000000000e2"),
        Type = WebhookActionType.Alias,
        Name = "Send to the CRM",
        Settings = SettingsObject.Parse("""{"target":"url","url":"https://crm.example.com/hook?token=abc"}"""),
    };

    [SetUp]
    public void Unpause() => GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Actions.Paused = false;

    [Test]
    public async Task TheLog_ListsEveryMailAndTheRunsThatQueuedNone_ProblemsFirst_WithTheActionsNameAndAWebhooksHost()
    {
        var site = await SeedAsync();

        var answer = await CallAsync(Admin, HttpMethod.Get, "delivery");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["total"]!.GetValue<long>(), Is.EqualTo(6));
            Assert.That(answer.Body["paused"]!.GetValue<bool>(), Is.False);
            var items = answer.Body["items"]!.AsArray();
            var states = items.Select(item => item!["state"]!.GetValue<string>()).ToList();
            Assert.That(states.Take(5), Is.EquivalentTo(new[] { "failed", "uncertain", "limited", "uncertain", "failed" }), "the problems first");
            Assert.That(states[5], Is.EqualTo("waiting"));
            Assert.That(items.Count(item => item!["kind"]!.GetValue<string>() == "run"), Is.EqualTo(1), "the email action's run stands behind its mail");
            var webhook = items.Single(item => item!["kind"]!.GetValue<string>() == "run")!;
            Assert.That(webhook["id"]!.GetValue<long>(), Is.EqualTo(site.WebhookRun));
            Assert.That(webhook["actionName"]!.GetValue<string>(), Is.EqualTo("Send to the CRM"));
            Assert.That(webhook["target"]!["kind"]!.GetValue<string>(), Is.EqualTo("host"));
            Assert.That(webhook["target"]!["text"]!.GetValue<string>(), Is.EqualTo("crm.example.com"), "the host, never the address");
            Assert.That(webhook["formName"]!.GetValue<string>(), Is.EqualTo("Job application"));
            Assert.That(webhook["entryNumber"]!.GetValue<long>(), Is.EqualTo(1));
            var failed = items.Single(item => item!["id"]!.GetValue<long>() == site.FailedMail && item["kind"]!.GetValue<string>() == "email")!;
            Assert.That(failed["recipients"]!.AsArray().Select(to => to!.GetValue<string>()), Is.EqualTo(new[] { "team@example.com", "hr@example.com" }));
            Assert.That(failed["actionName"]!.GetValue<string>(), Is.EqualTo("Email the team"));
            Assert.That(failed["emailKind"]!.GetValue<string>(), Is.EqualTo("action"));
            Assert.That(failed.AsObject().ContainsKey("subject"), Is.False, "never a subject");
            var limited = items.Single(item => item!["state"]!.GetValue<string>() == "limited")!;
            Assert.That(limited["errorCode"]!.GetValue<string>(), Is.EqualTo(EmailRepository.RecipientLimit));
            var test = items.Single(item => item!["id"]!.GetValue<long>() == site.TestMail && item["kind"]!.GetValue<string>() == "email")!;
            Assert.That(test["formKey"], Is.Null, "a mail of no entry has no form");
            Assert.That(test["emailKind"]!.GetValue<string>(), Is.EqualTo("test"));
        });
    }

    [Test]
    public async Task TheFilters_AndTheSearch_NarrowTheLog_AndAFilterItDoesNotKnow_IsRefused()
    {
        var site = await SeedAsync();

        async Task<long> TotalAsync(string query) => (await CallAsync(Admin, HttpMethod.Get, $"delivery?{query}")).Body!["total"]!.GetValue<long>();

        var unknown = await CallAsync(Admin, HttpMethod.Get, "delivery?states=lost");
        var tooMany = await CallAsync(Admin, HttpMethod.Get, "delivery?take=101");
        Assert.Multiple(async () =>
        {
            Assert.That(await TotalAsync("states=problems"), Is.EqualTo(5));
            Assert.That(await TotalAsync("states=waiting"), Is.EqualTo(1));
            Assert.That(await TotalAsync("states=delivered"), Is.EqualTo(0));
            Assert.That(await TotalAsync("kind=webhook"), Is.EqualTo(1));
            Assert.That(await TotalAsync("kind=email"), Is.EqualTo(5));
            Assert.That(await TotalAsync("kind=other"), Is.EqualTo(0));
            Assert.That(await TotalAsync("search=visitor%40example"), Is.EqualTo(2), "a mail's recipients");
            Assert.That(await TotalAsync("search=job%20app"), Is.EqualTo(5), "a form's name");
            Assert.That(await TotalAsync($"form={site.Form}"), Is.EqualTo(5));
            Assert.That(await TotalAsync("days=1"), Is.EqualTo(6));
            Assert.That(await TotalAsync("take=2&skip=4"), Is.EqualTo(6));
            Assert.That(unknown.Outcome, Is.EqualTo("400 invalid_request"), unknown.ToString());
            Assert.That(tooMany.Outcome, Is.EqualTo("400 invalid_request"), tooMany.ToString());
        });
    }

    [Test]
    public async Task ALimitedMail_NamesItsLimit_AndCantBeRetried()
    {
        var site = await SeedAsync();
        var limits = GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Limits.Mail;

        var details = await CallAsync(Admin, HttpMethod.Get, $"delivery/email/{site.LimitedMail}");
        var retry = await CallAsync(Admin, HttpMethod.Post, $"delivery/email/{site.LimitedMail}/retry", new { confirmed = true });

        Assert.Multiple(() =>
        {
            Assert.That(details.Outcome, Is.EqualTo("200"), details.ToString());
            Assert.That(details.Body!["retryable"]!.GetValue<bool>(), Is.False);
            Assert.That(details.Body["mayRetry"]!.GetValue<bool>(), Is.False);
            Assert.That(details.Body["limit"]!["kind"]!.GetValue<string>(), Is.EqualTo("recipient"));
            Assert.That(details.Body["limit"]!["count"]!.GetValue<int>(), Is.EqualTo(limits.PerRecipientPerDay));
            Assert.That(details.Body["limit"]!["key"]!.GetValue<string>(), Is.EqualTo("JironFormBuilder:Limits:Mail:PerRecipientPerDay"));
            Assert.That(retry.Outcome, Is.EqualTo("409 run_not_retryable"), retry.ToString());
        });
    }

    [Test]
    public async Task AFailedMail_IsQueuedAgain_AndAnUncertainOne_OnlyWhenConfirmed_EachAudited()
    {
        var site = await SeedAsync();

        var failed = await CallAsync(Admin, HttpMethod.Post, $"delivery/email/{site.FailedMail}/retry", new { });
        var unconfirmed = await CallAsync(Admin, HttpMethod.Post, $"delivery/email/{site.UncertainMail}/retry", new { });
        var stillUncertain = (await Mail.GetAsync(site.UncertainMail))!.State;
        var confirmed = await CallAsync(Admin, HttpMethod.Post, $"delivery/email/{site.UncertainMail}/retry", new { confirmed = true });
        var gone = await CallAsync(Admin, HttpMethod.Get, "delivery/email/999999");
        var audited = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = DeliveryLog.EmailRetriedAction }, PageRequest.First(10));

        Assert.Multiple(async () =>
        {
            Assert.That(failed.Outcome, Is.EqualTo("200"), failed.ToString());
            Assert.That(failed.Body!["delivery"]!["state"]!.GetValue<string>(), Is.EqualTo("waiting"));
            Assert.That((await Mail.GetAsync(site.FailedMail))!.State, Is.EqualTo(EmailState.Queued));
            Assert.That(unconfirmed.Outcome, Is.EqualTo("409 retry_needs_confirmation"), unconfirmed.ToString());
            Assert.That(stillUncertain, Is.EqualTo(EmailState.Uncertain));
            Assert.That(confirmed.Outcome, Is.EqualTo("200"), confirmed.ToString());
            Assert.That((await Mail.GetAsync(site.UncertainMail))!.State, Is.EqualTo(EmailState.Queued));
            Assert.That(gone.Outcome, Is.EqualTo("404 delivery_not_found"), gone.ToString());
            Assert.That(audited.Items, Has.Count.EqualTo(2));
            Assert.That(audited.Items.All(entry => entry.EntityType == FormAudit.EntityType && entry.EntityKey == site.Form), Is.True);
            Assert.That(audited.Items[0].Details, Does.Contain("\"uncertain\":true"));
        });
    }

    [Test]
    public async Task AnUncertainRun_IsQueuedAgain_OnlyWhenConfirmed()
    {
        var site = await SeedAsync();

        // Paused, so no worker claims the run once it is queued again.
        GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Actions.Paused = true;

        var unconfirmed = await CallAsync(Admin, HttpMethod.Post, $"delivery/run/{site.WebhookRun}/retry", new { confirmed = false });
        var confirmed = await CallAsync(Admin, HttpMethod.Post, $"delivery/run/{site.WebhookRun}/retry", new { confirmed = true });

        Assert.Multiple(async () =>
        {
            Assert.That(unconfirmed.Outcome, Is.EqualTo("409 retry_needs_confirmation"), unconfirmed.ToString());
            Assert.That(confirmed.Outcome, Is.EqualTo("200"), confirmed.ToString());
            Assert.That(confirmed.Body!["delivery"]!["state"]!.GetValue<string>(), Is.EqualTo("waiting"));
            Assert.That((await Runs.GetAsync(site.WebhookRun))!.State, Is.EqualTo(ActionRunState.Pending));
        });
    }

    [Test]
    public async Task WhoMayReadButNotManageTheFormsActions_SeesNoRetry_AndIsRefused_WhileAMailOfNoEntryNeedsTheSettingsAlone()
    {
        var site = await SeedAsync();
        await GrantAsync(People.GroupKeys["hr"], PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.ManageSettings);

        var list = await CallAsync("hr", HttpMethod.Get, "delivery");
        var details = await CallAsync("hr", HttpMethod.Get, $"delivery/run/{site.WebhookRun}");
        var run = await CallAsync("hr", HttpMethod.Post, $"delivery/run/{site.WebhookRun}/retry", new { confirmed = true });
        var mail = await CallAsync("hr", HttpMethod.Post, $"delivery/email/{site.FailedMail}/retry", new { });
        var test = await CallAsync("hr", HttpMethod.Post, $"delivery/email/{site.TestMail}/retry", new { });
        var designer = await CallAsync("designer", HttpMethod.Get, "delivery");

        Assert.Multiple(() =>
        {
            Assert.That(list.Outcome, Is.EqualTo("200"), list.ToString());
            Assert.That(details.Body!["retryable"]!.GetValue<bool>(), Is.True);
            Assert.That(details.Body["mayRetry"]!.GetValue<bool>(), Is.False);
            Assert.That(run.Outcome, Is.EqualTo("403 forbidden"), run.ToString());
            Assert.That(run.Body!["permission"]!.GetValue<string>(), Is.EqualTo("manageActions"));
            Assert.That(mail.Outcome, Is.EqualTo("403 forbidden"), mail.ToString());
            Assert.That(test.Outcome, Is.EqualTo("200"), test.ToString());
            Assert.That(designer.Outcome, Is.EqualTo("403 forbidden"), "managing actions does not show the log");
        });
    }

    [Test]
    public async Task AVisitorsAddress_StaysHiddenFromWhoMayNotSeeTheFormsSensitiveAnswers_InTheListTheDetailsAndTheSearch()
    {
        var site = await SeedAsync();
        var hr = People.GroupKeys["hr"];
        await GrantAsync(hr, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.ManageSettings);

        var list = await CallAsync("hr", HttpMethod.Get, "delivery");
        var detail = await CallAsync("hr", HttpMethod.Get, $"delivery/email/{site.LimitedMail}");
        var searched = await CallAsync("hr", HttpMethod.Get, "delivery?search=visitor%40example");
        var byAdmin = await CallAsync(Admin, HttpMethod.Get, "delivery?search=visitor%40example");

        // With the form's sensitive answers, the address shows; the search still needs them on every form.
        await GrantAsync(hr, PermissionTarget.Form, site.Form, FormBuilderPermissions.ViewEntries | FormBuilderPermissions.ViewSensitive);
        var granted = await CallAsync("hr", HttpMethod.Get, $"delivery/email/{site.LimitedMail}");
        var grantedSearch = await CallAsync("hr", HttpMethod.Get, "delivery?search=visitor%40example");

        static IEnumerable<string> Shown(JsonNode? delivery) => delivery!["recipients"]!.AsArray().Select(each => each!.GetValue<string>());
        var visitors = list.Body!["items"]!.AsArray().Where(item => item!["recipientsHidden"]!.GetValue<bool>()).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(visitors, Has.Count.EqualTo(2), "both mails to the visitor");
            Assert.That(visitors.SelectMany(Shown), Is.Empty);
            Assert.That(list.Text, Does.Not.Contain("visitor@example.com"));
            Assert.That(list.Text, Does.Contain("team@example.com"), "an address of the action's own stays");
            Assert.That((detail.Body!["delivery"]!["recipientsHidden"]!.GetValue<bool>(), detail.Text.Contains("visitor@example.com", StringComparison.Ordinal)), Is.EqualTo((true, false)), detail.ToString());
            Assert.That(searched.Body!["total"]!.GetValue<long>(), Is.Zero, "a search finds no hidden address");
            Assert.That(byAdmin.Body!["total"]!.GetValue<long>(), Is.EqualTo(2));
            Assert.That(Shown(granted.Body!["delivery"]), Is.EqualTo(new[] { "visitor@example.com" }), granted.ToString());
            Assert.That(grantedSearch.Body!["total"]!.GetValue<long>(), Is.Zero);
        });
    }

    [Test]
    public async Task TheLog_SaysWhenTheHostPausesSending()
    {
        GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Actions.Paused = true;

        var answer = await CallAsync(Admin, HttpMethod.Get, "delivery");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(answer.Body!["paused"]!.GetValue<bool>(), Is.True);
            Assert.That(answer.Body["total"]!.GetValue<long>(), Is.EqualTo(0));
        });
    }

    [Test]
    public void AWebhooksTarget_IsItsHost_OrTheKeyOfItsReference_AndAChatIsNamedAsSuch()
    {
        static DeliveryTarget? Of(string settings) =>
            DeliveryLog.TargetOf(new FormAction { Key = Guid.NewGuid(), Type = WebhookActionType.Alias, Settings = SettingsObject.Parse(settings) });

        Assert.Multiple(() =>
        {
            Assert.That(Of("""{"target":"url","url":"https://user:pw@hooks.example.org:8443/x?y=z"}"""), Is.EqualTo(new DeliveryTarget(DeliveryTargetKind.Host, "hooks.example.org:8443")));
            Assert.That(Of("""{"url":"$JironFormBuilder:Secrets:Crm"}"""), Is.EqualTo(new DeliveryTarget(DeliveryTargetKind.Reference, "JironFormBuilder:Secrets:Crm")));
            Assert.That(Of("""{"target":"slack"}"""), Is.EqualTo(new DeliveryTarget(DeliveryTargetKind.Slack, null)));
            Assert.That(Of("""{"target":"teams","url":"$JironFormBuilder:Secrets:Sales"}"""), Is.EqualTo(new DeliveryTarget(DeliveryTargetKind.Teams, "JironFormBuilder:Secrets:Sales")));
            Assert.That(Of("""{"target":"url","url":"not an address"}"""), Is.Null);
            Assert.That(DeliveryLog.TargetOf(EmailTheTeam), Is.Null);
        });
    }

    // A form whose entry has an email action's run, which queued a mail that failed and one that is uncertain, a mail to a
    // visitor that waits and one the limit held back, and an uncertain webhook run; and a test mail of no entry that failed.
    // The seeding claims the runs and the mails itself, as a worker would, so sending is paused while it runs: each run
    // and mail it queues wakes the host's worker, which on SQL Server claimed the email run before the seeding could
    // (ROADMAP 26.1, NullReferenceException at the claim). It is unpaused again at the end, as the set-up leaves it.
    private async Task<Seeded> SeedAsync()
    {
        var options = GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue;
        options.Actions.Paused = true;
        try
        {
            return await SeedPausedAsync();
        }
        finally
        {
            options.Actions.Paused = false;
        }
    }

    private async Task<Seeded> SeedPausedAsync()
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest("Job application", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        _ = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!) with { Actions = [EmailTheTeam, SendToTheCrm] }, null, null))!;
        var row = (await GetRequiredService<IFormRepository>().GetAsync(created.Key))!;
        var entry = (await GetRequiredService<IEntryRepository>().CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.DraftVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
        var runs = (await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(),
        [
            new NewActionRun(EmailTheTeam.Key, EmailTheTeam.Type, ActionTrigger.Submitted, 0, false),
            new NewActionRun(SendToTheCrm.Key, SendToTheCrm.Type, ActionTrigger.Submitted, 1, false),
        ]))!;
        var emailRun = runs[0];
        await Runs.SucceedAsync(emailRun.Id, (await Runs.ClaimAsync(emailRun.Id, "server-a", TimeSpan.FromMinutes(5)))!.LockedBy!);
        var webhookRun = runs[1];
        await Runs.CutOffAsync(webhookRun.Id, (await Runs.ClaimAsync(webhookRun.Id, "server-a", TimeSpan.FromMinutes(5)))!.LockedBy!, new DeliveryError("attempt_timed_out"));

        NewEmail Team(string to) => new(EmailKind.Action, to, string.Empty, "Subject", "Text", "en", EntryId: entry.Id, ActionRunId: emailRun.Id);
        var failed = await FailAsync((await Mail.AddAsync(Team("team@example.com, hr@example.com")))!, uncertain: false);
        var uncertain = await FailAsync((await Mail.AddAsync(Team("lead@example.com")))!, uncertain: true);
        var limits = new EmailLimits(1, 200, TimeSpan.FromDays(30));
        NewEmail Visitor() => Team("visitor@example.com") with { RecipientKey = "visitor@example.com" };
        _ = (await Mail.AddWithinLimitsAsync(Visitor(), limits))!;
        var limited = (await Mail.AddWithinLimitsAsync(Visitor(), limits))!;
        var test = await FailAsync((await Mail.AddAsync(new NewEmail(EmailKind.Test, "admin@example.com", string.Empty, "Test", "Text", "en")))!, uncertain: false);
        Assert.That(limited.State, Is.EqualTo(EmailState.Limited), "the second mail to the visitor is held back");
        return new Seeded(created.Key, webhookRun.Id, failed, uncertain, limited.Id, test);
    }

    private async Task<long> FailAsync(StoredEmail mail, bool uncertain)
    {
        var token = (await Mail.ClaimAsync(mail.Id, "server-a", TimeSpan.FromMinutes(5)))!.LockedBy!;
        _ = uncertain
            ? await Mail.CutOffAsync(mail.Id, token, new DeliveryError("attempt_timed_out"), TimeSpan.FromDays(30))
            : await Mail.FailAsync(mail.Id, token, new DeliveryError("exception", "System.Net.Mail.SmtpException"), TimeSpan.FromDays(30));
        return mail.Id;
    }

    private sealed record Seeded(Guid Form, long WebhookRun, long FailedMail, long UncertainMail, long LimitedMail, long TestMail);
}
