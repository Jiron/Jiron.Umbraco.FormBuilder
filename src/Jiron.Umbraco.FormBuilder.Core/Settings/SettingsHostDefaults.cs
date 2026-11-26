using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The host configuration's defaults of the backoffice settings (27.1): a value the configuration gives for a field that
/// nothing stored names, for every site or for the one read, in place of the package's own default. A stored value wins,
/// a site's choice of the package's default among them, which its row keeps as a null; the defaults are no lock, so the
/// backoffice shows and saves them as any value.
/// </summary>
internal static class SettingsHostDefaults
{
    /// <summary>The stored name of the General settings' time zone.</summary>
    public const string TimeZoneField = "timeZone";

    /// <summary>The section with the host's defaults in the fields the stored rows do not name.</summary>
    /// <param name="section">The section as the stored rows and the package's defaults make it.</param>
    /// <param name="global">The global row's JSON.</param>
    /// <param name="site">The site row's JSON, or null.</param>
    /// <param name="host">The host configuration.</param>
    public static T Apply<T>(T section, JsonObject global, JsonObject? site, FormBuilderOptions host)
        where T : ISettingsSection
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(host);
        if (section is GeneralSettings general && host.General.DefaultTimeZone is { Length: > 0 } zone && !Names(global, site, TimeZoneField))
        {
            return (T)(object)(general with { TimeZone = zone });
        }

        return section;
    }

    // Whether a stored row names the field: the global row with a value, or the site row at all, as a site that chose the
    // package's default stores a null.
    private static bool Names(JsonObject global, JsonObject? site, string field) =>
        global[field] is not null || (site?.ContainsKey(field) ?? false);
}
