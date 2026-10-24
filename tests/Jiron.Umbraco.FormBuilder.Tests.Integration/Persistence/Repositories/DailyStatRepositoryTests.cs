using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// Daily statistics (section 4.5, ADR-0041): counts are added to today's values per form, metric and dimension, right
/// under parallel increments from two servers, never for a form that does not exist or is being deleted, in the
/// caller's unit; they are listed by day, deleted with their form, and purged a form and a day at a time.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class DailyStatRepositoryTests : RepositoryTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(Now.UtcDateTime);
    private const string Page = "1c9d2e3f-4a5b-4c6d-8e7f-90a1b2c3d4e5";
    private const string FieldError = "5e6f7a8b-9c0d-4e1f-a2b3-c4d5e6f7a8b9:minLength";

    [Test]
    public async Task Counts_AreAddedToTodaysValues_AndListedByDayMetricAndDimension()
    {
        var form = await CreateFormAsync();
        var other = await CreateFormAsync();

        Assert.That(await Stats.IncrementAsync(form.Id, [new DailyStatCount("views"), new DailyStatCount("pageReached", Page), new DailyStatCount("invalid", FieldError)]), Is.True);
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views", By: 2), new DailyStatCount("completionSeconds", By: 95)]);
        await Stats.IncrementAsync(other.Id, [new DailyStatCount("views", By: 7)]);
        Clock.Advance(TimeSpan.FromDays(1));
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views"), new DailyStatCount("hour", "8")]);

        Assert.Multiple(async () =>
        {
            Assert.That(await Stats.ListAsync(form.Id, Today, Today.AddDays(1)), Is.EqualTo(new[]
            {
                new DailyStat(Today, form.Id, "completionSeconds", string.Empty, 95),
                new DailyStat(Today, form.Id, "invalid", FieldError, 1),
                new DailyStat(Today, form.Id, "pageReached", Page, 1),
                new DailyStat(Today, form.Id, "views", string.Empty, 3),
                new DailyStat(Today.AddDays(1), form.Id, "hour", "8", 1),
                new DailyStat(Today.AddDays(1), form.Id, "views", string.Empty, 1),
            }));
            Assert.That(await Stats.ListAsync(form.Id, Today.AddDays(1), Today.AddDays(1)), Has.Count.EqualTo(2), "one day");
            Assert.That(await Stats.ListAsync(form.Id, Today.AddDays(-7), Today.AddDays(-1)), Is.Empty, "days without counts");
            Assert.That(await Stats.ListAsync(other.Id, Today, Today.AddDays(1)), Is.EqualTo(new[] { new DailyStat(Today, other.Id, "views", string.Empty, 7) }));
        });
    }

    [Test]
    public async Task CountsOfOneMetricAndDimension_AreAddedUpFirst_AndZerosWriteNothing()
    {
        var form = await CreateFormAsync();

        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views"), new DailyStatCount("spam", By: 0), new DailyStatCount("views", By: 4), new DailyStatCount("origin", Page, 0)]);

        Assert.That(await Stats.ListAsync(form.Id, Today, Today), Is.EqualTo(new[] { new DailyStat(Today, form.Id, "views", string.Empty, 5) }));
    }

    [Test]
    public async Task AFormThatDoesNotExist_IsCountedNothing()
    {
        var form = await CreateFormAsync();
        var missing = form.Id + 1000;

        Assert.Multiple(async () =>
        {
            Assert.That(await Stats.IncrementAsync(missing, [new DailyStatCount("views"), new DailyStatCount("starts")]), Is.False);
            Assert.That(await Stats.ListAsync(missing, Today, Today), Is.Empty);
            Assert.That(await Stats.IncrementAsync(form.Id, [new DailyStatCount("views")]), Is.True);
        });
    }

    [Test]
    public async Task AFormWhoseRowIsGone_IsCountedNothing_EvenWhereValuesOfItAreLeft()
    {
        var missing = (await CreateFormAsync()).Id + 1000;
        await Database.WriteAsync(db =>
        {
            var n = new SqlNames(db);
            return db.ExecAsync(
                $"INSERT INTO {n.T(FormBuilderTables.DailyStat)} ({n.C("day")}, {n.C("formId")}, {n.C("metric")}, {n.C("dimension")}, {n.C("value")}) VALUES (@0, @1, @2, @3, @4)",
                Now.UtcDateTime.Date,
                missing,
                "views",
                string.Empty,
                5L);
        });

        Assert.Multiple(async () =>
        {
            Assert.That(await Stats.IncrementAsync(missing, [new DailyStatCount("views"), new DailyStatCount("starts")]), Is.False);
            Assert.That(await Stats.ListAsync(missing, Today, Today), Is.EqualTo(new[] { new DailyStat(Today, missing, "views", string.Empty, 5) }), "the value left is as it was");
            Assert.That(await Stats.DeleteFormAsync(missing), Is.EqualTo(1), "deleting values needs no form");
        });
    }

    [Test]
    public async Task CountsDuringTheDeleteOfTheirForm_LeaveNoValueBehind()
    {
        var form = await CreateFormAsync();
        var other = await CreateFormAsync();
        var servers = new[] { Stats, new DailyStatRepository(Database) };
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views")]);

        var counts = Enumerable.Range(0, 100).Select(i => servers[i % 2].IncrementAsync(i % 4 == 0 ? other.Id : form.Id,
        [
            new DailyStatCount("views"),
            new DailyStatCount("hour", (i % 24).ToString(CultureInfo.InvariantCulture)),
        ])).ToList();
        var delete = Database.WriteAsync(async db =>
        {
            // As the form's delete (4.5) does it: the form's row first, then its values.
            var n = new SqlNames(db);
            await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Form)} WHERE {n.C("id")} = @0", form.Id);
            return await Stats.DeleteFormAsync(form.Id);
        });
        var counted = await Task.WhenAll(counts);
        await delete;

        Assert.Multiple(async () =>
        {
            Assert.That(await Stats.ListAsync(form.Id, Today, Today), Is.Empty, "no count reached the form after its delete");
            Assert.That(await Stats.IncrementAsync(form.Id, [new DailyStatCount("views")]), Is.False);
            Assert.That((await Stats.ListAsync(other.Id, Today, Today)).Single(stat => stat.Metric == "views").Value, Is.EqualTo(25), "the other form counted on");
            Assert.That(counted.Where((_, i) => i % 4 == 0), Is.All.True);
        });
    }

    [Test]
    public async Task Counts_JoinTheCallersUnit_AndRollBackWithIt()
    {
        var form = await CreateFormAsync();

        Assert.That(
            () => Database.WriteAsync<bool>(async db =>
            {
                await Stats.IncrementAsync(form.Id, [new DailyStatCount("submissions")]);
                throw new InvalidOperationException("JFB_TEST the submission fails");
            }),
            Throws.InvalidOperationException);
        await Database.WriteAsync(_ => Stats.IncrementAsync(form.Id, [new DailyStatCount("views")]));

        Assert.That(await Stats.ListAsync(form.Id, Today, Today), Is.EqualTo(new[] { new DailyStat(Today, form.Id, "views", string.Empty, 1) }));
    }

    [Test]
    public async Task Counters_AreRight_UnderParallelIncrements_FromTwoServers()
    {
        var form = await CreateFormAsync();
        var servers = new[] { Stats, new DailyStatRepository(Database) };

        // 400 increments of one counter, 100 batches of three counters of which one is new mid-run, and on the next day
        // 100 first counts at once, all split between two servers.
        await Task.WhenAll(Enumerable.Range(0, 400).Select(i => servers[i % 2].IncrementAsync(form.Id, [new DailyStatCount("views")])));
        // Fields added to the form at the same time lock its row too, as any change of the form does.
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => servers[i % 2].IncrementAsync(form.Id,
        [
            new DailyStatCount("starts"),
            new DailyStatCount("completionSeconds", By: 10),
            new DailyStatCount("pageReached", i < 50 ? Page : "second", 1),
        ])).Cast<Task>().Concat(Enumerable.Range(0, 20).Select(i => (Task)Forms.AddFieldAsync(form.Id, FormRepositoryTests.Field("field" + i.ToString(CultureInfo.InvariantCulture), i)))));
        Clock.Advance(TimeSpan.FromDays(1));
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => servers[i % 2].IncrementAsync(form.Id, [new DailyStatCount("views"), new DailyStatCount("hour", "8")])));

        Assert.That(await Stats.ListAsync(form.Id, Today, Today.AddDays(1)), Is.EqualTo(new[]
        {
            new DailyStat(Today, form.Id, "completionSeconds", string.Empty, 1000),
            new DailyStat(Today, form.Id, "pageReached", Page, 50),
            new DailyStat(Today, form.Id, "pageReached", "second", 50),
            new DailyStat(Today, form.Id, "starts", string.Empty, 100),
            new DailyStat(Today, form.Id, "views", string.Empty, 400),
            new DailyStat(Today.AddDays(1), form.Id, "hour", "8", 100),
            new DailyStat(Today.AddDays(1), form.Id, "views", string.Empty, 100),
        }));
        Assert.That(await Forms.ListFieldsAsync(form.Id), Has.Count.EqualTo(20));
    }

    [Test]
    public async Task DeletingAForm_DeletesAllOfItsValues_AndOnlyItsOwn()
    {
        var form = await CreateFormAsync();
        var other = await CreateFormAsync();
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views"), new DailyStatCount("origin", Page)]);
        Clock.Advance(TimeSpan.FromDays(1));
        await Stats.IncrementAsync(form.Id, [new DailyStatCount("views")]);
        await Stats.IncrementAsync(other.Id, [new DailyStatCount("views")]);

        Assert.Multiple(async () =>
        {
            Assert.That(await Stats.DeleteFormAsync(form.Id), Is.EqualTo(3));
            Assert.That(await Stats.ListAsync(form.Id, Today, Today.AddDays(1)), Is.Empty);
            Assert.That(await Stats.ListAsync(other.Id, Today, Today.AddDays(1)), Has.Count.EqualTo(1));
            Assert.That(await Stats.DeleteFormAsync(form.Id), Is.EqualTo(0));
        });
    }

    [Test]
    public async Task ThePurge_DeletesAFormAndADayAtATime_TheEarliestFirst_AndKeepsTheDaysWithinTheTime()
    {
        var form = await CreateFormAsync();
        var other = await CreateFormAsync();
        for (var day = 0; day < 5; day++)
        {
            await Stats.IncrementAsync(form.Id, [new DailyStatCount("views"), new DailyStatCount("spam")]);
            await Stats.IncrementAsync(other.Id, [new DailyStatCount("views")]);
            Clock.Advance(TimeSpan.FromDays(1));
        }

        // Now is five days after the first day: keeping three days leaves the values of the third, fourth and fifth day.
        var keep = TimeSpan.FromDays(3);
        var first = await Stats.PurgeAsync(keep);
        var formAfterFirst = await Stats.ListAsync(form.Id, Today, Today.AddDays(5));
        var otherAfterFirst = await Stats.ListAsync(other.Id, Today, Today.AddDays(5));
        var rest = new List<int>();
        for (var purge = 0; purge < 4; purge++)
        {
            rest.Add(await Stats.PurgeAsync(keep));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(first, Is.EqualTo(2), "the first day's values of the first form");
            Assert.That(formAfterFirst.Select(stat => stat.Day).Distinct(), Is.EqualTo(new[] { Today.AddDays(1), Today.AddDays(2), Today.AddDays(3), Today.AddDays(4) }));
            Assert.That(otherAfterFirst, Has.Count.EqualTo(5), "the other form's first day comes next");
            Assert.That(rest, Is.EqualTo(new[] { 1, 2, 1, 0 }), "the other form, then the next day of each, until no day is older");
            Assert.That((await Stats.ListAsync(form.Id, Today, Today.AddDays(5))).Select(stat => stat.Day).Distinct(), Is.EqualTo(new[] { Today.AddDays(2), Today.AddDays(3), Today.AddDays(4) }));
            Assert.That(await Stats.ListAsync(other.Id, Today, Today.AddDays(5)), Has.Count.EqualTo(3));
        });
    }
}
