using System.Buffers.Text;
using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The tokens of confirmation links (ADR-0162): encrypted and signed by this site's Data Protection, which load-balanced servers share,
/// so a link names one entry and a time and nothing can be read from it or made up.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
internal sealed class VerificationTokens(IDataProtectionProvider protection) : IVerificationTokens
{
    /// <summary>The Data Protection purpose of the tokens.</summary>
    public const string Purpose = "Jiron.FormBuilder.Verification.v1";

    /// <summary>The longest token, in UTF-16 code units; a longer one is none of this site's.</summary>
    public const int MaxLength = 1_024;

    private const byte Version = 1;
    private const int PayloadLength = 1 + 16 + sizeof(long);

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>A token for an entry until a time.</summary>
    /// <param name="entry">The entry's key.</param>
    /// <param name="expires">When the link's time is over.</param>
    public string Issue(Guid entry, DateTimeOffset expires)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        entry.TryWriteBytes(payload.AsSpan(1, 16));
        BitConverter.TryWriteBytes(payload.AsSpan(17), expires.ToUnixTimeSeconds());
        return Base64Url.EncodeToString(_protector.Protect(payload));
    }

    /// <summary>The entry and time a token names; null for anything this site did not write.</summary>
    /// <param name="token">The token as the link gave it.</param>
    public (Guid Entry, DateTimeOffset Expires)? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxLength)
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = _protector.Unprotect(Base64Url.DecodeFromChars(token));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }

        return payload.Length == PayloadLength && payload[0] == Version
            ? (new Guid(payload.AsSpan(1, 16)), DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(payload, 17)))
            : null;
    }
}
