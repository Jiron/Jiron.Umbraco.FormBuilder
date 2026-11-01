using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>Why a submission looked like spam, as an entry stores it (ADR-0012).</summary>
internal static class SpamReasons
{
    /// <summary>The honeypot is filled.</summary>
    public const string Honeypot = "honeypot";

    /// <summary>Submitted sooner than the minimum fill time after the challenge was issued.</summary>
    public const string TooFast = "too_fast";

    /// <summary>The request's address lies in a blocked range.</summary>
    public const string BlockedRange = "blocked_range";

    /// <summary>The reasons as an entry stores them: comma-separated, or "" for none.</summary>
    /// <param name="reasons">The reasons.</param>
    public static string Stored(IEnumerable<string> reasons) => string.Join(',', reasons);
}

/// <summary>A submission's spam score and the reasons for it (ADR-0012).</summary>
/// <param name="Score">The score, 0 or more.</param>
/// <param name="Reasons">Why, in the order the signals were read.</param>
internal sealed record SpamVerdict(int Score, IReadOnlyList<string> Reasons)
{
    /// <summary>The score from which a submission is Spam: accepted, stored and answered as any other, with no action run.</summary>
    public const int SpamFrom = 100;

    /// <summary>A submission no signal spoke against.</summary>
    public static SpamVerdict None { get; } = new(0, []);

    /// <summary>Whether the protection puts the submission into Spam.</summary>
    public bool IsSpam => Score >= SpamFrom;
}

/// <summary>
/// Scores a submission (section 4.4, step 6, ADR-0012, ADR-0139). A server key's request has no page behind it, so neither
/// the honeypot nor the time speaks for or against it; a post without JavaScript has no challenge, so no time. The blocked
/// ranges hold for every request.
/// </summary>
/// <param name="settings">The site's protection settings.</param>
/// <param name="time">The site's clock.</param>
internal sealed class SubmissionProtection(IFormBuilderSettingsService settings, TimeProvider time)
{
    private const int HoneypotScore = 100;
    private const int TooFastScore = 60;
    private const int BlockedRangeScore = 100;

    /// <summary>The score of a submission and why.</summary>
    /// <param name="request">The submission, with what the gate found in its challenge.</param>
    /// <param name="site">The site its page is on.</param>
    /// <param name="cancellationToken">Cancels the read of the settings.</param>
    public async Task<SpamVerdict> ScoreAsync(SubmissionRequest request, Guid? site, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var score = 0;
        var reasons = new List<string>();
        void Add(int points, string reason)
        {
            score += points;
            reasons.Add(reason);
        }

        if (request.ServerKey is null && !string.IsNullOrWhiteSpace(request.Honeypot))
        {
            Add(HoneypotScore, SpamReasons.Honeypot);
        }

        if (request.Ticket is { } ticket && time.GetUtcNow() - ticket.IssuedUtc < TimeSpan.FromSeconds(FormChallenges.MinFillSeconds))
        {
            Add(TooFastScore, SpamReasons.TooFast);
        }

        if ((await settings.GetAsync<ProtectionSettings>(site, cancellationToken)).Value.Blocks(request.Address))
        {
            Add(BlockedRangeScore, SpamReasons.BlockedRange);
        }

        return reasons.Count == 0 ? SpamVerdict.None : new SpamVerdict(score, reasons);
    }
}
