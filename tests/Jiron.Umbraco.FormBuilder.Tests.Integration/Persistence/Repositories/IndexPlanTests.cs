using System.Data;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The indexes added before the schema froze (ROADMAP 4.8a, ADR-0033) serve the statements they were made for, and a
/// create's checks seek each unique index on their own (4.8b): SQLite's <c>EXPLAIN QUERY PLAN</c> of each statement
/// the repository issues names the index and scans no table, as <see cref="EntryQueryPlanTests"/> shows for the entries
/// query builder. The plans are SQLite's; SQL Server's optimizer
/// picks its own, and the correctness tests run there.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class IndexPlanTests : RepositoryTestBase
{
    private const string EntryFileIndex = "IX_jfbEntryFile_entryId_createdUtc";
    private const string EntryFileFormIndex = "IX_jfbEntryFile_formId_entryId";
    private const string EntryFileScanIndex = "IX_jfbEntryFile_scanState_entryId_id";
    private const string DailyStatDayIndex = "IX_jfbDailyStat_day_formId";
    private const string ReferenceIndex = "IX_jfbReference_contentKey";

    [Test]
    public async Task AnEntrysFiles_AndTheUploadsThatWaitForOne_UseTheIndexOverTheEntryAndTheTime()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");
        var form = await CreateTestFormAsync("cv");
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));

        var plans = await PlansAsync(async () =>
        {
            await Entries.ListFilesAsync(1L);
            await Entries.ListFilesAsync([1L, 2L]);
            await Entries.ListOrphanFilesAsync(Now, 10);
            await Entries.SumWaitingBytesAsync();
        });

        AssertEach(plans, FormBuilderTables.EntryFile, 4, EntryFileIndex);
    }

    [Test]
    public async Task TheMailLimits_CountThroughTheirIndexes_NotThroughAllKeptMail()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");
        var mail = new NewEmail(EmailKind.Action, "anna@jfb.test", "site@jfb.test", "S", "T", "en", RecipientKey: "anna@jfb.test");

        var plans = await PlansAsync(() => GetRequiredService<IEmailRepository>().AddWithinLimitsAsync(mail, new EmailLimits(3, 200, TimeSpan.FromDays(30))));

        var insert = plans.Single(plan => plan.Statement.StartsWith("INSERT", StringComparison.Ordinal)).Plan;
        Assert.Multiple(() =>
        {
            Assert.That(insert, Has.Some.Contains("IX_jfbEmail_recipientKey_createdUtc (recipientKey=? AND createdUtc>?)"), "the recipient's day");
            Assert.That(insert, Has.Some.Contains("IX_jfbEmail_createdUtc_recipientKey (createdUtc>?)"), "the site's hour");
        });
    }

    [Test]
    public async Task TheFilesThatWaitForAScan_UseTheirIndex_AndTheUploadsComeInOrderFromIt()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");
        var form = await CreateTestFormAsync("cv");
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));

        var entryFiles = await PlansAsync(() => Entries.ListEntryFilesToScanAsync(0, 10));
        var uploads = await PlansAsync(() => Entries.ListUploadsToScanAsync(0, 10));

        Assert.Multiple(() =>
        {
            AssertEach(entryFiles, FormBuilderTables.EntryFile, 1, EntryFileScanIndex);
            AssertEach(uploads, FormBuilderTables.EntryFile, 1, EntryFileScanIndex);
            Assert.That(entryFiles.Single().Plan, Has.Some.Contains("(scanState=? AND entryId>?)"), "the uploads, whose entry is null, are not read");
            Assert.That(uploads.Single().Plan, Has.Some.Contains("(scanState=? AND entryId=? AND id>?)"));
            Assert.That(uploads.Single().Plan, Has.None.Contains("TEMP B-TREE"), "in the order of the ids, without a sort");
        });
    }

    [Test]
    public async Task OneFormsWaitingUploads_ForItsDelete_UseTheIndexThatLeadsWithTheForm_InOrder()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");
        var form = await CreateTestFormAsync("cv");
        var other = await CreateTestFormAsync("other");
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(form.Id));
        await Entries.AddFileAsync(EntryRepositoryTests.NewFile(other.Id));

        var plans = await PlansAsync(() => Entries.ListUploadsAsync(form.Id, 10));

        Assert.Multiple(() =>
        {
            AssertEach(plans, FormBuilderTables.EntryFile, 1, EntryFileFormIndex);
            Assert.That(plans.Single().Plan, Has.Some.Contains("(formId=? AND entryId=?)"), "a seek on both columns, so no other form's upload is read");
            Assert.That(plans.Single().Plan, Has.None.Contains("TEMP B-TREE"), "in the order of the ids, without a sort");
        });
    }

    [Test]
    public async Task OneFormsDays_AndTheirDelete_UseTheKeyThatLeadsWithTheForm_AndRetentionUsesTheDay()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");
        var form = await CreateFormAsync();
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views", string.Empty, 3)]);
        var key = Query(db => db.ExecuteScalar<string>("SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = @0 AND name LIKE 'sqlite_autoindex%'", FormBuilderTables.DailyStat));

        var listed = await PlansAsync(() => Stats.ListAsync(form.Id, DateOnly.FromDateTime(Now.UtcDateTime).AddDays(-30), DateOnly.FromDateTime(Now.UtcDateTime)));
        var deleted = await PlansAsync(() => Stats.DeleteFormAsync(form.Id));
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views", string.Empty, 3)]);
        var purged = await PlansAsync(() => Stats.PurgeAsync(TimeSpan.FromDays(1)));

        Assert.Multiple(() =>
        {
            Assert.That(key, Is.Not.Null, "the primary key's own index");
            AssertEach(listed, FormBuilderTables.DailyStat, 1, key!);
            AssertEach([.. deleted.Where(plan => plan.Statement.StartsWith("SELECT", StringComparison.Ordinal))], FormBuilderTables.DailyStat, 1, key!);

            // A day at a time, so no statement locks more than one form's day (ADR-0041): a seek on either index.
            AssertEach([.. deleted.Where(plan => plan.Statement.StartsWith("DELETE", StringComparison.Ordinal))], FormBuilderTables.DailyStat, 1, key!, DailyStatDayIndex);
            AssertEach(purged, FormBuilderTables.DailyStat, 1, DailyStatDayIndex);
            Assert.That(listed.Concat(deleted).SelectMany(plan => plan.Plan), Has.Some.Contains("(formId=?"), "the key leads with the form");
        });
    }

    [Test]
    public void AContentItemsReferences_AreFoundAndReplacedByItsKey()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");

        // The statements with which every save, publish, move or delete of a content item reads and replaces its rows
        // (ADR-0017); the repository gains them with the step that records where content uses a form.
        var plans = Query(db => new[]
        {
            Explain(db, $"SELECT * FROM {T(FormBuilderTables.Reference)} WHERE {Q("contentKey")} = @0", [Guid.NewGuid()]),
            Explain(db, $"DELETE FROM {T(FormBuilderTables.Reference)} WHERE {Q("contentKey")} = @0", [Guid.NewGuid()]),
        });

        AssertEach(plans, FormBuilderTables.Reference, 2, ReferenceIndex);
    }

    [Test]
    public async Task ACreate_ChecksItsAliasAndItsKey_EachWithASearchOfItsOwnUniqueIndex()
    {
        Assume.That(IsSqlite, Is.True, "plans are SQLite's");

        var form = await PlansAsync(() => CreateFormAsync("contact"));
        var list = await PlansAsync(() => OptionLists.CreateAsync(new NewOptionList(Guid.NewGuid(), "colours", "Colours", OptionListKind.Manual)));

        Assert.Multiple(() =>
        {
            AssertChecks(form, FormBuilderTables.Form, "IX_jfbForm_alias", "IX_jfbForm_key");
            AssertChecks(list, FormBuilderTables.OptionList, "IX_jfbOptionList_alias", "IX_jfbOptionList_key");
        });
    }

    // The create's insert searches each index once on its own, and neither scans the table nor combines the two.
    private void AssertChecks(IReadOnlyList<(string Statement, IReadOnlyList<string> Plan)> plans, string table, params string[] indexes)
    {
        var (statement, plan) = plans.Single(plan => plan.Statement.StartsWith("INSERT INTO " + T(table) + " (", StringComparison.Ordinal));
        var searches = plan.Where(line => line.StartsWith("SEARCH " + table + " ", StringComparison.Ordinal)).ToList();
        Assert.That(searches, Has.Count.EqualTo(indexes.Length), $"{statement}: {string.Join("; ", plan)}");
        foreach (var index in indexes)
        {
            Assert.That(searches, Has.One.Contains("INDEX " + index + " ("), $"{statement}: {string.Join("; ", plan)}");
        }

        Assert.That(plan, Has.None.Contains("MULTI-INDEX OR").And.None.StartsWith("SCAN " + table), $"{statement}: {string.Join("; ", plan)}");
    }

    // Each plan of a statement on the table names one of the indexes and scans nothing; there are as many as expected.
    private static void AssertEach(IReadOnlyList<(string Statement, IReadOnlyList<string> Plan)> plans, string table, int expected, params string[] indexes)
    {
        var onTable = plans.Where(plan => plan.Statement.Contains(table, StringComparison.Ordinal)).ToList();
        Assert.That(onTable, Has.Count.EqualTo(expected), $"statements on {table}: {string.Join(" | ", plans.Select(plan => plan.Statement))}");
        foreach (var (statement, plan) in onTable)
        {
            Assert.That(plan.Any(line => indexes.Any(index => line.Contains(index, StringComparison.Ordinal))), Is.True, $"{statement}: {string.Join("; ", plan)}");
            Assert.That(plan, Has.None.StartsWith("SCAN"), $"{statement}: {string.Join("; ", plan)}");
        }
    }

    // The plans of every statement the work issues, explained afterwards with the values it ran with.
    private async Task<IReadOnlyList<(string Statement, IReadOnlyList<string> Plan)>> PlansAsync(Func<Task> work)
    {
        var recorder = new StatementRecorder();
        await WatchedAsync(recorder, async () =>
        {
            await work();
            return 0;
        });
        return Query(db => recorder.Statements.Select(statement => Explain(db, statement.Text, statement.Values)).ToList());
    }

    private static (string Statement, IReadOnlyList<string> Plan) Explain(IUmbracoDatabase db, string statement, object?[] values)
    {
        db.ExecuteScalar<int>("SELECT 1");
        using var command = db.CreateCommand(db.Connection!, CommandType.Text, "EXPLAIN QUERY PLAN " + statement, values!);
        using var reader = command.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return (statement, plan);
    }
}
