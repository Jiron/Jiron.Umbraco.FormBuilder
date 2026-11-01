using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The signed challenges of the public API (ADR-0012, ADR-0138): each names the form, its id, when it was issued, when it
/// expires on the site's clock, on a form for members the member, and at the proof-of-work level its puzzle's difficulty. Data Protection protects them under a purpose of
/// their own, so no other token reads as one; a load-balanced site shares its key ring.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="time">The site's clock.</param>
internal sealed class ChallengeTokens(IDataProtectionProvider protection, TimeProvider time) : IChallengeReader
{
    /// <summary>The Data Protection purpose of the tokens.</summary>
    public const string Purpose = "Jiron.FormBuilder.Challenge.v1";

    private const byte Version = 1;
    private const int KeyLength = 16;
    private const int PayloadLength = 1 + (3 * KeyLength) + (2 * sizeof(long)) + 1;

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>
    /// A new challenge for a form. A valid earlier challenge of the form, even an expired one, carries its issue time over,
    /// so a visitor who took a long time is not taken for one who rushed; its id is never carried over. Only for
    /// <see cref="FormChallenges.ReceiptGrace"/> past its expiry, so one old challenge does not switch the time signal off for good.
    /// </summary>
    /// <param name="form">The form's key.</param>
    /// <param name="member">On a form for members, the signed-in member.</param>
    /// <param name="previous">The earlier challenge the runtime still holds, or null.</param>
    /// <param name="proofBits">How many zero bits its puzzle asks for; 0 for none (ADR-0141).</param>
    public string Issue(Guid form, Guid? member, string? previous = null, int proofBits = 0)
    {
        var now = time.GetUtcNow();
        var issued = Read(previous, form, FormChallenges.ReceiptGrace)?.IssuedUtc ?? now;
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        form.TryWriteBytes(payload.AsSpan(1, KeyLength));
        Guid.NewGuid().TryWriteBytes(payload.AsSpan(1 + KeyLength, KeyLength));
        (member ?? Guid.Empty).TryWriteBytes(payload.AsSpan(1 + (2 * KeyLength), KeyLength));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1 + (3 * KeyLength)), issued.UtcTicks);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1 + (3 * KeyLength) + sizeof(long)), (now + FormChallenges.Lifetime).UtcTicks);
        payload[PayloadLength - 1] = checked((byte)proofBits);
        return Base64Url.EncodeToString(_protector.Protect(payload));
    }

    /// <inheritdoc />
    public ChallengeTicket? Read(string? token, Guid form, TimeSpan grace)
    {
        if (string.IsNullOrEmpty(token) || token.Length > EntryBodies.MaxChallengeLength)
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

        if (payload.Length != PayloadLength || payload[0] != Version || new Guid(payload.AsSpan(1, KeyLength)) != form)
        {
            return null;
        }

        var expires = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1 + (3 * KeyLength) + sizeof(long))), TimeSpan.Zero);
        var late = time.GetUtcNow() - expires;
        if (late >= TimeSpan.Zero && late >= grace)
        {
            return null;
        }

        var member = new Guid(payload.AsSpan(1 + (2 * KeyLength), KeyLength));
        return new ChallengeTicket(
            new Guid(payload.AsSpan(1 + KeyLength, KeyLength)),
            new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1 + (3 * KeyLength))), TimeSpan.Zero),
            member == Guid.Empty ? null : member,
            payload[PayloadLength - 1]);
    }
}
