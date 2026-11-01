using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// Taking an entry out of Spam (ROADMAP 7.1d, section 4.4, ADR-0012, ADR-0140): it moves to Submitted and queues the
/// <c>submitted</c> actions the rules chose when it came in, waiting out the undo window when a user did it; it takes its
/// places again; on a form with email verification and an address not confirmed, it moves to Unverified and queues none.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntrySpamFolderTests : PublicApiTestBase
{
    private const string Spammy = """{"revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""";

    private static readonly FormAction Always = new() { Key = Guid.Parse("71d00000-0000-4000-8000-000000000001"), Type = "email", Name = "Always" };

    private static readonly FormAction OnlyForGrace = new()
    {
        Key = Guid.Parse("71d00000-0000-4000-8000-000000000002"),
        Type = "email",
        Name = "Only for Grace",
        Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Run, Conditions = [new RuleCondition { Field = NameField.Key, Operator = "equals", Value = new ConditionOperand.Text("Grace") }] }],
    };

    private static readonly FormAction OnApproval = new() { Key = Guid.Parse("71d00000-0000-4000-8000-000000000003"), Type = "email", Name = "On approval", Trigger = ActionTrigger.Approved };

    private static readonly FormField Seat = new()
    {
        Key = Guid.Parse("71d00000-0000-4000-8000-000000000004"),
        Type = "dropdown",
        Alias = "seat",
        Label = new LocalizedText("Seat"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 1 }] },
    };

    private static readonly Guid User = Guid.Parse("71d00000-0000-4000-8000-000000000005");

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private IActionRunRepository Runs => GetRequiredService<IActionRunRepository>();

    private EntrySpamFolder Folder => GetRequiredService<EntrySpamFolder>();

    [Test]
    public async Task NotSpam_MovesTheEntryToSubmitted_AndRunsTheActionsTheRulesChose()
    {
        var key = await PublishAsync(change: definition => definition with { Actions = [Always, OnlyForGrace, OnApproval] });
        var entry = await SpamAsync(key);
        var before = await Runs.ListForEntryAsync(entry.Id);

        var moved = await Folder.MarkAsNotSpamAsync(entry.Key, null);

        var runs = await Runs.ListForEntryAsync(entry.Id);
        var timeline = await GetRequiredService<IEntryEventRepository>().ListForEntryAsync(entry.Id);
        var form = (await FormRows.GetAsync(key))!;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stats = await GetRequiredService<IDailyStatRepository>().ListAsync(form.Id, today.AddDays(-1), today.AddDays(1));
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Empty, "none while it is spam");
            Assert.That(moved!.State, Is.EqualTo(EntryState.Submitted));
            Assert.That(runs.Select(run => run.ActionKey), Is.EqualTo(new[] { Always.Key }), "not one whose rule did not match, nor another trigger's");
            Assert.That(runs.Single().Trigger, Is.EqualTo(ActionTrigger.Submitted));
            Assert.That(timeline.Select(item => item.Kind), Does.Contain("notSpam"));
            Assert.That(timeline.Single(item => item.Kind == "notSpam").ChangeKey, Is.EqualTo(runs.Single().ChangeKey), "so Undo finds what the change did");
            Assert.That(stats.Single(stat => stat.Metric == "submissions").Value, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AUsersMove_WaitsOutTheUndoWindow_AndTheSystemsDoesNot()
    {
        var key = await PublishAsync(change: definition => definition with { Actions = [Always] });
        var byUser = await SpamAsync(key);
        var bySystem = await SpamAsync(key);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var moved = await Folder.MarkAsNotSpamAsync(byUser.Key, User);
        _ = await Folder.MarkAsNotSpamAsync(bySystem.Key, null);

        var waiting = (await Runs.ListForEntryAsync(byUser.Id)).Single();
        var atOnce = (await Runs.ListForEntryAsync(bySystem.Id)).Single();
        var timeline = await GetRequiredService<IEntryEventRepository>().ListForEntryAsync(byUser.Id);
        Assert.Multiple(() =>
        {
            Assert.That(moved!.StateChangedByUserKey, Is.EqualTo(User));
            Assert.That(timeline.Single(item => item.Kind == "notSpam").UserKey, Is.EqualTo(User));
            Assert.That(waiting.NextAttemptUtc, Is.GreaterThan(before.AddSeconds(9)), "ten seconds on");
            Assert.That(atOnce.NextAttemptUtc, Is.LessThan(before.AddSeconds(9)));
        });
    }

    [Test]
    public async Task NotSpam_TakesItsPlacesAgain()
    {
        var key = await PublishAsync(fields: [NameField, Seat]);
        var entry = await SpamAsync(key, """{"revision":1,"values":{"name":"Ada","seat":"front"},"honeypot":"Buy now"}""");
        var form = (await FormRows.GetAsync(key))!;
        var option = new OptionPlace(Seat.Key, "front");
        var whileSpam = (await Entries.GetPlacesTakenAsync(form.Id, [option])).GetValueOrDefault(option);

        _ = await Folder.MarkAsNotSpamAsync(entry.Key, null);

        Assert.Multiple(async () =>
        {
            Assert.That(whileSpam, Is.Zero);
            Assert.That((await Entries.GetPlacesTakenAsync(form.Id, [option]))[option], Is.EqualTo(1));
        });
    }

    [Test]
    public async Task OnAFormWithVerification_AnAddressNotConfirmed_GoesToUnverified_AndRunsNothing()
    {
        var email = new FormField { Key = Guid.Parse("71d00000-0000-4000-8000-000000000006"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };
        var key = await PublishAsync(
            change: definition => definition with
            {
                Actions = [Always],
                Settings = definition.Settings with { Submission = definition.Settings.Submission with { Verification = new EmailVerification { EmailField = email.Key } } },
            },
            fields: [NameField, email]);
        var entry = await SpamAsync(key, """{"revision":1,"values":{"name":"Ada","email":"ada@example.com"},"honeypot":"Buy now"}""");

        var moved = await Folder.MarkAsNotSpamAsync(entry.Key, null);

        Assert.Multiple(async () =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Spam), "spam comes before verification");
            Assert.That(moved!.State, Is.EqualTo(EntryState.Unverified));
            Assert.That(await Runs.ListForEntryAsync(entry.Id), Is.Empty);
        });
    }

    [Test]
    public async Task AnEntryNoLongerInSpam_IsRefused_AndRunsNothingAgain()
    {
        var key = await PublishAsync(change: definition => definition with { Actions = [Always] });
        var entry = await SpamAsync(key);
        _ = await Folder.MarkAsNotSpamAsync(entry.Key, null);

        Assert.ThrowsAsync<FormBuilderConcurrencyException>(() => Folder.MarkAsNotSpamAsync(entry.Key, null));
        Assert.That(await Runs.ListForEntryAsync(entry.Id), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task AnEntryThatIsNotThere_IsNull() => Assert.That(await Folder.MarkAsNotSpamAsync(Guid.NewGuid(), null), Is.Null);

    private async Task<StoredEntry> SpamAsync(Guid key, string body = Spammy)
    {
        var challenge = await ChallengeAsync(key);
        var posted = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        var submissionId = Guid.NewGuid();
        posted["submissionId"] = submissionId.ToString();
        posted["challenge"] = challenge;
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(posted.ToJsonString(), Encoding.UTF8, "application/json"));
        Assert.That(answer.IsSuccessStatusCode, Is.True, await answer.Content.ReadAsStringAsync());
        return (await Entries.GetBySubmissionAsync((await FormRows.GetAsync(key))!.Id, submissionId))!;
    }
}
