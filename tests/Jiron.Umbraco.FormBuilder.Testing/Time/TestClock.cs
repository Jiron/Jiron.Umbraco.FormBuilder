namespace Jiron.Umbraco.FormBuilder.Testing.Time;

/// <summary>
/// A test host's clock: the real time, moved by an offset that browser tests set through <c>/jfb-testing/clock</c>.
/// Only a host with <c>JironFormBuilderTesting:Clock</c> on registers it as its container's <see cref="TimeProvider"/>,
/// so Jiron Form Builder and everything else that takes the time from there (ASP.NET Core's sign-in cookies, for one)
/// see the moved time. Timers are not moved. A suite that moves it resets it when it ends.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private long _offsetTicks;

    /// <summary>How far the clock is moved from the real time.</summary>
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + Offset;

    /// <summary>Moves the clock so that it shows <paramref name="utc"/> now.</summary>
    public void SetNow(DateTimeOffset utc) => Interlocked.Exchange(ref _offsetTicks, (utc - System.GetUtcNow()).Ticks);

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Interlocked.Add(ref _offsetTicks, by.Ticks);
    }

    /// <summary>Back to the real time.</summary>
    public void Reset() => Interlocked.Exchange(ref _offsetTicks, 0);
}
