using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using MimeKit;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>The team recipients hear of a delivery that failed for good (section 4.8, ROADMAP 8.6).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class DeliveryNoticeTests : EmailTestBase
{
    private const string Team = "team@jfb.test";

    private IFormBuilderSettingsService SiteSettings => GetRequiredService<IFormBuilderSettingsService>();

    private async Task SetTeamAsync(params string[] team)
    {
        var current = await SiteSettings.GetAsync<EmailSettings>();
        await SiteSettings.SaveAsync(current.Value with { TeamRecipients = team }, current.Version, Editor);
    }

    private async Task<StoredEntry> FailingEntryAsync(Guid form, string answer = "Ada Lovelace") =>
        await SubmitAsync(form, $$"""{"name":"{{answer}}"}""");

    private Task<Guid> FailingFormAsync()
    {
        Scripted.Scripts["never"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        return ActionFormAsync(Action("never"));
    }

    [Test]
    public async Task AnActionThatFailsForGood_TellsTheTeam_WithNoAnswer()
    {
        await SetTeamAsync(Team);
        var form = await FailingFormAsync();

        await DrainAsync((await FailingEntryAsync(form)).Id);
        await SendDueAsync();

        var notice = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(notice.To.Mailboxes.Single().Address, Is.EqualTo(Team));
            Assert.That(notice.Subject, Does.StartWith("A delivery failed"));
            Assert.That(((TextPart)notice.Body!).Text, Does.Contain("refused").And.Not.Contain("Ada"));
        });
    }

    [Test]
    public async Task ManyFailures_TellTheTeamOnceAnHour_PerForm()
    {
        await SetTeamAsync(Team);
        var form = await FailingFormAsync();

        await DrainAsync((await FailingEntryAsync(form)).Id);
        await DrainAsync((await FailingEntryAsync(form, "Ben")).Id);
        await SendDueAsync();
        var inTheHour = Written().Count;
        await DrainAsync((await FailingEntryAsync(await FailingFormAsync(), "Cy")).Id);
        await SendDueAsync();
        var anotherForm = Written().Count;
        Clock.Advance(DeliveryNotices.Pause);
        await DrainAsync((await FailingEntryAsync(form, "Di")).Id);
        await SendDueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(inTheHour, Is.EqualTo(1));
            Assert.That(anotherForm, Is.EqualTo(2));
            Assert.That(Written(), Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task WithNoTeam_NobodyIsTold()
    {
        await SetTeamAsync();
        var form = await FailingFormAsync();

        await DrainAsync((await FailingEntryAsync(form)).Id);
        await SendDueAsync();

        Assert.That(Written(), Is.Empty);
    }

    [Test]
    public async Task AnActionThatWillBeTriedAgain_TellsNobodyYet()
    {
        await SetTeamAsync(Team);
        Scripted.Scripts["again"] = (_, _) => Task.FromResult(ActionResult.TryAgain("busy"));
        var form = await ActionFormAsync(Action("again"));

        await SubmitAsync(form);
        await Pass.RunAsync(CancellationToken.None);
        await SendDueAsync();

        Assert.That(Written(), Is.Empty);
    }

    [Test]
    public async Task AMailThatFailsForGood_TellsTheTeam_AndATestOrANoticeTellsNobody()
    {
        await SetTeamAsync(Team);
        var notices = GetRequiredService<DeliveryNotices>();
        var form = await PublishAsync(name: "Registration");
        var entry = await SubmitAsync(form);
        var mail = (await Outbox.QueueAsync(Mail("visitor@jfb.test") with { EntryId = entry.Id }))!;
        StoredEmail Stored(EmailKind kind) => mail with { Kind = kind, LastError = new DeliveryError(ActionAttempts.Threw, "NoMailbox") };
        List<MimeMessage> ToTeam() => [.. Written().Where(each => each.To.Mailboxes.Single().Address == Team)];

        await notices.TellOfMailAsync(Stored(EmailKind.Test));
        await notices.TellOfMailAsync(Stored(EmailKind.Notice));
        await SendDueAsync();
        var silent = ToTeam().Count;
        await notices.TellOfMailAsync(Stored(EmailKind.Action));
        await SendDueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(silent, Is.Zero);
            Assert.That(((TextPart)ToTeam().Single().Body!).Text, Does.Contain("Registration").And.Contain("NoMailbox"));
        });
    }
}
