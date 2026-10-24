using System.Data;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Scoping;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The audit log and the daily statistics refuse what their columns cannot hold before they reach the database (ADR-0036,
/// ADR-0041): actions and entity types that are not names, metrics and dimensions that could carry text, counts outside
/// their range, times the databases cannot store, and windows, lists, retention times and batch sizes outside their
/// ranges.
/// </summary>
[TestFixture]
public sealed class OperationsRepositoryArgumentTests
{
    private static readonly Guid Item = Guid.Parse("6a0c2e1d-5b4f-4e3a-9d8c-7b6a5f4e3d2c");
    private static readonly Guid User = Guid.Parse("1f2e3d4c-5b6a-4987-8a9b-0c1d2e3f4a5b");
    private IScopeProvider _scopeProvider = null!;
    private IFormBuilderDatabaseGate _gate = null!;
    private FormBuilderDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeProvider = Substitute.For<IScopeProvider>();
        _gate = Substitute.For<IFormBuilderDatabaseGate>();
        _database = new FormBuilderDatabase(_scopeProvider, Substitute.For<IScopeAccessor>(), _gate, TimeProvider.System, NullLogger<FormBuilderDatabase>.Instance);
    }

    // Every unit of work passes the gate first, and the substitute gate never runs one: a call that reached it would have
    // reached the database.
    [TearDown]
    public void NothingReachedTheDatabase()
    {
        Assert.That(_gate.ReceivedCalls(), Is.Empty, "a unit of work was started");
        _scopeProvider.DidNotReceiveWithAnyArgs().CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [TestCase("entry.deleted", true)]
    [TestCase("entries.exported", true)]
    [TestCase("settings.saved", true)]
    [TestCase("emailLayout.published", true)]
    [TestCase("entry.stateChanged", true)]
    [TestCase("entry2.v2", true)]
    [TestCase("entry", false, TestName = "one word")]
    [TestCase("entry.state.changed", false, TestName = "three words")]
    [TestCase("Entry.deleted", false, TestName = "a capital first")]
    [TestCase("entry.Deleted", false, TestName = "a capital verb")]
    [TestCase("entry.", false, TestName = "no verb")]
    [TestCase(".deleted", false, TestName = "no entity")]
    [TestCase("entry deleted", false, TestName = "a space")]
    [TestCase("entry.deleted\n", false, TestName = "a line break at the end")]
    [TestCase("entry_note.added", false, TestName = "an underscore")]
    [TestCase("2fa.enabled", false, TestName = "a digit first")]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void AnAction_IsEntityDotVerb(string? action, bool valid)
    {
        Assert.That(AuditRepository.IsAction(action), Is.EqualTo(valid));
    }

    [Test]
    public void AnAction_HoldsAtMost64Characters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AuditRepository.IsAction("e." + new string('v', 62)), Is.True);
            Assert.That(AuditRepository.IsAction("e." + new string('v', 63)), Is.False);
            Assert.That(AuditRepository.IsEntityType(new string('e', 64)), Is.True);
            Assert.That(AuditRepository.IsEntityType(new string('e', 65)), Is.False);
            Assert.That(AuditRepository.IsEntityType("optionList"), Is.True);
            Assert.That(AuditRepository.IsEntityType("option.list"), Is.False);
            Assert.That(AuditRepository.IsEntityType("Entry"), Is.False);
        });
    }

    [Test]
    public void AuditEntries_AreRefused_WhenTheyCannotBeRecorded()
    {
        var log = new AuditRepository(_database);

        Assert.Multiple(() =>
        {
            Refused(() => log.AddAsync(null!), "null");
            Refused(() => log.AddAsync(new NewAuditEntry("entry deleted", "entry", Item, User)), "an action with a space");
            Refused(() => log.AddAsync(new NewAuditEntry("entry.deleted", "Entry", Item, User)), "an entity type with a capital");
            Refused(() => log.AddAsync(new NewAuditEntry("entry.deleted", "entry", Guid.Empty, User)), "an empty item key");
            Refused(() => log.AddAsync(new NewAuditEntry("entry.deleted", "entry", Item, Guid.Empty)), "an empty user key");
            Refused(() => log.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", null, User), TimeSpan.FromMinutes(15)), "once per window without an item");
            Refused(() => log.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", Item, null), TimeSpan.FromMinutes(15)), "once per window without a user");
            Refused(() => log.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", Item, User), TimeSpan.Zero), "no window");
            Refused(() => log.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", Item, User), AuditRepository.MinWindow - TimeSpan.FromTicks(1)), "a window shorter than the stored times are exact");
            Refused(() => log.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", Item, User), AuditRepository.MaxWindow + TimeSpan.FromTicks(1)), "a long window");
            Refused(() => log.QueryAsync(new AuditQuery { Action = "deleted" }, PageRequest.First(10)), "a query by a malformed action");
            Refused(() => log.QueryAsync(new AuditQuery { EntityType = "en try" }, PageRequest.First(10)), "a query by a malformed entity type");
            Refused(() => log.QueryAsync(null!, PageRequest.First(10)), "no query");
            Refused(() => log.QueryAsync(new AuditQuery { FromUtc = FormBuilderDbTime.MinStorable.AddTicks(-1) }, PageRequest.First(10)), "from before 1753");
            Refused(() => log.QueryAsync(new AuditQuery { FromUtc = DateTimeOffset.MaxValue }, PageRequest.First(10)), "from the end of time");
            Refused(() => log.QueryAsync(new AuditQuery { BeforeUtc = DateTimeOffset.MinValue }, PageRequest.First(10)), "before the year 1");
            Refused(() => log.QueryAsync(new AuditQuery { BeforeUtc = FormBuilderDbTime.MaxStorable.AddTicks(1) }, PageRequest.First(10)), "before the end of time");
            Refused(() => log.PurgeAsync(TimeSpan.Zero, 10), "keeping nothing");
            Refused(() => log.PurgeAsync(AuditRepository.MaxKeep + TimeSpan.FromTicks(1), 10), "keeping too long");
            Refused(() => log.PurgeAsync(TimeSpan.FromDays(365), 0), "a purge of none");
            Refused(() => log.PurgeAsync(TimeSpan.FromDays(365), AuditRepository.MaxPurgeTake + 1), "a large purge");
        });
    }

    [TestCase("views", true)]
    [TestCase("pageReached", true)]
    [TestCase("completionSeconds", true)]
    [TestCase("hour24", true)]
    [TestCase("Views", false, TestName = "a capital first")]
    [TestCase("page.reached", false, TestName = "a dot")]
    [TestCase("page reached", false, TestName = "a space")]
    [TestCase("views\n", false, TestName = "a line break at the end")]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void AMetric_IsOneCamelCaseWord(string? metric, bool valid)
    {
        Assert.That(DailyStatRepository.IsMetric(metric), Is.EqualTo(valid));
    }

    [TestCase("", true, TestName = "none")]
    [TestCase("23", true, TestName = "an hour")]
    [TestCase("6a0c2e1d-5b4f-4e3a-9d8c-7b6a5f4e3d2c", true, TestName = "a key")]
    [TestCase("6a0c2e1d-5b4f-4e3a-9d8c-7b6a5f4e3d2c:minLength", true, TestName = "a field key and an error kind")]
    [TestCase("a_b.c", true)]
    [TestCase("Mia Muster", false, TestName = "a name")]
    [TestCase("mia@example.test", false, TestName = "an email address")]
    [TestCase("+4930123456", false, TestName = "a phone number")]
    [TestCase("a/b", false, TestName = "a slash")]
    [TestCase("x\n", false, TestName = "a line break at the end")]
    [TestCase(null, false)]
    public void ADimension_HoldsNoText(string? dimension, bool valid)
    {
        Assert.That(DailyStatRepository.IsDimension(dimension), Is.EqualTo(valid));
    }

    [Test]
    public void Counts_AreRefused_WhenTheColumnsCannotHoldThem()
    {
        var stats = new DailyStatRepository(_database);

        Assert.Multiple(() =>
        {
            Refused(() => stats.IncrementAsync(1, null!), "null");
            Refused(() => stats.IncrementAsync(1, [null!]), "a null count");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("Views")]), "a metric with a capital");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount(new string('m', 65))]), "a long metric");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("origin", new string('d', 129))]), "a long dimension");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("origin", "Mia Muster")]), "text as a dimension");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("origin", null!)]), "a null dimension");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("views", By: -1)]), "a negative count");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("views"), new DailyStatCount("views", By: -1)]), "a negative count after a good one");
            Refused(() => stats.IncrementAsync(1, [new DailyStatCount("views", By: DailyStatRepository.MaxBy + 1)]), "a count too large");
            Refused(() => stats.IncrementAsync(1, Enumerable.Repeat(new DailyStatCount("views"), DailyStatRepository.MaxCountsPerCall + 1).ToList()), "too many counts");
            Refused(() => stats.IncrementAsync(0, [new DailyStatCount("views")]), "no form");
            Refused(() => stats.DeleteFormAsync(0), "no form to delete the values of");
            Refused(() => stats.ListAsync(1, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 1)), "a range that ends before it starts");
            Refused(() => stats.ListAsync(1, new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 2)), "367 days");
            Refused(() => stats.ListAsync(1, new DateOnly(1752, 12, 31), new DateOnly(1753, 1, 1)), "a day before 1753");
            Refused(() => stats.PurgeAsync(TimeSpan.Zero), "keeping nothing");
            Refused(() => stats.PurgeAsync(DailyStatRepository.MaxKeep + TimeSpan.FromTicks(1)), "keeping too long");

        });
    }

    [Test]
    public async Task NothingToCount_WritesNothing()
    {
        var stats = new DailyStatRepository(_database);

        Assert.Multiple(async () =>
        {
            Assert.That(await stats.IncrementAsync(1, []), Is.True);
            Assert.That(await stats.IncrementAsync(1, [new DailyStatCount("views", By: 0), new DailyStatCount("spam", By: 0)]), Is.True);
        });
    }

    [Test]
    public void TheLimits_ReachTheirEdges()
    {
        // Each of these passes the checks and so reaches the gate, which the substitute never opens.
        var stats = new DailyStatRepository(_database);
        var log = new AuditRepository(_database);
        var calls = new Func<Task>[]
        {
            () => stats.IncrementAsync(1, [.. Enumerable.Repeat(new DailyStatCount("views", By: DailyStatRepository.MaxBy), DailyStatRepository.MaxCountsPerCall)]),
            () => stats.ListAsync(1, new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31)),
            () => stats.ListAsync(1, new DateOnly(1753, 1, 1), new DateOnly(1753, 1, 1)),
            () => log.AddOnceAsync(new NewAuditEntry("entry.viewed", "entry", Item, User), AuditRepository.MinWindow),
            () => log.QueryAsync(new AuditQuery { FromUtc = FormBuilderDbTime.MinStorable, BeforeUtc = FormBuilderDbTime.MaxStorable }, PageRequest.First(10)),
        };

        Assert.Multiple(() =>
        {
            foreach (var call in calls)
            {
                Assert.That(() => call(), Throws.Nothing);
            }
        });
        Assert.That(_gate.ReceivedCalls(), Has.Exactly(calls.Length).Items, "every call reached the gate");
        _gate.ClearReceivedCalls();
    }

    private static void Refused<T>(Func<Task<T>> call, string why) =>
        Assert.That(call, Throws.InstanceOf<ArgumentException>(), why);
}
