using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Insights;

/// <summary>
/// The pieces of insights (ADR-0211) that need no site: the buckets of the time to complete and what an entry adds to
/// its unit, the beacon's reader, which reads four members and nothing else, what a beacon may count, the sums that
/// wait in memory, and a report built from the daily values.
/// </summary>
[TestFixture]
public sealed class InsightTests
{
    private static readonly Guid Second = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Name = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Guests = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid GuestName = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Grid = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid RowKey = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 23, 59, 58, TimeSpan.Zero);

    [TestCase(0, "15")]
    [TestCase(15, "15")]
    [TestCase(15.5, "30")]
    [TestCase(59, "60")]
    [TestCase(61, "120")]
    [TestCase(3600, "3600")]
    [TestCase(3601, "more")]
    [TestCase(-4, "15")]
    public void ATimeToComplete_FallsIntoTheFirstBucketItFits(double seconds, string bucket) =>
        Assert.That(InsightMetrics.BucketOf(TimeSpan.FromSeconds(seconds)), Is.EqualTo(bucket));

    [Test]
    public void AStoredEntry_AddsItsPage_ItsHour_AndItsTimeToComplete_EachWhereItIsKnown()
    {
        var page = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var local = new DateTimeOffset(2026, 11, 7, 14, 30, 0, TimeSpan.FromHours(1));
        Assert.Multiple(() =>
        {
            Assert.That(InsightMetrics.OfEntry(page, local, TimeSpan.FromSeconds(42)), Is.EqualTo(new[]
            {
                new DailyStatCount("origin", "88888888888888888888888888888888"),
                new DailyStatCount("hour", "14"),
                new DailyStatCount("completion", "60"),
            }));
            Assert.That(InsightMetrics.OfEntry(null, local, null), Is.EqualTo(new[] { new DailyStatCount("hour", "14") }), "no page and no challenge");
            Assert.That(InsightMetrics.OfEntry(page, local, null).All(count => DailyStatRepository.IsMetric(count.Metric) && DailyStatRepository.IsDimension(count.Dimension)), Is.True);
        });
    }

    [Test]
    public void ABeacon_IsReadFromItsFourMembers_AndEveryOtherIsSkippedUnread()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Read("""{"type":"view"}"""), Is.EqualTo(new InsightBeacon("view")));
            Assert.That(Read("""{"type":"invalid","field":"guests[0].name","kind":"required"}"""), Is.EqualTo(new InsightBeacon("invalid", Field: "guests[0].name", Kind: "required")));
            Assert.That(
                Read("""{"email":"mia@example.com","type":"page","answers":{"name":["Mia"],"deep":[1,{"a":null}]},"page":"22222222-2222-2222-2222-222222222222","n":3,"t":true}"""),
                Is.EqualTo(new InsightBeacon("page", Page: "22222222-2222-2222-2222-222222222222")),
                "members of any shape beside them");
        });
    }

    [TestCase("")]
    [TestCase("view")]
    [TestCase("[]")]
    [TestCase("""["type","view"]""")]
    [TestCase("""{"page":"x"}""")]
    [TestCase("""{"type":1}""")]
    [TestCase("""{"type":null}""")]
    [TestCase("""{"type":"view","type":"start"}""")]
    [TestCase("""{"type":"view"} {}""")]
    [TestCase("""{"type":"view" """)]
    [TestCase("""{"type":"view","x":[[[[[1]]]]]}""")]
    public void ABodyThatIsNoBeacon_IsReadAsNone(string body) => Assert.That(Read(body), Is.Null);

    [Test]
    public void ABeaconLongerThanItsLimit_IsReadAsNone()
    {
        var padded = "{\"type\":\"view\",\"pad\":\"" + new string('x', FormInsights.MaxBeaconBytes) + "\"}";
        Assert.Multiple(() =>
        {
            Assert.That(FormInsights.Read(Encoding.UTF8.GetBytes(padded)), Is.Null);
            Assert.That(FormInsights.Read(Encoding.UTF8.GetBytes(padded[..(FormInsights.MaxBeaconBytes - 2)] + "\"}")), Is.Not.Null, "one at the limit");
        });
    }

    [Test]
    public async Task ABeacon_CountsOnlyWhatTheVersionHas_AndOnlyWhileTheFormsAndTheSitesInsightsAreOn()
    {
        var counts = new InsightCounts(new StoppedClock(Now));
        var insights = new FormInsights(Settings(on: true), counts);
        var form = Published(Definition());

        Assert.Multiple(async () =>
        {
            Assert.That(await insights.CountAsync(form, new InsightBeacon("view")), Is.True);
            Assert.That(await insights.CountAsync(form, new InsightBeacon("start", Page: "ignored")), Is.True);
            Assert.That(await insights.CountAsync(form, new InsightBeacon("page", Page: Second.ToString("D"))), Is.True);
            Assert.That(await insights.CountAsync(form, new InsightBeacon("page", Page: Guid.NewGuid().ToString("D"))), Is.False, "a page the version lacks");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("page", Page: Second.ToString("N"))), Is.False, "a key in another shape");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "name", Kind: "required")), Is.True);
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "guests[3].guestName", Kind: "maxLength")), Is.True, "a row's child");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "grid." + RowKey.ToString("D"), Kind: "rule")), Is.True, "a grid's row counts under its grid");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "name.part", Kind: "check")), Is.True, "a part counts under its field");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "nobody", Kind: "required")), Is.False, "a field the version lacks");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "name", Kind: "Mia Muster")), Is.False, "a kind no form knows");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: "name")), Is.False, "no kind");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("invalid", Field: new string('a', 600), Kind: "required")), Is.False, "a path too long");
            Assert.That(await insights.CountAsync(form, new InsightBeacon("submit")), Is.False, "a type no beacon has");
        });

        var day = DateOnly.FromDateTime(Now.UtcDateTime);
        Assert.That(counts.Take(), Is.EquivalentTo(new Dictionary<InsightKey, long>
        {
            [new(form.Form.Key, day, "views", "")] = 1,
            [new(form.Form.Key, day, "starts", "")] = 1,
            [new(form.Form.Key, day, "page", "22222222222222222222222222222222")] = 1,
            [new(form.Form.Key, day, "invalid", "33333333333333333333333333333333:required")] = 1,
            [new(form.Form.Key, day, "invalid", "55555555555555555555555555555555:maxLength")] = 1,
            [new(form.Form.Key, day, "invalid", "66666666666666666666666666666666:rule")] = 1,
            [new(form.Form.Key, day, "invalid", "33333333333333333333333333333333:check")] = 1,
        }));

        var off = new FormInsights(Settings(on: false), counts);
        var formOff = Published(Definition() with { Settings = new FormSettings { Privacy = new FormPrivacySettings { Insights = false } } });
        Assert.Multiple(async () =>
        {
            Assert.That(await off.CountAsync(form, new InsightBeacon("view")), Is.False, "the site's are off");
            Assert.That(await insights.CountAsync(formOff, new InsightBeacon("view")), Is.False, "the form's are off");
            Assert.That(counts.Pending, Is.Zero);
        });
    }

    [Test]
    public async Task APostRefusedWithoutJavaScript_CountsEachFieldAndKindOnce_ByThePathsOfItsProblems()
    {
        var counts = new InsightCounts(new StoppedClock(Now));
        var form = Published(Definition());
        await new FormInsights(Settings(on: true), counts).RefusedAsync(form,
        [
            new SubmissionProblem("required", "name"),
            new SubmissionProblem("required", "name"),
            new SubmissionProblem("required", "guests[0].guestName"),
            new SubmissionProblem("required", "guests[1].guestName"),
            new SubmissionProblem("rule", "grid." + RowKey.ToString("D"), Rule: Guid.NewGuid()),
            new SubmissionProblem("empty", string.Empty),
            new SubmissionProblem("invalid_value", "query.campaign"),
        ]);

        var day = DateOnly.FromDateTime(Now.UtcDateTime);
        Assert.That(counts.Take(), Is.EquivalentTo(new Dictionary<InsightKey, long>
        {
            [new(form.Form.Key, day, "refused", "33333333333333333333333333333333:required")] = 1,
            [new(form.Form.Key, day, "refused", "55555555555555555555555555555555:required")] = 1,
            [new(form.Form.Key, day, "refused", "66666666666666666666666666666666:rule")] = 1,
        }));
    }

    [Test]
    public void TheSumsThatWait_AddUp_StopAtTheirLimit_AndComeBackWhenAWriteFails()
    {
        var clock = new StoppedClock(Now);
        var counts = new InsightCounts(clock);
        var form = Guid.NewGuid();
        Assert.That(counts.Add(form, "views"), Is.True);
        Assert.That(counts.Add(form, "views"), Is.True);
        clock.Now = Now.AddSeconds(5);
        Assert.That(counts.Add(form, "views"), Is.True, "a beacon after midnight starts the next day's sum");

        var taken = counts.Take();
        Assert.Multiple(() =>
        {
            Assert.That(taken[new InsightKey(form, new DateOnly(2026, 11, 7), "views", "")], Is.EqualTo(2));
            Assert.That(taken[new InsightKey(form, new DateOnly(2026, 11, 8), "views", "")], Is.EqualTo(1));
            Assert.That(counts.Pending, Is.Zero, "what was taken waits no more");
        });

        counts.Add(form, "views");
        counts.Restore(taken);
        Assert.That(counts.Take()[new InsightKey(form, new DateOnly(2026, 11, 8), "views", "")], Is.EqualTo(2), "put back beside what came meanwhile");

        for (var index = 0; index < InsightCounts.MaxPending; index++)
        {
            counts.Add(form, "page", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Multiple(() =>
        {
            Assert.That(counts.Add(form, "page", "one more"), Is.False, "a sum past the limit is dropped");
            Assert.That(counts.Add(form, "page", "0"), Is.True, "one that waits still adds up");
            Assert.That(counts.Pending, Is.EqualTo(InsightCounts.MaxPending));
        });
    }

    [Test]
    public void AReport_AddsUpItsDays_FollowsTheVersionsPages_AndFindsTheMiddleTimeToComplete()
    {
        var day = new DateOnly(2026, 11, 2);
        List<DailyStat> now =
        [
            Stat(day, "views", "", 40),
            Stat(day.AddDays(1), "views", "", 60),
            Stat(day, "starts", "", 30),
            Stat(day, "submissions", "", 7),
            Stat(day.AddDays(2), "submissions", "", 3),
            Stat(day, "spam", "", 2),
            Stat(day, "page", Key(Second), 12),
            Stat(day, "page", Key(Guid.NewGuid()), 99),
            Stat(day, "completion", "30", 2),
            Stat(day, "completion", "60", 2),
            Stat(day, "completion", "300", 6),
            Stat(day, "hour", "9", 4),
            Stat(day, "hour", "23", 6),
            Stat(day, "hour", "24", 1),
            Stat(day, "invalid", Key(Name) + ":required", 5),
            Stat(day, "refused", Key(Name) + ":check", 6),
            Stat(day, "invalid", Key(GuestName) + ":maxLength", 2),
            Stat(day, "origin", Key(Second), 3),
            Stat(day, "origin", Key(Name), 7),
        ];
        var report = InsightReports.Build(new InsightRequest(new InsightSpan(day, day.AddDays(6))), now, before: null, Definition(), members: new MemberEntryCount(2, 3), siteOn: true);

        Assert.Multiple(() =>
        {
            Assert.That(report.Totals, Is.EqualTo(new InsightTotals(100, 30, 10, 2)));
            Assert.That(report.Totals.Conversion, Is.EqualTo(0.1).Within(1e-9));
            Assert.That(report.Before, Is.Null);
            Assert.That(report.Periods.Select(period => (period.Start, period.Entries, period.Before)), Is.EqualTo(Enumerable.Range(0, 7).Select(offset => (day.AddDays(offset), offset switch { 0 => 7L, 2 => 3L, _ => 0L }, (long?)null))));
            Assert.That(report.Pages, Is.EqualTo(new[] { new InsightPageReach(Second, 12) }), "the pages after the first, of the version");
            Assert.That(report.MedianCompletion, Is.EqualTo("300"));
            Assert.That(report.Hours[9], Is.EqualTo(4));
            Assert.That(report.Hours[23], Is.EqualTo(6));
            Assert.That(report.Hours.Sum(), Is.EqualTo(10), "an hour past the day is no hour");
            Assert.That(report.Fields, Is.EqualTo(new[] { new InsightFieldStops(Name, 11, "check"), new InsightFieldStops(GuestName, 2, "maxLength") }), "both kinds of stop, the kind that stops most");
            Assert.That(report.Origins, Is.EqualTo(new[] { new InsightOrigin(Name, 7), new InsightOrigin(Second, 3) }));
            Assert.That((report.Members, report.MembersBefore, report.SiteOn, report.FormOn), Is.EqualTo((new MemberEntryCount(2, 3), (long?)null, true, true)));
        });
    }

    [Test]
    public void AReport_GroupsByWeekFromMonday_OrByMonth_AndComparesEachGroupWithItsCounterpart()
    {
        // Wednesday 2026-11-04 to Sunday 2026-11-22, compared with the same span before.
        var span = new InsightSpan(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 22));
        var compare = new InsightSpan(new DateOnly(2026, 10, 16), new DateOnly(2026, 11, 3));
        List<DailyStat> now = [Stat(new DateOnly(2026, 11, 4), "submissions", "", 1), Stat(new DateOnly(2026, 11, 9), "submissions", "", 2), Stat(new DateOnly(2026, 11, 22), "submissions", "", 4)];
        List<DailyStat> before = [Stat(new DateOnly(2026, 10, 16), "submissions", "", 5), Stat(new DateOnly(2026, 11, 2), "submissions", "", 6)];

        var weeks = InsightReports.Build(new InsightRequest(span, InsightGrouping.Week, compare), now, before, null, default, true);
        var months = InsightReports.Build(new InsightRequest(span, InsightGrouping.Month, compare), now, before, null, default, true);

        Assert.Multiple(() =>
        {
            Assert.That(weeks.Periods.Select(period => (period.Start, period.Entries, period.Before)), Is.EqualTo(new[]
            {
                (new DateOnly(2026, 11, 2), 1L, (long?)5),
                (new DateOnly(2026, 11, 9), 2L, (long?)0),
                (new DateOnly(2026, 11, 16), 4L, (long?)0),
            }), "weeks from Monday, each beside the week in its place of the span compared with, which starts on a Friday");
            Assert.That(months.Periods.Select(period => (period.Start, period.Entries, period.Before)), Is.EqualTo(new[] { (new DateOnly(2026, 11, 1), 7L, (long?)5) }), "October's part of the span compared with is its first group");
            Assert.That(weeks.Before, Is.EqualTo(new InsightTotals(0, 0, 11, 0)));
            Assert.That(weeks.Pages, Is.Empty, "no version, no pages");
            Assert.That(weeks.MedianCompletion, Is.Null);
        });
    }

    private static InsightBeacon? Read(string body) => FormInsights.Read(Encoding.UTF8.GetBytes(body));

    private static string Key(Guid key) => key.ToString("N");

    private static DailyStat Stat(DateOnly day, string metric, string dimension, long value) => new(day, 1, metric, dimension, value);

    private static IFormBuilderSettingsService Settings(bool on)
    {
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<PrivacySettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new SettingsSnapshot<PrivacySettings>(new PrivacySettings { Insights = on }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>()));
        return settings;
    }

    private static FormDefinition Definition() => new()
    {
        Key = Guid.NewGuid(),
        Pages =
        [
            new FormPage
            {
                Key = Guid.NewGuid(),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Guid.NewGuid(),
                        Fields =
                        [
                            new FormField { Key = Name, Type = "text", Alias = "name" },
                            new FormField { Key = Guests, Type = "repeater", Alias = "guests", Fields = [new FormField { Key = GuestName, Type = "text", Alias = "guestName" }] },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = Second,
                Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [new FormField { Key = Grid, Type = "grid", Alias = "grid", Rows = [new GridRow { Key = RowKey }] }] }],
            },
        ],
    };

    private static PublishedForm Published(FormDefinition definition) =>
        new(new FormInfo(Guid.NewGuid(), "contact", "Contact", null, 1, true, 1, false, null, Now, Now, null, null), definition, 1);

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
