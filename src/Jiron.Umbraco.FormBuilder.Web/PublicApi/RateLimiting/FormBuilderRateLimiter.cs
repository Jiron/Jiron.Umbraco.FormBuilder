using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;

/// <summary>What the limiter says about one request.</summary>
/// <param name="Allowed">Whether the request goes on.</param>
/// <param name="RetryAfter">For a refusal, how long until a request fits.</param>
/// <param name="Renders">
/// For a refusal, whether a post without JavaScript may render its page again with its answers: only for a client a
/// little over a limit of its own or of the form (ADR-0012). The site-wide and overflow partitions, and a client at ten
/// times its limit, get a bare <c>429</c>, so a flood costs next to nothing.
/// </param>
internal readonly record struct RateLimitDecision(bool Allowed, TimeSpan RetryAfter, bool Renders)
{
    /// <summary>The request goes on.</summary>
    public static readonly RateLimitDecision Go = new(true, TimeSpan.Zero, false);

    /// <summary>The refusal's <c>Retry-After</c>: whole seconds, rounded up, at least one.</summary>
    public int RetryAfterSeconds => Math.Max(1, (int)Math.Ceiling(RetryAfter.TotalSeconds));
}

/// <summary>What one request has counted so far, while its form is looked up (ADR-0149).</summary>
internal sealed class RateLimitCount(FormBuilderRatePolicyOptions limits, string policy, string client, int factor)
{
    /// <summary>The policy's limits.</summary>
    public FormBuilderRatePolicyOptions Limits { get; } = limits;

    /// <summary>The policy's name.</summary>
    public string Policy { get; } = policy;

    /// <summary>Who the request counts for.</summary>
    public string Client { get; } = client;

    /// <summary>How many times an address's limits the client gets.</summary>
    public int Factor { get; } = factor;

    /// <summary>The windows that counted it, which a refusal further on gives back.</summary>
    public List<RateWindow> Taken { get; } = new(5);

    /// <summary>The refusal, when its address's own limits refused it.</summary>
    public RateLimitDecision? Refusal { get; set; }
}

/// <summary>
/// The rate limits of the public API and of posts without JavaScript, in memory on each server (ADR-0012, ADR-0137): each
/// policy's partitions, cheapest first, on the site's <see cref="TimeProvider"/>. A request a partition refuses is not
/// counted by the ones it would have met next, and those before it give it back, so one flooding client uses up no one
/// else's limit. It keeps at most <c>Limits:MaxTrackedClients</c> windows: past that a new client shares one of
/// <see cref="OverflowBuckets"/> overflow windows per partition, by a hash of its key, so a flood of distinct addresses is
/// limited, never let through, memory stays bounded, and a new visitor shares a window with a part of the flood only.
/// Emptied windows are swept every minute; while the bound is nearly full, so are windows that counted one request and have
/// had none for a minute, so a flood of addresses that each asked once does not hold the bound for an hour (16.1's review). A request that names a form counts its address first and the rest once the form is
/// known, so a key that names no form spends none of a form's or the site's limits (<see cref="FormRateLimits"/>, ADR-0149).
/// </summary>
internal sealed class FormBuilderRateLimiter
{
    /// <summary>How many times its limit a client may try before a refusal stops rendering a page.</summary>
    public const int RenderedTimesLimit = 10;

    /// <summary>How many overflow windows each partition has, which new clients share by a hash of their key past the bound.</summary>
    public const int OverflowBuckets = 64;

    /// <summary>
    /// How many times an address's share of uploads the /48 network of IPv6 addresses holds together: a site is usually
    /// given a whole /48, 65,536 /64 networks, so the /64 alone would let one site take every share (16.1's review).
    /// </summary>
    public const int NetworkShareFactor = 4;

    private const long Mebibyte = 1_048_576;

    private static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(1);

    // As long as an upload may wait for an entry (ADR-0148).
    private static readonly TimeSpan UploadShare = OrphanFileCleanup.KeptFor;
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);
    private static readonly ImmutableArray<string> OverflowClients = [.. Enumerable.Range(0, OverflowBuckets).Select(bucket => string.Create(CultureInfo.InvariantCulture, $"{WindowKey.Overflow}{bucket}"))];

    private readonly ConcurrentDictionary<WindowKey, RateWindow> _windows = new();
    private readonly Dictionary<string, FormBuilderRatePolicyOptions> _policies;
    private readonly int _maxTracked;
    private readonly int _serverKeyFactor;
    private readonly int _uploadMebibytes;
    private readonly TimeProvider _time;
    private int _tracked;
    private long _nextSweep;

    /// <summary>The limiter with the host's limits.</summary>
    /// <param name="options">The host configuration.</param>
    /// <param name="time">The site's clock.</param>
    public FormBuilderRateLimiter(IOptions<FormBuilderOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        var limits = options.Value.Limits;
        _policies = new Dictionary<string, FormBuilderRatePolicyOptions>(StringComparer.Ordinal)
        {
            [FormBuilderRateLimitPolicies.Read] = limits.Read,
            [FormBuilderRateLimitPolicies.Challenge] = limits.Challenge,
            [FormBuilderRateLimitPolicies.Submit] = limits.Submit,
            [FormBuilderRateLimitPolicies.Upload] = limits.Upload,
            [FormBuilderRateLimitPolicies.Events] = limits.Events,
            [FormBuilderRateLimitPolicies.Verify] = limits.Verify,
        };
        _maxTracked = limits.MaxTrackedClients;
        _serverKeyFactor = limits.ServerKeyFactor;
        _uploadMebibytes = (int)Math.Min(options.Value.Files.MaxWaitingBytesPerAddress / Mebibyte, int.MaxValue);
        _time = time;
        _nextSweep = time.GetUtcNow().Add(SweepEvery).UtcTicks;
    }

    /// <summary>How many windows it keeps for clients and forms now.</summary>
    public int TrackedWindows => Volatile.Read(ref _tracked);

    /// <summary>Counts one request under a policy, or refuses it.</summary>
    /// <param name="http">The request, whose server key or address is the client.</param>
    /// <param name="policy">One of <see cref="FormBuilderRateLimitPolicies"/>.</param>
    /// <param name="form">The form it asks about; null for one that names none.</param>
    public RateLimitDecision Take(HttpContext http, string policy, Guid? form)
    {
        var count = Start(http, policy);

        // The empty key names no form: it is the overflow window's mark, and a route may send it.
        return count.Refusal ?? Finish(count, form == Guid.Empty ? null : form);
    }

    /// <summary>Counts one request under its address's own limits, which ask nothing about a form.</summary>
    /// <param name="http">The request, whose server key or address is the client.</param>
    /// <param name="policy">One of <see cref="FormBuilderRateLimitPolicies"/>.</param>
    public RateLimitCount Start(HttpContext http, string policy)
    {
        ArgumentNullException.ThrowIfNull(http);
        SweepWhenDue();
        var limits = _policies[policy];
        var factor = ServerKeyRequest.NameOf(http) is null ? 1 : _serverKeyFactor;
        var count = new RateLimitCount(limits, policy, FormBuilderRateLimitPolicies.ClientOf(http), factor);
        count.Refusal = TakeAll(count, AddressPartitions(limits, factor), form: null);
        return count;
    }

    /// <summary>Counts a request that was started under the form's limits and the site's, or refuses it and gives back what it counted.</summary>
    /// <param name="count">What it has counted.</param>
    /// <param name="form">The form it asks about; null for one that names none.</param>
    public RateLimitDecision Finish(RateLimitCount count, Guid? form)
    {
        ArgumentNullException.ThrowIfNull(count);
        return TakeAll(count, FormPartitions(count.Limits, form is not null, count.Factor), form) ?? RateLimitDecision.Go;
    }

    /// <summary>
    /// Counts a request that was started and names a form this server does not keep, before the database is asked about it: a
    /// site-wide window of its own, as large as the site's, so made-up keys cannot read without limit or spend the site's.
    /// A refusal gives back what it counted.
    /// </summary>
    /// <param name="count">What it has counted.</param>
    public RateLimitDecision? TakeLookup(RateLimitCount count)
    {
        ArgumentNullException.ThrowIfNull(count);
        return count.Limits.SitePerSecond > 0
            ? TakeAll(count, [new Partition(PartitionKind.Lookup, count.Limits.SitePerSecond, Second, UsesClient: false, UsesForm: false)], form: null)
            : null;
    }

    /// <summary>
    /// Whether a client may upload another file, by its share of what may wait for an entry (<c>Files:MaxWaitingBytesPerAddress</c>,
    /// ADR-0148). It counts nothing, as the bytes are only known once they are stored; a refusal renders, as for one over its own limit.
    /// </summary>
    /// <param name="http">The request, whose server key or address is the client.</param>
    public RateLimitDecision CheckUploadShare(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (_uploadMebibytes == 0)
        {
            return RateLimitDecision.Go;
        }

        foreach (var (key, _) in UploadShares(http))
        {
            if (_windows.TryGetValue(key, out var window) && window.IsFull(out var retryAfter))
            {
                return new RateLimitDecision(false, retryAfter, true);
            }
        }

        return RateLimitDecision.Go;
    }

    /// <summary>Counts what a client has just stored towards its share, in whole mebibytes.</summary>
    /// <param name="http">The request, whose server key or address is the client.</param>
    /// <param name="bytes">What it stored.</param>
    public void CountUploadShare(HttpContext http, long bytes)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (_uploadMebibytes == 0)
        {
            return;
        }

        var count = (int)Math.Min((bytes + Mebibyte - 1) / Mebibyte, int.MaxValue);
        foreach (var (key, limit) in UploadShares(http))
        {
            var part = new Partition(PartitionKind.ClientUploads, limit, UploadShare, UsesClient: true, UsesForm: false);
            var (window, _) = WindowFor(key, part);
            while (!window.TryCount(Math.Max(1, count)))
            {
                // The sweep ended it just after this request found it: it counts on a new one.
                Forget(key, window);
                (window, _) = WindowFor(key, part);
            }
        }
    }

    // The shares a client's uploads count towards, with their limits in mebibytes: its own, and for an IPv6 address
    // without a server key its /48 network's too.
    private IEnumerable<(WindowKey Key, int Limit)> UploadShares(HttpContext http)
    {
        var signed = ServerKeyRequest.NameOf(http) is not null;
        var factor = signed ? _serverKeyFactor : 1;
        yield return (new WindowKey(FormBuilderRateLimitPolicies.Upload, PartitionKind.ClientUploads, FormBuilderRateLimitPolicies.ClientOf(http), null), (int)Math.Min(_uploadMebibytes * (long)factor, int.MaxValue));
        if (!signed && RateLimitClients.NetworkOf(http.Connection.RemoteIpAddress) is { } network)
        {
            yield return (new WindowKey(FormBuilderRateLimitPolicies.Upload, PartitionKind.ClientUploads, "network:" + network, null), (int)Math.Min(_uploadMebibytes * (long)NetworkShareFactor, int.MaxValue));
        }
    }

    // Counts a request in each of these partitions; the first that refuses it answers, and those before it give it back.
    private RateLimitDecision? TakeAll(RateLimitCount count, IEnumerable<Partition> partitions, Guid? form)
    {
        foreach (var part in partitions)
        {
            var key = new WindowKey(count.Policy, part.Kind, part.UsesClient ? count.Client : null, part.UsesForm ? form : null);
            var (window, overflow) = WindowFor(key, part);
            var fits = window.TryTake(out var retryAfter, out var retired);
            while (retired)
            {
                // The sweep ended it just after this request found it: the request counts on a new one.
                Forget(key, window);
                (window, overflow) = WindowFor(key, part);
                fits = window.TryTake(out retryAfter, out retired);
            }

            if (fits)
            {
                count.Taken.Add(window);
                continue;
            }

            count.Taken.ForEach(earlier => earlier.Release());

            var renders = (part.UsesClient || part.UsesForm) && !overflow && window.Tried < window.Limit * RenderedTimesLimit;
            return new RateLimitDecision(false, retryAfter, renders);
        }

        return null;
    }

    // The partitions of an address alone: those whose limit is 0 do not exist.
    private static IEnumerable<Partition> AddressPartitions(FormBuilderRatePolicyOptions limits, int factor)
    {
        if (limits.PerAddressPerMinute > 0)
        {
            yield return new Partition(PartitionKind.ClientMinute, limits.PerAddressPerMinute * factor, Minute, UsesClient: true, UsesForm: false);
        }

        if (limits.PerAddressPerHour > 0)
        {
            yield return new Partition(PartitionKind.ClientHour, limits.PerAddressPerHour * factor, Hour, UsesClient: true, UsesForm: false);
        }
    }

    // The other partitions of a policy: those whose limit is 0 do not exist, and a form's need the form.
    private static IEnumerable<Partition> FormPartitions(FormBuilderRatePolicyOptions limits, bool hasForm, int factor)
    {
        if (hasForm && limits.PerAddressAndFormPerMinute > 0)
        {
            yield return new Partition(PartitionKind.ClientForm, limits.PerAddressAndFormPerMinute * factor, Minute, UsesClient: true, UsesForm: true);
        }

        if (hasForm && limits.PerFormPerMinute > 0)
        {
            yield return new Partition(PartitionKind.Form, limits.PerFormPerMinute, Minute, UsesClient: false, UsesForm: true);
        }

        if (limits.SitePerSecond > 0)
        {
            yield return new Partition(PartitionKind.Site, limits.SitePerSecond, Second, UsesClient: false, UsesForm: false);
        }
    }

    // The window of a key. The site-wide windows are few and always kept; past the bound a new client's or form's window
    // is the partition's overflow window, and a client that has one keeps it.
    private (RateWindow Window, bool Overflow) WindowFor(WindowKey key, Partition part)
    {
        if (_windows.TryGetValue(key, out var existing))
        {
            return (existing, key.IsOverflow);
        }

        if (!part.UsesClient && !part.UsesForm)
        {
            return (_windows.GetOrAdd(key, _ => new RateWindow(part.Limit, part.Length, _time)), false);
        }

        if (Volatile.Read(ref _tracked) >= _maxTracked)
        {
            var overflow = key with { Client = key.Client is null ? null : OverflowOf(key.Client), Form = key.Form is null ? null : Guid.Empty };
            return (_windows.GetOrAdd(overflow, _ => new RateWindow(part.Limit, part.Length, _time)), true);
        }

        var window = new RateWindow(part.Limit, part.Length, _time);
        if (_windows.TryAdd(key, window))
        {
            Interlocked.Increment(ref _tracked);
            return (window, false);
        }

        return (_windows[key], false);
    }

    /// <summary>The overflow window's client a client shares past the bound: one of <see cref="OverflowBuckets"/>, by a hash of its key.</summary>
    /// <param name="client">The client's key.</param>
    internal static string OverflowOf(string client) => OverflowClients[(int)((uint)client.GetHashCode(StringComparison.Ordinal) % OverflowBuckets)];

    private void SweepWhenDue()
    {
        var now = _time.GetUtcNow().UtcTicks;
        var due = Interlocked.Read(ref _nextSweep);
        if (now < due || Interlocked.CompareExchange(ref _nextSweep, now + SweepEvery.Ticks, due) != due)
        {
            return;
        }

        // Near the bound, a client's window that counted one request and has had none for a minute goes too. A share of
        // uploads stays: it counts mebibytes, so forgetting it would hand back a whole file's worth.
        var pressed = Volatile.Read(ref _tracked) >= _maxTracked - (_maxTracked / 10);
        foreach (var (key, window) in _windows)
        {
            if (window.TryRetire() || (pressed && key.Counts && key.Kind != PartitionKind.ClientUploads && window.TryRetireLight(SweepEvery)))
            {
                Forget(key, window);
            }
        }
    }

    private void Forget(WindowKey key, RateWindow window)
    {
        if (_windows.TryRemove(new KeyValuePair<WindowKey, RateWindow>(key, window)) && key.Counts)
        {
            Interlocked.Decrement(ref _tracked);
        }
    }

    private enum PartitionKind : byte
    {
        ClientMinute,
        ClientHour,
        ClientForm,
        Form,
        Site,
        Lookup,
        ClientUploads,
    }

    private readonly record struct Partition(PartitionKind Kind, int Limit, TimeSpan Length, bool UsesClient, bool UsesForm);

    private readonly record struct WindowKey(string Policy, PartitionKind Kind, string? Client, Guid? Form)
    {
        // A client's key starts with "key:" or "address:", so no client is an overflow window.
        public const string Overflow = "overflow:";

        public bool IsOverflow => (Client?.StartsWith(Overflow, StringComparison.Ordinal) ?? false) || Form == Guid.Empty;

        // Whether the key counts toward the bound: the site-wide and overflow windows are few, whatever a flood does.
        public bool Counts => Kind is not (PartitionKind.Site or PartitionKind.Lookup) && !IsOverflow;
    }
}

/// <summary>Who a request counts for in the per-client partitions, from the connection only (ADR-0012).</summary>
internal static class RateLimitClients
{
    /// <summary>The key when the connection has no address.</summary>
    public const string Unknown = "unknown";

    /// <summary>The key of an address: IPv4 as it is, an IPv4-mapped one as IPv4, IPv6 as its /64 network.</summary>
    /// <param name="address">The connection's address.</param>
    public static string KeyOf(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return string.Create(CultureInfo.InvariantCulture, $"{new IPAddress(bytes)}/64");
    }

    /// <summary>The /48 network of an IPv6 address, which a site is usually given whole; null for IPv4, a mapped address or none.</summary>
    /// <param name="address">The connection's address.</param>
    public static string? NetworkOf(IPAddress? address)
    {
        if (address is null || address.IsIPv4MappedToIPv6 || address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 6, 10);
        return string.Create(CultureInfo.InvariantCulture, $"{new IPAddress(bytes)}/48");
    }
}
