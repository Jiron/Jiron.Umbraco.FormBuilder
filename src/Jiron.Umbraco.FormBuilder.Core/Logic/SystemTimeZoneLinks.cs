using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// The older names of time zones as the server's own time zone database links them (ROADMAP 26.1): on Linux, the link
/// lines (<c>L &lt;zone&gt; &lt;name&gt;</c>) of <c>tzdata.zi</c> in the zone folder .NET reads (<c>TZDIR</c>, by default
/// <c>/usr/share/zoneinfo</c>). A system without its package of older names (Ubuntu's <c>tzdata-legacy</c>, absent on
/// GitHub's runners) has no file for <c>America/Buenos_Aires</c> or <c>Asia/Calcutta</c>, so .NET does not find them,
/// although browsers list them under those names; the database's own source still links each to its zone. Windows
/// finds such names through ICU and has no such file.
/// </summary>
/// <remarks>
/// One per server, as a singleton: what it holds is that server's own system file. The links are parsed once and kept
/// with the file's stamp (its last write time and length); the stamp is looked at again at most once a minute, and the
/// file parsed again only when it changed, so an update of the system's time zone data is seen without a restart. A
/// missing file is remembered as missing, at no exception per call. No lock is held while the file is read: two callers
/// that find it changed at once may both parse it, and either result is the same.
/// </remarks>
internal sealed class SystemTimeZoneLinks
{
    /// <summary>The most links followed from one name; the database links a name straight to its zone.</summary>
    internal const int MostHops = 4;

    /// <summary>How often the file's stamp is looked at, in milliseconds.</summary>
    internal const long RecheckMilliseconds = 60_000;

    private readonly string? _file;
    private readonly Func<long> _milliseconds;
    private Snapshot? _snapshot;
    private int _reads;

    /// <summary>The links of this server's system, read from where .NET reads its zones; none on Windows.</summary>
    public SystemTimeZoneLinks()
        : this(OperatingSystem.IsWindows() ? null : Environment.GetEnvironmentVariable("TZDIR") is { Length: > 0 } folder ? folder : "/usr/share/zoneinfo", () => Environment.TickCount64)
    {
    }

    /// <summary>The links of the <c>tzdata.zi</c> in a zone folder, the stamp looked at again by the given clock.</summary>
    /// <param name="folder">The zone folder; null for none.</param>
    /// <param name="milliseconds">A clock in milliseconds that only goes forward.</param>
    internal SystemTimeZoneLinks(string? folder, Func<long> milliseconds)
    {
        _file = folder is null ? null : Path.Combine(folder, "tzdata.zi");
        _milliseconds = milliseconds;
    }

    /// <summary>How many times the file has been parsed.</summary>
    internal int Reads => Volatile.Read(ref _reads);

    /// <summary>
    /// The zone the system's database links a name to, spelled exactly as the database spells both; null when it links
    /// none, or the system has no such file.
    /// </summary>
    /// <param name="name">The name, such as <c>Asia/Calcutta</c>.</param>
    public string? Target(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Current() is { } links && links.TryGetValue(name, out var zone) ? zone : null;
    }

    /// <summary>
    /// The link lines of a <c>tzdata.zi</c>: each name and the zone it leads to in the end, ordinal; a loop leads nowhere.
    /// Only the names .NET would not find itself count, those without a file of their own (<paramref name="hasFile"/>):
    /// <c>UTC</c>, which the database links to <c>Etc/UTC</c> and every system has a file for, stays <c>UTC</c>, and on a
    /// system with its package of older names every older name has its own file and its own id, as before.
    /// </summary>
    /// <param name="lines">The file's lines.</param>
    /// <param name="hasFile">Whether the zone folder holds a file of this name; none when null.</param>
    internal static FrozenDictionary<string, string> Links(IEnumerable<string> lines, Func<string, bool>? hasFile = null)
    {
        var direct = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Length < 5 || line[0] != 'L' || line[1] != ' ')
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 3)
            {
                direct.TryAdd(parts[2], parts[1]);
            }
        }

        var followed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in direct.Keys)
        {
            var current = name;
            for (var hop = 0; hop < MostHops && direct.TryGetValue(current, out var target); hop++)
            {
                current = target;
            }

            // A name still linked further after the last hop belongs to a loop, which leads to no zone.
            if (!string.Equals(current, name, StringComparison.Ordinal) && !direct.ContainsKey(current) && hasFile?.Invoke(name) != true)
            {
                followed[name] = current;
            }
        }

        return followed.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // The links as the file now holds them, parsed again only when its stamp changed.
    private FrozenDictionary<string, string>? Current()
    {
        if (_file is null)
        {
            return null;
        }

        var now = _milliseconds();
        var known = Volatile.Read(ref _snapshot);
        if (known is not null && now - known.Checked < RecheckMilliseconds)
        {
            return known.Links;
        }

        var stamp = StampOf(_file);
        if (known is not null && known.Stamp == stamp)
        {
            Volatile.Write(ref _snapshot, known with { Checked = now });
            return known.Links;
        }

        FrozenDictionary<string, string>? links = null;
        if (stamp.Exists)
        {
            try
            {
                var folder = Path.GetDirectoryName(_file)!;
                links = Links(File.ReadLines(_file), name => File.Exists(Path.Combine(folder, name)));
                Interlocked.Increment(ref _reads);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                links = null;
            }
        }

        Volatile.Write(ref _snapshot, new Snapshot(stamp, links, now));
        return links;
    }

    private static Stamp StampOf(string file)
    {
        var info = new FileInfo(file);
        return info.Exists ? new Stamp(true, info.LastWriteTimeUtc, info.Length) : new Stamp(false, default, 0);
    }

    private readonly record struct Stamp(bool Exists, DateTime Written, long Length);

    private sealed record Snapshot(Stamp Stamp, FrozenDictionary<string, string>? Links, long Checked);
}
