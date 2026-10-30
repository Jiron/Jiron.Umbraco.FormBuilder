using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The tokens a post without JavaScript redirects with once it sent the form (ADR-0100): <c>?jfb-sent=&lt;instance&gt;.&lt;token&gt;</c>,
/// each token naming the form, the entry's submission id and the instance, and when it expires on the site's clock, so
/// the page after the redirect shows that entry's outcome and no answer is in the address. Data Protection protects them
/// under a purpose of their own, so no other token reads as one; a load-balanced site shares its key ring.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="time">The site's clock.</param>
internal sealed class SentTokens(IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The query name the redirect carries the token in.</summary>
    public const string QueryName = "jfb-sent";

    /// <summary>The Data Protection purpose of the tokens.</summary>
    public const string Purpose = "Jiron.FormBuilder.Sent.v1";

    /// <summary>How long a token counts: long enough for a slow redirect and a reload, short enough not to linger in a history.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    // The longest value a page reads: the longest instance id and a token, with room to spare.
    private const int MaxValueLength = 512;
    private const byte Version = 1;
    private const int KeyLength = 16;
    private const int FixedLength = 1 + (2 * KeyLength) + sizeof(long);

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>The query value for an entry of a form sent from an instance, which expires <see cref="Lifetime"/> from now.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="submissionId">The entry's submission id.</param>
    /// <param name="instance">The form's instance id on the page.</param>
    public string Issue(Guid formKey, Guid submissionId, string instance) => Issue(formKey, submissionId, instance, time.GetUtcNow() + Lifetime);

    /// <summary>
    /// The query value for an entry of a form sent from an instance, which expires at <paramref name="expires"/>: the
    /// browser tests' test host gives an expired one through it, since its clock is shared by every test.
    /// </summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="submissionId">The entry's submission id.</param>
    /// <param name="instance">The form's instance id on the page.</param>
    /// <param name="expires">When the value stops counting.</param>
    internal string Issue(Guid formKey, Guid submissionId, string instance, DateTimeOffset expires)
    {
        ArgumentException.ThrowIfNullOrEmpty(instance);
        var name = Encoding.UTF8.GetBytes(instance);
        var payload = new byte[FixedLength + name.Length];
        payload[0] = Version;
        formKey.TryWriteBytes(payload.AsSpan(1, KeyLength));
        submissionId.TryWriteBytes(payload.AsSpan(1 + KeyLength, KeyLength));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1 + (2 * KeyLength)), expires.UtcTicks);
        name.CopyTo(payload.AsSpan(FixedLength));
        return instance + "." + Base64Url.EncodeToString(_protector.Protect(payload));
    }

    /// <summary>
    /// The submission id a query value names for this form and instance while it counts; null for anything else: a value
    /// that is malformed or too long, another form's or another instance's, protected for another purpose or another
    /// site, changed, or expired.
    /// </summary>
    /// <param name="value">The <c>jfb-sent</c> value as the page's address holds it.</param>
    /// <param name="formKey">The form the page renders.</param>
    /// <param name="instance">The form's instance id on the page.</param>
    public Guid? Read(string? value, Guid formKey, string instance)
    {
        ArgumentException.ThrowIfNullOrEmpty(instance);
        if (string.IsNullOrEmpty(value) || value.Length > MaxValueLength || !value.StartsWith(instance + ".", StringComparison.Ordinal))
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = _protector.Unprotect(Base64Url.DecodeFromChars(value.AsSpan(instance.Length + 1)));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }

        if (payload.Length <= FixedLength || payload[0] != Version || new Guid(payload.AsSpan(1, KeyLength)) != formKey
            || !Encoding.UTF8.GetString(payload.AsSpan(FixedLength)).Equals(instance, StringComparison.Ordinal))
        {
            return null;
        }

        var expires = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1 + (2 * KeyLength))), TimeSpan.Zero);
        return expires > time.GetUtcNow() ? new Guid(payload.AsSpan(1 + KeyLength, KeyLength)) : null;
    }
}
