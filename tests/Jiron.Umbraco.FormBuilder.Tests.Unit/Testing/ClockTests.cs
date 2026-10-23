using Jiron.Umbraco.FormBuilder.Testing.Time;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Testing;

/// <summary>The fake clocks of the Testing project (ADR-0011: fake time where time matters).</summary>
[TestFixture]
public sealed class ClockTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Test]
    public void TestTimeProvider_New_StartsAtTheFirstOf2026()
    {
        Assert.That(new TestTimeProvider().GetUtcNow(), Is.EqualTo(Start));
    }

    [Test]
    public void TestTimeProvider_Advance_MovesOnlyWhenTold()
    {
        var time = new TestTimeProvider(Start);
        var before = time.GetUtcNow();

        time.Advance(TimeSpan.FromMinutes(90));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(Start));
            Assert.That(time.GetUtcNow(), Is.EqualTo(Start.AddMinutes(90)));
            Assert.That(time.GetUtcNow().Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void TestTimeProvider_AdvanceBackwards_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestTimeProvider().Advance(TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void TestClock_New_ShowsTheRealTime()
    {
        var clock = new TestClock();

        Assert.Multiple(() =>
        {
            Assert.That(clock.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(clock.GetUtcNow(), Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void TestClock_SetNowAdvanceReset_MoveTheOffset()
    {
        var clock = new TestClock();
        var target = DateTimeOffset.UtcNow.AddDays(400);

        clock.SetNow(target);
        var set = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(2));
        var advanced = clock.GetUtcNow();
        clock.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(set, Is.EqualTo(target).Within(TimeSpan.FromSeconds(5)));
            Assert.That(advanced, Is.EqualTo(target.AddHours(2)).Within(TimeSpan.FromSeconds(5)));
            Assert.That(clock.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(clock.GetUtcNow(), Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
        });
    }

    [Test]
    public void TestClock_AdvanceBackwards_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TestClock().Advance(TimeSpan.FromTicks(-1)));
    }

    [Test]
    public void ManualTimers_CreateTimer_RecordsItWithoutFiring()
    {
        var timers = new ManualTimers();
        var fired = 0;

        using var timer = timers.CreateTimer(_ => fired++, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));

        Assert.Multiple(() =>
        {
            Assert.That(fired, Is.Zero);
            Assert.That(timers.Timers, Has.Count.EqualTo(1));
            Assert.That(timers.Timers[0].DueTime, Is.EqualTo(TimeSpan.FromMinutes(5)));
            Assert.That(timers.Timers[0].Period, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public void ManualTimers_FireAll_RunsTheLiveTimersWithTheirState()
    {
        var timers = new ManualTimers();
        var seen = new List<object?>();
        using var live = timers.CreateTimer(seen.Add, "live", TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        var gone = timers.CreateTimer(seen.Add, "gone", TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        gone.Dispose();

        timers.FireAll();

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(new object?[] { "live" }));
            Assert.That(timers.Timers[1].Disposed, Is.True);
        });
    }

    [Test]
    public void ManualTimer_Change_UpdatesTheTimesUntilDisposed()
    {
        var timer = (ManualTimer)new ManualTimers().CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        var changed = timer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
        timer.Dispose();
        var changedAfterDispose = timer.Change(TimeSpan.Zero, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True);
            Assert.That(changedAfterDispose, Is.False);
            Assert.That(timer.DueTime, Is.EqualTo(TimeSpan.Zero));
        });
    }
}
