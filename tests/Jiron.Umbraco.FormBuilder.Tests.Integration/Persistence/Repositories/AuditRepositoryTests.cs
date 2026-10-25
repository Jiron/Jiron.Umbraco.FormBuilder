using System.Diagnostics;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The audit log (section 4.10, ADR-0041): entries are added in the unit of the change they record, read back by id and
/// through filtered pages newest first with the time as stored, recorded once per window for views, and purged oldest
/// first in batches. No field value of an entry reaches it, and the search that proves it finds what does.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class AuditRepositoryTests : RepositoryTestBase
{
    private static readonly Guid Reviewer = Guid.Parse("2b3c4d5e-6f70-4812-9a3b-4c5d6e7f8091");
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [Test]
    public async Task AnEntry_IsStoredAsAdded_AndReadBackById()
    {
        var item = Guid.NewGuid();
        var details = new AuditDetails().AddIdentifier("section", "general").Add("version", 4).AddIdentifiers("paths", ["privacy.retention-days"]);

        var added = await Audit.AddAsync(new NewAuditEntry("settings.saved", "settings", item, Editor, details));
        var job = await Audit.AddAsync(new NewAuditEntry("entries.purged", "form", null, null));

        Assert.Multiple(async () =>
        {
            Assert.That(added with { OccurredUtc = Now }, Is.EqualTo(new StoredAuditEntry(added.Id, Now, Editor, "settings.saved", "settings", item, """{"section":"general","version":4,"paths":["privacy.retention-days"]}""")));
            Assert.That(added.OccurredUtc, Is.EqualTo(Now).Within(TimeSpan.FromMilliseconds(4)), "the clock's time, which SQL Server rounds");
            Assert.That(await Audit.GetAsync(added.Id), Is.EqualTo(added), "returned as stored");
            Assert.That(job.UserKey, Is.Null, "Jiron Form Builder's own action");
            Assert.That(job.EntityKey, Is.Null, "about no single item");
            Assert.That(job.Details, Is.EqualTo("{}"), "no details");
            Assert.That(await Audit.GetAsync(job.Id), Is.EqualTo(job));
            Assert.That(await Audit.GetAsync(job.Id + 1), Is.Null);
        });
    }

    [Test]
    public async Task AnEntry_CommitsAndRollsBack_WithTheChangeItRecords()
    {
        await Database.WriteAsync(async db =>
        {
            var folder = await CreateFolderAsync("Kept");
            return await Audit.AddAsync(new NewAuditEntry("folder.created", "folder", folder.Key, Editor));
        });

        Assert.That(
            () => Database.WriteAsync<int>(async db =>
            {
                var folder = await CreateFolderAsync("Undone");
                await Audit.AddAsync(new NewAuditEntry("folder.created", "folder", folder.Key, Editor));
                throw new InvalidOperationException("JFB_TEST the change fails");
            }),
            Throws.InvalidOperationException);

        var log = await Audit.QueryAsync(new AuditQuery(), PageRequest.First(10));
        Assert.Multiple(() =>
        {
            Assert.That(log.Total, Is.EqualTo(1), "only the change that committed is in the log");
            Assert.That(log.Items.Single().EntityKey, Is.Not.Null);
        });
    }

    [Test]
    public async Task TheQuery_FiltersByEachCondition_NewestFirst_WithTheTotal()
    {
        var form = Guid.NewGuid();
        var entry = Guid.NewGuid();
        var first = await Audit.AddAsync(new NewAuditEntry("form.saved", "form", form, Editor));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await Audit.AddAsync(new NewAuditEntry("entry.viewed", "entry", entry, Reviewer));
        var tie = await Audit.AddAsync(new NewAuditEntry("entry.deleted", "entry", entry, Reviewer));
        Clock.Advance(TimeSpan.FromMinutes(1));
        var last = await Audit.AddAsync(new NewAuditEntry("form.saved", "form", form, Reviewer));

        Assert.Multiple(async () =>
        {
            Assert.That(await Ids(new AuditQuery()), Is.EqualTo(new[] { last.Id, tie.Id, second.Id, first.Id }), "newest first, the later id first at one time");
            Assert.That(await Ids(new AuditQuery { Action = "form.saved" }), Is.EqualTo(new[] { last.Id, first.Id }));
            Assert.That(await Ids(new AuditQuery { EntityType = "entry" }), Is.EqualTo(new[] { tie.Id, second.Id }));
            Assert.That(await Ids(new AuditQuery { EntityKey = form }), Is.EqualTo(new[] { last.Id, first.Id }));
            Assert.That(await Ids(new AuditQuery { UserKey = Reviewer }), Is.EqualTo(new[] { last.Id, tie.Id, second.Id }));
            Assert.That(await Ids(new AuditQuery { FromUtc = Now.AddMinutes(1) }), Is.EqualTo(new[] { last.Id, tie.Id, second.Id }), "from: included");
            Assert.That(await Ids(new AuditQuery { BeforeUtc = Now.AddMinutes(2) }), Is.EqualTo(new[] { tie.Id, second.Id, first.Id }), "before: excluded");
            Assert.That(await Ids(new AuditQuery { UserKey = Reviewer, Action = "form.saved", EntityKey = form, FromUtc = Now, BeforeUtc = Now.AddDays(1) }), Is.EqualTo(new[] { last.Id }), "all at once");
            Assert.That(await Ids(new AuditQuery { Action = "form.deleted" }), Is.Empty);

            var page = await Audit.QueryAsync(new AuditQuery(), new PageRequest(1, 2));
            Assert.That(page.Items.Select(item => item.Id), Is.EqualTo(new[] { tie.Id, second.Id }), "the second page of two");
            Assert.That(page.Total, Is.EqualTo(4), "the total counts every match");
            var filtered = await Audit.QueryAsync(new AuditQuery { EntityType = "form" }, new PageRequest(5, 2));
            Assert.That(filtered.Items, Is.Empty, "past the end");
            Assert.That(filtered.Total, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AQueryPastItsBudget_StopsWithQueryTimeout_AndRunsOnce()
    {
        await Audit.AddAsync(new NewAuditEntry("settings.saved", "settings", Guid.NewGuid(), Editor));
        var where = T(FormBuilderTables.AuditEntry) + " WHERE ";
        var slowly = new CommandRewriter(where, where + SlowCondition);
        var budgeted = new AuditRepository(Database, TimeSpan.FromMilliseconds(250));
        var query = new AuditQuery { Action = "settings.saved" };
        var watch = Stopwatch.StartNew();

        Assert.That(async () => await WatchedReadAsync(slowly, () => budgeted.QueryAsync(query, PageRequest.First(10))), Throws.InstanceOf<FormBuilderQueryTimeoutException>());
        var stopped = watch.Elapsed;
        Assert.Multiple(async () =>
        {
            Assert.That(slowly.Rewritten, Is.EqualTo(1), "the count ran once, and never again");
            Assert.That(stopped, Is.LessThan(TimeSpan.FromSeconds(5)), "interrupted, not run to its end");
            Assert.That((await budgeted.QueryAsync(query, PageRequest.First(10))).Total, Is.EqualTo(1), "the next list runs within the budget");
        });
    }

    [Test]
    public async Task ABudgetedQuery_StoppedThroughItsUnit_AnswersAsCancelled_NotWithTheProvidersError()
    {
        await Audit.AddAsync(new NewAuditEntry("settings.saved", "settings", Guid.NewGuid(), Editor));
        var where = T(FormBuilderTables.AuditEntry) + " WHERE ";
        var slowly = new CommandRewriter(where, where + SlowCondition);
        var budgeted = new AuditRepository(Database, TimeSpan.FromMinutes(1));
        using var unit = new CancellationTokenSource();
        var watch = Stopwatch.StartNew();

        // The query has no token of its own: the unit it runs in is what the caller cancels. SQLite runs the statement
        // as the call is made, so the cancellation is set going first.
        unit.CancelAfter(TimeSpan.FromMilliseconds(250));
        var reading = Database.ReadAsync(
            async db =>
            {
                db.Interceptors.Add(slowly);
                try
                {
                    return await budgeted.QueryAsync(new AuditQuery { Action = "settings.saved" }, PageRequest.First(10));
                }
                finally
                {
                    db.Interceptors.Remove(slowly);
                }
            },
            unit.Token);

        Assert.That(async () => await reading, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "interrupted for the unit, long before the budget");
    }

    [Test]
    public async Task ABudgetedQueryInsideAnother_StoppedThroughTheUnit_AnswersItsOwnCallerAsCancelled()
    {
        await Audit.AddAsync(new NewAuditEntry("settings.saved", "settings", Guid.NewGuid(), Editor));
        var where = T(FormBuilderTables.AuditEntry) + " WHERE ";
        var slowly = new CommandRewriter(where, where + SlowCondition);
        var budgeted = new AuditRepository(Database, TimeSpan.FromMinutes(1));
        using var unit = new CancellationTokenSource();
        Exception? heard = null;

        // The inner query's caller catches what the query answers, as code between two reads may: it hears of the
        // cancellation as one, not as the provider's error, before the outer read does.
        unit.CancelAfter(TimeSpan.FromMilliseconds(250));
        try
        {
            await Database.ReadAsync(
                _ => Database.QueryAsync(
                    async db =>
                    {
                        db.Interceptors.Add(slowly);
                        try
                        {
                            await budgeted.QueryAsync(new AuditQuery { Action = "settings.saved" }, PageRequest.First(10));
                        }
                        catch (Exception exception)
                        {
                            heard = exception;
                        }
                        finally
                        {
                            db.Interceptors.Remove(slowly);
                        }

                        return 0;
                    },
                    TimeSpan.FromMinutes(1)),
                unit.Token);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(heard, Is.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task ABudgetedQueryInsideAnother_KeepsItsOwnCallersToken()
    {
        using var own = new CancellationTokenSource();
        var seen = false;

        // The inner caller cancels while its read runs; what follows the read may answer that as a cancellation.
        try
        {
            await Database.QueryAsync(
                _ => Database.QueryAsync(
                    async _ =>
                    {
                        await own.CancelAsync();
                        seen = FormBuilderDbExtensions.CurrentToken.IsCancellationRequested;
                        return 0;
                    },
                    TimeSpan.FromSeconds(30),
                    own.Token),
                TimeSpan.FromSeconds(30));
        }
        catch (OperationCanceledException)
        {
        }

        Assert.That(seen, Is.True, "the inner read's statements carry its caller's token, inside the outer budget");
    }

    [Test]
    public async Task AddOnce_RecordsOneEntryPerUserItemAndAction_WithinTheWindow()
    {
        var entry = Guid.NewGuid();
        var view = new NewAuditEntry("entry.viewed", "entry", entry, Editor, new AuditDetails().AddKeys("sensitiveFieldKeys", [Guid.NewGuid()]));

        var first = await Audit.AddOnceAsync(view, Window);
        Clock.Advance(Window - TimeSpan.FromSeconds(1));
        var again = await Audit.AddOnceAsync(view, Window);
        var otherUser = await Audit.AddOnceAsync(view with { UserKey = Reviewer }, Window);
        var otherItem = await Audit.AddOnceAsync(view with { EntityKey = Guid.NewGuid() }, Window);
        var otherAction = await Audit.AddOnceAsync(view with { Action = "entry.revealed" }, Window);
        // Past the edge of the window, as stored times are never compared for equality (section 4.5).
        Clock.Advance(TimeSpan.FromSeconds(2));
        var later = await Audit.AddOnceAsync(view, Window);

        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(first, Is.EqualTo(await Audit.GetAsync(first!.Id)), "returned as stored");
            Assert.That(first!.Details, Does.Contain("sensitiveFieldKeys"));
            Assert.That(again, Is.Null, "within the window");
            Assert.That(otherUser, Is.Not.Null, "another user");
            Assert.That(otherItem, Is.Not.Null, "another item");
            Assert.That(otherAction, Is.Not.Null, "another action");
            Assert.That(later, Is.Not.Null, "once the window has passed");
            Assert.That(later!.Id, Is.GreaterThan(otherAction!.Id));
            Assert.That((await Audit.QueryAsync(new AuditQuery { EntityKey = entry, UserKey = Editor, Action = "entry.viewed" }, PageRequest.First(10))).Total, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AddOnce_InAUnitThatRollsBack_LeavesTheWindowOpen()
    {
        var view = new NewAuditEntry("entry.viewed", "entry", Guid.NewGuid(), Editor);
        StoredAuditEntry? undone = null;

        Assert.That(
            () => Database.WriteAsync<int>(async db =>
            {
                undone = await Audit.AddOnceAsync(view, Window);
                throw new InvalidOperationException("JFB_TEST the view fails");
            }),
            Throws.InvalidOperationException);
        var again = await Audit.AddOnceAsync(view, Window);

        Assert.Multiple(async () =>
        {
            Assert.That(undone, Is.Not.Null, "recorded in the unit");
            Assert.That(again, Is.Not.Null, "the entry that rolled back does not count");
            Assert.That((await Audit.QueryAsync(new AuditQuery(), PageRequest.First(10))).Total, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AddOnce_InParallel_RecordsOneEntry()
    {
        var view = new NewAuditEntry("entry.viewed", "entry", Guid.NewGuid(), Editor);
        var second = new AuditRepository(Database);

        var added = await Task.WhenAll(Enumerable.Range(0, 20).Select(i => (i % 2 == 0 ? Audit : second).AddOnceAsync(view, Window)));

        Assert.Multiple(async () =>
        {
            Assert.That(added.Count(entry => entry is not null), Is.EqualTo(1));
            Assert.That((await Audit.QueryAsync(new AuditQuery(), PageRequest.First(50))).Total, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ThePurge_DeletesTheOldestFirst_InBatches_AndKeepsTheRest()
    {
        var ids = new List<long>();
        for (var day = 0; day < 6; day++)
        {
            ids.Add((await Audit.AddAsync(new NewAuditEntry("form.saved", "form", Guid.NewGuid(), Editor))).Id);
            Clock.Advance(TimeSpan.FromDays(1));
        }

        // Now is six days after the first entry: those more than four days old go, the one exactly four days old stays.
        var keep = TimeSpan.FromDays(4);
        var first = await Audit.PurgeAsync(keep, 1);
        var remainingAfterFirst = await Ids(new AuditQuery());
        var second = await Audit.PurgeAsync(keep, 10);
        var third = await Audit.PurgeAsync(keep, 10);

        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(remainingAfterFirst, Does.Not.Contain(ids[0]), "the oldest first");
            Assert.That(second, Is.EqualTo(1));
            Assert.That(third, Is.EqualTo(0), "until nothing is older");
            Assert.That(await Ids(new AuditQuery()), Is.EqualTo(new[] { ids[5], ids[4], ids[3], ids[2] }));
        });
    }

    [Test]
    public async Task NoFieldValue_OfAnEntry_ReachesTheLog()
    {
        // An entry full of personal data, through every kind of action the log records about entries (section 4.10),
        // each naming fields, files and the entry by key with numbers, flags, enum values, identifiers and times.
        var form = await CreateTestFormAsync("firstName", "email", "phone", "message", "postcode", "birthday");
        string[] values = ["Mia", "mia.muster@example.test", "+49 30 1234567", "Ich habe eine Frage zu meinem Termin.", "10115", "1990-05-17"];
        var entry = (await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, [.. form.Fields.Values.Zip(values, (field, value) => new NewEntryValue(field, value))])))!;
        var fieldKeys = (await Forms.ListFieldsAsync(form.Id)).Select(field => field.Values.FieldKey).ToList();

        await Audit.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", entry.Key, Editor, new AuditDetails().AddKeys("sensitiveFieldKeys", fieldKeys.Take(3))), Window);
        await Audit.AddAsync(new NewAuditEntry("entry.revealed", "entry", entry.Key, Editor, new AuditDetails().Add("fieldKey", fieldKeys[1])));
        await Audit.AddAsync(new NewAuditEntry("entry.updated", "entry", entry.Key, Editor, new AuditDetails().AddKeys("fieldKeys", fieldKeys).Add("number", entry.Number)));
        await Audit.AddAsync(new NewAuditEntry("entry.stateChanged", "entry", entry.Key, Reviewer, new AuditDetails().Add("from", EntryState.Submitted).Add("to", EntryState.Approved).Add("undoable", true)));
        await Audit.AddAsync(new NewAuditEntry("entries.exported", "form", Guid.NewGuid(), Reviewer, new AuditDetails().AddIdentifier("format", "csv").Add("count", 1).Add("withSensitive", false).Add("fromUtc", Now.AddDays(-30))));
        await Audit.AddAsync(new NewAuditEntry("entry.deleted", "entry", entry.Key, null, new AuditDetails().Add("number", entry.Number).AddIdentifier("by", "retention")));

        await AuditLogSearch.AssertHoldsNoneOfAsync(Database, values);
        Assert.Multiple(() =>
        {
            // What the identifier rule refuses on its own; the single words that it cannot refuse are why entries' details
            // name fields by key and never take a string from an entry (ADR-0041).
            foreach (var value in values.Where(value => !AuditDetails.IsIdentifier(value)))
            {
                Assert.That(() => new AuditDetails().AddIdentifier("value", value), Throws.ArgumentException, value);
            }

            Assert.That(values.Where(AuditDetails.IsIdentifier), Is.EqualTo(new[] { "Mia", "10115", "1990-05-17" }));
        });
    }

    [Test]
    public async Task TheLogSearch_FindsAValue_InAColumn_InTheDetails_AndEscaped_ButNeverInsideAKey()
    {
        var name = "M" + (char)0xFC + "ller";
        await Audit.AddAsync(new NewAuditEntry("entry.viewed", "mia", Guid.Parse("10115aaa-1990-4005-8017-000000010115"), Editor, new AuditDetails().AddIdentifier("first", "Anna")));

        // A row that only a mistake could write: the details hold text, and escaped as JSON escapes it by default.
        await Database.WriteAsync(db => db.InsertRowAsync(new AuditEntryDto
        {
            OccurredUtc = Now.UtcDateTime,
            Action = "entry.updated",
            EntityType = "entry",
            Details = JsonSerializer.Serialize(new { last = name }),
        }));
        var found = await AuditLogSearch.FindAsync(Database, ["Mia", "anna", name, "10115", "1990", "Berlin"]);

        Assert.That(found, Is.EqualTo(new[] { "Mia", "anna", name }), "ignoring case; the digits inside the key are no value");
    }

    private async Task<IReadOnlyList<long>> Ids(AuditQuery query) =>
        (await Audit.QueryAsync(query, PageRequest.First(PageRequest.MaxTake))).Items.Select(item => item.Id).ToList();
}
