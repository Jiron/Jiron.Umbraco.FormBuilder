namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The aliases of the backoffice settings sections (section 4.8, ADR-0008): the key of a section in storage, in
/// errors, in the management API and in <c>JironFormBuilder:LockedSections</c>.
/// </summary>
public static class FormBuilderSettingsSections
{
    /// <summary>The default theme, labels, new-form defaults, history, and per site the base URL and the time zone.</summary>
    public const string General = "general";

    /// <summary>The sender, the default layout and the team addresses, per site.</summary>
    public const string Email = "email";

    /// <summary>Spam protection: the default level, content rules and blocked address ranges.</summary>
    public const string Protection = "protection";

    /// <summary>Allowed file types and sizes.</summary>
    public const string Files = "files";

    /// <summary>Retention, IP handling, sensitive fields, captcha consent and insights.</summary>
    public const string Privacy = "privacy";

    /// <summary>Webhook signing, Slack and Teams defaults, and Umbraco webhook events.</summary>
    public const string Integrations = "integrations";

    /// <summary>
    /// Who holds which permission, globally and per folder or form. It is stored in <c>jfbPermission</c>, not with the
    /// sections (ADR-0013, ADR-0048), and <c>JironFormBuilder:LockedSections</c> takes it like a section.
    /// </summary>
    public const string Access = "access";

    /// <summary>Every section alias, in the order the settings screen shows them.</summary>
    public static IReadOnlyList<string> All { get; } = [General, Email, Protection, Files, Privacy, Integrations];

    /// <summary>Every alias <c>JironFormBuilder:LockedSections</c> takes: the sections and <see cref="Access"/>.</summary>
    public static IReadOnlyList<string> Lockable { get; } = [General, Email, Protection, Files, Privacy, Access, Integrations];
}
