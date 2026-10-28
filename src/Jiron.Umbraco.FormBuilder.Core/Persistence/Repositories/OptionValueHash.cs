using System.Security.Cryptography;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// The key of an option in <c>jfbOptionCapacity</c> (ADR-0033): the SHA-256, in lowercase hex, of the option value's
/// UTF-8 bytes. The key compares exactly on both databases, which the value would not: SQL Server's collation folds
/// more than case.
/// </summary>
internal static class OptionValueHash
{
    // Refuses text that is not well-formed UTF-16 (a lone surrogate) instead of hashing a replacement character, so two
    // different values never share a hash.
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The hash of an option's value.</summary>
    /// <exception cref="ArgumentException">An empty value, or one with a lone surrogate.</exception>
    public static string For(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            throw new ArgumentException("An option's value is not empty.", nameof(value));
        }

        try
        {
            return Convert.ToHexStringLower(SHA256.HashData(Strict.GetBytes(value)));
        }
        catch (EncoderFallbackException invalid)
        {
            throw new ArgumentException("An option's value is well-formed text, without a lone surrogate.", nameof(value), invalid);
        }
    }
}
