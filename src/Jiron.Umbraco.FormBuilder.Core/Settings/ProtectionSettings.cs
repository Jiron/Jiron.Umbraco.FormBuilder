using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The spam protection settings (section 4.8, ADR-0012, ADR-0139, ADR-0140, ADR-0141), set per site: the default level, the
/// blocked address ranges and the content rules.
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

    /// <summary>The easiest puzzle a site may set, which still costs a bot something.</summary>
    public const int FewestProofBits = 8;

    /// <summary>The hardest puzzle a site may set, which a slow phone can still solve.</summary>
    public const int MostProofBits = 24;

    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Protection;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <inheritdoc />
    public static bool PerSite => true;

    /// <summary>The level of the site's forms that inherit it: <c>standard</c>, <c>proofOfWork</c> or <c>captcha</c>, never <c>inherit</c>, which is how a form takes this one.</summary>
    [SettingsField("level", Except = ["inherit"], Editor = "protectionLevel")]
    public ProtectionLevel Level { get; init; } = ProtectionLevel.Standard;

    /// <summary>At the <c>captcha</c> level, the provider of the site's forms that inherit it (ADR-0142).</summary>
    [SettingsField("level", Order = 1, Editor = "captchaProvider")]
    public string? CaptchaProvider { get; init; }

    /// <summary>When a page may load a captcha's script (section 4.10, ADR-0157): <c>explicit</c> by default.</summary>
    [SettingsField("consent", Editor = "captchaConsent", Help = false)]
    public CaptchaConsentMode CaptchaConsent { get; init; } = CaptchaConsentMode.Explicit;

    /// <summary>
    /// In the <c>cookiebot</c> mode, the category whose consent loads the script: <c>necessary</c> by default, as Cookiebot
    /// files a captcha's cookie, or <c>marketing</c> (the owner, ADR-0199).
    /// </summary>
    [SettingsField("consent", Order = 1, Choices = ["necessary", "marketing"])]
    public string CookiebotCategory { get; init; } = "necessary";

    /// <summary>How many zero bits a puzzle of the site's forms asks for; each more doubles the work (ADR-0141).</summary>
    [SettingsField("level", Order = 2, Unit = true)]
    [Range(FewestProofBits, MostProofBits)]
    public int ProofBits { get; init; } = 16;

    /// <summary>How strictly the site's forms that inherit it judge their entries; never <c>inherit</c>, which is how a form takes this one.</summary>
    [SettingsField("content", Except = ["inherit"], Help = false)]
    public ContentRuleLevel ContentRules { get; init; } = ContentRuleLevel.Normal;

    /// <summary>The words and phrases that count against a submission, compared as rules compare texts (ADR-0005), each as words of their own.</summary>
    [SettingsField("content", Order = 1)]
    [MaxLength(MaxBlockedItems)]
    public IReadOnlyList<string> BlockedWords { get; init; } = [];

    /// <summary>The domains whose addresses in an email field put a submission into Spam, with what lies under them, such as <c>example.com</c>.</summary>
    [SettingsField("content", Order = 2)]
    [MaxLength(MaxBlockedItems)]
    public IReadOnlyList<string> BlockedDomains { get; init; } = [];

    /// <summary>The most links a submission's texts may hold before they count against it.</summary>
    [SettingsField("content", Order = 3)]
    [Range(0, MostMaxLinks)]
    public int MaxLinks { get; init; } = 3;

    /// <summary>
    /// The address ranges whose submissions go to Spam, in CIDR notation such as <c>203.0.113.0/24</c> or
    /// <c>2001:db8::/32</c>; matched against the request and never stored.
    /// </summary>
    [SettingsField("ranges")]
    [MaxLength(MaxBlockedRanges)]
    public IReadOnlyList<string> BlockedRanges { get; init; } = [];

    /// <summary>
    /// The strictest of these sections (ADR-0147), for a request that names no page of its form: the highest level and the hardest
    /// puzzle, every range, word and domain, the strictest content rules and the fewest links.
    /// </summary>
    /// <param name="sections">The sections, the first of which names the captcha when several ask for one.</param>
    public static ProtectionSettings Strictest(IReadOnlyList<ProtectionSettings> sections)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var level = sections.Max(section => section.Level);
        return new ProtectionSettings
        {
            Level = level,
            CaptchaProvider = sections.FirstOrDefault(section => section.Level == ProtectionLevel.Captcha)?.CaptchaProvider,
            CaptchaConsent = sections.All(section => section.CaptchaConsent == sections[0].CaptchaConsent && section.CookiebotCategory == sections[0].CookiebotCategory) ? sections[0].CaptchaConsent : CaptchaConsentMode.Explicit,
            CookiebotCategory = sections[0].CookiebotCategory,
            ProofBits = sections.Max(section => section.ProofBits),
            BlockedRanges = [.. sections.SelectMany(section => section.BlockedRanges).Distinct(StringComparer.Ordinal)],
            ContentRules = sections.Max(section => section.ContentRules),
            BlockedWords = [.. sections.SelectMany(section => section.BlockedWords).DistinctBy(TextSemantics.Fold, StringComparer.Ordinal)],
            BlockedDomains = [.. sections.SelectMany(section => section.BlockedDomains).Distinct(StringComparer.Ordinal)],
            MaxLinks = sections.Min(section => section.MaxLinks),
        };
    }

    /// <summary>The level a form is at: its own, or the site's for one that inherits it.</summary>
    /// <param name="definition">The form.</param>
    public ProtectionLevel LevelOf(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Settings.Protection.Level is ProtectionLevel.Inherit ? Level : definition.Settings.Protection.Level;
    }

    /// <summary>The captcha a form asks for: its own at the captcha level, the site's for one that inherits that level; null for none.</summary>
    /// <param name="definition">The form.</param>
    public CaptchaChoice? CaptchaOf(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.Settings.Protection.Level switch
        {
            ProtectionLevel.Captcha => definition.Settings.Protection.Captcha,
            ProtectionLevel.Inherit when Level == ProtectionLevel.Captcha && CaptchaProvider is { } provider => new CaptchaChoice { Provider = provider },
            _ => null,
        };
    }

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

        if (section.Level == ProtectionLevel.Inherit)
        {
            errors.Add(nameof(ProtectionSettings.Level), SettingsErrors.ProtectionLevelCode, "A site's protection level is standard, proofOfWork or captcha.");
        }

        if ((section.Level == ProtectionLevel.Captcha) == string.IsNullOrWhiteSpace(section.CaptchaProvider))
        {
            errors.Add(nameof(ProtectionSettings.CaptchaProvider), SettingsErrors.ProtectionLevelCode, "A site names a captcha provider at the captcha level, and only there.");
        }

        if (section.CookiebotCategory is not ("necessary" or "marketing"))
        {
            errors.Add(nameof(ProtectionSettings.CookiebotCategory), SettingsErrors.ProtectionLevelCode, "A Cookiebot category is necessary or marketing.");
        }

        if (section.ProofBits is < ProtectionSettings.FewestProofBits or > ProtectionSettings.MostProofBits)
        {
            errors.Add(nameof(ProtectionSettings.ProofBits), SettingsErrors.Range, $"A puzzle asks for {ProtectionSettings.FewestProofBits} to {ProtectionSettings.MostProofBits} zero bits.");
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

/// <summary>When a page may load a captcha's script (section 4.10, ADR-0157).</summary>
[JsonConverter(typeof(DefinitionEnumConverter<CaptchaConsentMode>))]
internal enum CaptchaConsentMode
{
    /// <summary>Once the visitor agrees on the form: its notice's button, or a consent field that says so (<c>explicit</c>, the default).</summary>
    Explicit,

    /// <summary>Once Cookiebot has the visitor's consent to the site's chosen category (<c>cookiebot</c>).</summary>
    Cookiebot,

    /// <summary>Once the site's consent manager says so, through <c>JironFormBuilder.consent.grant('captcha')</c> (<c>cmp</c>).</summary>
    Cmp,

    /// <summary>With the form, where the host allows it (<c>implicit</c>, <c>Privacy:AllowImplicitCaptchaConsent</c>).</summary>
    Implicit,
}

/// <summary>
/// The host's rule on the captcha's consent (ADR-0008, ADR-0157): the <c>implicit</c> mode is refused unless the host allows
/// it, and a stored one reads as the nearest allowed one, <c>explicit</c>.
/// </summary>
/// <param name="options">The host configuration.</param>
internal sealed class ImplicitCaptchaConsentRule(IOptions<FormBuilderOptions> options) : ISettingsHostRule<ProtectionSettings>
{
    /// <summary>The configuration key behind the rule.</summary>
    public const string ConfigurationKey = FormBuilderOptions.SectionName + ":Privacy:AllowImplicitCaptchaConsent";

    /// <inheritdoc />
    public ProtectionSettings Apply(ProtectionSettings section, SettingsLocks locks)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(locks);
        if (options.Value.Privacy.AllowImplicitCaptchaConsent)
        {
            return section;
        }

        locks.Add("captchaConsent", SettingsLockKind.Restricted, ConfigurationKey);
        return section.CaptchaConsent == CaptchaConsentMode.Implicit ? section with { CaptchaConsent = CaptchaConsentMode.Explicit } : section;
    }
}
