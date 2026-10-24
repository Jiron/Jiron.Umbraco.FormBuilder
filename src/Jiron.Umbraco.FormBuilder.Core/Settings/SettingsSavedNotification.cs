using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// Published on the saving server after a backoffice settings section was saved and the save committed (ADR-0008,
/// ADR-0043). It names what changed, never a value. A handler that fails is logged; the save stands.
/// </summary>
public sealed class SettingsSavedNotification : INotification
{
    /// <summary>Creates the notification.</summary>
    /// <param name="section">The section's alias, one of <see cref="FormBuilderSettingsSections"/>.</param>
    /// <param name="siteKey">The key of the site's root content, or null for the values every site shares.</param>
    /// <param name="version">The section's new version in its scope.</param>
    /// <param name="userKey">The backoffice user who saved it, or null when Jiron Form Builder did.</param>
    /// <param name="changedFields">The dotted paths of the fields that changed.</param>
    public SettingsSavedNotification(string section, Guid? siteKey, int version, Guid? userKey, IReadOnlyList<string> changedFields)
    {
        ArgumentException.ThrowIfNullOrEmpty(section);
        ArgumentNullException.ThrowIfNull(changedFields);
        Section = section;
        SiteKey = siteKey;
        Version = version;
        UserKey = userKey;
        ChangedFields = changedFields;
    }

    /// <summary>The section's alias, one of <see cref="FormBuilderSettingsSections"/>.</summary>
    public string Section { get; }

    /// <summary>The key of the site's root content, or null for the values every site shares.</summary>
    public Guid? SiteKey { get; }

    /// <summary>
    /// The section's new version in its scope; 0 when the site no longer overrides any field and follows the global
    /// values again.
    /// </summary>
    public int Version { get; }

    /// <summary>The backoffice user who saved it, or null when Jiron Form Builder did.</summary>
    public Guid? UserKey { get; }

    /// <summary>The dotted paths of the fields that changed, such as <c>timeZone</c>; never their values.</summary>
    public IReadOnlyList<string> ChangedFields { get; }
}
