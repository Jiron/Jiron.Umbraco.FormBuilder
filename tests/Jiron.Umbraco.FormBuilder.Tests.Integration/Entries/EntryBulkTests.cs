using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>What a site's handler heard of deleted entries, for the tests.</summary>
internal sealed class HeardDeletes
{
    public List<EntryDeletedNotification> Heard { get; } = [];
}

/// <summary>The entries list's bulk bar beyond the state changes (ROADMAP 11.1, ADR-0207): Delete... and Re-run actions.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryBulkTests : ActionTestBase
{
    private static readonly Guid User = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c085");

    private IAuditRepository Audit => GetRequiredService<IAuditRepository>();

    [Test]
    public async Task ADelete_TakesTheEntriesForGood_RecordsTheirNumbers_AndTellsTheSite()
    {
        var form = await ActionFormAsync(Action("Notify"));
        Pause(true);
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        var kept = await SubmitAsync(form);
        var other = await SubmitAsync(await ActionFormAsync());

        var plan = await GetRequiredService<EntryDeletions>().PlanAsync(form, [first.Key, second.Key, other.Key, Guid.NewGuid()]);
        var deleted = await GetRequiredService<EntryDeletions>().DeleteAsync(form, [first.Key, second.Key, other.Key, Guid.NewGuid()], User);
        var audited = (await Audit.QueryAsync(new AuditQuery { Action = EntryDeletions.DeletedAction }, PageRequest.First(5))).Items;

        Assert.Multiple(async () =>
        {
            Assert.That(plan, Is.EqualTo(new EntryDeletion(2, 0)), "what would go");
            Assert.That(deleted, Is.EqualTo(new EntryDeletion(2, 0)), "those of the form that exist");
            Assert.That(await Entries.GetByIdAsync(first.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(second.Id), Is.Null);
            Assert.That(await Runs.ListForEntryAsync(first.Id), Is.Empty, "its runs went with it");
            Assert.That(await Entries.GetByIdAsync(kept.Id), Is.Not.Null);
            Assert.That(await Entries.GetByIdAsync(other.Id), Is.Not.Null, "an entry of another form stays");
            Assert.That(audited, Has.Count.EqualTo(1));
            Assert.That(audited[0].UserKey, Is.EqualTo(User));
            Assert.That(audited[0].EntityKey, Is.EqualTo(form));
            Assert.That(audited[0].Details, Does.Contain($"\"numbers\":[{first.Number},{second.Number}]"));
            Assert.That(GetRequiredService<HeardDeletes>().Heard.Select(heard => (heard.EntryKey, heard.EntryNumber)), Is.EqualTo(new[] { (first.Key, first.Number), (second.Key, second.Number) }));
        });
    }

    [Test]
    public async Task ADeleteOfManyEntries_RecordsThemInAuditEntriesItsDetailsHold()
    {
        var form = await ActionFormAsync();
        var keys = new List<Guid>();
        for (var i = 0; i < 205; i++)
        {
            keys.Add((await SubmitAsync(form)).Key);
        }

        var deleted = await GetRequiredService<EntryDeletions>().DeleteAsync(form, keys, User);
        var audited = (await Audit.QueryAsync(new AuditQuery { Action = EntryDeletions.DeletedAction }, PageRequest.First(5))).Items;

        Assert.Multiple(() =>
        {
            Assert.That(deleted.Entries, Is.EqualTo(205));
            Assert.That(audited.Select(item => item.Details), Has.Exactly(1).Contains("\"entries\":200").And.Exactly(1).Contains("\"entries\":5"));
        });
    }

    [Test]
    public async Task ARerun_SaysWhatWouldRun_AndRunsTheActionsOfThePassedTriggers_InOneNewChange_OnTheAuditLog()
    {
        var notify = Action("Notify");
        var approved = Action("On approval", ActionTrigger.Approved);
        var rejected = Action("On rejection", ActionTrigger.Rejected);
        var form = await ActionFormAsync(notify, approved, rejected);
        Pause(true);
        var waiting = await SubmitAsync(form);
        var accepted = await SubmitAsync(form);
        await GetRequiredService<EntryStateChanges>().ChangeAsync(form, [accepted.Key], EntryState.Approved, userKey: null);
        var spam = await SubmitAsync(form);
        await GetRequiredService<EntryStateChanges>().ChangeAsync(form, [spam.Key], EntryState.Spam, userKey: null);
        var reruns = GetRequiredService<EntryActionReruns>();
        var before = (await Runs.ListForEntryAsync(accepted.Id)).Count;

        var plan = await reruns.PlanAsync(form, [waiting.Key, accepted.Key, spam.Key]);
        var planned = (await Runs.ListForEntryAsync(accepted.Id)).Count;
        var done = await reruns.RerunAsync(form, [waiting.Key, accepted.Key, spam.Key], User);
        var runs = await Runs.ListForEntryAsync(accepted.Id);
        var again = runs.Skip(before).ToList();
        var timeline = await Events.ListForEntryAsync(accepted.Id);
        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = EntryActionReruns.RerunAction }, PageRequest.First(10))).Items;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Actions.Select(action => action.Name), Is.EqualTo(new[] { "Notify", "On approval" }), "the triggers they passed, in the form's order");
            Assert.That(audited.Select(item => (item.EntityKey, item.UserKey)), Is.EqualTo(new[] { (form, (Guid?)User) }), "one entry on the form's audit log, by the user, for the plan none");
            Assert.That(audited.Single().Details, Does.Contain("\"entries\":2").And.Contain($"\"numbers\":[{waiting.Number},{accepted.Number}]").Or.Contain($"\"numbers\":[{accepted.Number},{waiting.Number}]"), "the entries it ran for, never the one in Spam");
            Assert.That((plan.Entries, plan.LeftOut), Is.EqualTo((2, 1)), "an entry in Spam is left out");
            Assert.That(planned, Is.EqualTo(before), "a plan runs nothing");
            Assert.That((done.Entries, done.LeftOut), Is.EqualTo((2, 1)));
            Assert.That(again.Select(run => run.ActionKey), Is.EqualTo(new[] { notify.Key, approved.Key }));
            Assert.That(again.Select(run => run.ChangeKey).Distinct().Count(), Is.EqualTo(1), "one new change");
            Assert.That(again.Select(run => run.State), Is.All.EqualTo(ActionRunState.Pending));
            Assert.That(timeline.Where(item => item.Kind == EntryActionReruns.RerunEvent).Select(item => item.UserKey), Is.EqualTo(new Guid?[] { User }), "the timeline says who ran them");
        });
    }

    [Test]
    public async Task ARerun_RunsOnlyWhatTheRulesChose()
    {
        var always = Action("Always");
        var never = Action("Never") with { Enabled = false };
        var form = await ActionFormAsync(always, never);
        Pause(true);
        var entry = await SubmitAsync(form);

        var plan = await GetRequiredService<EntryActionReruns>().PlanAsync(form, [entry.Key]);

        Assert.That(plan.Actions.Select(action => action.Key), Is.EqualTo(new[] { always.Key }), "a disabled action does not run again");
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<HeardDeletes>();
        builder.AddNotificationAsyncHandler<EntryDeletedNotification, HeardDeletesHandler>();
    }

    /// <summary>Passes what it hears to the fixture's list.</summary>
    internal sealed class HeardDeletesHandler(HeardDeletes heard) : INotificationAsyncHandler<EntryDeletedNotification>
    {
        public Task HandleAsync(EntryDeletedNotification notification, CancellationToken cancellationToken)
        {
            heard.Heard.Add(notification);
            return Task.CompletedTask;
        }
    }
}
