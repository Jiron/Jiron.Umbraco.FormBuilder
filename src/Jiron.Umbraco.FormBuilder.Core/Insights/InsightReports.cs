using System.Collections.Frozen;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;

namespace Jiron.Umbraco.FormBuilder.Core.Insights;

/// <summary>How a report groups its days: by day, by week from Monday, or by calendar month.</summary>
internal enum InsightGrouping
{
    /// <summary>Each day.</summary>
    Day,

    /// <summary>Each week, from Monday.</summary>
    Week,

    /// <summary>Each calendar month.</summary>
    Month,
}

/// <summary>UTC days from one to another, both included (ADR-0211).</summary>
/// <param name="From">The first day.</param>
/// <param name="To">The last day, on or after the first.</param>
internal sealed record InsightSpan(DateOnly From, DateOnly To)
{
    /// <summary>The first moment of the span.</summary>
    public DateTimeOffset Start => new(From.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    /// <summary>The first moment after the span.</summary>
    public DateTimeOffset End => new(To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

/// <summary>A report's span, how it groups its days, and the span it compares with.</summary>
/// <param name="Span">The days it covers.</param>
/// <param name="GroupBy">How it groups them.</param>
/// <param name="Compare">The span it compares with, or null for none.</param>
internal sealed record InsightRequest(InsightSpan Span, InsightGrouping GroupBy = InsightGrouping.Day, InsightSpan? Compare = null);

/// <summary>A form's main numbers over a span.</summary>
/// <param name="Views">How often the form was shown.</param>
/// <param name="Starts">How often a visitor started it.</param>
/// <param name="Entries">How many entries it received (<c>submissions</c>).</param>
/// <param name="Spam">How many submissions the protection put into Spam.</param>
internal sealed record InsightTotals(long Views, long Starts, long Entries, long Spam)
{
    /// <summary>Nothing counted.</summary>
    public static readonly InsightTotals None = new(0, 0, 0, 0);

    /// <summary>Entries per view; null without views.</summary>
    public double? Conversion => Views == 0 ? null : (double)Entries / Views;
}

/// <summary>One group of days of a report: when it starts, its entries, and those of its counterpart in the span compared with.</summary>
/// <param name="Start">The group's first day: the day, the Monday of the week, or the first of the month.</param>
/// <param name="Entries">Its entries.</param>
/// <param name="Before">Its counterpart's entries; null without a comparison, or where the span compared with has none.</param>
internal sealed record InsightPeriod(DateOnly Start, long Entries, long? Before);

/// <summary>How often visitors reached a page after the first.</summary>
/// <param name="Page">The page's key.</param>
/// <param name="Reached">How often.</param>
internal sealed record InsightPageReach(Guid Page, long Reached);

/// <summary>A field that stopped visitors: how often, and the kind of error that stopped them most.</summary>
/// <param name="Field">The field's key.</param>
/// <param name="Stops">How often it stopped one, in the browser and in posts without JavaScript.</param>
/// <param name="Kind">The kind of error that stopped them most.</param>
/// <param name="Label">The field's label in the default culture as the catalogue keeps it, or its alias; null for a field the catalogue does not know.</param>
internal sealed record InsightFieldStops(Guid Field, long Stops, string Kind, string? Label = null);

/// <summary>A website page entries came from.</summary>
/// <param name="Content">The content's key.</param>
/// <param name="Entries">How many entries came from it.</param>
internal sealed record InsightOrigin(Guid Content, long Entries);

/// <summary>
/// A form's insights over a span (ADR-0211): its numbers, with those of the span compared with; its entries by group of
/// days; how often visitors reached each page after the first; the bucket that holds the middle time to complete; the
/// fields that stop visitors most; the pages entries come from most; its entries by hour of the site's day; and how many
/// members sent entries. Whether its insights are on now, for the site and for the form, goes with it.
/// </summary>
/// <param name="Totals">The span's numbers.</param>
/// <param name="Before">Those of the span compared with; null without a comparison.</param>
/// <param name="Periods">The entries by group of days.</param>
/// <param name="Pages">The pages after the first of the version given, in its order, and how often each was reached.</param>
/// <param name="MedianCompletion">The bucket (<see cref="InsightMetrics.CompletionLimits"/> in seconds, or <c>more</c>) that holds the middle entry's time to complete; null without one.</param>
/// <param name="Fields">The fields that stop visitors most, most first, at most <see cref="InsightReports.MostFields"/>.</param>
/// <param name="Origins">The pages entries come from most, most first, at most <see cref="InsightReports.MostOrigins"/>.</param>
/// <param name="Hours">The entries by hour of the site's day, 24 of them.</param>
/// <param name="Members">How many members sent entries, and how many entries they sent.</param>
/// <param name="MembersBefore">How many members sent entries in the span compared with; null without one.</param>
/// <param name="SiteOn">Whether the site's insights are on.</param>
/// <param name="FormOn">Whether the form's own are.</param>
internal sealed record InsightReport(
    InsightTotals Totals,
    InsightTotals? Before,
    IReadOnlyList<InsightPeriod> Periods,
    IReadOnlyList<InsightPageReach> Pages,
    string? MedianCompletion,
    IReadOnlyList<InsightFieldStops> Fields,
    IReadOnlyList<InsightOrigin> Origins,
    IReadOnlyList<long> Hours,
    MemberEntryCount Members,
    long? MembersBefore,
    bool SiteOn,
    bool FormOn);

/// <summary>A form's main numbers in a span and in the span compared with, for the site's table of forms.</summary>
/// <param name="Form">The form's key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Totals">The span's numbers.</param>
/// <param name="Before">Those of the span compared with.</param>
internal sealed record InsightGlance(Guid Form, string Name, InsightTotals Totals, InsightTotals Before);

/// <summary>
/// The reports of insights (ADR-0211), read from <c>jfbDailyStat</c>, and the members from the entries at query time.
/// A span covers at most <see cref="DailyStatRepository.MaxListDays"/> days.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="stats">The daily statistics.</param>
/// <param name="entries">The entries, whose members are counted.</param>
/// <param name="settings">The site's privacy settings.</param>
internal sealed class InsightReports(IFormRepository forms, IDailyStatRepository stats, IEntryRepository entries, IFormBuilderSettingsService settings)
{
    /// <summary>The most fields a report lists.</summary>
    public const int MostFields = 10;

    /// <summary>The most website pages a report lists.</summary>
    public const int MostOrigins = 10;

    /// <summary>The most forms the site's table reads, by name.</summary>
    public const int MostForms = PageRequest.MaxTake;

    /// <summary>The most forms the site's table lists: those with the most entries, then views.</summary>
    public const int MostGlances = 10;

    private static readonly FrozenSet<string> MainMetrics = FrozenSet.Create(StringComparer.Ordinal, InsightMetrics.Views, InsightMetrics.Starts, SubmissionEntries.SubmissionsMetric, SubmissionEntries.SpamMetric);

    /// <summary>A form's report; null when the form is gone.</summary>
    /// <param name="formKey">The form.</param>
    /// <param name="definition">The version whose pages the report follows: the published one, or the draft of a form never published; null for none.</param>
    /// <param name="request">The span, its grouping and the span compared with.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<InsightReport?> ReportAsync(Guid formKey, FormDefinition? definition, InsightRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return null;
        }

        var now = await stats.ListAsync(form.Id, request.Span.From, request.Span.To, cancellationToken);
        var before = request.Compare is { } compare ? await stats.ListAsync(form.Id, compare.From, compare.To, cancellationToken) : null;
        var members = await entries.CountMembersAsync(form.Id, request.Span.Start, request.Span.End, cancellationToken);
        var membersBefore = request.Compare is { } earlier ? (await entries.CountMembersAsync(form.Id, earlier.Start, earlier.End, cancellationToken)).Members : (long?)null;
        var siteOn = (await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)).Value.Insights;
        var report = Build(request, now, before, definition, members, siteOn) with { MembersBefore = membersBefore };
        if (report.Fields.Count == 0)
        {
            return report;
        }

        // The fields by the catalogue, which knows those a later version took out too (ADR-0003).
        var catalogue = (await forms.ListFieldsAsync(form.Id, cancellationToken)).GroupBy(row => row.Values.FieldKey).ToDictionary(group => group.Key, group => group.First().Values);
        return report with
        {
            Fields = [.. report.Fields.Select(field => field with { Label = catalogue.TryGetValue(field.Field, out var row) ? (row.Label.Length > 0 ? row.Label : row.Alias) : null })],
        };
    }

    /// <summary>
    /// The site's table of forms: the forms the user may view entries of with the most entries, then views, in the span
    /// or the one compared with, each with both spans' numbers; forms with nothing in either are left out.
    /// </summary>
    /// <param name="visibility">The forms the user may view entries of.</param>
    /// <param name="span">The span.</param>
    /// <param name="compare">The span compared with.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyList<InsightGlance>> GlanceAsync(FormVisibility visibility, InsightSpan span, InsightSpan compare, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(compare);
        if (visibility.IsNothing)
        {
            return [];
        }

        var listed = (await forms.ListAsync(new FormFilter { AllFolders = true, Visibility = visibility }, PageRequest.First(MostForms), cancellationToken)).Items;
        if (listed.Count == 0)
        {
            return [];
        }

        var ids = listed.Select(form => form.Id).ToList();
        var now = Totals(await stats.SumAsync(ids, MainMetrics, span.From, span.To, cancellationToken));
        var before = Totals(await stats.SumAsync(ids, MainMetrics, compare.From, compare.To, cancellationToken));
        return [.. listed
            .Select(form => new InsightGlance(form.Key, form.Name, now.GetValueOrDefault(form.Id, InsightTotals.None), before.GetValueOrDefault(form.Id, InsightTotals.None)))
            .Where(glance => glance.Totals != InsightTotals.None || glance.Before != InsightTotals.None)
            .OrderByDescending(glance => glance.Totals.Entries)
            .ThenByDescending(glance => glance.Totals.Views)
            .ThenBy(glance => glance.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MostGlances)];
    }

    /// <summary>A report from the values of its span and of the span compared with.</summary>
    /// <param name="request">The span, its grouping and the span compared with.</param>
    /// <param name="now">The span's values.</param>
    /// <param name="before">The values of the span compared with; null without one.</param>
    /// <param name="definition">The version whose pages the report follows, or null.</param>
    /// <param name="members">How many members sent entries in the span, and how many entries they sent.</param>
    /// <param name="siteOn">Whether the site's insights are on.</param>
    internal static InsightReport Build(InsightRequest request, IReadOnlyList<DailyStat> now, IReadOnlyList<DailyStat>? before, FormDefinition? definition, MemberEntryCount members, bool siteOn)
    {
        var hours = new long[24];
        foreach (var stat in now.Where(stat => stat.Metric == InsightMetrics.Hour))
        {
            if (int.TryParse(stat.Dimension, NumberStyles.None, CultureInfo.InvariantCulture, out var hour) && hour is >= 0 and < 24)
            {
                hours[hour] += stat.Value;
            }
        }

        var reached = Sums(now, InsightMetrics.Page);
        return new InsightReport(
            TotalsOf(now),
            before is null ? null : TotalsOf(before),
            Periods(request, now, before),
            [.. (definition?.Pages.Skip(1) ?? []).Select(page => new InsightPageReach(page.Key, reached.GetValueOrDefault(InsightMetrics.KeyOf(page.Key))))],
            Median(Sums(now, InsightMetrics.Completion)),
            Stops(now),
            [.. Sums(now, InsightMetrics.Origin)
                .Select(pair => (Key: Guid.TryParseExact(pair.Key, "N", out var key) ? key : (Guid?)null, pair.Value))
                .Where(pair => pair.Key is not null && pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(MostOrigins)
                .Select(pair => new InsightOrigin(pair.Key!.Value, pair.Value))],
            hours,
            members,
            null,
            siteOn,
            definition?.Settings.Privacy.Insights ?? true);
    }

    // The span's groups of days, each with its counterpart in the span compared with, by their place.
    private static List<InsightPeriod> Periods(InsightRequest request, IReadOnlyList<DailyStat> now, IReadOnlyList<DailyStat>? before)
    {
        var groups = Groups(request.Span, request.GroupBy);
        var entries = Entries(now, request.GroupBy);
        var earlier = request.Compare is { } compare && before is not null ? Groups(compare, request.GroupBy) : null;
        var earlierEntries = before is null ? null : Entries(before, request.GroupBy);
        return [.. groups.Select((start, index) => new InsightPeriod(
            start,
            entries.GetValueOrDefault(start),
            earlier is not null && earlierEntries is not null && index < earlier.Count ? earlierEntries.GetValueOrDefault(earlier[index]) : null))];
    }

    // The first day of each group of days the span touches.
    private static List<DateOnly> Groups(InsightSpan span, InsightGrouping grouping)
    {
        var starts = new List<DateOnly>();
        for (var start = StartOf(span.From, grouping); start <= span.To; start = Next(start, grouping))
        {
            starts.Add(start);
        }

        return starts;
    }

    private static Dictionary<DateOnly, long> Entries(IReadOnlyList<DailyStat> stats, InsightGrouping grouping)
    {
        var entries = new Dictionary<DateOnly, long>();
        foreach (var stat in stats.Where(stat => stat.Metric == SubmissionEntries.SubmissionsMetric && stat.Dimension.Length == 0))
        {
            var start = StartOf(stat.Day, grouping);
            entries[start] = entries.GetValueOrDefault(start) + stat.Value;
        }

        return entries;
    }

    private static DateOnly StartOf(DateOnly day, InsightGrouping grouping) => grouping switch
    {
        InsightGrouping.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        InsightGrouping.Month => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    private static DateOnly Next(DateOnly start, InsightGrouping grouping) => grouping switch
    {
        InsightGrouping.Week => start.AddDays(7),
        InsightGrouping.Month => start.AddMonths(1),
        _ => start.AddDays(1),
    };

    private static InsightTotals TotalsOf(IReadOnlyList<DailyStat> stats)
    {
        long Sum(string metric) => stats.Where(stat => stat.Metric == metric && stat.Dimension.Length == 0).Sum(stat => stat.Value);
        return new InsightTotals(Sum(InsightMetrics.Views), Sum(InsightMetrics.Starts), Sum(SubmissionEntries.SubmissionsMetric), Sum(SubmissionEntries.SpamMetric));
    }

    private static Dictionary<int, InsightTotals> Totals(IReadOnlyList<DailyStatSum> sums) =>
        sums.GroupBy(sum => sum.FormId).ToDictionary(
            form => form.Key,
            form =>
            {
                long Sum(string metric) => form.Where(sum => sum.Metric == metric).Sum(sum => sum.Value);
                return new InsightTotals(Sum(InsightMetrics.Views), Sum(InsightMetrics.Starts), Sum(SubmissionEntries.SubmissionsMetric), Sum(SubmissionEntries.SpamMetric));
            });

    private static Dictionary<string, long> Sums(IReadOnlyList<DailyStat> stats, string metric) =>
        stats.Where(stat => stat.Metric == metric).GroupBy(stat => stat.Dimension, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Sum(stat => stat.Value), StringComparer.Ordinal);

    // The bucket that holds the middle entry: the first whose entries, with those of the buckets before it, reach half.
    private static string? Median(Dictionary<string, long> buckets)
    {
        var order = InsightMetrics.CompletionLimits.Select(limit => limit.ToString(CultureInfo.InvariantCulture)).Append(InsightMetrics.Longer).ToList();
        var total = order.Sum(bucket => buckets.GetValueOrDefault(bucket));
        if (total == 0)
        {
            return null;
        }

        long counted = 0;
        foreach (var bucket in order)
        {
            counted += buckets.GetValueOrDefault(bucket);
            if (counted * 2 >= total)
            {
                return bucket;
            }
        }

        return InsightMetrics.Longer;
    }

    // The fields that stopped visitors, in the browser and in posts without JavaScript, with the kind that stopped them most.
    private static List<InsightFieldStops> Stops(IReadOnlyList<DailyStat> stats)
    {
        var byField = new Dictionary<Guid, Dictionary<string, long>>();
        foreach (var stat in stats.Where(stat => stat.Metric is InsightMetrics.Invalid or InsightMetrics.Refused))
        {
            var colon = stat.Dimension.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0 || !Guid.TryParseExact(stat.Dimension[..colon], "N", out var field))
            {
                continue;
            }

            var kinds = byField.TryGetValue(field, out var found) ? found : byField[field] = new Dictionary<string, long>(StringComparer.Ordinal);
            var kind = stat.Dimension[(colon + 1)..];
            kinds[kind] = kinds.GetValueOrDefault(kind) + stat.Value;
        }

        return [.. byField
            .Select(pair => new InsightFieldStops(
                pair.Key,
                pair.Value.Values.Sum(),
                pair.Value.OrderByDescending(kind => kind.Value).ThenBy(kind => kind.Key, StringComparer.Ordinal).First().Key))
            .Where(field => field.Stops > 0)
            .OrderByDescending(field => field.Stops)
            .ThenBy(field => field.Field)
            .Take(MostFields)];
    }
}
