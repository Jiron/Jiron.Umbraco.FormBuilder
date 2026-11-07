namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The privacy settings (ADR-0008, ADR-0009), the same for every site, on Privacy, Data collection: whether the forms
/// count what visitors do with them (ADR-0211). The rest of the screen's settings come with their features.
/// </summary>
internal sealed record PrivacySettings : ISettingsSection
{
    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.Privacy;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <summary>Whether the forms count views, starts, pages and the fields that stop visitors; on by default, each form may turn it off.</summary>
    public bool Insights { get; init; } = true;
}
