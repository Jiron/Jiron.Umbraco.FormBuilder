namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The general settings (section 4.8, ADR-0008), each field set per site. It holds the time zone so far; the other
/// fields come with what they configure (ADR-0043).
/// </summary>
internal sealed record GeneralSettings : ISettingsSection
{
    /// <summary>The time zone of a site that has not chosen one.</summary>
    public const string DefaultTimeZone = "UTC";

    /// <inheritdoc />
    public static string Alias => FormBuilderSettingsSections.General;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <inheritdoc />
    public static bool PerSite => true;

    /// <summary>
    /// The site's time zone, an IANA id as the server spells it: "today", opening dates and relative dates are in it
    /// (ADR-0005, ADR-0030).
    /// </summary>
    public string TimeZone { get; init; } = DefaultTimeZone;
}

/// <summary>Checks the general settings: the time zone is one the server resolves, spelled as the server spells it.</summary>
internal sealed class GeneralSettingsValidator : ISettingsValidator<GeneralSettings>
{
    /// <inheritdoc />
    public void Validate(GeneralSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        errors.TimeZone("timeZone", section.TimeZone);
    }
}
