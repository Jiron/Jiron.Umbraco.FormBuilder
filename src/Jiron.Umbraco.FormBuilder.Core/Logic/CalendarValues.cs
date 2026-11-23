using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>One step of a relative date: a signed amount and a unit (d, w, m or y).</summary>
internal readonly record struct RelativeStep(int Amount, char Unit);

/// <summary>
/// Calendar values on the wire (ADR-0005), the same in TypeScript: ISO 8601 local dates and times without a zone, years
/// 1 to 9999, validated as real calendar values. Dates are day numbers (<see cref="DateOnly.DayNumber"/>), times are
/// seconds since midnight.
/// </summary>
internal static class CalendarValues
{
    private const long SecondsPerDay = 86400;

    private static readonly int MinDay = DateOnly.MinValue.DayNumber;
    private static readonly int MaxDay = DateOnly.MaxValue.DayNumber;
    private static readonly int UnixEpochDay = new DateOnly(1970, 1, 1).DayNumber;

    /// <summary>The first instant "today" takes: before 1970, time zone data differs between platforms.</summary>
    private const long FirstInstant = 0;

    /// <summary>The last instant "today" takes: the end of 9998 in UTC, so the local date stays within year 9999.</summary>
    private static readonly long LastInstant = ((new DateOnly(9999, 1, 1).DayNumber - UnixEpochDay) * SecondsPerDay) - 1;

    /// <summary><c>YYYY-MM-DD</c>, a real calendar date in years 1 to 9999, as a day number; otherwise null.</summary>
    public static int? ParseDate(string text)
    {
        if (text.Length != 10 || text[4] != '-' || text[7] != '-')
        {
            return null;
        }

        if (Number(text, 0, 4) is not { } year || Number(text, 5, 2) is not { } month || Number(text, 8, 2) is not { } day
            || year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        return new DateOnly(year, month, day).DayNumber;
    }

    /// <summary><c>HH:mm</c> or <c>HH:mm:ss</c> on a 24-hour clock, as seconds since midnight; otherwise null.</summary>
    public static int? ParseTime(string text)
    {
        if ((text.Length != 5 && text.Length != 8) || text[2] != ':' || (text.Length == 8 && text[5] != ':'))
        {
            return null;
        }

        var s = text.Length == 8 ? Number(text, 6, 2) : 0;
        if (Number(text, 0, 2) is not { } hours || Number(text, 3, 2) is not { } minutes || s is not { } seconds
            || hours > 23 || minutes > 59 || seconds > 59)
        {
            return null;
        }

        return (hours * 3600) + (minutes * 60) + seconds;
    }

    /// <summary><c>YYYY-MM-DDTHH:mm(:ss)</c>, local wall-clock time without a zone, as seconds; otherwise null.</summary>
    public static long? ParseDateTime(string text)
    {
        if (text.Length < 11 || text[10] != 'T')
        {
            return null;
        }

        return ParseDate(text[..10]) is { } day && ParseTime(text[11..]) is { } time ? (day * SecondsPerDay) + time : null;
    }

    /// <summary>A day number as <c>YYYY-MM-DD</c>.</summary>
    public static string FormatDate(int day) =>
        DateOnly.FromDayNumber(day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The instant of evaluation on the wire: <c>YYYY-MM-DDTHH:mm:ss</c>, an optional fraction of 1 to 9 digits, and
    /// <c>Z</c> or an offset <c>+HH:mm</c> or <c>-HH:mm</c>, between 1970 and the end of 9998 in UTC. Seconds since
    /// 1970 (the fraction never changes a date); otherwise null.
    /// </summary>
    public static long? ParseInstant(string text)
    {
        if (text.Length < 20 || text[10] != 'T' || ParseDate(text[..10]) is not { } day
            || ParseTime(text[11..19]) is not { } time)
        {
            return null;
        }

        var i = 19;
        if (text[i] == '.')
        {
            var digits = 0;
            while (i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                digits++;
            }

            if (digits is < 1 or > 9)
            {
                return null;
            }

            i++;
        }

        var offset = Offset(text, i);
        if (offset is null)
        {
            return null;
        }

        var instant = ((day - UnixEpochDay) * SecondsPerDay) + time - offset.Value;
        return instant >= FirstInstant && instant <= LastInstant ? instant : null;
    }

    /// <summary>
    /// Whether the name is an IANA time zone this runtime knows, as <see cref="Today"/> takes it: Windows names and
    /// offsets are refused, because the browser does not take the one and the server not the other. It takes the zone's
    /// own names, which every reader passes on (a resolved zone's id); a name a site stored goes through the overload
    /// with the server's links.
    /// </summary>
    public static TimeZoneInfo? FindTimeZone(string timeZone) => IsZoneName(timeZone) ? FindIana(timeZone) : null;

    /// <summary>
    /// The same for a name a site stored, which may be an older name that browsers list: one the server's own time zone
    /// database links to its zone, such as <c>Asia/Calcutta</c>, finds that zone, whose id is then the zone's own name
    /// (ROADMAP 26.1). The links are looked up first: a lookup in memory, which spares a name .NET does not find the
    /// exception of each call, and costs a zone's own name nothing it did not cost before.
    /// </summary>
    /// <param name="timeZone">The name as stored.</param>
    /// <param name="links">The server's links (<see cref="SystemTimeZoneLinks"/>).</param>
    public static TimeZoneInfo? FindTimeZone(string timeZone, SystemTimeZoneLinks links)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (!IsZoneName(timeZone))
        {
            return null;
        }

        return links.Target(timeZone) is { } zone ? FindIana(zone) : FindIana(timeZone);
    }

    /// <summary>
    /// The zone of a name a site may store, when the server knows the name in exactly this spelling: the zone's own name,
    /// or an older name its system's database links to it or ICU converts; null otherwise. Another letter case is not:
    /// Windows finds it only once the exact spelling has been looked up, so the answer would depend on what the server
    /// did before (ADR-0043).
    /// </summary>
    /// <param name="timeZone">The name as given.</param>
    /// <param name="links">The server's links.</param>
    public static TimeZoneInfo? FindExactTimeZone(string timeZone, SystemTimeZoneLinks links)
    {
        ArgumentNullException.ThrowIfNull(links);
        if (!IsZoneName(timeZone))
        {
            return null;
        }

        if (links.Target(timeZone) is { } linked)
        {
            return FindIana(linked);
        }

        return FindIana(timeZone) is { } zone
            && (string.Equals(zone.Id, timeZone, StringComparison.Ordinal) || TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone, out _))
            ? zone
            : null;
    }

    private static bool IsZoneName(string timeZone) => timeZone.Length > 0 && char.IsAsciiLetter(timeZone[0]);

    private static TimeZoneInfo? FindIana(string timeZone)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone);
            return zone.HasIanaId ? zone : null;
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The site's date: the local date of the instant <paramref name="now"/> in an IANA time zone.</summary>
    public static int Today(string now, string timeZone)
    {
        var instant = ParseInstant(now) ?? throw new LogicException(LogicErrors.InvalidContext);
        var zone = FindTimeZone(timeZone) ?? throw new LogicException(LogicErrors.UnknownTimeZone);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(instant), zone);
        return DateOnly.FromDateTime(local.DateTime).DayNumber;
    }

    /// <summary>Reads <c>([+-][0-9]{1,4}[dwmy])*</c>, such as "+1m+1d"; the empty text is today. Null when malformed.</summary>
    public static IReadOnlyList<RelativeStep>? ParseRelative(string text)
    {
        var steps = new List<RelativeStep>();
        var i = 0;
        while (i < text.Length)
        {
            var sign = text[i] switch
            {
                '+' => 1,
                '-' => -1,
                _ => 0,
            };
            if (sign == 0)
            {
                return null;
            }

            var end = i + 1;
            while (end < text.Length && end - i <= 4 && char.IsAsciiDigit(text[end]))
            {
                end++;
            }

            var count = end - i - 1;
            if (count is < 1 or > 4 || end >= text.Length || text[end] is not ('d' or 'w' or 'm' or 'y'))
            {
                return null;
            }

            var amount = int.Parse(text.AsSpan((i + 1)..end), NumberStyles.None, CultureInfo.InvariantCulture);
            steps.Add(new RelativeStep(sign * amount, text[end]));
            i = end + 1;
        }

        return steps;
    }

    /// <summary>Applies the steps in written order; months and years clamp to the end of the month. Null outside 1 to 9999.</summary>
    public static int? ApplyRelative(int day, IReadOnlyList<RelativeStep> steps)
    {
        long current = day;
        foreach (var step in steps)
        {
            current = step.Unit switch
            {
                'd' => current + step.Amount,
                'w' => current + (step.Amount * 7L),
                'm' => AddMonths((int)current, step.Amount),
                _ => AddMonths((int)current, step.Amount * 12),
            };
            if (current < MinDay || current > MaxDay)
            {
                return null;
            }
        }

        return (int)current;
    }

    /// <summary>Moves by whole months, clamping the day to the end of the month; <see cref="long.MinValue"/> outside 1 to 9999.</summary>
    private static long AddMonths(int day, int months)
    {
        var date = DateOnly.FromDayNumber(day);
        var total = (date.Year * 12L) + (date.Month - 1) + months;
        var year = total >= 0 ? total / 12 : ((total + 1) / 12) - 1;
        if (year is < 1 or > 9999)
        {
            return long.MinValue;
        }

        var month = (int)(total - (year * 12)) + 1;
        return new DateOnly((int)year, month, Math.Min(date.Day, DateTime.DaysInMonth((int)year, month))).DayNumber;
    }

    /// <summary>Seconds east of UTC for <c>Z</c>, <c>+HH:mm</c> or <c>-HH:mm</c> ending the text at <paramref name="i"/>; otherwise null.</summary>
    private static int? Offset(string text, int i)
    {
        if (i == text.Length - 1 && text[i] == 'Z')
        {
            return 0;
        }

        if (i != text.Length - 6 || text[i] is not ('+' or '-') || text[i + 3] != ':'
            || Number(text, i + 1, 2) is not { } hours || Number(text, i + 4, 2) is not { } minutes
            || hours > 23 || minutes > 59)
        {
            return null;
        }

        var seconds = (hours * 3600) + (minutes * 60);
        return text[i] == '-' ? -seconds : seconds;
    }

    private static int? Number(string text, int start, int length)
    {
        var value = 0;
        for (var i = start; i < start + length; i++)
        {
            if (!char.IsAsciiDigit(text[i]))
            {
                return null;
            }

            value = (value * 10) + (text[i] - '0');
        }

        return value;
    }
}
