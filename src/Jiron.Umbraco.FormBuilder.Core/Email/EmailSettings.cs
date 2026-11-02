using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Email;

/// <summary>
/// The email settings (section 4.8, ADR-0159), each field per site: who a site's mail comes from, how it looks, and who hears
/// about what goes wrong. A site without a sender of its own sends from Umbraco's <c>Umbraco:CMS:Global:Smtp:From</c>.
/// </summary>
internal sealed record EmailSettings : ISettingsSection
{
    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Email;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <inheritdoc />
    public static bool PerSite => true;

    /// <summary>The name mail comes from, or null for the address alone.</summary>
    public string? SenderName { get; init; }

    /// <summary>The address mail comes from, or null for Umbraco's own; it is never a visitor's (section 4.10).</summary>
    public string? SenderAddress { get; init; }

    /// <summary>The key of the layout a mail uses unless it names one; null for the built-in card.</summary>
    public Guid? DefaultLayout { get; init; }

    /// <summary>Who hears of failed deliveries.</summary>
    public IReadOnlyList<string> TeamRecipients { get; init; } = [];

    /// <summary>The addresses entries may be forwarded to, besides backoffice users.</summary>
    public IReadOnlyList<string> ForwardAddresses { get; init; } = [];
}

/// <summary>Holds the email settings to what a mail needs: addresses that are one address each, and a name of one line.</summary>
internal sealed class EmailSettingsValidator : ISettingsValidator<EmailSettings>
{
    /// <summary>The longest sender's name.</summary>
    public const int MaxNameLength = 200;

    /// <summary>The most addresses of a list.</summary>
    public const int MaxAddresses = 100;

    /// <inheritdoc />
    public EmailSettings Normalize(EmailSettings section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section with
        {
            SenderName = Blank(EmailHeaders.Clean(section.SenderName)),
            SenderAddress = Blank(section.SenderAddress?.Trim()),
            TeamRecipients = Addresses(section.TeamRecipients),
            ForwardAddresses = Addresses(section.ForwardAddresses),
        };
    }

    /// <inheritdoc />
    public void Validate(EmailSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        if (section.SenderName is { Length: > MaxNameLength })
        {
            errors.Add("senderName", SettingsErrors.Range, $"Has at most {MaxNameLength} characters.");
        }

        if (section.SenderAddress is { } sender && !EmailAddresses.IsOne(sender))
        {
            errors.Add("senderAddress", SettingsErrors.AddressCode, "Must be one address, such as hello@example.com.");
        }

        CheckList("teamRecipients", section.TeamRecipients, errors);
        CheckList("forwardAddresses", section.ForwardAddresses, errors);
    }

    private static void CheckList(string field, IReadOnlyList<string> addresses, SettingsErrors errors)
    {
        // A mail holds its recipients in one column, so a list has to fit there as it will be written.
        if (addresses.Count > MaxAddresses || string.Join(", ", addresses).Length > EmailRepository.MaxAddressesLength)
        {
            errors.Add(field, SettingsErrors.Range, $"Has at most {MaxAddresses} addresses, and {EmailRepository.MaxAddressesLength} characters in all.");
        }

        if (addresses.Any(address => !EmailAddresses.IsOne(address)))
        {
            errors.Add(field, SettingsErrors.AddressCode, "Each must be one address, such as hello@example.com.");
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static List<string> Addresses(IReadOnlyList<string>? list) =>
        [.. (list ?? []).Select(address => address?.Trim() ?? string.Empty).Where(address => address.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
}
