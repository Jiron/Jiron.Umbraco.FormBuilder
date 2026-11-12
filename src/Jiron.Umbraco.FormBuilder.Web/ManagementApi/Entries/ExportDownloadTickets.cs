using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// The tickets that download a background export's file (ADR-0210): what lets the browser save a file of any size as it
/// comes, through a plain link, since a link's request carries no backoffice token. The management API issues one to the
/// export's owner, after the checks a download needs, and it holds for that user, form and export for one minute on the
/// site's clock; the download makes those checks again (<see cref="ExportDownloadHolders"/>). Data Protection protects them
/// under a purpose of their own, one for a form's exports and another for a person's (16.1's review), so no other token
/// reads as one and neither reads as the other; a load-balanced site shares its key ring.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="time">The site's clock.</param>
internal sealed class ExportDownloadTickets(IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The Data Protection purpose of the tickets of a form's exports.</summary>
    public const string Purpose = "Jiron.FormBuilder.ExportDownload.v1";

    /// <summary>The Data Protection purpose of the tickets of a person's exports (ADR-0223).</summary>
    public const string PersonPurpose = "Jiron.FormBuilder.PersonExportDownload.v1";

    /// <summary>How long a ticket counts: long enough for the browser to start its download, and no longer.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    /// <summary>The longest ticket the download reads; one is about 200 characters.</summary>
    public const int MaxLength = 1_024;

    // The payload's layout: its version, the user's, the form's and the export's keys, and the expiry's ticks.
    private const byte Version = 1;
    private const int KeyLength = 16;
    private const int ExpiresAt = 1 + (3 * KeyLength);
    private const int PayloadLength = ExpiresAt + sizeof(long);

    private readonly IDataProtector _forms = protection.CreateProtector(Purpose);
    private readonly IDataProtector _people = protection.CreateProtector(PersonPurpose);

    /// <summary>A ticket for the file of a user's export of a form, which expires <see cref="Lifetime"/> from now.</summary>
    /// <param name="userKey">The export's owner.</param>
    /// <param name="formKey">The form, or <see cref="PersonExports.Scope"/> for a person's export.</param>
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
        return new ExportDownloadTicket(Base64Url.EncodeToString(ProtectorOf(formKey).Protect(payload)), userKey, formKey, exportKey, expires);
    }

    /// <summary>
    /// What a ticket of the form's exports names while it counts; null for anything else: a ticket that is malformed, longer
    /// than any is, protected for another purpose or another site, changed, expired, or for another form.
    /// </summary>
    /// <param name="ticket">The ticket as the link carries it.</param>
    /// <param name="formKey">The form the link names, or <see cref="PersonExports.Scope"/> for a person's export.</param>
    public ExportDownloadTicket? Read(string? ticket, Guid formKey)
    {
        if (string.IsNullOrEmpty(ticket) || ticket.Length > MaxLength)
        {
            return null;
        }

        byte[] payload;
        try
        {
            payload = ProtectorOf(formKey).Unprotect(Base64Url.DecodeFromChars(ticket));
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
        var named = new Guid(payload.AsSpan(1 + KeyLength, KeyLength));
        return expires > time.GetUtcNow() && named == formKey
            ? new ExportDownloadTicket(ticket, new Guid(payload.AsSpan(1, KeyLength)), named, new Guid(payload.AsSpan(1 + (2 * KeyLength), KeyLength)), expires)
            : null;
    }

    // A person's exports have their own purpose, so neither kind of ticket reads as the other.
    private IDataProtector ProtectorOf(Guid formKey) => formKey == PersonExports.Scope ? _people : _forms;
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
