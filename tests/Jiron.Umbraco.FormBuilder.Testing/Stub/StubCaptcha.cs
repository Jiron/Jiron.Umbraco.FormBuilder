using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace Jiron.Umbraco.FormBuilder.Testing.Stub;

/// <summary>
/// The stub's captcha verification (D22: no real keys, no real provider). A provider's widget gives the browser a
/// token, and the server posts it with its secret to the provider's verification address; on test hosts that address
/// is the stub's <c>/captcha/siteverify</c> (or <c>/captcha/{name}/siteverify</c>, one per provider). The answer
/// depends only on the token, so a browser test chooses the outcome by the token its fake widget hands over.
/// </summary>
public static class StubCaptcha
{
    /// <summary>A token that verifies.</summary>
    public const string Pass = "JFB_FAKE-captcha-pass";

    /// <summary>A token that the provider refuses as invalid.</summary>
    public const string Fail = "JFB_FAKE-captcha-fail";

    /// <summary>A token that was already used or is too old.</summary>
    public const string Expired = "JFB_FAKE-captcha-expired";

    /// <summary>
    /// The start of a token that verifies with a score: <c>JFB_FAKE-captcha-score-0.3</c> verifies with the score 0.3,
    /// as score-based providers answer.
    /// </summary>
    public const string ScorePrefix = "JFB_FAKE-captcha-score-";

    /// <summary>True for <c>/captcha/siteverify</c> and <c>/captcha/{name}/siteverify</c>.</summary>
    public static bool IsVerifyPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Trim('/').Split('/') is ["captcha", "siteverify"] or ["captcha", { Length: > 0 }, "siteverify"];
    }

    /// <summary>The verification answer for a posted form (<c>secret</c>, <c>response</c>, optionally <c>remoteip</c>).</summary>
    public static StubCaptchaAnswer Verify(string body, string? contentType, DateTimeOffset now)
    {
        var form = contentType?.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true
            ? QueryHelpers.ParseQuery("?" + body)
            : [];
        var secret = form.TryGetValue("secret", out var secrets) ? secrets.ToString() : string.Empty;
        var token = form.TryGetValue("response", out var tokens) ? tokens.ToString() : string.Empty;
        if (secret.Length == 0)
        {
            return StubCaptchaAnswer.Refused("missing-input-secret");
        }

        if (token.Length == 0)
        {
            return StubCaptchaAnswer.Refused("missing-input-response");
        }

        var at = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        if (token == Pass)
        {
            return new StubCaptchaAnswer(true, at, "localhost", null, null, []);
        }

        if (token.StartsWith(ScorePrefix, StringComparison.Ordinal)
            && decimal.TryParse(token[ScorePrefix.Length..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var score)
            && score is >= 0m and <= 1m)
        {
            return new StubCaptchaAnswer(true, at, "localhost", score, "submit", []);
        }

        return StubCaptchaAnswer.Refused(token == Expired ? "timeout-or-duplicate" : "invalid-input-response");
    }
}

/// <summary>A verification answer, in the shape the common providers use.</summary>
/// <param name="Success">Whether the token verified.</param>
/// <param name="ChallengeTimestamp">When the challenge was solved, or null.</param>
/// <param name="Hostname">The site the token was made for, or null.</param>
/// <param name="Score">The score of a score-based check, or null.</param>
/// <param name="Action">The action of a score-based check, or null.</param>
/// <param name="ErrorCodes">Why it failed; empty on success.</param>
public sealed record StubCaptchaAnswer(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("challenge_ts"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChallengeTimestamp,
    [property: JsonPropertyName("hostname"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Hostname,
    [property: JsonPropertyName("score"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Score,
    [property: JsonPropertyName("action"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Action,
    [property: JsonPropertyName("error-codes")] IReadOnlyList<string> ErrorCodes)
{
    /// <summary>A refusal with one error code.</summary>
    public static StubCaptchaAnswer Refused(string code) => new(false, null, null, null, null, [code]);
}
