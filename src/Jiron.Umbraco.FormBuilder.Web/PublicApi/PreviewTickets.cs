using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The preview's tickets (ADR-0012, ADR-0203): what lets the preview frame, whose origin is <c>null</c>, call the public
/// API for one version of one form, for ten minutes on the site's clock. The management API issues them to who may design
/// the form, and one whose test entries run their actions only to who may manage its actions (ADR-0204); the frame sends
/// one as <c>Authorization: Jfb-Preview &lt;ticket&gt;</c>, never in an address. Data Protection protects them under a
/// purpose of their own, so no other token reads as one; a load-balanced site shares its key ring.
/// </summary>
/// <param name="protection">The site's Data Protection.</param>
/// <param name="time">The site's clock.</param>
internal sealed class PreviewTickets(IDataProtectionProvider protection, TimeProvider time)
{
    /// <summary>The Data Protection purpose of the tickets.</summary>
    public const string Purpose = "Jiron.FormBuilder.Preview.v1";

    /// <summary>The authorization scheme the frame sends a ticket with.</summary>
    public const string Scheme = "Jfb-Preview";

    /// <summary>How long a ticket counts.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>The longest ticket the public API reads; one is about 200 characters.</summary>
    public const int MaxLength = 1_024;

    // The payload's layout: its version, the form's key, the version's number, the expiry's ticks and the flags.
    private const byte Version = 2;
    private const int KeyLength = 16;
    private const int FlagsAt = 1 + KeyLength + sizeof(int) + sizeof(long);
    private const int PayloadLength = FlagsAt + 1;
    private const byte RunsActionsFlag = 1;

    private readonly IDataProtector _protector = protection.CreateProtector(Purpose);

    /// <summary>A ticket for a version of a form, which expires <see cref="Lifetime"/> from now.</summary>
    /// <param name="formKey">The form's key.</param>
    /// <param name="version">The number of the version the preview shows.</param>
    /// <param name="runsActions">Whether the test entries sent with it run their actions (ADR-0204).</param>
    public PreviewTicket Issue(Guid formKey, int version, bool runsActions = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        var expires = time.GetUtcNow() + Lifetime;
        var payload = new byte[PayloadLength];
        payload[0] = Version;
        formKey.TryWriteBytes(payload.AsSpan(1, KeyLength));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1 + KeyLength), version);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1 + KeyLength + sizeof(int)), expires.UtcTicks);
        payload[FlagsAt] = runsActions ? RunsActionsFlag : (byte)0;
        return new PreviewTicket(Base64Url.EncodeToString(_protector.Protect(payload)), formKey, version, expires, runsActions);
    }

    /// <summary>
    /// What a ticket names while it counts; null for anything else: a ticket that is malformed, longer than any is,
    /// protected for another purpose or another site, changed, or expired.
    /// </summary>
    /// <param name="ticket">The ticket as the frame sends it.</param>
    public PreviewTicket? Read(string? ticket)
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

        if (payload.Length != PayloadLength || payload[0] != Version || (payload[FlagsAt] & ~RunsActionsFlag) != 0)
        {
            return null;
        }

        var version = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1 + KeyLength));
        var expires = new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1 + KeyLength + sizeof(int))), TimeSpan.Zero);
        return version > 0 && expires > time.GetUtcNow()
            ? new PreviewTicket(ticket, new Guid(payload.AsSpan(1, KeyLength)), version, expires, payload[FlagsAt] == RunsActionsFlag)
            : null;
    }

    /// <summary>Whether a request carries the preview's scheme in its <c>Authorization</c> header, with a ticket or without.</summary>
    /// <param name="request">The request.</param>
    /// <param name="ticket">The ticket after the scheme, or null when the header names another scheme or none.</param>
    public static bool Carries(HttpRequest request, out string? ticket)
    {
        ArgumentNullException.ThrowIfNull(request);
        ticket = null;
        var values = request.Headers.Authorization;
        if (!values.Any(IsScheme))
        {
            return false;
        }

        // Only one header names a ticket; a second one, or the scheme alone, carries one that does not hold.
        if (values.Count == 1 && values[0]!.Length > Scheme.Length + 1)
        {
            ticket = values[0]![(Scheme.Length + 1)..];
        }

        return true;
    }

    // The scheme, in any letter case, alone or before a space.
    private static bool IsScheme(string? value) =>
        value is not null
        && value.Length >= Scheme.Length
        && value.AsSpan(0, Scheme.Length).Equals(Scheme, StringComparison.OrdinalIgnoreCase)
        && (value.Length == Scheme.Length || value[Scheme.Length] == ' ');
}

/// <summary>A preview's ticket and what it names (ADR-0203).</summary>
/// <param name="Value">The ticket, as the frame sends it.</param>
/// <param name="FormKey">The form it is for.</param>
/// <param name="Version">The number of the version the preview shows.</param>
/// <param name="ExpiresUtc">When it stops counting.</param>
/// <param name="RunsActions">Whether the test entries sent with it run their actions (ADR-0204).</param>
internal sealed record PreviewTicket(string Value, Guid FormKey, int Version, DateTimeOffset ExpiresUtc, bool RunsActions = false)
{
    /// <inheritdoc />
    public override string ToString() => nameof(PreviewTicket);
}

/// <summary>
/// A request from the preview frame whose ticket holds (ADR-0203), set by the public API's middleware: the endpoints that
/// take it read the version the ticket names in place of the published one.
/// </summary>
/// <param name="FormKey">The form the ticket is for.</param>
/// <param name="Version">The number of the version the preview shows.</param>
/// <param name="RunsActions">Whether the test entries it sends run their actions (ADR-0204).</param>
internal sealed record PreviewRequest(Guid FormKey, int Version, bool RunsActions = false)
{
    /// <summary>The preview the request comes from, or null for a visitor's.</summary>
    public static PreviewRequest? Of(HttpContext http) => http?.Features.Get<PreviewRequest>();
}
