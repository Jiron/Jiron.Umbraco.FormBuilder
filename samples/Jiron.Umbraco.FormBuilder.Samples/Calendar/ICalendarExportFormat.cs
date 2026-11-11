using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Exports;

namespace Jiron.Umbraco.FormBuilder.Samples.Calendar;

/// <summary>
/// The iCalendar sample's export format (docs/extending.md): a form of bookings, such as a room hire or an event
/// registration, exported as an <c>.ics</c> file that a calendar opens, an event per entry. The entry's first answer that
/// reads as a date in its language is the event's day, its first answer that reads as a time its start, and an answer
/// whose alias is <c>hours</c> its length (an hour without one); an entry without a time is an event of the whole day,
/// and one without a date an event of the whole day it was received. The event is named by the form, its description
/// lists the answers, and the first answer that is an email address is the person who booked.
/// </summary>
public sealed partial class ICalendarExportFormat : IFormBuilderExportFormat
{
    /// <summary>The format's name.</summary>
    public const string FormatName = "sampleIcalendar";

    /// <inheritdoc />
    public string Name => FormatName;

    /// <inheritdoc />
    public string DisplayName => "#jfbSamples_icalendar";

    /// <inheritdoc />
    public string FileExtension => ".ics";

    /// <inheritdoc />
    public string ContentType => "text/calendar";

    /// <inheritdoc />
    public bool TakesHeaders => false;

    /// <inheritdoc />
    public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(output);
        var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true) { NewLine = "\r\n" };
        await using (writer.ConfigureAwait(false))
        {
            await Line(writer, "BEGIN:VCALENDAR");
            await Line(writer, "VERSION:2.0");
            await Line(writer, "PRODID:-//Jiron Form Builder samples//iCalendar export//EN");
            await Line(writer, "CALSCALE:GREGORIAN");
            await Line(writer, $"X-WR-CALNAME:{Escape(content.FormName)}");
            await foreach (var entry in content.Entries.WithCancellation(cancellationToken))
            {
                foreach (var line in Event(content, entry))
                {
                    await Line(writer, line);
                }
            }

            await Line(writer, "END:VCALENDAR");
        }
    }

    /// <summary>An entry's event, line by line, unfolded.</summary>
    /// <param name="content">The export.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>Its lines.</returns>
    internal static IEnumerable<string> Event(FormBuilderExport content, FormBuilderExportEntry entry)
    {
        var columns = content.Columns;
        string Cell(int at) => at < entry.Cells.Count ? string.Join(", ", entry.Cells[at]) : string.Empty;
        var language = columns.Select((column, at) => (column, at)).FirstOrDefault(each => each.column.Kind == FormBuilderExportColumnKind.Language);
        var culture = CultureOf(language.column is null ? string.Empty : Cell(language.at), content.Culture);
        var answers = columns.Select((column, at) => (Column: column, Text: Cell(at)))
            .Where(each => each.Column.Kind == FormBuilderExportColumnKind.Answer && each.Text.Length > 0)
            .ToList();

        DateOnly? day = null;
        TimeOnly? start = null;
        double hours = 1;
        string? attendee = null;
        foreach (var (column, text) in answers)
        {
            if (string.Equals(column.Field, "hours", StringComparison.OrdinalIgnoreCase) && Number().Match(text) is { Success: true } number
                && double.TryParse(number.Value, NumberStyles.Number, culture, out var given) && given is > 0 and <= 24 * 14)
            {
                hours = given;
            }
            else if (day is null && DateOnly.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var date))
            {
                day = date;
            }
            else if (start is null && TimeOnly.TryParse(text, culture, DateTimeStyles.AllowWhiteSpaces, out var time))
            {
                start = time;
            }
            else if (attendee is null && Email().IsMatch(text))
            {
                attendee = text;
            }
        }

        yield return "BEGIN:VEVENT";
        yield return $"UID:{entry.Key:N}@jiron-form-builder";
        yield return $"DTSTAMP:{entry.ReceivedUtc.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}";
        var first = day ?? DateOnly.FromDateTime(entry.ReceivedUtc.UtcDateTime);
        if (day is not null && start is { } at)
        {
            // Floating times: the site's own, as the entry's answers give them.
            var begins = first.ToDateTime(at);
            yield return $"DTSTART:{begins:yyyyMMdd'T'HHmmss}";
            yield return $"DTEND:{begins.AddHours(hours):yyyyMMdd'T'HHmmss}";
        }
        else
        {
            yield return $"DTSTART;VALUE=DATE:{first:yyyyMMdd}";
            yield return $"DTEND;VALUE=DATE:{first.AddDays(1):yyyyMMdd}";
        }

        yield return $"SUMMARY:{Escape($"{content.FormName} #{entry.Number}")}";
        yield return $"DESCRIPTION:{Escape(string.Join("\n", answers.Select(each => $"{each.Column.Label}: {each.Text}")))}";
        if (attendee is not null)
        {
            yield return $"ATTENDEE;ROLE=REQ-PARTICIPANT:mailto:{attendee}";
        }

        yield return "END:VEVENT";
    }

    /// <summary>A content line folded at 75 octets, as RFC 5545 asks, each further line starting with a space.</summary>
    /// <param name="line">The line, unfolded.</param>
    /// <returns>Its folded lines.</returns>
    internal static IEnumerable<string> Fold(string line)
    {
        var start = 0;
        var limit = 75;
        while (Encoding.UTF8.GetByteCount(line.AsSpan(start)) > limit)
        {
            var end = start;
            var bytes = 0;
            while (end < line.Length)
            {
                var width = char.IsSurrogatePair(line, end) ? 2 : 1;
                var size = Encoding.UTF8.GetByteCount(line.AsSpan(end, width));
                if (bytes + size > limit)
                {
                    break;
                }

                bytes += size;
                end += width;
            }

            yield return (start == 0 ? string.Empty : " ") + line[start..end];
            start = end;
            limit = 74;
        }

        yield return (start == 0 ? string.Empty : " ") + line[start..];
    }

    /// <summary>A text as an iCalendar value: backslashes, semicolons, commas and line breaks escaped.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The value.</returns>
    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);

    private static async Task Line(StreamWriter writer, string line)
    {
        foreach (var folded in Fold(line))
        {
            await writer.WriteLineAsync(folded);
        }
    }

    private static CultureInfo CultureOf(string language, string fallback)
    {
        foreach (var name in new[] { language, fallback })
        {
            try
            {
                if (name.Length > 0)
                {
                    return CultureInfo.GetCultureInfo(name);
                }
            }
            catch (CultureNotFoundException)
            {
            }
        }

        return CultureInfo.InvariantCulture;
    }

    [GeneratedRegex(@"^\d+([.,]\d+)?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Number();

    [GeneratedRegex(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Email();
}
