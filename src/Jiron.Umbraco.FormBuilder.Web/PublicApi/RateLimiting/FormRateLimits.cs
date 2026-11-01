using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;

/// <summary>What is left to count of a request that names a form, once its address has counted it (ADR-0149).</summary>
/// <param name="Count">What it has counted.</param>
/// <param name="Named">The key or the alias it names; anything else names no form.</param>
internal sealed record FormLimitsToTake(RateLimitCount Count, string Named);

/// <summary>
/// The rate limits of a request that names a form (ADR-0149): its address counts first, and what is left only for a form that
/// exists, so a made-up key spends no form's window and none of the site's limit, and an alias counts as its form. Looking up a
/// form this server does not keep has a site-wide window of its own, so the flood cannot read without limit either.
/// </summary>
/// <param name="limiter">The rate limits.</param>
/// <param name="forms">The forms the website serves.</param>
internal sealed class FormRateLimits(FormBuilderRateLimiter limiter, PublishedFormCache forms)
{
    /// <summary>Counts a post without JavaScript that names a form by its key, or refuses it.</summary>
    /// <param name="http">The request.</param>
    /// <param name="policy">One of <see cref="FormBuilderRateLimitPolicies"/>.</param>
    /// <param name="form">The key it names.</param>
    /// <param name="cancellationToken">Stops waiting for the form's read.</param>
    public async Task<RateLimitDecision> TakeAsync(HttpContext http, string policy, Guid form, CancellationToken cancellationToken)
    {
        var count = limiter.Start(http, policy);
        return count.Refusal ?? await FinishAsync(new FormLimitsToTake(count, form.ToString("D")), cancellationToken);
    }

    /// <summary>Counts what is left of a request whose address has counted it, or refuses it and gives that back.</summary>
    /// <param name="limits">What is left.</param>
    /// <param name="cancellationToken">Stops waiting for the form's read.</param>
    public async Task<RateLimitDecision> FinishAsync(FormLimitsToTake limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var (count, named) = limits;
        var isKey = Guid.TryParse(named, out var key);

        // What can be no form asks nothing of the database.
        if (isKey ? key == Guid.Empty : !DefinitionStructureChecks.IsAlias(named.ToLowerInvariant()))
        {
            return RateLimitDecision.Go;
        }

        var kept = isKey ? (forms.IsKept(key) ? key : (Guid?)null) : forms.KeptKeyOf(named);
        if (kept is { } form)
        {
            return limiter.Finish(count, form);
        }

        if (limiter.TakeLookup(count) is { } over)
        {
            return over;
        }

        var read = isKey ? (await forms.GetAsync(key, cancellationToken) is null ? null : key) : await forms.KeyOfAsync(named, cancellationToken);
        return read is { } found ? limiter.Finish(count, found) : RateLimitDecision.Go;
    }
}
