namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The general settings (section 4.8, ADR-0008), set per site unless a field says otherwise. It holds the time zone and
/// the new-form defaults so far; the other fields come with what they configure (ADR-0043).
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

    /// <summary>What a form created from blank starts with; every site shares it, since a new form belongs to no site (ADR-0044).</summary>
    [GlobalOnly]
    public NewFormDefaults NewForms { get; init; } = new();
}

/// <summary>What a form created from blank starts with (section 4.8, ADR-0044).</summary>
internal sealed record NewFormDefaults
{
    /// <summary>A required consent field at the end of the first group, or null for none (the default).</summary>
    public NewFormConsent? Consent { get; init; }
}

/// <summary>
/// The consent field a new form starts with: the site's own wording, which no default could supply (ADR-0044). Each text
/// maps cultures to texts, the culture <c>""</c> holding the default, as a definition's texts do (ADR-0002).
/// </summary>
internal sealed record NewFormConsent
{
    /// <summary>The field's alias in a new form.</summary>
    public const string FieldAlias = "consent";

    /// <summary>The field's label: one line of at most 500 characters per culture.</summary>
    public IReadOnlyDictionary<string, string> Label { get; init; } = new Dictionary<string, string>();

    /// <summary>The wording beside the box, rich text of at most 50,000 characters per culture.</summary>
    public IReadOnlyDictionary<string, string> Text { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Checks the general settings: the time zone is one the server resolves, spelled as the server spells it, and the
/// consent default's texts are texts a definition takes.
/// </summary>
internal sealed class GeneralSettingsValidator : ISettingsValidator<GeneralSettings>
{
    /// <inheritdoc />
    public void Validate(GeneralSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        errors.TimeZone("timeZone", section.TimeZone);
        if (section.NewForms?.Consent is { } consent)
        {
            errors.Text("newForms.consent.label", consent.Label, html: false);
            errors.Text("newForms.consent.text", consent.Text, html: true);
        }
    }
}
