using System.Buffers.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The placement's page after a send on its way through a page with JavaScript (ADR-0129, ADR-0120): encrypted and
/// signed by this site for one form, as Umbraco's route token carries it for a post, so a visitor can neither name another
/// page nor bring one from another form's placement.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
internal sealed class PlacementTokens(IDataProtectionProvider protection)
{
    /// <summary>The Data Protection purpose of the tokens.</summary>
    public const string Purpose = "Jiron.FormBuilder.Placement.v1";

    /// <summary>The longest token, in UTF-16 code units; a longer one is none of this site's.</summary>
    public const int MaxLength = 1_024;

    private const byte Version = 1;
    private const int KeyLength = 16;
    private const int PayloadLength = 1 + (2 * KeyLength);

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>A token for a form's placement.</summary>
    /// <param name="form">The form's key.</param>
    /// <param name="page">The content key of the page after a send.</param>
    public string Issue(Guid form, Guid page)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        form.TryWriteBytes(payload.AsSpan(1, KeyLength));
        page.TryWriteBytes(payload.AsSpan(1 + KeyLength, KeyLength));
        return Base64Url.EncodeToString(_protector.Protect(payload));
    }

    /// <summary>The page a token names for a form; null for anything this site did not write for that form.</summary>
    /// <param name="token">The token as the page gave it.</param>
    /// <param name="form">The form's key, from the route.</param>
    public Guid? Read(string? token, Guid form)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = _protector.Unprotect(Base64Url.DecodeFromChars(token));
        }
        catch (Exception exception) when (exception is FormatException or System.Security.Cryptography.CryptographicException)
        {
            return null;
        }

        return payload.Length == PayloadLength && payload[0] == Version && new Guid(payload.AsSpan(1, KeyLength)) == form
            ? new Guid(payload.AsSpan(1 + KeyLength, KeyLength))
            : null;
    }
}
