using System.Net;
using System.Net.Sockets;

namespace Jiron.Umbraco.FormBuilder.Core.Outbound;

/// <summary>
/// Which addresses Jiron Form Builder may connect to when it calls another host (ADR-0076): only globally reachable
/// ones. Every range the IANA special-purpose registries list as not globally reachable is refused, and an IPv6 address
/// that carries an IPv4 address counts by that address.
/// </summary>
internal static class PublicAddresses
{
    /// <summary>Whether <paramref name="address"/> is globally reachable.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Span<byte> bytes = stackalloc byte[16];
        if (!address.TryWriteBytes(bytes, out var written))
        {
            return false;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork when written == 4 => IsPublicV4(bytes[..4]),
            AddressFamily.InterNetworkV6 when written == 16 => IsPublicV6(bytes),
            _ => false,
        };
    }

    private static bool IsPublicV4(ReadOnlySpan<byte> a) => !(
        a[0] is 0 or 10 or 127 or >= 224
        || (a[0] == 100 && (a[1] & 0xC0) == 64)
        || (a[0] == 169 && a[1] == 254)
        || (a[0] == 172 && (a[1] & 0xF0) == 16)
        || (a[0] == 192 && a[1] == 0 && a[2] is 0 or 2)
        || (a[0] == 192 && a[1] == 88 && a[2] == 99)
        || (a[0] == 192 && a[1] == 168)
        || (a[0] == 198 && (a[1] & 0xFE) == 18)
        || (a[0] == 198 && a[1] == 51 && a[2] == 100)
        || (a[0] == 203 && a[1] == 0 && a[2] == 113));

    private static bool IsPublicV6(ReadOnlySpan<byte> b)
    {
        // ::ffff:0:0/96, an IPv4 address in IPv6 form.
        if (b[..10].IndexOfAnyExcept((byte)0) < 0 && b[10] == 0xFF && b[11] == 0xFF)
        {
            return IsPublicV4(b[12..]);
        }

        // 64:ff9b::/96, the well-known NAT64 prefix, which reaches the IPv4 address it carries.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].IndexOfAnyExcept((byte)0) < 0)
        {
            return IsPublicV4(b[12..]);
        }

        // 2002::/16, 6to4, which carries the IPv4 address after its prefix.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4(b[2..6]);
        }

        // Only global unicast (2000::/3), and in it neither the protocol assignments (2001::/23) nor documentation
        // (2001:db8::/32, 3fff::/20).
        return (b[0] & 0xE0) == 0x20
            && !(b[0] == 0x20 && b[1] == 0x01 && (b[2] & 0xFE) == 0)
            && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
            && !(b[0] == 0x3F && b[1] == 0xFF && (b[2] & 0xF0) == 0);
    }
}
