using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The privacy settings (ADR-0008, ADR-0009), the same for every site: on Privacy, Retention, how long entries are kept in
/// each state (ADR-0222); on Privacy, Data collection, what entries keep of visitors' IP addresses (ADR-0224), whether
/// the forms count what visitors do with them (ADR-0211) and what a member's deletion in Umbraco does to their entries
/// (ADR-0231).
/// </summary>
internal sealed record PrivacySettings : ISettingsSection
{
    /// <summary>The box of the retention days, which Privacy, Retention shows and Data collection leaves out.</summary>
    public const string RetentionBox = "retention";

    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Privacy;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <summary>How long entries are kept in each state, counted from their last change of state; a form may set its own.</summary>
    [SettingsField(RetentionBox)]
    public SiteRetention Retention { get; init; } = new();

    /// <summary>
    /// What entries keep of the visitor's IP address (ADR-0009, ADR-0224): nothing by default, the address with its end
    /// removed, or all of it where the host allows it; each form may choose its own.
    /// </summary>
    [SettingsField("ip", Except = ["inherit"], Editor = "ipHandling")]
    public IPHandling IP { get; init; } = IPHandling.None;

    /// <summary>Whether the forms count views, starts, pages and the fields that stop visitors; on by default, each form may turn it off.</summary>
    [SettingsField("insights")]
    public bool Insights { get; init; } = true;

    /// <summary>
    /// What a member's deletion in Umbraco does to their entries (ADR-0231): anonymised by default, or deleted; for every
    /// site at once, since a member belongs to no site. Entries a legal hold keeps stay as they are.
    /// </summary>
    [SettingsField("members", Editor = "choiceLines")]
    public MemberDeletion MemberDeleted { get; init; } = MemberDeletion.Anonymise;
}

/// <summary>What a member's deletion in Umbraco does to their entries (ADR-0231), as the privacy tools erase a person (ADR-0223).</summary>
[JsonConverter(typeof(DefinitionEnumConverter<MemberDeletion>))]
internal enum MemberDeletion
{
    /// <summary>
    /// The entries stay with their form, number, state and times, for counts and statistics, and everything else about the
    /// member goes (<c>anonymise</c>, the default).
    /// </summary>
    Anonymise,

    /// <summary>The entries go, with their files and mail (<c>delete</c>).</summary>
    Delete,
}

/// <summary>
/// The days entries are kept in each state (ADR-0009, ADR-0222), counted from the entry's last change of state: 1 to
/// <see cref="MaxDays"/>, or <see cref="ForGood"/> for kept for good, which only a host that sets
/// <c>JironFormBuilder:Privacy:AllowRetentionOff</c> takes. A stored section leaves its nulls out, so kept for good is a
/// number of its own.
/// </summary>
internal sealed record SiteRetention
{
    /// <summary>
    /// The most days a state keeps its entries, as many as a form may set (section 4.3.4): ten years, the longest that
    /// laws on keeping records commonly ask for. Longer is kept for good, which needs the host (ADR-0226).
    /// </summary>
    public const int MaxDays = DefinitionSaveChecks.MaxDays;

    /// <summary>The days of a state kept for good.</summary>
    public const int ForGood = 0;

    /// <summary>Entries submitted, those waiting for approval among them: 365 days by default.</summary>
    [SettingsField("", Unit = true)]
    [Range(ForGood, MaxDays)]
    public int Submitted { get; init; } = 365;

    /// <summary>Approved entries: 365 days by default.</summary>
    [SettingsField("", Order = 1, Unit = true)]
    [Range(ForGood, MaxDays)]
    public int Approved { get; init; } = 365;

    /// <summary>Rejected entries: 90 days by default.</summary>
    [SettingsField("", Order = 2, Unit = true)]
    [Range(ForGood, MaxDays)]
    public int Rejected { get; init; } = 90;

    /// <summary>Entries in Spam: 30 days by default.</summary>
    [SettingsField("", Order = 3, Unit = true)]
    [Range(ForGood, MaxDays)]
    public int Spam { get; init; } = 30;
}

/// <summary>
/// Checks the privacy settings: each state keeps its entries 1 to <see cref="SiteRetention.MaxDays"/> days, or for good (0),
/// and the IP addresses are kept in one of the site's modes, not <c>inherit</c>, which is how a form takes the site's.
/// </summary>
internal sealed class PrivacySettingsValidator : ISettingsValidator<PrivacySettings>
{
    /// <inheritdoc />
    /// <remarks>A section without retention days, as an old row may be, takes the defaults.</remarks>
    public PrivacySettings Normalize(PrivacySettings section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section.Retention is null ? section with { Retention = new SiteRetention() } : section;
    }

    /// <inheritdoc />
    public void Validate(PrivacySettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        var retention = section.Retention ?? new SiteRetention();
        foreach (var (field, days) in new[] { ("submitted", retention.Submitted), ("approved", retention.Approved), ("rejected", retention.Rejected), ("spam", retention.Spam) })
        {
            errors.Between($"retention.{field}", days, SiteRetention.ForGood, SiteRetention.MaxDays);
        }

        if (section.IP is not (IPHandling.None or IPHandling.Anonymised or IPHandling.Full))
        {
            errors.Add("ip", SettingsErrors.IPHandlingCode, "Must be none, anonymised or full: inherit is how a form takes the site's.");
        }
    }
}

/// <summary>
/// The host's rule on keeping entries for good (ADR-0009, ADR-0222): refused unless the host allows it, and a state stored
/// as kept for good reads as its default days.
/// </summary>
/// <param name="options">The host configuration.</param>
internal sealed class RetentionOffRule(IOptions<FormBuilderOptions> options) : ISettingsHostRule<PrivacySettings>
{
    /// <summary>The configuration key behind the rule.</summary>
    public const string ConfigurationKey = FormBuilderOptions.SectionName + ":Privacy:AllowRetentionOff";

    /// <summary>The states whose days the rule holds, as the section's JSON names them.</summary>
    public static readonly IReadOnlyList<string> Fields = ["submitted", "approved", "rejected", "spam"];

    /// <inheritdoc />
    public PrivacySettings Apply(PrivacySettings section, SettingsLocks locks)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(locks);
        if (options.Value.Privacy.AllowRetentionOff)
        {
            return section;
        }

        foreach (var field in Fields)
        {
            locks.Add($"retention.{field}", SettingsLockKind.Restricted, ConfigurationKey);
        }

        var stored = section.Retention ?? new SiteRetention();
        var defaults = new SiteRetention();
        var kept = stored with
        {
            Submitted = Kept(stored.Submitted, defaults.Submitted),
            Approved = Kept(stored.Approved, defaults.Approved),
            Rejected = Kept(stored.Rejected, defaults.Rejected),
            Spam = Kept(stored.Spam, defaults.Spam),
        };
        return kept == section.Retention ? section : section with { Retention = kept };
    }

    // A state kept for good reads as its default days.
    private static int Kept(int days, int byDefault) => days == SiteRetention.ForGood ? byDefault : days;
}

/// <summary>
/// The host's rule on full IP addresses (ADR-0008, ADR-0224): <c>full</c> is refused unless the host allows it, and a stored
/// one reads as the nearest allowed mode, <c>anonymised</c>.
/// </summary>
/// <param name="options">The host configuration.</param>
internal sealed class FullIPRule(IOptions<FormBuilderOptions> options) : ISettingsHostRule<PrivacySettings>
{
    /// <summary>The configuration key behind the rule.</summary>
    public const string ConfigurationKey = FormBuilderOptions.SectionName + ":Privacy:AllowFullIp";

    /// <inheritdoc />
    public PrivacySettings Apply(PrivacySettings section, SettingsLocks locks)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(locks);
        if (options.Value.Privacy.AllowFullIp)
        {
            return section;
        }

        locks.Add("ip", SettingsLockKind.Restricted, ConfigurationKey);
        return section.IP == IPHandling.Full ? section with { IP = IPHandling.Anonymised } : section;
    }
}
