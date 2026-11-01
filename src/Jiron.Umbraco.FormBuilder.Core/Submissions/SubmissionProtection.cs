using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>Why a submission looked like spam, as an entry stores it (ADR-0012).</summary>
internal static class SpamReasons
{
    /// <summary>The honeypot is filled.</summary>
    public const string Honeypot = "honeypot";

    /// <summary>Submitted sooner than the minimum fill time after the challenge was issued.</summary>
    public const string TooFast = "too_fast";

    /// <summary>A post without JavaScript, on a form whose protection needs it.</summary>
    public const string NoScript = "no_script";

    /// <summary>The request's address lies in a blocked range.</summary>
    public const string BlockedRange = "blocked_range";

    /// <summary>A blocked email domain in an email field.</summary>
    public const string BlockedDomain = "blocked_domain";

    /// <summary>A blocked word in the texts.</summary>
    public const string BlockedWord = "blocked_word";

    /// <summary>More links in the texts than the site allows.</summary>
    public const string TooManyLinks = "too_many_links";

    /// <summary>What a spam check's reason is stored as when it is not one (ADR-0140).</summary>
    public const string SpamCheck = "spam_check";

    /// <summary>
    /// The reasons as an entry stores them: comma-separated, each once, or "" for none; those past what the column holds
    /// are left out, as the score still counts them.
    /// </summary>
    /// <param name="reasons">The reasons.</param>
    public static string Stored(IEnumerable<string> reasons)
    {
        var stored = new StringBuilder();
        foreach (var reason in reasons.Distinct(StringComparer.Ordinal))
        {
            if (stored.Length + reason.Length + 1 > EntryRepository.MaxSpamReasonsLength)
            {
                break;
            }

            stored.Append(stored.Length == 0 ? string.Empty : ",").Append(reason);
        }

        return stored.ToString();
    }

    /// <summary>A spam check's reason as it can be stored.</summary>
    /// <param name="reason">The reason it gave.</param>
    public static string OfCheck(string? reason) =>
        reason is { Length: > 0 and <= 64 } && reason.All(letter => char.IsAsciiLetterLower(letter) || char.IsAsciiDigit(letter) || letter is '_' or '-') ? reason : SpamCheck;
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
/// Scores a submission (section 4.4, step 6, ADR-0012, ADR-0139, ADR-0140). A server key's request has no page behind it,
/// so neither the honeypot nor the time speaks for or against it; a post without JavaScript has no challenge, so no time.
/// The blocked ranges, the content rules and the site's own checks hold for every request.
/// </summary>
/// <param name="settings">The site's protection settings.</param>
/// <param name="types">The field types, which say whose answers are text.</param>
/// <param name="checks">The site's own spam checks.</param>
/// <param name="logger">Records a check that failed.</param>
/// <param name="time">The site's clock.</param>
internal sealed partial class SubmissionProtection(
    IFormBuilderSettingsService settings,
    FieldTypeRegistry types,
    FormBuilderSpamCheckCollection checks,
    ILogger<SubmissionProtection> logger,
    TimeProvider time)
{
    private const int MostCheckScore = 1_000;
    private static readonly TimeSpan CheckLimit = TimeSpan.FromSeconds(5);

    private const int HoneypotScore = 100;
    private const int TooFastScore = 60;
    private const int BlockedRangeScore = 100;
    private const int NoScriptScore = 100;

    /// <summary>
    /// Whether a submission has the answer its form's level asks for (ADR-0141). A server key and a post without JavaScript
    /// have no puzzle to solve: the first skips it, and the second is scored instead.
    /// </summary>
    /// <param name="request">The submission, with what the gate found in its challenge.</param>
    /// <param name="definition">The form.</param>
    /// <param name="page">The page and the site it is on.</param>
    /// <param name="cancellationToken">Cancels the read of the settings.</param>
    public async Task<bool> HasItsProofAsync(SubmissionRequest request, FormDefinition definition, RequestPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(page);
        if (request.Challenges is null || request.ServerKey is not null)
        {
            return true;
        }

        var site = (await settings.GetAsync<ProtectionSettings>(page.Site, cancellationToken)).Value;
        return site.LevelOf(definition) != ProtectionLevel.ProofOfWork || (request.Ticket is { } ticket && ProofOfWork.IsSolved(ticket, request.ProofOfWork));
    }

    /// <summary>The score of a submission and why.</summary>
    /// <param name="request">The submission, with what the gate found in its challenge.</param>
    /// <param name="definition">The form.</param>
    /// <param name="answers">The answers as they were read.</param>
    /// <param name="culture">The culture the page was in.</param>
    /// <param name="page">The page and the site it is on.</param>
    /// <param name="cancellationToken">Cancels the read of the settings and the checks.</param>
    public async Task<SpamVerdict> ScoreAsync(SubmissionRequest request, FormDefinition definition, SubmissionAnswers answers, string culture, RequestPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(page);
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

        var site = (await settings.GetAsync<ProtectionSettings>(page.Site, cancellationToken)).Value;
        if (site.Blocks(request.Address))
        {
            Add(BlockedRangeScore, SpamReasons.BlockedRange);
        }

        // A real visitor without JavaScript is never lost and never trusted (ADR-0012).
        if (request.Challenges is null && request.ServerKey is null && site.LevelOf(definition) is ProtectionLevel.ProofOfWork or ProtectionLevel.Captcha)
        {
            Add(NoScriptScore, SpamReasons.NoScript);
        }

        var level = definition.Settings.Protection.ContentRules is ContentRuleLevel.Inherit ? site.ContentRules : definition.Settings.Protection.ContentRules;
        foreach (var (points, reason) in ContentRules.Check(site, level, definition, answers.Answers, types))
        {
            Add(points, reason);
        }

        // A check may be a slow call elsewhere, so it is not asked once the score has decided.
        if (checks.Count > 0 && score < SpamVerdict.SpamFrom)
        {
            var context = new FormBuilderSpamCheckContext
            {
                Form = request.Form,
                Culture = culture,
                Page = page.Content,
                Answers = Texts(definition, answers),
                Address = request.Address,
                IsFromServerKey = request.ServerKey is not null,
            };
            foreach (var check in checks)
            {
                if (await SignalAsync(check, context, cancellationToken) is { } signal)
                {
                    Add(Math.Clamp(signal.Score, 0, MostCheckScore), SpamReasons.OfCheck(signal.Reason));
                }

                if (score >= SpamVerdict.SpamFrom)
                {
                    break;
                }
            }
        }

        return reasons.Count == 0 ? SpamVerdict.None : new SpamVerdict(score, reasons);
    }

    // The texts of the answers by their field's alias.
    private static Dictionary<string, IReadOnlyList<string>> Texts(FormDefinition definition, SubmissionAnswers answers)
    {
        var aliases = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).Where(field => field.Alias is not null).ToDictionary(field => field.Key, field => field.Alias!);
        return answers.Answers
            .Where(answer => aliases.ContainsKey(answer.Field))
            .GroupBy(answer => aliases[answer.Field], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, IReadOnlyList<string> (group) => [.. group.SelectMany(answer => answer.Values).Select(value => value.Text)], StringComparer.Ordinal);
    }

    // A check's signal; one that fails or takes too long counts for nothing, so it never loses an entry. Only the
    // submission's own cancellation ends the submission.
    private async Task<FormBuilderSpamSignal?> SignalAsync(IFormBuilderSpamCheck check, FormBuilderSpamCheckContext context, CancellationToken cancellationToken)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(CheckLimit);
        try
        {
            return await check.CheckAsync(context, limit.Token).WaitAsync(limit.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogCheckFailed(logger, check.GetType().Name, exception.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The spam check {Check} counted for nothing: {Failure}.")]
    private static partial void LogCheckFailed(ILogger logger, string check, string failure);
}
