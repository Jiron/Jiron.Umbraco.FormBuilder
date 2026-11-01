using System.Net;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The spam protection settings (section 4.8, ADR-0012, ADR-0139), set per site. It holds the blocked address ranges so
/// far; the other fields come with what they configure (ADR-0043).
/// </summary>
internal sealed record ProtectionSettings : ISettingsSection
{
    /// <summary>The most ranges a site may block.</summary>
    public const int MaxBlockedRanges = 1_000;

    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Protection;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <inheritdoc />
    public static bool PerSite => true;

    /// <summary>
    /// The address ranges whose submissions go to Spam, in CIDR notation such as <c>203.0.113.0/24</c> or
    /// <c>2001:db8::/32</c>; matched against the request and never stored.
    /// </summary>
    public IReadOnlyList<string> BlockedRanges { get; init; } = [];

    /// <summary>
    /// Whether a request's address lies in one of the blocked ranges; an address the host could not tell lies in none. An
    /// IPv4 range holds the mapped address a dual-stack listener reports for an IPv4 visitor.
    /// </summary>
    /// <param name="address">The request's address.</param>
    public bool Blocks(IPAddress? address) =>
        address is not null && BlockedRanges.Any(range => IPNetwork.TryParse(range, out var network) && network.Contains(address));
}

/// <summary>Validates the spam protection settings.</summary>
internal sealed class ProtectionSettingsValidator : ISettingsValidator<ProtectionSettings>
{
    /// <inheritdoc />
    /// <remarks>Each range is written as the host writes it, without surrounding space, once.</remarks>
    public ProtectionSettings Normalize(ProtectionSettings section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section with
        {
            BlockedRanges =
            [
                .. (section.BlockedRanges ?? [])
                    .Select(range => range?.Trim() ?? string.Empty)
                    .Select(range => IPNetwork.TryParse(range, out var network) ? network.ToString() : range)
                    .Distinct(StringComparer.Ordinal),
            ],
        };
    }

    /// <inheritdoc />
    public void Validate(ProtectionSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        if (section.BlockedRanges.Count > ProtectionSettings.MaxBlockedRanges)
        {
            errors.Add(nameof(ProtectionSettings.BlockedRanges), SettingsErrors.Range, $"A site can block at most {ProtectionSettings.MaxBlockedRanges} address ranges.");
        }

        foreach (var range in section.BlockedRanges)
        {
            // A mapped range would never match: the address it means is compared as IPv4.
            if (!IPNetwork.TryParse(range, out var network) || network.BaseAddress.IsIPv4MappedToIPv6)
            {
                errors.Add(nameof(ProtectionSettings.BlockedRanges), SettingsErrors.RangeCode, $"\"{range}\" is not an address range such as 203.0.113.0/24 or 2001:db8::/32.");
            }
        }
    }
}
