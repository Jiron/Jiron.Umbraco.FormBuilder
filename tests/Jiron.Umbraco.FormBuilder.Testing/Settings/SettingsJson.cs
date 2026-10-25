using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Testing.Settings;

/// <summary>The JSON of a settings section's global row, as the settings service writes it, for tests that seed or compare one.</summary>
internal static class SettingsJson
{
    /// <summary>The JSON of a global row: every field, nulls left out (ADR-0043).</summary>
    public static string Global<T>(T section)
        where T : ISettingsSection => JsonSerializer.Serialize(section, SettingsSectionJson.Options);
}
