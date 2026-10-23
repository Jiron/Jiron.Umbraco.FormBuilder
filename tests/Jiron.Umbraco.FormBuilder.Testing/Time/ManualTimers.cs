namespace Jiron.Umbraco.FormBuilder.Testing.Time;

/// <summary>
/// A clock whose timers fire only when a test says so (ADR-0011: fake time where time matters). The wall clock is the
/// system's; every timer created through it is recorded with its due time and period.
/// </summary>
public sealed class ManualTimers : TimeProvider
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];

    /// <summary>The timers created so far, oldest first.</summary>
    public IReadOnlyList<ManualTimer> Timers
    {
        get
        {
            lock (_gate)
            {
                return _timers.ToList();
            }
        }
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Fires every timer that was not disposed.</summary>
    public void FireAll()
    {
        foreach (var timer in Timers)
        {
            timer.Fire();
        }
    }
}

/// <summary>A timer of <see cref="ManualTimers"/>.</summary>
public sealed class ManualTimer : ITimer
{
    private readonly TimerCallback _callback;
    private readonly object? _state;
    private int _disposed;

    internal ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _callback = callback;
        _state = state;
        DueTime = dueTime;
        Period = period;
    }

    /// <summary>When it was last set to fire.</summary>
    public TimeSpan DueTime { get; private set; }

    /// <summary>How often it was last set to repeat; <see cref="Timeout.InfiniteTimeSpan"/> for once.</summary>
    public TimeSpan Period { get; private set; }

    /// <summary>True once disposed; a disposed timer never fires.</summary>
    public bool Disposed => Volatile.Read(ref _disposed) == 1;

    /// <summary>Runs the callback now, unless the timer was disposed.</summary>
    public void Fire()
    {
        if (!Disposed)
        {
            _callback(_state);
        }
    }

    /// <inheritdoc />
    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        DueTime = dueTime;
        Period = period;
        return !Disposed;
    }

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
