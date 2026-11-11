using System.Globalization;
using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Samples.Captcha;

/// <summary>
/// The arithmetic sample's captcha (docs/extending.md): a sum of two small numbers that the visitor works out, for a site
/// that wants a check of its own without a third party. Every challenge brings a new sum: its site key holds the two
/// numbers and their answer protected with the site's Data Protection keys, which every server of the site shares, for
/// 20 minutes. Its script (<c>/jfb-samples/arithmetic-captcha.js</c>, the global <c>jfbSampleArithmetic</c>) shows the
/// sum, and its token is the protected answer with the visitor's. Bots that read the page work a sum out too: it shows
/// how a provider plugs in, not a strong check.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="requests">The request a challenge is made for, whose address the script is loaded from.</param>
/// <param name="time">The clock.</param>
public sealed class ArithmeticCaptchaProvider(IDataProtectionProvider protection, IHttpContextAccessor requests, TimeProvider time) : IFormBuilderCaptchaProvider
{
    /// <summary>The name a form chooses the provider by.</summary>
    public const string ProviderName = "sampleArithmetic";

    /// <summary>The global object its script defines.</summary>
    public const string ScriptGlobal = "jfbSampleArithmetic";

    /// <summary>The path of its script on the site.</summary>
    public const string ScriptPath = "/jfb-samples/arithmetic-captcha.js";

    /// <summary>How long a sum may be answered.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(20);

    private readonly IDataProtector _protector = protection.CreateProtector("Jiron.FormBuilder.Samples.Arithmetic");

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Label => "#jfbSamples_arithmetic";

    /// <inheritdoc />
    public bool IsConfigured => true;

    /// <inheritdoc />
    /// <remarks>A new sum each time it is read; none outside a request, whose address the script comes from.</remarks>
    public FormBuilderCaptchaClient? Client =>
        requests.HttpContext?.Request is { } request ? Challenge($"{request.Scheme}://{request.Host}{request.PathBase}{ScriptPath}") : null;

    /// <summary>A new sum with its script's address.</summary>
    /// <param name="scriptAddress">The absolute address of the script.</param>
    /// <returns>What a page needs to show it.</returns>
    public FormBuilderCaptchaClient Challenge(string scriptAddress)
    {
        var first = RandomNumberGenerator.GetInt32(1, 10);
        var second = RandomNumberGenerator.GetInt32(1, 10);
        var expires = time.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var sealedAnswer = _protector.Protect(string.Create(CultureInfo.InvariantCulture, $"{first + second}|{expires}"));
        return new FormBuilderCaptchaClient($"{first}+{second}:{sealedAnswer}", scriptAddress) { ScriptGlobal = ScriptGlobal };
    }

    /// <inheritdoc />
    /// <remarks>The token is the protected answer and the visitor's, as <c>protected:answer</c>.</remarks>
    public Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        var split = check.Token.LastIndexOf(':');
        if (split <= 0 || !int.TryParse(check.Token.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var given))
        {
            return Task.FromResult(FormBuilderCaptchaOutcome.Failed);
        }

        string opened;
        try
        {
            opened = _protector.Unprotect(check.Token[..split]);
        }
        catch (CryptographicException)
        {
            return Task.FromResult(FormBuilderCaptchaOutcome.Failed);
        }

        var parts = opened.Split('|');
        var passes = parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var answer)
            && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expires)
            && time.GetUtcNow().ToUnixTimeSeconds() <= expires
            && answer == given;
        return Task.FromResult(passes ? FormBuilderCaptchaOutcome.Passed : FormBuilderCaptchaOutcome.Failed);
    }
}
