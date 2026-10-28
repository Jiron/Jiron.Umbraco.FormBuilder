using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The tokens an upload answers with (ADR-0006, ADR-0087), which a submission posts for the field's value: each names
/// the form, the field and the file's key, never its stored id, and when it expires on the site's clock. Data
/// Protection protects them under a purpose of their own, so no other token reads as one; a load-balanced site shares
/// its key ring.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="time">The site's clock.</param>
internal sealed class UploadTokens(IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The Data Protection purpose of the tokens.</summary>
    public const string Purpose = "Jiron.FormBuilder.Upload.v1";

    /// <summary>How long a token counts: as long as an upload without an entry is kept (ADR-0009).</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private const byte Version = 1;
    private const int KeyLength = 16;
    private const int PayloadLength = 1 + (3 * KeyLength) + sizeof(long);

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>A token for an upload to a form's field, which expires <see cref="Lifetime"/> from now.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="fieldKey">The field's key.</param>
    /// <param name="fileKey">The recorded file's key.</param>
    public string Issue(Guid formKey, Guid fieldKey, Guid fileKey)
    {
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        formKey.TryWriteBytes(payload.AsSpan(1, KeyLength));
        fieldKey.TryWriteBytes(payload.AsSpan(1 + KeyLength, KeyLength));
        fileKey.TryWriteBytes(payload.AsSpan(1 + (2 * KeyLength), KeyLength));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1 + (3 * KeyLength)), (time.GetUtcNow() + Lifetime).UtcTicks);
        return Base64Url.EncodeToString(_protector.Protect(payload));
    }

    /// <summary>
    /// The upload a token names when it was issued for this form and has not expired; null for anything else: a token
    /// that is malformed, longer than any answer takes, protected for another purpose or another site, or changed.
    /// </summary>
    /// <param name="token">The token as a submission posts it.</param>
    /// <param name="formKey">The form the submission is for.</param>
    public UploadToken? Read(string? token, Guid formKey)
    {
        if (string.IsNullOrEmpty(token) || token.Length > FieldAnswerDefaults.TokenLength)
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

        if (payload.Length != PayloadLength || payload[0] != Version || new Guid(payload.AsSpan(1, KeyLength)) != formKey)
        {
            return null;
        }

        var expires = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1 + (3 * KeyLength))), TimeSpan.Zero);
        return expires > time.GetUtcNow()
            ? new UploadToken(new Guid(payload.AsSpan(1 + KeyLength, KeyLength)), new Guid(payload.AsSpan(1 + (2 * KeyLength), KeyLength)), expires)
            : null;
    }
}

/// <summary>The upload a token names.</summary>
/// <param name="FieldKey">The field it was uploaded to.</param>
/// <param name="FileKey">The recorded file's key.</param>
/// <param name="ExpiresUtc">When the token stops counting.</param>
internal sealed record UploadToken(Guid FieldKey, Guid FileKey, DateTimeOffset ExpiresUtc);
