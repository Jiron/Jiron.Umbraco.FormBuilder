using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WebhookSignatureSample;

/// <summary>Checks the <c>X-Jfb-Signature</c> header of a webhook from Jiron Form Builder (docs/actions.md).</summary>
public static class SignatureCheck
{
    /// <summary>How far the header's time may be from now, in seconds, so a recorded request cannot be sent again later.</summary>
    public const int ToleranceSeconds = 300;

    /// <summary>Whether the header is genuine for these bytes of the body, exactly as they arrived, and recent.</summary>
    /// <param name="secret">The signing secret.</param>
    /// <param name="header">The value of the <c>X-Jfb-Signature</c> header.</param>
    /// <param name="body">The request's body, before any parsing.</param>
    /// <param name="now">The time now.</param>
    /// <returns>True when the request came from the holder of the secret, and not long ago.</returns>
    public static bool IsGenuine(string secret, string? header, byte[] body, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(body);
        string? time = null;
        string? signature = null;
        foreach (var part in (header ?? string.Empty).Split(',', StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("t=", StringComparison.Ordinal))
            {
                if (time is not null)
                {
                    return false;
                }

                time = part[2..];
            }
            else if (part.StartsWith("v1=", StringComparison.Ordinal))
            {
                if (signature is not null)
                {
                    return false;
                }

                signature = part[3..];
            }
        }

        if (time is null || signature is null
            || !long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds()
            || Math.Abs(now.ToUnixTimeSeconds() - seconds) > ToleranceSeconds)
        {
            return false;
        }

        var signed = Encoding.ASCII.GetBytes(time + ".").Concat(body).ToArray();
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }
}
