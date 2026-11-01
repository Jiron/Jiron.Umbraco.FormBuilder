using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The spam protection settings (section 4.8, ADR-0012, ADR-0139, ADR-0140), set per site: the blocked address ranges and
/// the content rules. The default level comes with what it configures (ADR-0043).
/// </summary>
internal sealed record ProtectionSettings : ISettingsSection
{
    /// <summary>The most ranges a site may block.</summary>
    public const int MaxBlockedRanges = 1_000;

    /// <summary>The most words, and the most domains, a site may block.</summary>
    public const int MaxBlockedItems = 1_000;

    /// <summary>The longest blocked word.</summary>
    public const int MaxBlockedWordLength = 100;

    /// <summary>The longest blocked domain.</summary>
    public const int MaxDomainLength = 253;

    /// <summary>The most links a site may allow.</summary>
    public const int MostMaxLinks = 1_000;

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

    /// <summary>How strictly the site's forms that inherit it judge their entries; never <c>inherit</c>, which is how a form takes this one.</summary>
    public ContentRuleLevel ContentRules { get; init; } = ContentRuleLevel.Normal;

    /// <summary>The words and phrases that count against a submission, compared as rules compare texts (ADR-0005), each as words of their own.</summary>
    public IReadOnlyList<string> BlockedWords { get; init; } = [];

    /// <summary>The domains whose addresses in an email field put a submission into Spam, with what lies under them, such as <c>example.com</c>.</summary>
    public IReadOnlyList<string> BlockedDomains { get; init; } = [];

    /// <summary>The most links a submission's texts may hold before they count against it.</summary>
    public int MaxLinks { get; init; } = 3;

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
    /// <remarks>Each range is written as the host writes it, each domain as it is compared and each word without surrounding space, all of them once.</remarks>
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
            BlockedWords =
            [
                .. (section.BlockedWords ?? [])
                    .Select(word => word?.Trim() ?? string.Empty)
                    .DistinctBy(TextSemantics.Fold, StringComparer.Ordinal),
            ],
            BlockedDomains =
            [
                .. (section.BlockedDomains ?? [])
                    .Select(domain => domain?.Trim() ?? string.Empty)
                    .Select(domain => Submissions.ContentRules.Domain(domain) ?? domain)
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

        if (section.ContentRules == ContentRuleLevel.Inherit)
        {
            errors.Add(nameof(ProtectionSettings.ContentRules), SettingsErrors.ContentRulesCode, "A site's content rules are off, normal or strict.");
        }

        if (section.BlockedWords.Count > ProtectionSettings.MaxBlockedItems)
        {
            errors.Add(nameof(ProtectionSettings.BlockedWords), SettingsErrors.Range, $"A site can block at most {ProtectionSettings.MaxBlockedItems} words.");
        }

        foreach (var word in section.BlockedWords.Where(word => word.Length is 0 or > ProtectionSettings.MaxBlockedWordLength || word.Contains('\n') || word.Contains('\r')))
        {
            errors.Add(nameof(ProtectionSettings.BlockedWords), SettingsErrors.WordCode, $"A blocked word is 1 to {ProtectionSettings.MaxBlockedWordLength} characters on one line.");
        }

        if (section.BlockedDomains.Count > ProtectionSettings.MaxBlockedItems)
        {
            errors.Add(nameof(ProtectionSettings.BlockedDomains), SettingsErrors.Range, $"A site can block at most {ProtectionSettings.MaxBlockedItems} domains.");
        }

        foreach (var domain in section.BlockedDomains.Where(domain => domain.Length is 0 or > ProtectionSettings.MaxDomainLength || Submissions.ContentRules.Domain(domain) != domain))
        {
            errors.Add(nameof(ProtectionSettings.BlockedDomains), SettingsErrors.DomainCode, $"\"{domain}\" is not a domain such as example.com.");
        }

        if (section.MaxLinks is < 0 or > ProtectionSettings.MostMaxLinks)
        {
            errors.Add(nameof(ProtectionSettings.MaxLinks), SettingsErrors.Range, $"The most links a site allows is from 0 to {ProtectionSettings.MostMaxLinks}.");
        }
    }
}
