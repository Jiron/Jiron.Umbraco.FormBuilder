using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// A captcha verified by a form post to the provider (ADR-0012, ADR-0142): only the secret and the token go out, never the
/// visitor's address. It holds the answer to success, the action, the host and the token's age. A timeout of 8 seconds, an
/// answer it cannot read, a status that is no success and the provider's complaint about the site's own keys are
/// <see cref="FormBuilderCaptchaOutcome.Unavailable"/>.
/// </summary>
/// <param name="transport">The calls to other hosts.</param>
/// <param name="time">The site's clock.</param>
internal abstract class SiteVerifyCaptchaProvider(IFormBuilderHttpTransport transport, TimeProvider time) : IFormBuilderCaptchaProvider
{
    private const int MaxAnswerBytes = 65_536;
    private static readonly TimeSpan CallLimit = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public bool IsConfigured => Keys is { SiteKey: { Length: > 0 }, SecretKey: { Length: > 0 } };

    /// <inheritdoc />
    public FormBuilderCaptchaClient? Client => IsConfigured ? new FormBuilderCaptchaClient(Keys.SiteKey!, ScriptAddress, Action) : null;

    /// <summary>The provider's keys.</summary>
    protected abstract FormBuilderCaptchaKeys Keys { get; }

    /// <summary>The address of the provider's script.</summary>
    protected abstract string ScriptAddress { get; }

    /// <summary>The address tokens are verified at, unless the host configuration names another.</summary>
    protected abstract string VerifyAddress { get; }

    /// <summary>How old a token may be.</summary>
    protected virtual TimeSpan LongestAge => TimeSpan.FromMinutes(5);

    /// <summary>The action a token must be for; null for a provider without actions.</summary>
    protected virtual string? Action => null;

    /// <summary>The fewest points an answer's score must reach; null for a provider without a score.</summary>
    /// <param name="check">The token and the form's settings.</param>
    protected virtual decimal? LeastScore(FormBuilderCaptchaCheck check) => null;

    /// <inheritdoc />
    public async Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        if (!IsConfigured)
        {
            return FormBuilderCaptchaOutcome.Unavailable;
        }

        using var timeout = new CancellationTokenSource(CallLimit, time);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Keys.VerifyAddress ?? VerifyAddress)
            {
                Content = new FormUrlEncodedContent([new("secret", Keys.SecretKey!), new("response", check.Token)]),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await transport.SendAsync(request, limit.Token);
            if (!response.IsSuccessStatusCode)
            {
                return FormBuilderCaptchaOutcome.Unavailable;
            }

            var body = await ReadAsync(response, limit.Token);
            return body is null ? FormBuilderCaptchaOutcome.Unavailable : Judge(body.RootElement, check);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FormBuilderCaptchaOutcome.Unavailable;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException or InvalidOperationException or FormatException)
        {
            return FormBuilderCaptchaOutcome.Unavailable;
        }
    }

    private static async Task<JsonDocument?> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var held = new MemoryStream();
        var buffer = new byte[8_192];
        for (var read = await stream.ReadAsync(buffer, cancellationToken); read > 0; read = await stream.ReadAsync(buffer, cancellationToken))
        {
            if (held.Length + read > MaxAnswerBytes)
            {
                return null;
            }

            await held.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return JsonDocument.Parse(held.ToArray());
    }

    private static string? Text(JsonElement answer, string name) =>
        answer.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    // What the provider said about the site's own setup, which no visitor can mend.
    private static bool BlamesTheKeys(JsonElement answer) =>
        answer.TryGetProperty("error-codes", out var codes)
        && codes.ValueKind == JsonValueKind.Array
        && codes.EnumerateArray().Any(code => code.GetString() is "missing-input-secret" or "invalid-input-secret" or "invalid-keys" or "sitekey-secret-mismatch" or "not-using-dummy-secret");

    private FormBuilderCaptchaOutcome Judge(JsonElement answer, FormBuilderCaptchaCheck check)
    {
        if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty("success", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return FormBuilderCaptchaOutcome.Unavailable;
        }

        if (!success.GetBoolean())
        {
            return BlamesTheKeys(answer) ? FormBuilderCaptchaOutcome.Unavailable : FormBuilderCaptchaOutcome.Failed;
        }

        return IsFresh(answer) && IsOurHost(answer, check) && IsOurAction(answer) && HasTheScore(answer, check) ? FormBuilderCaptchaOutcome.Passed : FormBuilderCaptchaOutcome.Failed;
    }

    private bool IsFresh(JsonElement answer)
    {
        if (!DateTimeOffset.TryParse(Text(answer, "challenge_ts"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var solved))
        {
            return false;
        }

        var age = time.GetUtcNow() - solved;
        return age <= LongestAge + ClockSkew && age >= -ClockSkew;
    }

    // A site without a host to compare has nothing to hold the token to.
    private static bool IsOurHost(JsonElement answer, FormBuilderCaptchaCheck check) =>
        check.Hosts.Count == 0 || (Text(answer, "hostname") is { } host && check.Hosts.Contains(host));

    private bool IsOurAction(JsonElement answer) => Action is null || Text(answer, "action") == Action;

    private bool HasTheScore(JsonElement answer, FormBuilderCaptchaCheck check) =>
        LeastScore(check) is not { } least
        || (answer.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number && score.GetDecimal() >= least);
}

/// <summary>reCAPTCHA v2, the checkbox (section 4.10), ported from the owner's earlier package.</summary>
/// <param name="options">The host's keys.</param>
/// <param name="transport">The calls to other hosts.</param>
/// <param name="time">The site's clock.</param>
internal sealed class RecaptchaV2Provider(IOptions<FormBuilderOptions> options, IFormBuilderHttpTransport transport, TimeProvider time) : SiteVerifyCaptchaProvider(transport, time)
{
    /// <summary>The name a form chooses it by.</summary>
    public const string ChosenAs = "recaptchaV2";

    /// <inheritdoc />
    public override string Name => ChosenAs;

    /// <inheritdoc />
    protected override FormBuilderCaptchaKeys Keys => options.Value.Captcha.RecaptchaV2;

    /// <inheritdoc />
    protected override string ScriptAddress => RecaptchaHosts.Host(options.Value.Captcha) + "/recaptcha/api.js";

    /// <inheritdoc />
    protected override string VerifyAddress => RecaptchaHosts.Host(options.Value.Captcha) + "/recaptcha/api/siteverify";
}

/// <summary>reCAPTCHA v3, scored against the form's threshold (section 4.10), ported from the owner's earlier package.</summary>
/// <param name="options">The host's keys.</param>
/// <param name="transport">The calls to other hosts.</param>
/// <param name="time">The site's clock.</param>
internal sealed class RecaptchaV3Provider(IOptions<FormBuilderOptions> options, IFormBuilderHttpTransport transport, TimeProvider time) : SiteVerifyCaptchaProvider(transport, time)
{
    /// <summary>The name a form chooses it by.</summary>
    public const string ChosenAs = "recaptchaV3";

    /// <summary>The action its tokens are asked for.</summary>
    public const string SubmitAction = "submit";

    /// <summary>The score a form without a threshold asks for.</summary>
    public const decimal DefaultThreshold = 0.5m;

    /// <inheritdoc />
    public override string Name => ChosenAs;

    /// <inheritdoc />
    protected override FormBuilderCaptchaKeys Keys => options.Value.Captcha.RecaptchaV3;

    /// <inheritdoc />
    protected override string ScriptAddress => $"{RecaptchaHosts.Host(options.Value.Captcha)}/recaptcha/api.js?render={Uri.EscapeDataString(Keys.SiteKey ?? string.Empty)}";

    /// <inheritdoc />
    protected override string VerifyAddress => RecaptchaHosts.Host(options.Value.Captcha) + "/recaptcha/api/siteverify";

    /// <inheritdoc />
    protected override TimeSpan LongestAge => TimeSpan.FromMinutes(2);

    /// <inheritdoc />
    protected override string Action => SubmitAction;

    /// <inheritdoc />
    /// <remarks>The form's <c>threshold</c>, a decimal from 0 to 1 written as text, as settings write decimals; anything else is the default.</remarks>
    protected override decimal? LeastScore(FormBuilderCaptchaCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.Settings is { } settings
            && settings.TryGetProperty("threshold", out var threshold)
            && threshold.ValueKind == JsonValueKind.String
            && decimal.TryParse(threshold.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var chosen)
            && chosen <= 1m
            ? chosen
            : DefaultThreshold;
    }
}

/// <summary>hCaptcha (section 4.10).</summary>
/// <param name="options">The host's keys.</param>
/// <param name="transport">The calls to other hosts.</param>
/// <param name="time">The site's clock.</param>
internal sealed class HcaptchaProvider(IOptions<FormBuilderOptions> options, IFormBuilderHttpTransport transport, TimeProvider time) : SiteVerifyCaptchaProvider(transport, time)
{
    /// <summary>The name a form chooses it by.</summary>
    public const string ChosenAs = "hcaptcha";

    /// <inheritdoc />
    public override string Name => ChosenAs;

    /// <inheritdoc />
    protected override FormBuilderCaptchaKeys Keys => options.Value.Captcha.Hcaptcha;

    /// <inheritdoc />
    protected override string ScriptAddress => "https://js.hcaptcha.com/1/api.js";

    /// <inheritdoc />
    protected override string VerifyAddress => "https://api.hcaptcha.com/siteverify";
}

/// <summary>Where reCAPTCHA is reached: <c>recaptcha.net</c> when the site chooses it (section 4.10).</summary>
internal static class RecaptchaHosts
{
    /// <summary>The origin of the provider's script and verification.</summary>
    /// <param name="options">The host's captcha settings.</param>
    public static string Host(FormBuilderCaptchaOptions options) => options.UseRecaptchaNet ? "https://www.recaptcha.net" : "https://www.google.com";
}
