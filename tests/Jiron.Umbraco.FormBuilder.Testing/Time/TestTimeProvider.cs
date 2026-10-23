namespace Jiron.Umbraco.FormBuilder.Testing.Time;

/// <summary>
/// A clock that tests move by hand (ADR-0011: fake time where time matters). Only the wall clock is faked; timers and
/// timestamps come from the base class.
/// </summary>
public sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    /// <summary>A clock that starts at 2026-01-01 00:00 UTC.</summary>
    public TestTimeProvider()
        : this(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Interlocked.Add(ref _ticks, by.Ticks);
    }
}
