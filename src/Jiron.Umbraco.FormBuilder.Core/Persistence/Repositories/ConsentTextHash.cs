using System.Security.Cryptography;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// The name of a consent wording in <c>jfbConsentText</c> (ADR-0009, ADR-0038): the SHA-256, in lowercase hex, of the
/// culture's UTF-8 bytes, one zero byte and the wording's UTF-8 bytes. A culture holds no zero byte, so no two pairs of
/// culture and wording share the bytes they hash. The form posts this hash for the wording it showed, and the server
/// compares it with the hash of the current wording.
/// </summary>
internal static class ConsentTextHash
{
    /// <summary>The longest culture the column holds.</summary>
    public const int MaxCultureLength = 32;

    // Refuses text that is not well-formed UTF-16 (a lone surrogate) instead of hashing a replacement character, so
    // two different wordings never share a hash.
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The hash of <paramref name="text"/> in <paramref name="culture"/> ("" for the invariant culture).</summary>
    /// <exception cref="ArgumentException">
    /// A culture longer than <see cref="MaxCultureLength"/> or with a NUL, an empty wording, or text with a lone surrogate.
    /// </exception>
    public static string For(string culture, string text)
    {
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(text);
        if (culture.Length > MaxCultureLength || culture.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException($"A culture holds at most {MaxCultureLength} characters and no NUL.", nameof(culture));
        }

        if (text.Length == 0)
        {
            throw new ArgumentException("A consent wording is not empty.", nameof(text));
        }

        var bytes = new byte[Strict.GetByteCount(culture) + 1 + Strict.GetByteCount(text)];
        var written = Strict.GetBytes(culture, bytes);
        Strict.GetBytes(text, bytes.AsSpan(written + 1));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
