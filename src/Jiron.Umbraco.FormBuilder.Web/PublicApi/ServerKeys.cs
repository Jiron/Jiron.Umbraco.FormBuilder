using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>What a request's <c>Authorization</c> header says of a server key (ADR-0126).</summary>
internal enum ServerKeyStatus
{
    /// <summary>The request carries no server key: an ordinary anonymous request.</summary>
    None,

    /// <summary>The request carries the scheme with a key that is malformed or matches none.</summary>
    Invalid,

    /// <summary>The request carries a configured key.</summary>
    Valid,
}

/// <summary>A request a configured server key signed, by the key's name (ADR-0126); 7.1 and 7.2 read it to skip the
/// challenge, the honeypot and the captcha.</summary>
/// <param name="Name">The key's name.</param>
internal sealed record ServerKeyRequest(string Name)
{
    /// <summary>The name of the server key that signed the request, or null.</summary>
    public static string? NameOf(HttpContext http) => http?.Features.Get<ServerKeyRequest>()?.Name;
}

/// <summary>
/// The server keys of the public API (ADR-0012, ADR-0126): <c>Authorization: Jfb-Key &lt;key&gt;</c>, the key's SHA-256
/// compared in fixed time with every configured hash. Neither the key nor its hash is ever logged or answered.
/// </summary>
internal sealed class ServerKeys(IOptionsMonitor<FormBuilderOptions> options)
{
    /// <summary>The authorization scheme.</summary>
    public const string Scheme = "Jfb-Key";

    /// <summary>The shortest key: 32 characters.</summary>
    public const int MinKeyLength = 32;

    /// <summary>The longest key: 512 characters.</summary>
    public const int MaxKeyLength = 512;

    /// <summary>What the request's header says, and the key's name when it is a configured one.</summary>
    public (ServerKeyStatus Status, string? Name) Check(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var configured = options.CurrentValue.PublicApi.ServerKeys
            .Select(key => (key.Name, Hash: PublicApiRules.ServerKeyHash(key.Sha256)))
            .Where(key => key.Hash is not null)
            .Select(key => (key.Name, key.Hash!));
        return Match(request.Headers.Authorization, configured, (a, b) => CryptographicOperations.FixedTimeEquals(a, b));
    }

    /// <summary>
    /// The header's key against the configured ones: no scheme is none; the scheme with anything but one key of 32 to
    /// 512 visible ASCII characters, or a key whose hash matches none, is invalid. Every configured hash is compared,
    /// whichever matched, so the time taken says nothing of which key or how much of one matched.
    /// </summary>
    /// <param name="values">The request's <c>Authorization</c> values.</param>
    /// <param name="configured">The configured keys' names and hashes.</param>
    /// <param name="equals">The comparison of two hashes, in fixed time.</param>
    internal static (ServerKeyStatus Status, string? Name) Match(IReadOnlyList<string?> values, IEnumerable<(string Name, byte[] Hash)> configured, Func<byte[], byte[], bool> equals)
    {
        var signed = values.Where(IsSigned).ToList();
        if (signed.Count == 0)
        {
            return (ServerKeyStatus.None, null);
        }

        if (signed.Count > 1 || KeyOf(signed[0]!) is not { } key)
        {
            return (ServerKeyStatus.Invalid, null);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        string? found = null;
        foreach (var (name, expected) in configured)
        {
            if (equals(hash, expected))
            {
                found ??= name;
            }
        }

        return found is null ? (ServerKeyStatus.Invalid, null) : (ServerKeyStatus.Valid, found);
    }

    // Whether a value names this scheme, in any letter case, alone or before a space; another scheme is no server key.
    private static bool IsSigned(string? value) =>
        value is not null && value.Length >= Scheme.Length && value.AsSpan(0, Scheme.Length).Equals(Scheme, StringComparison.OrdinalIgnoreCase)
        && (value.Length == Scheme.Length || value[Scheme.Length] == ' ');

    // The key after the scheme and one space: 32 to 512 visible ASCII characters, or null.
    private static string? KeyOf(string value)
    {
        if (value.Length <= Scheme.Length || value[Scheme.Length] != ' ')
        {
            return null;
        }

        var key = value[(Scheme.Length + 1)..];
        return key.Length is >= MinKeyLength and <= MaxKeyLength && key.All(c => c is > ' ' and < (char)0x7F) ? key : null;
    }
}
