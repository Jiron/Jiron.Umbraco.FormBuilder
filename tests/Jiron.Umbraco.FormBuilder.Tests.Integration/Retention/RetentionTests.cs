using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Retention;

/// <summary>
/// Retention with a hand-moved clock (ROADMAP 13.1, ADR-0009, ADR-0222): each entry's time from its submission and every
/// change of state, the job that deletes an entry once its time has come with its mail and audits it, legal holds, a
/// form's days of storage shorter and longer than its states' days, a change of the rules reaching old entries either way,
/// and the dry run counting by days not saved yet.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RetentionTests : ActionTestBase
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    private EntryStateChanges Moves => GetRequiredService<EntryStateChanges>();

    private EntryHolds Holds => GetRequiredService<EntryHolds>();

    private IFormBuilderRetentionPart Purge => Part<EntryPurge>();

    private IFormBuilderRetentionPart Walk => Part<RetentionRecompute>();

    [Test]
    public void TheJob_RunsTheNewParts_InTheirOrder()
    {
        var order = GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().Select(part => part.GetType()).ToList();
        var mine = new[] { typeof(DraftVersionCleanup), typeof(EntryPurge), typeof(RetentionRecompute), typeof(FileStoreSweep), typeof(AuditLogPurge), typeof(DailyStatPurge) };

        Assert.That(order.Where(mine.Contains), Is.EqualTo(mine));
    }

    [Test]
    public async Task AnEntry_GetsItsTime_WhenSubmitted_AndAgainWithEveryChangeOfState()
    {
        var form = await PublishAsync(change: Manual);
        var entry = await SubmitAsync(form);
        var submitted = await TimeOfAsync(entry);
        Clock.Advance(TimeSpan.FromDays(1));
        var approved = await MoveAsync(form, entry, EntryState.Approved);
        Clock.Advance(TimeSpan.FromDays(1));
        var rejected = await MoveAsync(form, entry, EntryState.Rejected);
        Clock.Advance(TimeSpan.FromDays(1));
        var spam = await MoveAsync(form, entry, EntryState.Spam);
        Clock.Advance(TimeSpan.FromDays(1));
        var back = await MoveAsync(form, entry, EntryState.Submitted);
        Clock.Advance(TimeSpan.FromDays(1));
        var byEditor = await Moves.ChangeAsync(form, [entry.Key], EntryState.Rejected, Editor);
        var rejectedByEditor = (await Entries.GetByIdAsync(entry.Id))!;
        var undone = await Moves.UndoAsync(form, byEditor.ChangeKey, Editor);
        var afterUndo = (await Entries.GetByIdAsync(entry.Id))!;

        Assert.Multiple(() =>
        {
            Assert.That(submitted.PurgeAfterUtc, Is.EqualTo(submitted.StateChangedUtc.AddDays(365)));
            Assert.That(approved.PurgeAfterUtc, Is.EqualTo(approved.StateChangedUtc.AddDays(365)));
            Assert.That(rejected.PurgeAfterUtc, Is.EqualTo(rejected.StateChangedUtc.AddDays(90)));
            Assert.That(spam.PurgeAfterUtc, Is.EqualTo(spam.StateChangedUtc.AddDays(30)));
            Assert.That((back.State, back.PurgeAfterUtc), Is.EqualTo((EntryState.Submitted, (DateTimeOffset?)back.StateChangedUtc.AddDays(365))), "out of Spam");
            Assert.That(rejectedByEditor.PurgeAfterUtc, Is.EqualTo(rejectedByEditor.StateChangedUtc.AddDays(90)), "a user's move, inside its undo window");
            Assert.That(undone, Is.EqualTo(EntryUndoStatus.Done));
            Assert.That((afterUndo.State, afterUndo.PurgeAfterUtc), Is.EqualTo((EntryState.Submitted, (DateTimeOffset?)afterUndo.StateChangedUtc.AddDays(365))), "an Undo");
        });
    }

    [Test]
    public async Task TheJob_DeletesAnEntry_OnceItsTimeHasCome_WithAllItsMail_AndAuditsIt()
    {
        var form = await PublishAsync(change: Manual);
        var entry = await SubmitAsync(form);
        var other = await SubmitAsync(form);
        var emails = GetRequiredService<IEmailRepository>();
        var queued = (await emails.AddAsync(new NewEmail(EmailKind.Action, "team@example.com", "site@example.com", "Hello", "Hello", "en-US", EntryId: entry.Id)))!;
        var sent = (await emails.AddAsync(new NewEmail(EmailKind.Action, "ada@example.com", "site@example.com", "Thanks", "Thanks", "en-US", EntryId: entry.Id)))!;
        var token = (await emails.ClaimAsync(sent.Id, "server", TimeSpan.FromMinutes(5)))!.LockedBy!;
        await emails.SentAsync(sent.Id, token, TimeSpan.FromDays(30));
        Clock.Advance(TimeSpan.FromDays(200));
        await MoveAsync(form, other, EntryState.Approved);

        Clock.Advance(TimeSpan.FromDays(165) - Minute);
        await RunAsync(Purge);
        var before = await Entries.GetByIdAsync(entry.Id);
        Clock.Advance(TimeSpan.FromMinutes(2));
        await RunAsync(Purge);

        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = EntryDeletions.PurgedAction }, PageRequest.First(10))).Items;
        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.Not.Null, "a minute early");
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
            Assert.That(await emails.GetAsync(queued.Id), Is.Null);
            Assert.That(await emails.GetAsync(sent.Id), Is.Null, "sent mail too: its copy of the answers goes with the entry");
            Assert.That(await Entries.GetByIdAsync(other.Id), Is.Not.Null, "approved later, so its year starts later");
            Assert.That(audited, Has.Count.EqualTo(1));
            Assert.That(audited[0].UserKey, Is.Null, "the system did it");
            Assert.That(audited[0].Details, Does.Contain($"\"numbers\":[{entry.Number}]").And.Contain("\"entries\":1"));
        });
    }

    [Test]
    public async Task AHold_KeepsAnEntry_PastItsTime_AndFromAUsersDelete_UntilItEnds()
    {
        var form = await PublishAsync();
        var entry = await SubmitAsync(form);
        Clock.Advance(TimeSpan.FromDays(300));
        var end = Clock.GetUtcNow().AddDays(200);
        var held = await Holds.HoldAsync(entry.Key, end, "  Case 2026-14  ", Editor);
        var row = (await Entries.GetByIdAsync(entry.Id))!;

        Clock.Advance(TimeSpan.FromDays(70));
        await RunAsync(Purge);
        var pastItsTime = await Entries.GetByIdAsync(entry.Id);
        var deletions = GetRequiredService<EntryDeletions>();
        var plan = await deletions.PlanAsync(form, [entry.Key]);
        var deleted = await deletions.DeleteAsync(form, [entry.Key], Editor);
        Clock.Advance(TimeSpan.FromDays(131));
        await RunAsync(Purge);

        var timeline = await Events.ListForEntryAsync(entry.Id);
        Assert.Multiple(async () =>
        {
            Assert.That(held, Is.EqualTo(EntryHoldRefusal.None));
            Assert.That((row.HoldUntilUtc, row.HoldReason), Is.EqualTo(((DateTimeOffset?)end, "Case 2026-14")));
            Assert.That(pastItsTime, Is.Not.Null, "retention leaves it");
            Assert.That(plan, Is.EqualTo(new EntryDeletion(0, 0, 1)));
            Assert.That(deleted, Is.EqualTo(new EntryDeletion(0, 0, 1)), "a user's delete leaves it too, and says so");
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null, "the hold has ended");
        });
        Assert.That(timeline, Is.Empty, "the timeline went with the entry");
    }

    [Test]
    public async Task AHold_IsRecorded_WithoutItsReason_AndARelease_EndsIt()
    {
        var form = await PublishAsync();
        var entry = await SubmitAsync(form);
        var end = Clock.GetUtcNow().AddDays(30);

        await Holds.HoldAsync(entry.Key, end, "Case 2026-14", Editor);
        var released = await Holds.ReleaseAsync(entry.Key, Editor);
        var again = await Holds.ReleaseAsync(entry.Key, Editor);

        var timeline = await Events.ListForEntryAsync(entry.Id);
        var audit = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { EntityType = "form" }, PageRequest.First(50))).Items.Where(item => item.Action.StartsWith("entry.", StringComparison.Ordinal) && item.Action != "entry.stateChanged").ToList();
        Assert.Multiple(async () =>
        {
            Assert.That((released, again), Is.EqualTo((EntryHoldRefusal.None, EntryHoldRefusal.None)));
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.HoldUntilUtc, Is.Null);
            Assert.That(timeline.Select(item => (item.Kind, item.UserKey)), Is.SupersetOf(new[] { (EntryHolds.HeldEvent, (Guid?)Editor), (EntryHolds.ReleasedEvent, (Guid?)Editor) }));
            Assert.That(audit.Select(item => item.Action), Is.EquivalentTo(new[] { EntryHolds.HeldAction, EntryHolds.ReleasedAction }), "a second release changes nothing");
            Assert.That(timeline.Select(item => item.Details).Concat(audit.Select(item => item.Details)), Has.None.Contains("2026-14"), "the reason stays with the entry");
        });
    }

    [Test]
    public async Task AHold_IsRefused_PastAYear_InThePast_WithoutAReason_AndOnADraft()
    {
        var form = await PublishAsync();
        var entry = await SubmitAsync(form);
        var now = Clock.GetUtcNow();
        var formRow = (await FormRows.GetAsync(form))!;
        var draft = (await Entries.CreateAsync(new NewEntry { Key = Guid.NewGuid(), FormId = formRow.Id, FormVersionId = formRow.PublishedVersionId!.Value, State = EntryState.Draft, SubmissionId = Guid.NewGuid(), SubmissionHash = new string('a', 64) }))!;

        Assert.Multiple(async () =>
        {
            Assert.That(await Holds.HoldAsync(entry.Key, now.AddDays(365).AddMinutes(1), "Case", Editor), Is.EqualTo(EntryHoldRefusal.End));
            Assert.That(await Holds.HoldAsync(entry.Key, now, "Case", Editor), Is.EqualTo(EntryHoldRefusal.End));
            Assert.That(await Holds.HoldAsync(entry.Key, now.AddDays(365), "Case", Editor), Is.EqualTo(EntryHoldRefusal.None), "a year to the day");
            Assert.That(await Holds.HoldAsync(entry.Key, now.AddDays(1), "   ", Editor), Is.EqualTo(EntryHoldRefusal.Reason));
            Assert.That(await Holds.HoldAsync(entry.Key, now.AddDays(1), "Case\nmore", Editor), Is.EqualTo(EntryHoldRefusal.Reason));
            Assert.That(await Holds.HoldAsync(entry.Key, now.AddDays(1), new string('x', 501), Editor), Is.EqualTo(EntryHoldRefusal.Reason));
            Assert.That(await Holds.HoldAsync(draft.Key, now.AddDays(1), "Case", Editor), Is.EqualTo(EntryHoldRefusal.NotKept));
            Assert.That(await Holds.HoldAsync(Guid.NewGuid(), now.AddDays(1), "Case", Editor), Is.EqualTo(EntryHoldRefusal.NotFound));
        });
    }

    [Test]
    public async Task AFormsDaysOfStorage_ShorterThanItsStates_DeleteEarlier_AndLongerOnes_LeaveTheStatesDays()
    {
        var shorter = await PublishAsync("Short", change: Stored(10));
        var longer = await PublishAsync("Long", change: Stored(400));
        var brief = await SubmitAsync(shorter);
        var kept = await SubmitAsync(longer);
        var spam = await SubmitAsync(longer);
        await MoveAsync(longer, spam, EntryState.Spam);

        Clock.Advance(TimeSpan.FromDays(10) + Minute);
        await RunAsync(Purge);
        var afterTen = (await Entries.GetByIdAsync(brief.Id), await Entries.GetByIdAsync(spam.Id));
        Clock.Advance(TimeSpan.FromDays(21));
        await RunAsync(Purge);
        var afterThirtyOne = await Entries.GetByIdAsync(spam.Id);
        Clock.Advance(TimeSpan.FromDays(334));
        await RunAsync(Purge);

        Assert.Multiple(async () =>
        {
            Assert.That(afterTen.Item1, Is.Null, "ten days of storage come before a year");
            Assert.That(afterTen.Item2, Is.Not.Null);
            Assert.That(afterThirtyOne, Is.Null, "Spam's thirty days come before four hundred");
            Assert.That(await Entries.GetByIdAsync(kept.Id), Is.Null, "a year comes before four hundred days");
        });
    }

    [Test]
    public async Task AShorterRule_ReachesOldEntries_ThroughTheWalk()
    {
        var form = await PublishAsync();
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);
        await MoveAsync(form, first, EntryState.Spam);
        await MoveAsync(form, second, EntryState.Spam);
        await RunAsync(Walk);
        var before = (await Entries.GetByIdAsync(first.Id))!.PurgeAfterUtc;

        await SaveSiteAsync(retention => retention with { Spam = 7 });
        await RunAsync(Walk);
        var shortened = (await Entries.GetByIdAsync(second.Id))!;
        Clock.Advance(TimeSpan.FromDays(7) + Minute);
        await RunAsync(Purge);

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.EqualTo(shortened.StateChangedUtc.AddDays(30)).Within(TimeSpan.FromSeconds(5)));
            Assert.That(shortened.PurgeAfterUtc, Is.EqualTo(shortened.StateChangedUtc.AddDays(7)));
            Assert.That(await Entries.GetByIdAsync(first.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(second.Id), Is.Null, "every entry of the form");
        });
    }

    [Test]
    public async Task ALongerRule_KeepsAnEntry_WhoseStoredTimeHasCome_AndGivesItItsLaterTime()
    {
        var form = await PublishAsync();
        var entry = await SubmitAsync(form);
        await MoveAsync(form, entry, EntryState.Spam);
        await RunAsync(Walk);
        await SaveSiteAsync(retention => retention with { Spam = 60 });
        Clock.Advance(TimeSpan.FromDays(31));

        await RunAsync(Purge);

        var row = await Entries.GetByIdAsync(entry.Id);
        Assert.Multiple(() =>
        {
            Assert.That(row, Is.Not.Null, "its rules say 60 days now");
            Assert.That(row!.PurgeAfterUtc, Is.EqualTo(row.StateChangedUtc.AddDays(60)));
        });
    }

    [Test]
    public async Task EntriesWithoutATime_GetTheirTimes_FromTheWalk_WhichGoesOnWhereItStopped()
    {
        var form = await PublishAsync();
        var row = (await FormRows.GetAsync(form))!;
        var ids = new List<long>();
        for (var i = 0; i < RetentionRecompute.Batch + 3; i++)
        {
            var stored = (await Entries.CreateAsync(new NewEntry { Key = Guid.NewGuid(), FormId = row.Id, FormVersionId = row.PublishedVersionId!.Value, SubmissionId = Guid.NewGuid(), SubmissionHash = new string('b', 64) }))!;
            ids.Add(stored.Id);
        }

        // A walk under way by the same terms goes on after the last entry it gave a time.
        var fingerprint = RetentionTerms.Of(new SiteRetention(), FormRetentionRules.None).Fingerprint;
        await KeyValues.TryAddAsync(RetentionRecompute.KeyOf(form), $"{fingerprint}|{ids[RetentionRecompute.Batch - 1]}");
        await RunAsync(Walk);
        var skipped = (await Entries.GetByIdAsync(ids[0]))!;
        var done = await KeyValues.GetAsync(RetentionRecompute.KeyOf(form));
        var last = (await Entries.GetByIdAsync(ids[^1]))!;

        // Other terms start the walk again from the first entry.
        await SaveSiteAsync(retention => retention with { Submitted = 100 });
        await RunAsync(Walk);
        var again = (await Entries.GetByIdAsync(ids[0]))!;

        Assert.Multiple(() =>
        {
            Assert.That(skipped.PurgeAfterUtc, Is.Null, "before the cursor");
            Assert.That(last.PurgeAfterUtc, Is.EqualTo(last.StateChangedUtc.AddDays(365)));
            Assert.That(done, Is.EqualTo(fingerprint), "done");
            Assert.That(again.PurgeAfterUtc, Is.EqualTo(again.StateChangedUtc.AddDays(100)));
        });
    }

    [Test]
    public async Task AWalkThatSawEveryForm_ForgetsTheTermsOfAFormThatIsGone()
    {
        var form = await PublishAsync();
        var gone = RetentionRecompute.KeyOf(Guid.NewGuid());
        await KeyValues.TryAddAsync(gone, "v1:365:365:90:30:");

        await RunAsync(Walk);

        Assert.Multiple(async () =>
        {
            Assert.That(await KeyValues.GetAsync(gone), Is.Null);
            Assert.That(await KeyValues.GetAsync(RetentionRecompute.KeyOf(form)), Is.Not.Null, "the live form's stay");
        });
    }

    [Test]
    public async Task TheDryRun_CountsByDaysNotSavedYet_PerForm_WithTheHeldApart_AndTheFilesThatGo()
    {
        var contact = await PublishAsync("Contact");
        var survey = await PublishAsync("Survey", change: OwnSpam(5));
        var spamOld = await SubmitAsync(contact);
        var heldOne = await SubmitAsync(contact);
        var surveyed = await SubmitAsync(survey);
        await MoveAsync(contact, spamOld, EntryState.Spam);
        await MoveAsync(contact, heldOne, EntryState.Spam);
        await MoveAsync(survey, surveyed, EntryState.Spam);
        await Holds.HoldAsync(heldOne.Key, Clock.GetUtcNow().AddDays(100), "Case", Editor);
        await FileOfAsync(spamOld, 2);
        await FileOfAsync(heldOne, 1);
        Clock.Advance(TimeSpan.FromDays(10));
        var dryRun = GetRequiredService<RetentionDryRun>();

        var saved = await dryRun.CountAsync(new SiteRetention(), 7);
        var typed = await dryRun.CountAsync(new SiteRetention { Spam = 14 }, 7);
        var kept = await dryRun.CountAsync(new SiteRetention { Spam = 90 }, 30);
        var own = await dryRun.OwnAsync();

        string Line(RetentionDryRunResult result) => string.Join("; ", result.Forms.Select(form => $"{form.Form.Name} {form.Entries}/{form.Held} {form.Files}"));
        Assert.Multiple(async () =>
        {
            Assert.That(Line(saved), Is.EqualTo("Survey 1/0 0"), "Spam's 30 days are 20 away");
            Assert.That(Line(typed), Is.EqualTo("Contact 1/1 2; Survey 1/0 0"), "14 days are 4 away, the hold lasts past the span and keeps its file");
            Assert.That((typed.Entries, typed.Held, typed.Files), Is.EqualTo((2L, 1L, 2L)));
            Assert.That(Line(kept), Is.EqualTo("Survey 1/0 0"), "the form's own 5 days stay");
            Assert.That(own.Select(each => (each.Form.Name, each.Rules.Spam)), Is.EqualTo(new[] { ("Survey", (int?)5) }));
            Assert.That(await Entries.GetByIdAsync(spamOld.Id), Is.Not.Null, "a dry run deletes nothing");
        });
    }

    [Test]
    public async Task TheAuditLog_StaysAYear_AndTheDailyStatistics_ThreeYears()
    {
        var audit = GetRequiredService<IAuditRepository>();
        var stats = GetRequiredService<IDailyStatRepository>();
        var form = (await FormRows.GetAsync(await PublishAsync()))!;
        await audit.AddAsync(new NewAuditEntry("form.saved", "form", form.Key, Editor, new AuditDetails()));
        var day = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        await stats.IncrementAsync(form.Id, [new DailyStatCount(InsightMetrics.Views)]);
        Clock.Advance(TimeSpan.FromDays(366));
        await audit.AddAsync(new NewAuditEntry("form.published", "form", form.Key, Editor, new AuditDetails()));

        await RunAsync(Part<AuditLogPurge>());
        var afterAYear = await stats.ListAsync(form.Id, day, day);
        Clock.Advance(TimeSpan.FromDays(3 * 365 - 365));
        await RunAsync(Part<DailyStatPurge>());

        var left = (await audit.QueryAsync(new AuditQuery(), PageRequest.First(50))).Items.Where(item => item.EntityKey == form.Key).Select(item => item.Action);
        Assert.Multiple(async () =>
        {
            Assert.That(left, Does.Contain("form.published").And.Not.Contain("form.saved"));
            Assert.That(afterAYear, Is.Not.Empty);
            Assert.That(await stats.ListAsync(form.Id, day, day), Is.Empty);
        });
    }

    private IFormBuilderRetentionPart Part<T>()
        where T : IFormBuilderRetentionPart =>
        GetRequiredService<IEnumerable<IFormBuilderRetentionPart>>().OfType<T>().Single();

    private IKeyValueRepository KeyValues => GetRequiredService<IKeyValueRepository>();

    private Task RunAsync(IFormBuilderRetentionPart part) => part.RunAsync(Clock.GetUtcNow().AddMinutes(5), CancellationToken.None);

    private async Task<StoredEntry> TimeOfAsync(StoredEntry entry) => (await Entries.GetByIdAsync(entry.Id))!;

    private async Task<StoredEntry> MoveAsync(Guid form, StoredEntry entry, EntryState to)
    {
        var change = await Moves.ChangeAsync(form, [entry.Key], to, null);
        Assert.That(change.Unmoved, Is.Empty, $"moved to {to}");
        return (await Entries.GetByIdAsync(entry.Id))!;
    }

    // Gives an entry uploads of its own; only the rows count, so no bytes go to the store.
    private async Task FileOfAsync(StoredEntry entry, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var file = (await Entries.AddFileAsync(EntryRepositoryTests.NewFile(entry.FormId)))!;
            await GetRequiredService<FormBuilderDatabase>().WriteAsync(
                db =>
                {
                    var n = new SqlNames(db);
                    return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryFile)} SET {n.C("entryId")} = @0 WHERE {n.C("id")} = @1", entry.Id, file.Id);
                });
        }
    }

    private async Task SaveSiteAsync(Func<SiteRetention, SiteRetention> change)
    {
        var service = GetRequiredService<IFormBuilderSettingsService>();
        var read = await service.GetAsync<PrivacySettings>();
        await service.UpdateAsync<PrivacySettings>(section => section with { Retention = change(section.Retention) }, read.Version, Editor);
    }

    // Entries wait for approval, so they arrive submitted.
    private static FormDefinition Manual(FormDefinition definition) =>
        definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual } } };

    private static Func<FormDefinition, FormDefinition> Stored(int days) => definition =>
        definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Store = EntryStorage.Days, StoreDays = days } } };

    private static Func<FormDefinition, FormDefinition> OwnSpam(int days) => definition =>
        definition with { Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { RetentionDays = new RetentionDaysByState { Spam = days } } } };
}
