using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Privacy;

/// <summary>
/// What an entry keeps of the visitor's IP address (ADR-0009, ADR-0224): the form's mode, or the site's where the form takes
/// it, with <c>full</c> kept anonymised unless the host allows full addresses. Anonymised keeps an IPv4 address's first three
/// bytes (a /24) and an IPv6 address's first six (a /48), the rest zero; an IPv4 address carried in IPv6 counts as IPv4, and
/// an IPv6 address keeps no scope, which names the server's own interface.
/// </summary>
/// <param name="settings">The site's privacy settings.</param>
/// <param name="options">The host configuration.</param>
internal sealed class VisitorAddresses(IFormBuilderSettingsService settings, IOptions<FormBuilderOptions> options)
{
    // The bytes an anonymised address keeps: an IPv4 /24 and an IPv6 /48.
    private const int IPv4Kept = 3;
    private const int IPv6Kept = 6;

    /// <summary>The mode the form's entries keep addresses in, as the site and the host have it; never <c>inherit</c>.</summary>
    /// <param name="definition">The form, as the site runs it.</param>
    /// <param name="cancellationToken">Cancels the read of the site's settings.</param>
    public async Task<IPHandling> ModeAsync(FormDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mode = definition.Settings.Privacy.IP;
        if (mode == IPHandling.Inherit)
        {
            mode = (await settings.GetAsync<PrivacySettings>(null, cancellationToken)).Value.IP;
        }

        return mode switch
        {
            IPHandling.Full when !options.Value.Privacy.AllowFullIp => IPHandling.Anonymised,
            IPHandling.Anonymised or IPHandling.Full => mode,
            _ => IPHandling.None,
        };
    }

    /// <summary>What an entry of the form keeps of <paramref name="address"/>: null for nothing.</summary>
    /// <param name="definition">The form, as the site runs it.</param>
    /// <param name="address">The address the submission came from, if known.</param>
    /// <param name="cancellationToken">Cancels the read of the site's settings.</param>
    public async Task<string?> KeptAsync(FormDefinition definition, IPAddress? address, CancellationToken cancellationToken = default) =>
        address is null ? null : Kept(address, await ModeAsync(definition, cancellationToken));

    /// <summary>What <paramref name="mode"/> keeps of <paramref name="address"/>, in its usual text form; null for nothing.</summary>
    /// <param name="address">The address.</param>
    /// <param name="mode">The mode; <c>inherit</c> keeps nothing, as it is never a mode an entry is kept in.</param>
    public static string? Kept(IPAddress address, IPHandling mode)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (mode is not (IPHandling.Anonymised or IPHandling.Full))
        {
            return null;
        }

        var bytes = (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).GetAddressBytes();
        if (mode == IPHandling.Anonymised)
        {
            var kept = bytes.Length == 4 ? IPv4Kept : IPv6Kept;
            Array.Clear(bytes, kept, bytes.Length - kept);
        }

        return new IPAddress(bytes).ToString();
    }
}
