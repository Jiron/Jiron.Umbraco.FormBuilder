using System.Collections.Frozen;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Insights;

/// <summary>
/// The daily metrics of insights (ADR-0211), beside <c>submissions</c> and <c>spam</c> (ADR-0088): their names, the
/// dimension each takes, and the buckets of the time to complete. A dimension holds keys, numbers and kinds, never an
/// answer or a person.
/// </summary>
internal static class InsightMetrics
{
    /// <summary>A form shown to a visitor, as the runtime enhanced it.</summary>
    public const string Views = "views";

    /// <summary>A visitor's first touch of a form.</summary>
    public const string Starts = "starts";

    /// <summary>A page reached after the first, by the page's key.</summary>
    public const string Page = "page";

    /// <summary>A field that stopped a visitor in the browser, by the field's key and the kind of error.</summary>
    public const string Invalid = "invalid";

    /// <summary>A field the server's own checks refused in a submission, by the field's key and the kind of error.</summary>
    public const string Refused = "refused";

    /// <summary>A stored entry's page, by the content's key.</summary>
    public const string Origin = "origin";

    /// <summary>A stored entry's hour, 0 to 23 in the site's time zone.</summary>
    public const string Hour = "hour";

    /// <summary>A stored entry's time to complete, by its bucket.</summary>
    public const string Completion = "completion";

    /// <summary>The bucket of a time to complete beyond the last limit.</summary>
    public const string Longer = "more";

    /// <summary>The upper limits of the buckets of the time to complete, in seconds: 15 s to an hour.</summary>
    public static readonly IReadOnlyList<int> CompletionLimits = [15, 30, 60, 120, 180, 300, 600, 900, 1800, 3600];

    /// <summary>The types a beacon names, and the metric each counts.</summary>
    public static readonly FrozenDictionary<string, string> Beacons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["view"] = Views,
        ["start"] = Starts,
        ["page"] = Page,
        ["invalid"] = Invalid,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>The dimension of a key: 32 lowercase hex characters.</summary>
    public static string KeyOf(Guid key) => key.ToString("N");

    /// <summary>The dimension of a field and a kind of error.</summary>
    public static string FieldKindOf(Guid field, string kind) => $"{KeyOf(field)}:{kind}";

    /// <summary>The bucket of a time to complete: the first limit it fits under, or <see cref="Longer"/>.</summary>
    public static string BucketOf(TimeSpan duration)
    {
        var seconds = Math.Max(0, duration.TotalSeconds);
        foreach (var limit in CompletionLimits)
        {
            if (seconds <= limit)
            {
                return limit.ToString(CultureInfo.InvariantCulture);
            }
        }

        return Longer;
    }

    /// <summary>
    /// What a stored entry adds to its unit beside <c>submissions</c>: its page, its hour in the site's time zone and its
    /// time to complete, each where it is known.
    /// </summary>
    /// <param name="content">The page the form was on.</param>
    /// <param name="local">When it was stored, in the site's time zone.</param>
    /// <param name="duration">How long the visitor took, from the challenge's first issue; null when unknown.</param>
    public static IReadOnlyList<DailyStatCount> OfEntry(Guid? content, DateTimeOffset local, TimeSpan? duration)
    {
        var counts = new List<DailyStatCount>(3);
        if (content is { } page)
        {
            counts.Add(new DailyStatCount(Origin, KeyOf(page)));
        }

        counts.Add(new DailyStatCount(Hour, local.Hour.ToString(CultureInfo.InvariantCulture)));
        if (duration is { } took)
        {
            counts.Add(new DailyStatCount(Completion, BucketOf(took)));
        }

        return counts;
    }
}
