using System.Text.Json.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>The form settings (section 4.3.4, ADR-0002), grouped as the designer shows them.</summary>
public sealed record FormSettings
{
    /// <summary>Labels, the summary page, progress, storage, approval, verification, resume and entry naming.</summary>
    public FormSubmissionSettings Submission { get; init; } = new();

    /// <summary>Opening and closing, the entry limit, the closed message and members.</summary>
    public FormAvailabilitySettings Availability { get; init; } = new();

    /// <summary>Spam protection (ADR-0012).</summary>
    public FormProtectionSettings Protection { get; init; } = new();

    /// <summary>Retention, IP handling and insights (ADR-0009).</summary>
    public FormPrivacySettings Privacy { get; init; } = new();

    /// <summary>The theme and how the form looks (ADR-0015).</summary>
    public FormAppearanceSettings Appearance { get; init; } = new();

    /// <summary>Headless access, query values and context values.</summary>
    public FormDeveloperSettings Developer { get; init; } = new();
}

/// <summary>How the form is sent and what happens to its entries.</summary>
public sealed record FormSubmissionSettings
{
    /// <summary>The submit button's label; absent means the default label.</summary>
    public LocalizedText? SubmitLabel { get; init; }

    /// <summary>The "next page" button's label.</summary>
    public LocalizedText? NextLabel { get; init; }

    /// <summary>The "previous page" button's label.</summary>
    public LocalizedText? BackLabel { get; init; }

    /// <summary>Whether a "check your answers" page comes before sending.</summary>
    public bool SummaryPage { get; init; }

    /// <summary>How a form with several pages shows progress.</summary>
    public ProgressIndicator Progress { get; init; } = ProgressIndicator.Steps;

    /// <summary>Whether entries are stored.</summary>
    public EntryStorage Store { get; init; } = EntryStorage.Always;

    /// <summary>For <see cref="EntryStorage.Days"/> only: how many days entries are kept, 1 to 36,500.</summary>
    public int? StoreDays { get; init; }

    /// <summary>Whether entries wait for approval.</summary>
    public ApprovalMode Approval { get; init; } = ApprovalMode.Automatic;

    /// <summary>Email verification (double opt-in) before the actions run; absent means off.</summary>
    public EmailVerification? Verification { get; init; }

    /// <summary>Save and resume; absent means off.</summary>
    public SaveAndResume? Resume { get; init; }

    /// <summary>A merge-tag line that names each entry in the lists; sensitive fields render empty in it.</summary>
    public string? EntryTitle { get; init; }

    /// <summary>The format of the entry reference, for example <c>REQ-2026-00042</c>.</summary>
    public EntryReferenceFormat? EntryReference { get; init; }
}

/// <summary>Email verification: the entry waits until the visitor confirms the address in a field.</summary>
public sealed record EmailVerification
{
    /// <summary>The key of the email field whose address is verified.</summary>
    public required Guid EmailField { get; init; }

    /// <summary>How many days the confirmation link stays valid, 1 to 90.</summary>
    public int Days { get; init; } = 7;
}

/// <summary>Save and resume: the visitor can save their answers and finish later through a link.</summary>
public sealed record SaveAndResume
{
    /// <summary>How many days the link stays valid, 1 to 365.</summary>
    public int Days { get; init; } = 30;
}

/// <summary>The format of an entry reference: a prefix, the year and the padded entry number.</summary>
public sealed record EntryReferenceFormat
{
    /// <summary>The text before the number, for example <c>REQ-</c>.</summary>
    public string? Prefix { get; init; }

    /// <summary>Whether the year of submission comes after the prefix.</summary>
    [JsonPropertyName("year")]
    public bool IncludeYear { get; init; } = true;

    /// <summary>How many digits the number is padded to, 1 to 10.</summary>
    public int Digits { get; init; } = 5;
}

/// <summary>When and for whom the form is open.</summary>
public sealed record FormAvailabilitySettings
{
    /// <summary>The instant the form opens, in UTC.</summary>
    public DateTime? OpensAt { get; init; }

    /// <summary>The instant the form closes, in UTC.</summary>
    public DateTime? ClosesAt { get; init; }

    /// <summary>The number of entries after which the form closes.</summary>
    public int? MaxEntries { get; init; }

    /// <summary>The message a closed form shows.</summary>
    public LocalizedHtml? ClosedMessage { get; init; }

    /// <summary>Who may fill in the form.</summary>
    [JsonPropertyName("members")]
    public MemberAccess Access { get; init; } = MemberAccess.Anyone;

    /// <summary>For <see cref="MemberAccess.Groups"/>: the keys of the member groups.</summary>
    public IReadOnlyList<Guid> MemberGroups { get; init => field = DefinitionList.Of(value); } = DefinitionList<Guid>.Empty;

    /// <summary>Whether a member may send one entry only.</summary>
    public bool OnePerMember { get; init; }
}

/// <summary>Spam protection for the form (ADR-0012).</summary>
public sealed record FormProtectionSettings
{
    /// <summary>The protection level.</summary>
    public ProtectionLevel Level { get; init; } = ProtectionLevel.Inherit;

    /// <summary>For <see cref="ProtectionLevel.Captcha"/>: the provider and its settings.</summary>
    public CaptchaChoice? Captcha { get; init; }

    /// <summary>How strictly the content rules judge entries.</summary>
    public ContentRuleLevel ContentRules { get; init; } = ContentRuleLevel.Inherit;
}

/// <summary>A captcha provider chosen for the form.</summary>
public sealed record CaptchaChoice
{
    /// <summary>The provider's alias.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's settings for this form; keys live in host configuration only.</summary>
    public SettingsObject? Settings { get; init; }
}

/// <summary>Privacy settings for the form's entries (ADR-0009).</summary>
public sealed record FormPrivacySettings
{
    /// <summary>How many days entries are kept in each state; absent means the site's default.</summary>
    public RetentionDaysByState RetentionDays { get; init; } = new();

    /// <summary>What entries keep of the visitor's IP address.</summary>
    public IPHandling IP { get; init; } = IPHandling.Inherit;

    /// <summary>Whether the form's entries count in insights.</summary>
    public bool Insights { get; init; } = true;
}

/// <summary>Retention in days per entry state, 1 to 36,500; absent means the site's default for that state.</summary>
public sealed record RetentionDaysByState
{
    /// <summary>Submitted entries.</summary>
    public int? Submitted { get; init; }

    /// <summary>Approved entries.</summary>
    public int? Approved { get; init; }

    /// <summary>Rejected entries.</summary>
    public int? Rejected { get; init; }

    /// <summary>Entries in Spam.</summary>
    public int? Spam { get; init; }
}

/// <summary>How the form looks.</summary>
public sealed record FormAppearanceSettings
{
    /// <summary>The alias of the form's theme; absent means the site's default theme.</summary>
    public string? Theme { get; init; }

    /// <summary>How required and optional fields are marked.</summary>
    public RequiredMarker RequiredMarker { get; init; } = RequiredMarker.Inherit;

    /// <summary>Whether a summary of the errors shows above the form.</summary>
    public bool ValidationSummary { get; init; } = true;

    /// <summary>How choice fields lay out their options unless a field says otherwise.</summary>
    public ChoiceLayout ChoiceLayout { get; init; } = ChoiceLayout.Vertical;

    /// <summary>A CSS class for the form's wrapper.</summary>
    public string? CssClass { get; init; }
}

/// <summary>Settings for developers.</summary>
public sealed record FormDeveloperSettings
{
    /// <summary>Whether the public definition endpoint serves this form (headless sites).</summary>
    public bool Headless { get; init; }

    /// <summary>The query values the form may read, for prefill and rules.</summary>
    public IReadOnlyList<string> QueryValues { get; init => field = DefinitionList.Of(value); } = DefinitionList<string>.Empty;

    /// <summary>The context values a developer passes that entries store.</summary>
    public IReadOnlyList<string> ContextValues { get; init => field = DefinitionList.Of(value); } = DefinitionList<string>.Empty;
}
