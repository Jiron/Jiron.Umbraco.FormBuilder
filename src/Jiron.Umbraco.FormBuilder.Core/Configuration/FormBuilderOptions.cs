using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>
/// Jiron Form Builder's host configuration: the <c>JironFormBuilder</c> section of appsettings (section 4.8, ADR-0008,
/// ADR-0042). It is for developers and for locks, and it wins over the backoffice. It is read once and checked when
/// the site starts: a value out of range or a key Jiron Form Builder does not know stops the site with a message that
/// names the key, and a change takes effect when the site restarts.
/// </summary>
public sealed class FormBuilderOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "JironFormBuilder";

    /// <summary>The public API visitors' browsers and headless sites call: where it lives and who may call it.</summary>
    public FormBuilderPublicApiOptions PublicApi { get; } = new();

    /// <summary>How the forms placed on the site's pages are written.</summary>
    public FormBuilderRenderingOptions Rendering { get; } = new();

    /// <summary>Where uploaded files are kept, how large one may be, and which extensions no field takes.</summary>
    public FormBuilderFilesOptions Files { get; } = new();

    /// <summary>Whether actions and email go out (section 4.4).</summary>
    public FormBuilderActionsOptions Actions { get; } = new();

    /// <summary>Whether published forms are also kept as JSON files for source control, and where (ADR-0220).</summary>
    public FormBuilderDefinitionsOptions Definitions { get; } = new();

    /// <summary>Hard limits that editors cannot exceed.</summary>
    public FormBuilderLimitsOptions Limits { get; } = new();

    /// <summary>Settings of field types and action types that the designer cannot change.</summary>
    public FormBuilderDesignerOptions Designer { get; } = new();

    /// <summary>Who holds which permission: the global grants, set from the host (ADR-0048).</summary>
    public FormBuilderAccessOptions Access { get; } = new();

    /// <summary>The keys of the captcha providers (ADR-0012, ADR-0142), which live only here.</summary>
    public FormBuilderCaptchaOptions Captcha { get; } = new();

    /// <summary>What the host allows of the settings that touch a visitor's privacy (ADR-0008).</summary>
    public FormBuilderPrivacyOptions Privacy { get; } = new();

    /// <summary>
    /// The secrets settings may name by reference, as <c>$JironFormBuilder:Secrets:&lt;Name&gt;</c>: each name is 1 to 64 letters, digits
    /// and '_', and its value lives only here, never in the database or a screen (ADR-0008).
    /// </summary>
    public IDictionary<string, string?> Secrets { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>The other hosts Jiron Form Builder may call: JSON option lists and webhooks (ADR-0076, ADR-0161).</summary>
    public FormBuilderWebhooksOptions Webhooks { get; } = new();

    /// <summary>
    /// Backoffice settings sections that are read-only: <c>general</c>, <c>email</c>, <c>protection</c>, <c>files</c>,
    /// <c>privacy</c>, <c>access</c> (every grant and restriction) or <c>integrations</c>, in any case.
    /// </summary>
    public IList<string> LockedSections { get; } = [];
}

/// <summary>What the host allows of the settings that touch a visitor's privacy (ADR-0008).</summary>
public sealed class FormBuilderPrivacyOptions
{
    /// <summary>
    /// Whether the site may keep entries of a state for good, without retention (ADR-0009, ADR-0222). Off by default, and a
    /// stored "for good" then reads as the default days of its state.
    /// </summary>
    public bool AllowRetentionOff { get; set; }

    /// <summary>
    /// Whether entries may keep visitors' full IP addresses (ADR-0009, ADR-0224). Off by default, and a site or form that
    /// stores <c>full</c> then keeps them anonymised.
    /// </summary>
    public bool AllowFullIp { get; set; }

    /// <summary>
    /// Whether a site may load a captcha's script without asking its visitors, the <c>implicit</c> consent mode (section 4.10,
    /// ADR-0157). Off by default, and a stored <c>implicit</c> then reads as <c>explicit</c>.
    /// </summary>
    public bool AllowImplicitCaptchaConsent { get; set; }
}

/// <summary>The keys of the captcha providers (ADR-0012, ADR-0142). A provider without both of its keys is not configured.</summary>
public sealed class FormBuilderCaptchaOptions
{
    /// <summary>reCAPTCHA v2's keys.</summary>
    public FormBuilderCaptchaKeys RecaptchaV2 { get; } = new();

    /// <summary>reCAPTCHA v3's keys.</summary>
    public FormBuilderCaptchaKeys RecaptchaV3 { get; } = new();

    /// <summary>hCaptcha's keys.</summary>
    public FormBuilderCaptchaKeys Hcaptcha { get; } = new();

    /// <summary>Whether reCAPTCHA is reached through <c>recaptcha.net</c> instead of <c>google.com</c> (section 4.10).</summary>
    public bool UseRecaptchaNet { get; set; }
}

/// <summary>One captcha provider's keys.</summary>
public sealed class FormBuilderCaptchaKeys
{
    /// <summary>The public key a page shows the captcha with.</summary>
    public string? SiteKey { get; set; }

    /// <summary>The secret key tokens are verified with; it never leaves the server except to the provider.</summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Another address to verify tokens at, for a test host's stub; null for the provider's own. An https address, or an http
    /// one on this machine, as the secret key goes with each token.
    /// </summary>
    public string? VerifyAddress { get; set; }
}

/// <summary>How the forms placed on the site's pages are written (section 4.8).</summary>
public sealed class FormBuilderRenderingOptions
{
    /// <summary>
    /// Whether a form placed on a page writes the tags that load the website runtime, the base rules and its theme's
    /// stylesheet, once per page, when its placement does not say: true by default. Turned off, a placement writes them
    /// only when it asks (<c>assets="true"</c> on <c>&lt;jfb-render&gt;</c>, <c>assets = true</c> for the view component),
    /// and the site loads them itself, with a <c>&lt;jfb-assets&gt;</c> in its layout for example.
    /// </summary>
    public bool IncludeAssetsAutomatically { get; set; } = true;
}

/// <summary>
/// The other hosts Jiron Form Builder may call (section 4.10, ADR-0076): JSON option lists now, webhooks later. Only
/// the host configuration names them, so editors cannot add one, and an address that resolves to a private, loopback or
/// link-local address is refused whatever this allows.
/// </summary>
public sealed class FormBuilderWebhooksOptions
{
    /// <summary>
    /// The hosts that may be called, each a host name such as <c>events.example.com</c> or an IP address, in any case,
    /// with no scheme, path or wildcard. A host alone is called on its scheme's standard port only (443, or 80 for
    /// <c>http</c>); to call it on another, name the port too: <c>events.example.com:8443</c>, or
    /// <c>[2001:db8::1]:8443</c> for an IPv6 address (ADR-0082). None by default, so nothing is called.
    /// </summary>
    public IList<string> AllowedHosts { get; } = [];

    /// <summary>Whether <c>http</c> addresses may be called too; only <c>https</c> by default.</summary>
    public bool AllowHttp { get; set; }
}

/// <summary>Hard limits that editors cannot exceed (section 4.8).</summary>
public sealed class FormBuilderLimitsOptions
{
    /// <summary>
    /// How large a form may be. Each limit is at least 1 and at most ten times its default. A lowered limit refuses to
    /// save a form above it, and forms stored above it keep working.
    /// </summary>
    public DefinitionLimits Definitions { get; } = new();

    /// <summary>How much mail may leave for addresses that visitors type, and how large a mail's attachments may be (ADR-0012, ADR-0159).</summary>
    public FormBuilderMailLimitsOptions Mail { get; } = new();

    /// <summary>The highest a rate limit may be.</summary>
    public const int HighestRate = 10_000_000;

    /// <summary>The fewest windows a server may keep.</summary>
    public const int LeastTrackedClients = 1_000;

    /// <summary>The most times an address's limits a server key may get.</summary>
    public const int HighestServerKeyFactor = 1_000;

    /// <summary>
    /// The most rate windows one server keeps for addresses, server keys and forms: 1,000 to 10,000,000, 100,000 by
    /// default. Past that, new ones share one overflow window per limit, so a flood of distinct addresses is limited and
    /// memory stays bounded (ADR-0012).
    /// </summary>
    public int MaxTrackedClients { get; set; } = 100_000;

    /// <summary>
    /// How many times an address's limits a server key gets, since one server sends for all its visitors: 1 to 1,000, 10
    /// by default. The limits of a form and of the site stay as they are (ADR-0137).
    /// </summary>
    public int ServerKeyFactor { get; set; } = 10;

    /// <summary>Reading a form, its markup and its options, and a post without JavaScript that only moves the visitor.</summary>
    public FormBuilderRatePolicyOptions Read { get; } = new() { PerAddressPerMinute = 120, SitePerSecond = 200 };

    /// <summary>Asking for a challenge.</summary>
    public FormBuilderRatePolicyOptions Challenge { get; } = new() { PerAddressPerMinute = 30, PerAddressAndFormPerMinute = 10, SitePerSecond = 100 };

    /// <summary>Sending a form, checking a page, saving a draft, asking for a receipt, and any other post without JavaScript.</summary>
    public FormBuilderRatePolicyOptions Submit { get; } = new() { PerAddressPerMinute = 20, PerAddressPerHour = 100, PerAddressAndFormPerMinute = 5, PerFormPerMinute = 600, SitePerSecond = 50 };

    /// <summary>Uploading a file.</summary>
    public FormBuilderRatePolicyOptions Upload { get; } = new() { PerAddressPerMinute = 30, PerAddressPerHour = 200, PerAddressAndFormPerMinute = 20, PerFormPerMinute = 600, SitePerSecond = 20 };

    /// <summary>The insights beacon.</summary>
    public FormBuilderRatePolicyOptions Events { get; } = new() { PerAddressPerMinute = 120, SitePerSecond = 500 };

    /// <summary>Confirming an address.</summary>
    public FormBuilderRatePolicyOptions Verify { get; } = new() { PerAddressPerMinute = 10 };
}

/// <summary>
/// One rate-limit policy's limits (ADR-0012), each counted on each server: 0 to 10,000,000, where 0 leaves that limit out.
/// A request over any of them answers <c>429 rate_limited</c> with <c>Retry-After</c>.
/// </summary>
public sealed class FormBuilderRatePolicyOptions
{
    /// <summary>Requests per minute from one address, or one server key.</summary>
    public int PerAddressPerMinute { get; set; }

    /// <summary>Requests per hour from one address, or one server key.</summary>
    public int PerAddressPerHour { get; set; }

    /// <summary>Requests per minute from one address, or one server key, about one form.</summary>
    public int PerAddressAndFormPerMinute { get; set; }

    /// <summary>Requests per minute about one form, from anyone.</summary>
    public int PerFormPerMinute { get; set; }

    /// <summary>Requests per second to the whole site, from anyone; whatever the other limits do, this one holds.</summary>
    public int SitePerSecond { get; set; }
}

/// <summary>The limits of mail (section 4.10, ADR-0012, ADR-0159).</summary>
public sealed class FormBuilderMailLimitsOptions
{
    /// <summary>The highest a limit of mail may be.</summary>
    public const int HighestCount = 1_000_000;

    /// <summary>The most bytes of attachments the host may allow on one mail.</summary>
    public const long HighestAttachmentBytes = 100L * 1024 * 1024;

    /// <summary>
    /// How many mails one address that visitors type may get in 24 hours, across all forms: 1 to 1,000,000, 3 by default. Every
    /// spelling of a mailbox counts as one. A mail over a limit is stored as limited and never sent.
    /// </summary>
    public int PerRecipientPerDay { get; set; } = 3;

    /// <summary>How many mails may leave for addresses that visitors type in an hour, on this installation: 1 to 1,000,000, 200 by default.</summary>
    public int PerSitePerHour { get; set; } = 200;

    /// <summary>
    /// How many bytes of attachments one mail may carry as they travel, a third more than the files: 1 to 104,857,600, 20 MB by
    /// default, since mail servers refuse larger mail. A file past it is left out, and the mail goes without it.
    /// </summary>
    public long AttachmentBytes { get; set; } = 20L * 1024 * 1024;
}

/// <summary>Whether actions and email go out (section 4.4, ADR-0007).</summary>
public sealed class FormBuilderActionsOptions
{
    /// <summary>
    /// Holds back every action run and email: entries are still stored and nothing is lost, and all of it goes out once this
    /// is off. It is meant for a copy of a live database restored onto a test server, and for incidents. Unlike the other
    /// options, a change takes effect at once.
    /// </summary>
    public bool Paused { get; set; }
}

/// <summary>
/// Forms as files (ADR-0220): with <see cref="Mirror"/> on, each published form is also written to a JSON file in
/// <see cref="Folder"/>, and a file changed in source control is imported as a new version.
/// </summary>
public sealed class FormBuilderDefinitionsOptions
{
    /// <summary>The configuration key of <see cref="Folder"/>.</summary>
    public const string FolderKey = FormBuilderOptions.SectionName + ":Definitions:" + nameof(Folder);

    /// <summary>The configuration key of <see cref="Mirror"/>.</summary>
    public const string MirrorKey = FormBuilderOptions.SectionName + ":Definitions:" + nameof(Mirror);

    /// <summary>The folder by default, under the content root and outside the web root.</summary>
    public const string DefaultFolder = "~/umbraco/JironFormBuilder/forms";

    /// <summary>
    /// Writes each published form to <c>&lt;alias&gt;.json</c> and reads the folder back on a background pass, so teams that deploy
    /// through source control review form changes as files; off by default.
    /// </summary>
    public bool Mirror { get; set; }

    /// <summary>
    /// Where the files are: <c>~/</c> and a path relative to the content root, or an absolute path, never inside the web root or
    /// around it; read only while <see cref="Mirror"/> is on.
    /// </summary>
    public string Folder { get; set; } = DefaultFolder;
}

/// <summary>Locks on the settings that editors give fields and actions in the designer (section 4.8).</summary>
public sealed class FormBuilderDesignerOptions
{
    /// <summary>
    /// Settings fixed for every form, for example the sender of every email action. Each names a kind, a type and a
    /// setting once.
    /// </summary>
    public IList<DesignerSettingLock> SettingLocks { get; } = [];
}

/// <summary>
/// A setting of a field type or an action type that is fixed in every form, for example
/// <c>{ "kind": "action", "type": "email", "setting": "from", "value": "noreply@example.com", "hidden": true }</c>. The
/// designer shows it read-only, or not at all when hidden, a save that gives it another value is refused, and the site
/// uses the locked value whatever a form says (ADR-0060). The site does not start when a field type or its setting is
/// missing, or the setting refuses the value.
/// </summary>
public sealed class DesignerSettingLock
{
    /// <summary>The <see cref="Kind"/> of a lock on a field type's setting.</summary>
    public const string FieldKind = "field";

    /// <summary>The <see cref="Kind"/> of a lock on an action type's setting.</summary>
    public const string ActionKind = "action";

    /// <summary>Whether the lock is on a field type (<c>field</c>) or an action type (<c>action</c>), in any case.</summary>
    [AllowedValues(FieldKind, ActionKind)]
    public string? Kind { get; set; }

    /// <summary>The type's alias, for example <c>email</c>: a letter, then letters, digits, '.', '-' and '_', at most 64 characters.</summary>
    public string? Type { get; set; }

    /// <summary>The setting's name at the top of the type's settings, for example <c>from</c>, spelt exactly as the type names it.</summary>
    public string? Setting { get; set; }

    /// <summary>
    /// The value every form uses, as text, at most 50,000 characters: a text, a number, a date, a time, a choice or a key
    /// as it is, true or false, a whole number as digits, a localised text as the text of every culture, and a list, an
    /// object or JSON written as JSON. Without one, the setting keeps its default.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>Hides the setting in the designer instead of showing it read-only.</summary>
    public bool Hidden { get; set; }
}

