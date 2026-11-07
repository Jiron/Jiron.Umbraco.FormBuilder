using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>How often a user gets the summary email of entries (IA 6.4, ADR-0209).</summary>
internal enum EntryDigestFrequency
{
    /// <summary>Never.</summary>
    Off,

    /// <summary>Each morning, for the day before.</summary>
    Daily,

    /// <summary>On Mondays, for the week before.</summary>
    Weekly,
}

/// <summary>
/// The days a summary covers, in the site's time zone, and the moments they start and end.
/// </summary>
/// <param name="First">Its first day.</param>
/// <param name="Last">Its last day.</param>
/// <param name="FromUtc">The start of its first day.</param>
/// <param name="ToUtc">The start of the day after its last.</param>
internal sealed record EntryDigestPeriod(DateOnly First, DateOnly Last, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

/// <summary>A form a summary names, with how many entries arrived in it.</summary>
/// <param name="Key">The form.</param>
/// <param name="Name">Its name.</param>
/// <param name="Entries">The entries that arrived.</param>
internal sealed record EntryDigestForm(Guid Key, string Name, long Entries);

/// <summary>What a summary says: the forms with entries in its period, and how many entries wait for approval now.</summary>
/// <param name="Period">The days it covers.</param>
/// <param name="Forms">The forms with entries, the most first.</param>
/// <param name="Waiting">The entries of the user's forms that wait for approval.</param>
internal sealed record EntryDigest(EntryDigestPeriod Period, IReadOnlyList<EntryDigestForm> Forms, long Waiting)
{
    /// <summary>How many entries arrived in all.</summary>
    public long Entries => Forms.Sum(form => form.Entries);
}

/// <summary>What sending a summary did.</summary>
internal enum EntryDigestOutcome
{
    /// <summary>It is in the outbox.</summary>
    Queued,

    /// <summary>No entry arrived in the period, so nothing went out.</summary>
    Empty,

    /// <summary>The user has no address, or is disabled, so nothing went out.</summary>
    NoAddress,
}

/// <summary>
/// The summary email of entries (IA 6.4, ADR-0209): a user's choice of how often, and the mail itself, which counts the
/// entries that arrived in each form the user may view entries of, says how many wait for approval and links each form's
/// Entries view and the Inbox; it never holds an answer. It goes out once the site's 07:00 has passed in its time zone,
/// for the day or the week before, in the user's backoffice language, to the user's own address; a period without
/// entries sends nothing. The hourly job (<see cref="FormBuilderDigestJob"/>) records each period it sent in a setting of
/// its own, so a user's change never races it and a period goes out once.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="settings">The users' choices.</param>
/// <param name="users">The backoffice users.</param>
/// <param name="access">Which forms a user may view entries of.</param>
/// <param name="forms">The forms.</param>
/// <param name="entries">The entries' counts.</param>
/// <param name="outbox">The outbox.</param>
/// <param name="timeZones">The site's time zone.</param>
/// <param name="baseUrls">The site's address, for the links.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Where a summary that could not go is told.</param>
internal sealed partial class EntryDigests(
    FormBuilderDatabase database,
    IUserSettingRepository settings,
    IUserService users,
    IFormBuilderAccessService access,
    IFormRepository forms,
    IEntryRepository entries,
    EmailOutbox outbox,
    SiteTimeZones timeZones,
    SiteBaseUrl baseUrls,
    TimeProvider time,
    ILogger<EntryDigests> logger)
{
    /// <summary>The user setting that holds the choice.</summary>
    public const string Setting = "entries.digest";

    /// <summary>The user setting in which the job records the last day it sent a summary for.</summary>
    public const string SentSetting = "entries.digest.sent";

    /// <summary>The time of day, in the site's time zone, from which a period's summary goes out.</summary>
    public static readonly TimeOnly SendsAt = new(7, 0);

    private static readonly JsonSerializerOptions Json = Definitions.Serialization.FrozenOptions.Of(new(JsonSerializerDefaults.Web));

    private static readonly FrozenDictionary<string, Words> AllWords = new Dictionary<string, Words>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new(
            Subject: "Summary of entries, {0}",
            Range: "{0} to {1}",
            ArrivedOne: "1 entry arrived on {0}.",
            ArrivedMany: "{0} entries arrived on {1}.",
            ArrivedOneWeek: "1 entry arrived from {0} to {1}.",
            ArrivedManyWeek: "{0} entries arrived from {1} to {2}.",
            None: "No entries arrived on {0}.",
            NoneWeek: "No entries arrived from {0} to {1}.",
            WaitingOne: "1 entry is waiting for approval.",
            WaitingMany: "{0} entries are waiting for approval.",
            Open: "Open the Inbox",
            Daily: "You get this summary each morning. Change it with Email me a summary\u2026 in the Inbox.",
            Weekly: "You get this summary on Mondays. Change it with Email me a summary\u2026 in the Inbox.",
            Test: "Test: {0}",
            TestNote: "This is a test of your summary email, for the period before."),
        ["de"] = new(
            Subject: "Zusammenfassung der Eintr\u00e4ge, {0}",
            Range: "{0} bis {1}",
            ArrivedOne: "Am {0} ist 1 Eintrag eingegangen.",
            ArrivedMany: "Am {1} sind {0} Eintr\u00e4ge eingegangen.",
            ArrivedOneWeek: "Vom {0} bis {1} ist 1 Eintrag eingegangen.",
            ArrivedManyWeek: "Vom {1} bis {2} sind {0} Eintr\u00e4ge eingegangen.",
            None: "Am {0} sind keine Eintr\u00e4ge eingegangen.",
            NoneWeek: "Vom {0} bis {1} sind keine Eintr\u00e4ge eingegangen.",
            WaitingOne: "1 Eintrag wartet auf Genehmigung.",
            WaitingMany: "{0} Eintr\u00e4ge warten auf Genehmigung.",
            Open: "Posteingang \u00f6ffnen",
            Daily: "Sie erhalten diese Zusammenfassung jeden Morgen. \u00c4ndern Sie das unter \u201eZusammenfassung per E-Mail\u2026\u201c im Posteingang.",
            Weekly: "Sie erhalten diese Zusammenfassung montags. \u00c4ndern Sie das unter \u201eZusammenfassung per E-Mail\u2026\u201c im Posteingang.",
            Test: "Test: {0}",
            TestNote: "Dies ist ein Test Ihrer Zusammenfassung, f\u00fcr den Zeitraum davor."),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>How often the user gets the summary.</summary>
    /// <param name="userKey">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<EntryDigestFrequency> GetAsync(Guid userKey, CancellationToken cancellationToken = default) =>
        Frequency(await settings.GetAsync(userKey, Setting, cancellationToken));

    /// <summary>
    /// Keeps how often the user gets the summary. The first one covers the first period that ends after the choice, so
    /// turning it on, or changing how often, sends nothing for days that passed before; saving the same choice again
    /// changes nothing, so the summary that is due still goes.
    /// </summary>
    /// <param name="userKey">The user.</param>
    /// <param name="frequency">How often.</param>
    /// <param name="cancellationToken">Cancels the writes.</param>
    public async Task SaveAsync(Guid userKey, EntryDigestFrequency frequency, CancellationToken cancellationToken = default)
    {
        if (frequency == EntryDigestFrequency.Off)
        {
            await settings.DeleteAsync(userKey, Setting, cancellationToken);
            return;
        }

        if (await GetAsync(userKey, cancellationToken) == frequency)
        {
            return;
        }

        // The record never goes back: a period the job covered stays covered when weekly becomes daily.
        var due = await DueAsync(frequency, cancellationToken);
        var covered = LastSent(await settings.GetAsync(userKey, SentSetting, cancellationToken));
        var last = covered > due.Last ? covered : due.Last;
        await database.WriteAsync(
            async _ =>
            {
                await settings.SetAsync(userKey, Setting, JsonSerializer.Serialize(new Choice(Name(frequency)), Json), cancellationToken);
                await settings.SetAsync(userKey, SentSetting, JsonSerializer.Serialize(new Sent(last), Json), cancellationToken);
                return true;
            },
            cancellationToken);
    }

    /// <summary>The latest period of this frequency whose summary is due by now, in the site's time zone.</summary>
    /// <param name="frequency">Daily or weekly; off counts as daily.</param>
    /// <param name="cancellationToken">Cancels the read of the time zone.</param>
    public async Task<EntryDigestPeriod> DueAsync(EntryDigestFrequency frequency, CancellationToken cancellationToken = default)
    {
        var zone = await timeZones.GetAsync(null, cancellationToken);
        var local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone);
        var today = DateOnly.FromDateTime(local.DateTime);

        // The day of the last 07:00 that passed: today from 07:00 on, yesterday before.
        var sentOn = TimeOnly.FromDateTime(local.DateTime) >= SendsAt ? today : today.AddDays(-1);
        if (frequency != EntryDigestFrequency.Weekly)
        {
            var day = sentOn.AddDays(-1);
            return Period(day, day, zone);
        }

        // The Monday of that 07:00 or before it, and the week from the Monday before to the Sunday.
        var monday = sentOn.AddDays(-(((int)sentOn.DayOfWeek + 6) % 7));
        return Period(monday.AddDays(-7), monday.AddDays(-1), zone);
    }

    /// <summary>What a summary of the period says for the user: the forms they may view entries of, and their counts.</summary>
    /// <param name="user">The user.</param>
    /// <param name="period">The days it covers.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<EntryDigest> ComposeAsync(IUser user, EntryDigestPeriod period, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(period);
        var visibility = await access.GetFormVisibilityAsync(AccessUser.Of(user), FormBuilderPermissions.ViewEntries, cancellationToken);
        if (visibility.IsNothing)
        {
            return new EntryDigest(period, [], 0);
        }

        var visible = new List<StoredForm>();
        for (var skip = 0L; ; skip += PageRequest.MaxTake)
        {
            var page = await forms.ListAsync(new FormFilter { AllFolders = true, Visibility = visibility }, new PageRequest(skip, PageRequest.MaxTake), cancellationToken);
            visible.AddRange(page.Items);
            if (page.Items.Count < PageRequest.MaxTake)
            {
                break;
            }
        }

        var ids = visible.Select(form => form.Id).ToList();
        var counts = await entries.CountBetweenAsync(ids, period.FromUtc, period.ToUtc, cancellationToken);
        var named = visible
            .Where(form => counts.ContainsKey(form.Id))
            .Select(form => new EntryDigestForm(form.Key, form.Name, counts[form.Id]))
            .OrderByDescending(form => form.Entries)
            .ThenBy(form => form.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new EntryDigest(period, named, await entries.CountWaitingAsync(ids, cancellationToken));
    }

    /// <summary>The summary mail of the period for the user, as it would go out, queueing nothing: the preview.</summary>
    /// <param name="user">The user.</param>
    /// <param name="frequency">How often, which the mail's last line names.</param>
    /// <param name="test">Whether it is marked as a test.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<ComposedEmail> PreviewAsync(IUser user, EntryDigestFrequency frequency, bool test, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var period = await DueAsync(frequency, cancellationToken);
        var digest = await ComposeAsync(user, period, cancellationToken);
        return await outbox.ComposeAsync(await MailAsync(user, digest, frequency, test, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Sends the summary of the period that is due to the user now: what Send test email sends, marked as a test, even for
    /// a period without entries.
    /// </summary>
    /// <param name="user">The user.</param>
    /// <param name="frequency">How often, which picks the period; off counts as daily.</param>
    /// <param name="cancellationToken">Cancels the reads and the write.</param>
    public async Task<EntryDigestOutcome> SendTestAsync(IUser user, EntryDigestFrequency frequency, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!Reachable(user))
        {
            return EntryDigestOutcome.NoAddress;
        }

        var digest = await ComposeAsync(user, await DueAsync(frequency, cancellationToken), cancellationToken);
        var prepared = await outbox.PrepareAsync(await MailAsync(user, digest, frequency, test: true, cancellationToken), cancellationToken);
        if (prepared is null)
        {
            return EntryDigestOutcome.NoAddress;
        }

        await outbox.QueueAsync(prepared, cancellationToken);
        return EntryDigestOutcome.Queued;
    }

    /// <summary>
    /// The job's run: each user who asked for a summary gets the one of the period due now, unless it went out already;
    /// a period without entries sends nothing but counts as sent. It stops at <paramref name="deadline"/>, and the next
    /// run goes on.
    /// </summary>
    /// <param name="deadline">When to stop.</param>
    /// <param name="cancellationToken">Cancels the reads and writes.</param>
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken = default)
    {
        var due = new Dictionary<EntryDigestFrequency, EntryDigestPeriod>();
        foreach (var chosen in await settings.ListAsync(Setting, cancellationToken))
        {
            if (time.GetUtcNow() >= deadline)
            {
                return;
            }

            var frequency = Frequency(chosen.Json);
            if (frequency == EntryDigestFrequency.Off)
            {
                continue;
            }

            if (!due.TryGetValue(frequency, out var period))
            {
                due[frequency] = period = await DueAsync(frequency, cancellationToken);
            }

            if (LastSent(await settings.GetAsync(chosen.UserKey, SentSetting, cancellationToken)) >= period.Last)
            {
                continue;
            }

            try
            {
                await SendAsync(chosen.UserKey, frequency, period, cancellationToken);
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
            {
                LogFailed(logger, chosen.UserKey, exception);
            }
        }
    }

    // One user's summary of the period, queued with the record that it went, in one unit of work.
    private async Task<EntryDigestOutcome> SendAsync(Guid userKey, EntryDigestFrequency frequency, EntryDigestPeriod period, CancellationToken cancellationToken)
    {
        var sent = JsonSerializer.Serialize(new Sent(period.Last), Json);
        var user = await users.GetAsync(userKey);
        var digest = user is not null && Reachable(user) ? await ComposeAsync(user, period, cancellationToken) : null;
        var prepared = user is not null && digest is { Forms.Count: > 0 }
            ? await outbox.PrepareAsync(await MailAsync(user, digest, frequency, test: false, cancellationToken), cancellationToken)
            : null;
        await database.WriteAsync(
            async _ =>
            {
                if (prepared is not null)
                {
                    await outbox.QueueAsync(prepared, cancellationToken);
                }

                await settings.SetAsync(userKey, SentSetting, sent, cancellationToken);
                return true;
            },
            cancellationToken);
        return prepared is not null ? EntryDigestOutcome.Queued : digest is null ? EntryDigestOutcome.NoAddress : EntryDigestOutcome.Empty;
    }

    // The mail: one line of what arrived, each form with its count and a link to its entries, what waits, and the Inbox.
    private async Task<OutgoingEmail> MailAsync(IUser user, EntryDigest digest, EntryDigestFrequency frequency, bool test, CancellationToken cancellationToken)
    {
        var culture = CultureOf(user);
        var words = WordsOf(culture);
        var format = CultureInfo.GetCultureInfo(culture);
        var period = digest.Period;
        var weekly = period.First != period.Last;
        string Day(DateOnly day) => day.ToString("D", format);
        var when = weekly ? string.Format(format, words.Range, Day(period.First), Day(period.Last)) : Day(period.First);
        var baseUrl = await baseUrls.GetAsync(null, cancellationToken);

        var html = new StringBuilder();
        if (test)
        {
            html.Append("<p><em>").Append(Encode(words.TestNote)).Append("</em></p>");
        }

        var arrived = digest.Entries switch
        {
            0 => weekly ? string.Format(format, words.NoneWeek, Day(period.First), Day(period.Last)) : string.Format(format, words.None, Day(period.First)),
            1 => weekly ? string.Format(format, words.ArrivedOneWeek, Day(period.First), Day(period.Last)) : string.Format(format, words.ArrivedOne, Day(period.First)),
            var many => weekly
                ? string.Format(format, words.ArrivedManyWeek, many.ToString("N0", format), Day(period.First), Day(period.Last))
                : string.Format(format, words.ArrivedMany, many.ToString("N0", format), Day(period.First)),
        };
        html.Append("<p>").Append(Encode(arrived)).Append("</p>");
        if (digest.Forms.Count > 0)
        {
            html.Append("<table>");
            foreach (var form in digest.Forms)
            {
                html.Append("<tr><td>");
                Link(html, baseUrl, BackofficeLinks.FormEntries(form.Key), form.Name);
                html.Append("</td><td align=\"right\">").Append(Encode(form.Entries.ToString("N0", format))).Append("</td></tr>");
            }

            html.Append("</table>");
        }

        if (digest.Waiting > 0)
        {
            var waiting = digest.Waiting == 1 ? words.WaitingOne : string.Format(format, words.WaitingMany, digest.Waiting.ToString("N0", format));
            html.Append("<p>").Append(Encode(waiting)).Append("</p>");
        }

        // Without the site's address (Settings, General; the health check names it) a link can't be made, so the forms are
        // named alone, as Forward leaves its link out.
        if (baseUrl is not null)
        {
            html.Append("<p><a href=\"").Append(Encode(baseUrl + BackofficeLinks.Inbox)).Append("\">").Append(Encode(words.Open)).Append("</a></p>");
        }

        html.Append("<p><small>").Append(Encode(frequency == EntryDigestFrequency.Weekly ? words.Weekly : words.Daily)).Append("</small></p>");

        var subject = string.Format(format, words.Subject, when);
        return new OutgoingEmail
        {
            Kind = EmailKind.Digest,
            To = [user.Email],
            Subject = test ? string.Format(format, words.Test, subject) : subject,
            Body = html.ToString(),
            Culture = culture,
        };
    }

    private static EntryDigestPeriod Period(DateOnly first, DateOnly last, TimeZoneInfo zone) =>
        new(first, last, new DateTimeOffset(EntryLists.StartOf(first, zone), TimeSpan.Zero), new DateTimeOffset(EntryLists.StartOf(last.AddDays(1), zone), TimeSpan.Zero));

    private static bool Reachable(IUser user) =>
        user.UserState is not (UserState.Disabled or UserState.LockedOut) && !string.IsNullOrWhiteSpace(user.Email);

    private static string CultureOf(IUser user) =>
        user.Language is { Length: > 0 } language && AllWords.ContainsKey(language.Split('-')[0]) ? language : "en-US";

    private static Words WordsOf(string culture) => AllWords.TryGetValue(culture.Split('-')[0], out var found) ? found : AllWords["en"];

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    // The text as a link to the backoffice path, or alone without the site's address.
    private static void Link(StringBuilder html, string? baseUrl, string path, string text)
    {
        if (baseUrl is null)
        {
            html.Append(Encode(text));
            return;
        }

        html.Append("<a href=\"").Append(Encode(baseUrl + path)).Append("\">").Append(Encode(text)).Append("</a>");
    }

    private static string Name(EntryDigestFrequency frequency) => frequency == EntryDigestFrequency.Weekly ? "weekly" : "daily";

    // The choice in its JSON; anything unreadable counts as off.
    private static EntryDigestFrequency Frequency(string? json)
    {
        try
        {
            return (json is null ? null : JsonSerializer.Deserialize<Choice>(json, Json))?.Frequency switch
            {
                "daily" => EntryDigestFrequency.Daily,
                "weekly" => EntryDigestFrequency.Weekly,
                _ => EntryDigestFrequency.Off,
            };
        }
        catch (JsonException)
        {
            return EntryDigestFrequency.Off;
        }
    }

    // The last day a summary went out for; the earliest day when none did.
    private static DateOnly LastSent(string? json)
    {
        try
        {
            return (json is null ? null : JsonSerializer.Deserialize<Sent>(json, Json))?.Last ?? DateOnly.MinValue;
        }
        catch (JsonException)
        {
            return DateOnly.MinValue;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not send the summary email of the user {UserKey}; the next run tries again")]
    private static partial void LogFailed(ILogger logger, Guid userKey, Exception exception);

    private sealed record Choice(string Frequency);

    private sealed record Sent(DateOnly Last);

    private sealed record Words(
        string Subject,
        string Range,
        string ArrivedOne,
        string ArrivedMany,
        string ArrivedOneWeek,
        string ArrivedManyWeek,
        string None,
        string NoneWeek,
        string WaitingOne,
        string WaitingMany,
        string Open,
        string Daily,
        string Weekly,
        string Test,
        string TestNote);
}
