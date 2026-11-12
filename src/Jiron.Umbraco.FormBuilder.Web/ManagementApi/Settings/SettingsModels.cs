using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Settings;

/// <summary>A settings section as its screen shows it (ADR-0218), in one scope: all sites, or one site.</summary>
internal sealed record SettingsSectionResponseModel
{
    /// <summary>The section's alias, such as <c>general</c>.</summary>
    public required string Section { get; init; }

    /// <summary>The site whose values these are, by the key of its root content; null for all sites.</summary>
    public Guid? Site { get; init; }

    /// <summary>The version of the scope's own stored values, 0 before the first save; a save sends it back.</summary>
    public required int Version { get; init; }

    /// <summary>The values the scope has, field by field, as the section's JSON writes them.</summary>
    public required JsonElement Values { get; init; }

    /// <summary>For a site, the values all sites have, which a field the site does not set follows; null for all sites.</summary>
    public JsonElement? GlobalValues { get; init; }

    /// <summary>For a site, the fields it sets of its own; empty for all sites.</summary>
    public required IReadOnlyList<string> Overrides { get; init; }

    /// <summary>The fields a site can set of its own; empty for a section every site shares.</summary>
    public required IReadOnlyList<string> SiteFields { get; init; }

    /// <summary>The sites, which are Umbraco's content roots, for a section a site can set; empty for one every site shares.</summary>
    public required IReadOnlyList<SettingsSiteResponseModel> Sites { get; init; }

    /// <summary>The fields, in the order the screen shows them, in the shape the inspector's editors draw (ADR-0170).</summary>
    public required IReadOnlyList<FieldSettingResponseModel> Fields { get; init; }

    /// <summary>The host's rules, by field: <c>*</c> for the whole section.</summary>
    public required IReadOnlyList<SettingsLockResponseModel> Locks { get; init; }

    /// <summary>What the section's screen says besides its fields; only the part of this section is set.</summary>
    public required SettingsFactsResponseModel Facts { get; init; }
}

/// <summary>A site of the settings: a content root, by its key and name.</summary>
/// <param name="Key">The root content's key, which a site's settings are kept by.</param>
/// <param name="Name">Its name in the content tree.</param>
/// <param name="HasOwnValues">Whether it sets something of the section of its own.</param>
internal sealed record SettingsSiteResponseModel(Guid Key, string Name, bool HasOwnValues);

/// <summary>A save of a settings section's fields in one scope (ADR-0218).</summary>
internal sealed record SettingsSectionRequestModel
{
    /// <summary>The version the values were read at.</summary>
    [Range(0, int.MaxValue)]
    public required int Version { get; init; }

    /// <summary>The site, by the key of its root content; null for all sites.</summary>
    public Guid? Site { get; init; }

    /// <summary>
    /// The fields to save, by name, as the section's JSON writes them. For all sites, every other field stays as stored;
    /// for a site, these are the values of the fields it sets of its own.
    /// </summary>
    public required JsonElement Values { get; init; }

    /// <summary>For a site, every field it sets of its own after the save; the others follow all sites. Ignored for all sites.</summary>
    public IReadOnlyList<string>? Overrides { get; init; }
}

/// <summary>A host rule on a settings field (ADR-0008).</summary>
internal sealed class SettingsLockResponseModel
{
    /// <summary>The field's path within the section, such as <c>history.draftDays</c>; <c>*</c> for the whole section.</summary>
    public required string Field { get; init; }

    /// <summary><c>locked</c>, <c>restricted</c> (some values refused) or <c>clamped</c> (lowered to a limit).</summary>
    public required string Kind { get; init; }

    /// <summary>The configuration key behind it, such as <c>JironFormBuilder:LockedSections:0</c>.</summary>
    public required string ConfigurationKey { get; init; }

    internal static SettingsLockResponseModel Of(string field, SettingsLock rule) => new()
    {
        Field = field,
        Kind = rule.Kind switch
        {
            SettingsLockKind.Locked => "locked",
            SettingsLockKind.Restricted => "restricted",
            _ => "clamped",
        },
        ConfigurationKey = rule.ConfigurationKey,
    };
}

/// <summary>What a section's screen says besides its fields (ADR-0218): each part only for its own section.</summary>
internal sealed record SettingsFactsResponseModel
{
    /// <summary>General's: the address a scope's links start with when it sets none, the themes and the form block.</summary>
    public GeneralFactsResponseModel? General { get; init; }

    /// <summary>Email's: whether Umbraco can send mail, and the layouts.</summary>
    public EmailFactsResponseModel? Email { get; init; }

    /// <summary>Spam protection's: the captchas and their keys.</summary>
    public ProtectionFactsResponseModel? Protection { get; init; }

    /// <summary>Files': the host's caps, the scanners and where files are stored.</summary>
    public FilesFactsResponseModel? Files { get; init; }

    /// <summary>Integrations': the hosts webhooks may call, and which references the host configuration holds.</summary>
    public IntegrationsFactsResponseModel? Integrations { get; init; }

    /// <summary>Privacy's: whether the host allows full IP addresses.</summary>
    public PrivacyFactsResponseModel? Privacy { get; init; }
}

/// <summary>General's facts.</summary>
/// <param name="DefaultBaseUrl">The address links start with when the scope sets none: the site's first <c>https</c> domain, else Umbraco's application URL; null when there is none.</param>
/// <param name="DefaultTheme">The theme forms get while the section names none.</param>
/// <param name="Themes">The themes a site may name: the code's, then the library's, by their aliases.</param>
/// <param name="FormBlock">Whether pages can show forms yet: the form block type.</param>
internal sealed record GeneralFactsResponseModel(
    string? DefaultBaseUrl,
    string DefaultTheme,
    IReadOnlyList<FieldSettingChoiceResponseModel> Themes,
    FormBlockFactsResponseModel FormBlock);

/// <summary>The form block type as Settings, General says it (IA 8, section 10).</summary>
/// <param name="Exists">Whether the element type is there.</param>
/// <param name="CreatedAt">When it was created; null while it is missing.</param>
/// <param name="CanCreate">Whether the user may create it: one with Umbraco's Settings section, where types are made.</param>
internal sealed record FormBlockFactsResponseModel(bool Exists, DateTimeOffset? CreatedAt, bool CanCreate);

/// <summary>Email's facts.</summary>
/// <param name="CanSend">Whether Umbraco can send mail: a mail server or a pickup folder is set up.</param>
/// <param name="SendingKey">The configuration key that sets up sending, which the screen names while it can't.</param>
/// <param name="DefaultLayout">The layout mail gets while the section names none, or one that is gone.</param>
/// <param name="Layouts">The layouts a site may name: the built-in ones, then the library's, by their keys.</param>
internal sealed record EmailFactsResponseModel(bool CanSend, string SendingKey, Guid DefaultLayout, IReadOnlyList<FieldSettingChoiceResponseModel> Layouts);

/// <summary>Spam protection's facts.</summary>
/// <param name="Captchas">The captcha providers the site has, in the order the screen offers them.</param>
/// <param name="ImplicitConsent">Whether the host allows the implicit consent mode (<c>Privacy:AllowImplicitCaptchaConsent</c>).</param>
internal sealed record ProtectionFactsResponseModel(IReadOnlyList<CaptchaKeysResponseModel> Captchas, bool ImplicitConsent);

/// <summary>Privacy's facts (ADR-0224).</summary>
/// <param name="FullIp">Whether the host allows full IP addresses (<c>Privacy:AllowFullIp</c>); the screen greys the choice out otherwise.</param>
internal sealed record PrivacyFactsResponseModel(bool FullIp);

/// <summary>A captcha provider as Spam protection offers it.</summary>
/// <param name="Alias">Its alias, which the section stores.</param>
/// <param name="Name">Its name.</param>
/// <param name="HasKeys">Whether the host configuration holds its keys; without them it can't be chosen.</param>
/// <param name="KeysAt">The configuration key its keys go under, which the screen names while they are missing.</param>
internal sealed record CaptchaKeysResponseModel(string Alias, string Name, bool HasKeys, string KeysAt);

/// <summary>Files' facts: what the host configuration sets, which the screen names, and how files are kept.</summary>
/// <param name="HostMaxFileBytes">The most bytes the host allows any file (<c>Files:MaxFileBytes</c>).</param>
/// <param name="MaxFileBytesKey">That key.</param>
/// <param name="BlockedExtensions">The extensions no field takes, whatever its type (<c>Files:BlockedExtensions</c>).</param>
/// <param name="BlockedExtensionsKey">That key.</param>
/// <param name="Groups">The extensions of each group of types, by the group's value.</param>
/// <param name="Scanners">How many malware scanners are registered; none checks files for malware.</param>
/// <param name="Folder">Whether files are kept in the folder <paramref name="RootKey"/> names, rather than in a store the site registers.</param>
/// <param name="RootKey">The key of the folder files are kept in (<c>Files:Root</c>).</param>
internal sealed record FilesFactsResponseModel(
    long HostMaxFileBytes,
    string MaxFileBytesKey,
    IReadOnlyList<string> BlockedExtensions,
    string BlockedExtensionsKey,
    IReadOnlyList<FileTypeGroupResponseModel> Groups,
    int Scanners,
    bool Folder,
    string RootKey);

/// <summary>A group of file types with its extensions.</summary>
/// <param name="Value">The group, as the section stores it.</param>
/// <param name="Extensions">Its extensions, lowercase without the point.</param>
internal sealed record FileTypeGroupResponseModel(string Value, IReadOnlyList<string> Extensions);

/// <summary>Integrations' facts.</summary>
/// <param name="AllowedHosts">The hosts webhooks may call (<c>Webhooks:AllowedHosts</c>); none by default, so nothing is called.</param>
/// <param name="AllowHttp">Whether webhooks may call plain <c>http</c> addresses (<c>Webhooks:AllowHttp</c>).</param>
/// <param name="Found">The fields whose saved reference the host configuration holds a secret for, by their names.</param>
internal sealed record IntegrationsFactsResponseModel(IReadOnlyList<string> AllowedHosts, bool AllowHttp, IReadOnlyList<string> Found);
