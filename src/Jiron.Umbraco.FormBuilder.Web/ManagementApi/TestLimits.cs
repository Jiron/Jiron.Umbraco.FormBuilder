using System.Collections.Concurrent;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi;

/// <summary>
/// How many tests a user runs in an hour (16.1's review): a test mail goes to the user's own address, and an action's test
/// to where the action sends, so without a limit one user could spend the site's mail server, or a receiver's patience, at
/// will. <see cref="PerHour"/> an hour for each user on each server, counted in memory as the public rate limits are; a
/// window per user who tests, which a site's backoffice users bound.
/// </summary>
/// <param name="time">The site's clock.</param>
internal sealed class TestLimits(TimeProvider time)
{
    /// <summary>How many tests a user runs in an hour: far more than anyone trying a mail or an action needs.</summary>
    public const int PerHour = 60;

    private static readonly TimeSpan Hour = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<Guid, RateWindow> _users = new();

    /// <summary>
    /// Counts one test of the user's, or refuses it: <c>429 tests_limited</c> with <c>Retry-After</c> once they ran
    /// <see cref="PerHour"/> in the last hour; null when it may run.
    /// </summary>
    /// <param name="userKey">The user who tests.</param>
    /// <param name="response">The response, which a refusal gives its <c>Retry-After</c>.</param>
    public FormBuilderProblemResult? Refusal(Guid userKey, HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var window = _users.GetOrAdd(userKey, _ => new RateWindow(PerHour, Hour, time));
        if (window.TryTake(out var retryAfter, out _))
        {
            return null;
        }

        response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return FormBuilderProblems.TestsLimited();
    }
}
