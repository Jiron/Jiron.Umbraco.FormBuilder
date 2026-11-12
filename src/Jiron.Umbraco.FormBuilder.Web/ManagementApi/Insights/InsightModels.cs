using Jiron.Umbraco.FormBuilder.Core.Insights;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Insights;

/// <summary>A form's report over a span (ADR-0211).</summary>
internal sealed class InsightReportResponseModel
{
    /// <summary>Whether the site's insights are on.</summary>
    public required bool SiteOn { get; init; }

    /// <summary>Whether the form's own are, in the version visitors get.</summary>
    public required bool FormOn { get; init; }

    /// <summary>The span's numbers.</summary>
    public required InsightTotalsResponseModel Totals { get; init; }

    /// <summary>Those of the span compared with; null without one.</summary>
    public InsightTotalsResponseModel? Before { get; init; }

    /// <summary>The entries by day, week or month.</summary>
    public required IReadOnlyList<InsightPeriodResponseModel> Periods { get; init; }

    /// <summary>The pages after the first, in the order of the version visitors get, and how often each was reached.</summary>
    public required IReadOnlyList<InsightPageResponseModel> Pages { get; init; }

    /// <summary>
    /// The bucket that holds the middle entry's time to complete: its upper limit in seconds (15, 30, 60, 120, 180, 300,
    /// 600, 900, 1800 or 3600), or <c>more</c>; null without entries that tell it.
    /// </summary>
    public string? MedianCompletion { get; init; }

    /// <summary>The fields that stop visitors most, most first, at most 10.</summary>
    public required IReadOnlyList<InsightFieldResponseModel> Fields { get; init; }

    /// <summary>The website pages entries come from most, most first, at most 10.</summary>
    public required IReadOnlyList<InsightOriginResponseModel> Origins { get; init; }

    /// <summary>The entries by hour of the site's day, 24 of them from midnight.</summary>
    public required IReadOnlyList<long> Hours { get; init; }

    /// <summary>How many members sent entries in the span.</summary>
    public required long Members { get; init; }

    /// <summary>How many entries they sent.</summary>
    public required long MemberEntries { get; init; }

    /// <summary>How many members sent entries in the span compared with; null without one.</summary>
    public long? MembersBefore { get; init; }

    /// <summary>The site's time zone, in which the entries' hours are counted, as an IANA id such as <c>Europe/Berlin</c>.</summary>
    public required string TimeZone { get; init; }

    /// <summary>The boxes the user shows as tables.</summary>
    public required IReadOnlyList<string> Tables { get; init; }
}

/// <summary>A form's main numbers over a span.</summary>
internal sealed class InsightTotalsResponseModel
{
    /// <summary>How often the form was shown.</summary>
    public required long Views { get; init; }

    /// <summary>How often a visitor started it.</summary>
    public required long Starts { get; init; }

    /// <summary>How many entries it received.</summary>
    public required long Entries { get; init; }

    /// <summary>How many submissions went into Spam.</summary>
    public required long Spam { get; init; }

    /// <summary>Entries per view, from 0; null without views.</summary>
    public double? Conversion { get; init; }

    internal static InsightTotalsResponseModel Of(InsightTotals totals) => new()
    {
        Views = totals.Views,
        Starts = totals.Starts,
        Entries = totals.Entries,
        Spam = totals.Spam,
        Conversion = totals.Conversion,
    };
}

/// <summary>A day, week or month of a report.</summary>
internal sealed class InsightPeriodResponseModel
{
    /// <summary>Its first day: the day, the Monday of the week, or the first of the month.</summary>
    public required DateOnly Start { get; init; }

    /// <summary>Its entries.</summary>
    public required long Entries { get; init; }

    /// <summary>Its counterpart's entries in the span compared with; null without one.</summary>
    public long? Before { get; init; }
}

/// <summary>A page after the first and how often visitors reached it.</summary>
internal sealed class InsightPageResponseModel
{
    /// <summary>The page's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its title in the default culture; null for a page without one.</summary>
    public string? Name { get; init; }

    /// <summary>How often visitors reached it.</summary>
    public required long Reached { get; init; }
}

/// <summary>A field that stopped visitors.</summary>
internal sealed class InsightFieldResponseModel
{
    /// <summary>The field's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its label in the default culture, or its alias; null for a field the form no longer knows.</summary>
    public string? Label { get; init; }

    /// <summary>How often it stopped a visitor.</summary>
    public required long Stops { get; init; }

    /// <summary>The kind of error that stopped them most, such as <c>required</c>, <c>check</c>, <c>rule</c> or <c>server</c>.</summary>
    public required string Kind { get; init; }
}

/// <summary>A website page entries came from.</summary>
internal sealed class InsightOriginResponseModel
{
    /// <summary>The content's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name; null for a page that is gone, or that the user may not open in Content.</summary>
    public string? Name { get; init; }

    /// <summary>How many entries came from it.</summary>
    public required long Entries { get; init; }
}

/// <summary>The site's table of forms over a span.</summary>
internal sealed class InsightGlanceResponseModel
{
    /// <summary>The forms with the most entries, then views, at most 10.</summary>
    public required IReadOnlyList<InsightGlanceItemResponseModel> Items { get; init; }
}

/// <summary>A form of the site's table.</summary>
internal sealed class InsightGlanceItemResponseModel
{
    /// <summary>The form's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>The span's numbers.</summary>
    public required InsightTotalsResponseModel Totals { get; init; }

    /// <summary>Those of the span compared with.</summary>
    public required InsightTotalsResponseModel Before { get; init; }
}

/// <summary>The boxes of insights the user shows as tables, as they are kept.</summary>
internal sealed class InsightTablesResponseModel
{
    /// <summary>The boxes, in order of their names.</summary>
    public required IReadOnlyList<string> Tables { get; init; }
}

/// <summary>The boxes of insights the user shows as tables.</summary>
internal sealed class InsightTablesRequestModel
{
    /// <summary>The boxes: <c>entries</c>, <c>funnel</c>, <c>fields</c>, <c>origins</c>, <c>hours</c> and <c>answers</c>.</summary>
    public IReadOnlyList<string>? Tables { get; init; }
}
