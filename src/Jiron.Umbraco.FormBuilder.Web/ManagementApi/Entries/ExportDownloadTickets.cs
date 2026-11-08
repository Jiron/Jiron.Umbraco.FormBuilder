using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// The tickets that download a background export's file (ADR-0210): what lets the browser save a file of any size as it
/// comes, through a plain link, since a link's request carries no backoffice token. The management API issues one to the
/// export's owner, after the checks a download needs, and it holds for that user, form and export for one minute on the
/// site's clock. Data Protection protects them under a purpose of their own, so no other token reads as one; a
/// load-balanced site shares its key ring.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="time">The site's clock.</param>
internal sealed class ExportDownloadTickets(IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The Data Protection purpose of the tickets.</summary>
    public const string Purpose = "Jiron.FormBuilder.ExportDownload.v1";

    /// <summary>How long a ticket counts: long enough for the browser to start its download, and no longer.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest ticket the download reads; one is about 200 characters.</summary>
    public const int MaxLength = 1_024;

    // The payload's layout: its version, the user's, the form's and the export's keys, and the expiry's ticks.
    private const byte Version = 1;
    private const int KeyLength = 16;
    private const int ExpiresAt = 1 + (3 * KeyLength);
    private const int PayloadLength = ExpiresAt + sizeof(long);

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>A ticket for the file of a user's export of a form, which expires <see cref="Lifetime"/> from now.</summary>
    /// <param name="userKey">The export's owner.</param>
    /// <param name="formKey">The form.</param>
    /// <param name="exportKey">The export.</param>
    public ExportDownloadTicket Issue(Guid userKey, Guid formKey, Guid exportKey)
    {
        var expires = time.GetUtcNow() + Lifetime;
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        userKey.TryWriteBytes(payload.AsSpan(1, KeyLength));
        formKey.TryWriteBytes(payload.AsSpan(1 + KeyLength, KeyLength));
        exportKey.TryWriteBytes(payload.AsSpan(1 + (2 * KeyLength), KeyLength));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(ExpiresAt), expires.UtcTicks);
        return new ExportDownloadTicket(Base64Url.EncodeToString(_protector.Protect(payload)), userKey, formKey, exportKey, expires);
    }

    /// <summary>
    /// What a ticket names while it counts; null for anything else: a ticket that is malformed, longer than any is,
    /// protected for another purpose or another site, changed, or expired.
    /// </summary>
    /// <param name="ticket">The ticket as the link carries it.</param>
    public ExportDownloadTicket? Read(string? ticket)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length > MaxLength)
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = _protector.Unprotect(Base64Url.DecodeFromChars(ticket));
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            return null;
        }

        if (payload.Length != PayloadLength || payload[0] != Version)
        {
            return null;
        }

        var expires = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(ExpiresAt)), TimeSpan.Zero);
        return expires > time.GetUtcNow()
            ? new ExportDownloadTicket(ticket, new Guid(payload.AsSpan(1, KeyLength)), new Guid(payload.AsSpan(1 + KeyLength, KeyLength)), new Guid(payload.AsSpan(1 + (2 * KeyLength), KeyLength)), expires)
            : null;
    }
}

/// <summary>A ticket that downloads a background export's file, and what it names (ADR-0210).</summary>
/// <param name="Value">The ticket, as the link carries it.</param>
/// <param name="UserKey">The export's owner, the only one it was issued to.</param>
/// <param name="FormKey">The form.</param>
/// <param name="ExportKey">The export.</param>
/// <param name="ExpiresUtc">When it stops counting.</param>
internal sealed record ExportDownloadTicket(string Value, Guid UserKey, Guid FormKey, Guid ExportKey, DateTimeOffset ExpiresUtc)
{
    /// <inheritdoc />
    public override string ToString() => nameof(ExportDownloadTicket);
}
