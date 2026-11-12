using System.Collections.Immutable;
using System.Net.Sockets;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// Whether this server reaches the mail server (ROADMAP 16.4, ADR-0237). A connection the mail server refuses, or that finds no
/// mail server, sent nothing, so the mail goes back to the queue with no attempt spent, and this server sends nothing more for
/// a pause that grows from half a minute to five; an outage of the mail server, however long, fails no mail. The first mail
/// that leaves ends it. Each server keeps its own, as each reaches the mail server itself.
/// </summary>
internal sealed class MailServerReach(TimeProvider time)
{
    /// <summary>How long this server waits after each refusal in a row, the last for every one after it.</summary>
    public static readonly ImmutableArray<TimeSpan> Pauses = [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5)];

    private readonly Lock _lock = new();
    private int _refusals;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    /// <summary>Whether this server waits before it tries the mail server again.</summary>
    public bool Paused
    {
        get
        {
            lock (_lock)
            {
                return time.GetUtcNow() < _pausedUntil;
            }
        }
    }

    /// <summary>Whether this server's last try found no mail server: from a refusal until a mail leaves.</summary>
    public bool Unreached
    {
        get
        {
            lock (_lock)
            {
                return _refusals > 0;
            }
        }
    }

    /// <summary>Whether an attempt ended because no mail server answered: a socket's error anywhere in what it threw.</summary>
    /// <param name="exception">What the attempt threw.</param>
    public static bool Refused(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var pending = new Stack<Exception>([exception]);
        for (var seen = 0; pending.Count > 0 && seen < 16; seen++)
        {
            var current = pending.Pop();
            if (current is SocketException)
            {
                return true;
            }

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    pending.Push(inner);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }

        return false;
    }

    /// <summary>
    /// Notes a refusal and says how long this server now waits. Attempts that were under way together end as one refusal: one
    /// that ends while the pause runs gets what is left of it.
    /// </summary>
    public TimeSpan Refusal()
    {
        lock (_lock)
        {
            var now = time.GetUtcNow();
            if (now < _pausedUntil)
            {
                return _pausedUntil - now;
            }

            var pause = Pauses[Math.Min(_refusals, Pauses.Length - 1)];
            _refusals++;
            _pausedUntil = now + pause;
            return pause;
        }
    }

    /// <summary>Notes that a mail left: the mail server is there again.</summary>
    public void Reached()
    {
        lock (_lock)
        {
            _refusals = 0;
            _pausedUntil = DateTimeOffset.MinValue;
        }
    }
}
