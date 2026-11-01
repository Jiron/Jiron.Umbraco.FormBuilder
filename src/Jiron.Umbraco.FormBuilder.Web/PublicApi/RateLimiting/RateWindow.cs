namespace Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;

/// <summary>
/// At most <see cref="Limit"/> requests per <see cref="Length"/>, counted in ten segments that roll out one by one, on a
/// <see cref="TimeProvider"/> so tests can move time (ADR-0012, ADR-0137). It lives in memory, so it counts per server.
/// It also counts what it refused, which says whether a client is only a little over its limit or flooding.
/// </summary>
internal sealed class RateWindow
{
    /// <summary>How many segments a window rolls through.</summary>
    public const int Segments = 10;

    private readonly Lock _gate = new();
    private readonly int[] _taken = new int[Segments];
    private readonly int[] _refused = new int[Segments];
    private readonly long _segmentTicks;
    private readonly TimeProvider _time;
    private long _segmentStart;
    private long _lastActivity;
    private int _current;
    private int _takenTotal;
    private int _refusedTotal;
    private bool _retired;

    /// <summary>A window of <paramref name="limit"/> requests per <paramref name="length"/>.</summary>
    /// <param name="limit">The requests per window, 1 or more.</param>
    /// <param name="length">The window's length.</param>
    /// <param name="time">The clock.</param>
    public RateWindow(int limit, TimeSpan length, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(time);
        Limit = limit;
        Length = length;
        _time = time;
        _segmentTicks = Math.Max(1, length.Ticks / Segments);
        _segmentStart = _lastActivity = time.GetUtcNow().UtcTicks;
    }

    /// <summary>The requests per window.</summary>
    public int Limit { get; }

    /// <summary>The window's length.</summary>
    public TimeSpan Length { get; }

    /// <summary>How many requests the window holds now, those it refused among them.</summary>
    public int Tried
    {
        get
        {
            lock (_gate)
            {
                Advance(_time.GetUtcNow().UtcTicks);
                return _takenTotal + _refusedTotal;
            }
        }
    }

    /// <summary>
    /// Ends a window that has been empty and unused for a whole length, so a request that holds it still does not count on
    /// a window nobody will ask again; false for one that is still in use.
    /// </summary>
    public bool TryRetire()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow().UtcTicks;
            Advance(now);
            _retired |= _takenTotal + _refusedTotal == 0 && now - _lastActivity >= Length.Ticks;
            return _retired;
        }
    }

    /// <summary>Counts one request when it fits; otherwise <paramref name="retryAfter"/> says when the next will.</summary>
    /// <param name="retryAfter">How long until a request fits; zero when this one did.</param>
    /// <param name="retired">Whether the window has ended and counted nothing, so the request belongs to a new one.</param>
    public bool TryTake(out TimeSpan retryAfter, out bool retired)
    {
        lock (_gate)
        {
            retired = _retired;
            if (retired)
            {
                retryAfter = TimeSpan.Zero;
                return false;
            }

            var now = _time.GetUtcNow().UtcTicks;
            Advance(now);
            _lastActivity = now;
            if (_takenTotal < Limit)
            {
                _taken[_current]++;
                _takenTotal++;
                retryAfter = TimeSpan.Zero;
                return true;
            }

            _refused[_current]++;
            _refusedTotal++;
            retryAfter = RetryAfter(now);
            return false;
        }
    }

    /// <summary>Counts what has happened already, past the limit if it must be; false for a window that has ended, which counted nothing.</summary>
    /// <param name="count">How much to count.</param>
    public bool TryCount(int count)
    {
        lock (_gate)
        {
            if (_retired)
            {
                return false;
            }

            var now = _time.GetUtcNow().UtcTicks;
            Advance(now);
            _lastActivity = now;
            _taken[_current] += count;
            _takenTotal += count;
            return true;
        }
    }

    /// <summary>Whether the window holds its limit already, with how long until it holds less; it counts nothing.</summary>
    /// <param name="retryAfter">How long until it holds less; zero when it does now.</param>
    public bool IsFull(out TimeSpan retryAfter)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow().UtcTicks;
            Advance(now);
            var full = _takenTotal >= Limit;
            retryAfter = full ? RetryAfter(now) : TimeSpan.Zero;
            return full;
        }
    }

    /// <summary>Gives back the newest request still counted: one that a later partition refused.</summary>
    public void Release()
    {
        lock (_gate)
        {
            Advance(_time.GetUtcNow().UtcTicks);
            for (var age = 0; age < Segments; age++)
            {
                var index = (_current - age + Segments) % Segments;
                if (_taken[index] > 0)
                {
                    _taken[index]--;
                    _takenTotal--;
                    return;
                }
            }
        }
    }

    private void Advance(long now)
    {
        // A clock that went back keeps the counts rather than freeing requests early.
        var steps = (now - _segmentStart) / _segmentTicks;
        if (steps <= 0)
        {
            return;
        }

        for (var i = 0; i < Math.Min(steps, Segments); i++)
        {
            _current = (_current + 1) % Segments;
            _takenTotal -= _taken[_current];
            _refusedTotal -= _refused[_current];
            _taken[_current] = 0;
            _refused[_current] = 0;
        }

        _segmentStart += steps * _segmentTicks;
    }

    // When the oldest counted request rolls out: the segment of a given age (0 = current) goes when (Segments - age) more
    // segments have begun.
    private TimeSpan RetryAfter(long now)
    {
        for (var age = Segments - 1; age >= 0; age--)
        {
            if (_taken[(_current - age + Segments) % Segments] > 0)
            {
                return TimeSpan.FromTicks(Math.Max(_segmentStart + ((Segments - age) * _segmentTicks) - now, 0));
            }
        }

        return Length;
    }
}
