using System.Security.Cryptography;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// Why an attempt of an action run or a mail did not succeed (ADR-0040): a stable code for the deliveries log to explain
/// in the editor's language, and an optional message the caller has sanitised (8.1), cut to its column when stored.
/// </summary>
/// <param name="Code">1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>_</c>, <c>.</c> and <c>-</c>.</param>
/// <param name="Message">What went wrong, without personal data; null for none.</param>
internal sealed record DeliveryError(string Code, string? Message = null)
{
    /// <summary>The longest code the columns hold.</summary>
    public const int MaxCodeLength = 64;

    /// <summary>A lease ran out before its attempt recorded a result.</summary>
    public const string LeaseExpired = "lease_expired";

    /// <summary>An earlier run that stops the rest failed for good.</summary>
    public const string Stopped = "stopped";

    /// <summary>The user's change that queued the run was undone (ADR-0023).</summary>
    public const string Undone = "undone";

    /// <summary>Whether <paramref name="code"/> is a code the columns hold as it is.</summary>
    public static bool IsCode(string? code) =>
        code is { Length: > 0 and <= MaxCodeLength } && code.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '.' or '-');
}

/// <summary>The rules both outbox repositories share (ADR-0007, ADR-0040).</summary>
internal static class Outbox
{
    /// <summary>The longest claim token the columns hold.</summary>
    public const int MaxTokenLength = 255;

    /// <summary>The longest claimant name: a token is the name, a slash and 32 hex digits.</summary>
    public const int MaxClaimantLength = 200;

    /// <summary>The most rows a due list or a purge takes at once.</summary>
    public const int MaxTake = 500;

    /// <summary>The longest lease: an attempt that needs longer is cut off (ADR-0007).</summary>
    public static readonly TimeSpan MaxLease = TimeSpan.FromDays(1);

    /// <summary>The longest wait before the next attempt.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromDays(30);

    /// <summary>The longest time mail is kept after it reached its state.</summary>
    public static readonly TimeSpan MaxKeep = TimeSpan.FromDays(3660);

    /// <summary>Refuses a token the columns cannot hold, or one with control characters.</summary>
    public static void CheckToken(string token)
    {
        if (token is not { Length: > 0 and <= MaxTokenLength } || token.Any(char.IsControl))
        {
            throw new ArgumentException($"A claim token is 1 to {MaxTokenLength} characters without control characters.", nameof(token));
        }
    }

    /// <summary>Refuses a claimant name that a token cannot hold, or one with control characters.</summary>
    public static void CheckClaimant(string claimant)
    {
        if (claimant is not { Length: > 0 and <= MaxClaimantLength } || claimant.Any(char.IsControl))
        {
            throw new ArgumentException($"A claimant is 1 to {MaxClaimantLength} characters without control characters.", nameof(claimant));
        }
    }

    /// <summary>A token for one claim: the claimant's name and 128 random bits, so no two claims share one (ADR-0007).</summary>
    public static string NewToken(string claimant) => claimant + "/" + RandomNumberGenerator.GetHexString(32, lowercase: true);

    /// <summary>Refuses an error whose code is not one.</summary>
    public static void CheckError(DeliveryError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (!DeliveryError.IsCode(error.Code))
        {
            throw new ArgumentException($"An error's code is 1 to {DeliveryError.MaxCodeLength} characters of a-z, 0-9, '_', '.' and '-'.", nameof(error));
        }
    }

    /// <summary>Refuses a lease that is not positive or longer than <see cref="MaxLease"/>.</summary>
    public static void CheckLease(TimeSpan lease)
    {
        if (lease <= TimeSpan.Zero || lease > MaxLease)
        {
            throw new ArgumentOutOfRangeException(nameof(lease), lease, $"A lease is longer than 0 and at most {MaxLease.TotalHours:0} hours.");
        }
    }

    /// <summary>Refuses a delay that is negative or longer than <see cref="MaxDelay"/>.</summary>
    public static void CheckDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero || delay > MaxDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, $"A delay is 0 to {MaxDelay.TotalDays:0} days.");
        }
    }

    /// <summary>Refuses a time to keep mail that is not positive or longer than <see cref="MaxKeep"/>.</summary>
    public static void CheckKeep(TimeSpan keep)
    {
        if (keep <= TimeSpan.Zero || keep > MaxKeep)
        {
            throw new ArgumentOutOfRangeException(nameof(keep), keep, $"Mail is kept for longer than 0 and at most {MaxKeep.TotalDays:0} days.");
        }
    }

    /// <summary>Refuses a number of rows outside 1 to <see cref="MaxTake"/>.</summary>
    public static void CheckTake(int take)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaxTake);
    }

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="length"/> characters, never between the two halves of a surrogate
    /// pair; null stays null.
    /// </summary>
    public static string? Cut(string? text, int length)
    {
        if (text is null || text.Length <= length)
        {
            return text;
        }

        var end = char.IsHighSurrogate(text[length - 1]) ? length - 1 : length;
        return text[..end];
    }
}
