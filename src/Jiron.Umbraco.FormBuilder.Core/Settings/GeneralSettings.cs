using System.ComponentModel.DataAnnotations;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// The general settings (section 4.8, ADR-0008), set per site unless a field says otherwise. It holds the time zone, the
/// base URL, the required-marker style, the buttons' default labels and the default theme, the new-form defaults and how
/// long draft saves stay so far; the other fields come with what they configure (ADR-0043).
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
    /// The alias of the theme the site's forms take when neither the form nor its placement names one (ADR-0015,
    /// ADR-0105); null for <c>default</c>. It is not held to the registered themes: one the site no longer has renders as
    /// <c>default</c>.
    /// </summary>
    [SettingsField("forms", Editor = "theme", Help = false)]
    public string? Theme { get; init; }

    /// <summary>
    /// How the site's forms whose marker is <c>inherit</c> mark their fields (ADR-0093, ADR-0095): <c>optional</c> (the
    /// default) adds "(optional)" to each field that needs no answer, <c>required</c> an asterisk to each that needs one,
    /// <c>none</c> nothing. A site's style is never <c>inherit</c>, which is how a form takes this one.
    /// </summary>
    [SettingsField("forms", Order = 1, Except = ["inherit"])]
    public RequiredMarker RequiredMarker { get; init; } = RequiredMarker.Optional;

    /// <summary>
    /// The label of the button that sends a form without a label of its own, as texts per culture, the culture <c>""</c>
    /// holding the default (ADR-0002), each on one line and a text may name a dictionary item (<c>#key</c>); null for the
    /// package's word in the page's language (ADR-0095).
    /// </summary>
    [SettingsField("forms", Order = 2)]
    public IReadOnlyDictionary<string, string>? SubmitLabel { get; init; }

    /// <summary>The label of the button to the next page of a form without one of its own, as <see cref="SubmitLabel"/>.</summary>
    [SettingsField("forms", Order = 3)]
    public IReadOnlyDictionary<string, string>? NextLabel { get; init; }

    /// <summary>The label of the button to the page before of a form without one of its own, as <see cref="SubmitLabel"/>.</summary>
    [SettingsField("forms", Order = 4)]
    public IReadOnlyDictionary<string, string>? BackLabel { get; init; }

    /// <summary>
    /// The site's own wording of the package's visitor texts (ADR-0230), which a form's own wording comes before: a text's
    /// key, such as <c>message.email</c>, to its wording per culture, a culture's own, its language's or, as <c>""</c>,
    /// every language's; null for the package's texts.
    /// </summary>
    [SettingsField("forms", Order = 5, Editor = "visitorTexts")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? Texts { get; init; }

    /// <summary>
    /// The address every link the server builds starts with (ADR-0008, ADR-0067): an <c>https</c> or <c>http</c> origin
    /// such as <c>https://www.example.com</c>, with a port when it is not the scheme's and nothing after it. Null for the
    /// default: the site's first <c>https</c> domain in Umbraco, otherwise Umbraco's application URL.
    /// </summary>
    [SettingsField("site", Editor = "baseUrl")]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// The site's time zone, an IANA id as the server spells it: "today", opening dates and relative dates are in it
    /// (ADR-0005, ADR-0030). The screen offers the zones as a list, so nobody types one (ADR-0043, ADR-0218).
    /// </summary>
    [SettingsField("site", Order = 1, Editor = "timeZone")]
    public string TimeZone { get; init; } = DefaultTimeZone;

    /// <summary>What a form created from blank starts with; every site shares it, since a new form belongs to no site (ADR-0044).</summary>
    [GlobalOnly]
    [SettingsField("newForms")]
    public NewFormDefaults NewForms { get; init; } = new();

    /// <summary>
    /// How long draft saves stay in a form's History; every site shares it, since the cleanup runs for all forms at once
    /// (ADR-0045).
    /// </summary>
    [GlobalOnly]
    [SettingsField("history")]
    public HistorySettings History { get; init; } = new();
}

/// <summary>
/// How long draft saves stay in a form's History (section 4.8, ADR-0045). A draft save stays while it is one of the
/// form's newest <see cref="DraftCount"/> or younger than <see cref="DraftDays"/>; publishes, pinned versions, the
/// form's draft and versions with entries always stay.
/// </summary>
internal sealed record HistorySettings
{
    /// <summary>The fewest days a draft save stays.</summary>
    public const int MinDraftDays = 1;

    /// <summary>The most days a setting may keep draft saves.</summary>
    public const int MaxDraftDays = 3650;

    /// <summary>The most draft saves a setting may keep per form however old they are.</summary>
    public const int MaxDraftCount = 1000;

    /// <summary>The days a draft save stays at least: 90 by default.</summary>
    [SettingsField("", Unit = true, Help = false)]
    [Range(MinDraftDays, MaxDraftDays)]
    public int DraftDays { get; init; } = 90;

    /// <summary>How many of a form's newest draft saves stay however old they are: 50 by default.</summary>
    [SettingsField("", Order = 1, Unit = true, Help = false)]
    [Range(0, MaxDraftCount)]
    public int DraftCount { get; init; } = 50;
}

/// <summary>What a form created from blank starts with (section 4.8, ADR-0044).</summary>
internal sealed record NewFormDefaults
{
    /// <summary>
    /// A required consent field at the end of the last group of the last page, or null for none (the default); its editor
    /// is a switch with the field's label and wording under it (ADR-0218).
    /// </summary>
    [SettingsField("", Editor = "newFormConsent")]
    public NewFormConsent? Consent { get; init; }

    /// <summary>Whether a new form starts with an email action to its creator, unless a template brings one; off by default (ADR-0160).</summary>
    [SettingsField("", Order = 1, Help = false)]
    public bool NotifyCreator { get; init; }
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
/// Checks the general settings: the time zone is one the server resolves, spelled as the server spells it, the base URL
/// is an origin, the required-marker style is a site's, the labels and the consent default's texts are texts a
/// definition takes, and the History keeps draft saves within its ranges.
/// </summary>
internal sealed class GeneralSettingsValidator : ISettingsValidator<GeneralSettings>
{
    private readonly SystemTimeZoneLinks _links;

    /// <summary>A validator over this system's links of older time zone names, read on its own.</summary>
    public GeneralSettingsValidator()
        : this(new SystemTimeZoneLinks())
    {
    }

    /// <summary>A validator over the server's links of older time zone names.</summary>
    /// <param name="links">The links, one per server.</param>
    public GeneralSettingsValidator(SystemTimeZoneLinks links)
    {
        ArgumentNullException.ThrowIfNull(links);
        _links = links;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A base URL of white space is the default; one that is an origin is written as its origin (<c>HTTPS://Example.com/</c>
    /// as <c>https://example.com</c>). A label without texts is the default, the package's word, and so is a theme of white
    /// space.
    /// </remarks>
    public GeneralSettings Normalize(GeneralSettings section)
    {
        ArgumentNullException.ThrowIfNull(section);
        var labelled = section.SubmitLabel is { Count: 0 } || section.NextLabel is { Count: 0 } || section.BackLabel is { Count: 0 }
            ? section with
            {
                SubmitLabel = section.SubmitLabel is { Count: 0 } ? null : section.SubmitLabel,
                NextLabel = section.NextLabel is { Count: 0 } ? null : section.NextLabel,
                BackLabel = section.BackLabel is { Count: 0 } ? null : section.BackLabel,
            }
            : section;
        if (labelled.Theme is { } theme && string.IsNullOrWhiteSpace(theme))
        {
            labelled = labelled with { Theme = null };
        }

        if (string.IsNullOrWhiteSpace(labelled.BaseUrl))
        {
            return labelled.BaseUrl is null ? labelled : labelled with { BaseUrl = null };
        }

        return labelled with { BaseUrl = BaseUrls.Normalize(labelled.BaseUrl) ?? labelled.BaseUrl };
    }

    /// <inheritdoc />
    public void Validate(GeneralSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        errors.TimeZone("timeZone", section.TimeZone, _links);
        if (section.BaseUrl is { } baseUrl && BaseUrls.Normalize(baseUrl) != baseUrl)
        {
            errors.Add("baseUrl", SettingsErrors.BaseUrlCode, "Must be the address the site is reached at, such as https://www.example.com: https or http, the host and a port, and nothing after them.");
        }

        if (section.RequiredMarker is not (RequiredMarker.Optional or RequiredMarker.Required or RequiredMarker.None))
        {
            errors.Add("requiredMarker", SettingsErrors.RequiredMarkerCode, "Must be optional, required or none: inherit is how a form takes the site's style.");
        }

        Label(errors, "submitLabel", section.SubmitLabel);
        Label(errors, "nextLabel", section.NextLabel);
        Label(errors, "backLabel", section.BackLabel);
        OwnWording(errors, section.Texts);
        if (section.Theme is { } theme && (theme.Length > DefinitionSaveChecks.ThemeLength || theme.Any(char.IsControl)))
        {
            errors.Add("theme", SettingsErrors.ThemeCode, $"Must be a theme's alias of at most {DefinitionSaveChecks.ThemeLength} characters on one line.");
        }

        if (section.NewForms?.Consent is { } consent)
        {
            errors.Text("newForms.consent.label", consent.Label, html: false);
            errors.Text("newForms.consent.text", consent.Text, html: true);
        }

        if (section.History is { } history)
        {
            errors.Between("history.draftDays", history.DraftDays, HistorySettings.MinDraftDays, HistorySettings.MaxDraftDays);
            errors.Between("history.draftCount", history.DraftCount, 0, HistorySettings.MaxDraftCount);
        }
    }

    // The site's own wording: each a text the package has, each culture a culture's name or "" for every language, each
    // wording one the text may take (ADR-0230).
    private static void OwnWording(SettingsErrors errors, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? texts)
    {
        foreach (var (key, cultures) in texts ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
        {
            var field = "texts." + key;
            if (VisitorTextKeys.Find(key) is not { } text)
            {
                errors.Add(field, SettingsErrors.VisitorTextCode, "The package has no visitor text of this key.");
                continue;
            }

            if (cultures is not { Count: > 0 })
            {
                errors.Add(field, SettingsErrors.VisitorTextCode, "A reworded text has a wording in at least one culture; leave the text out to keep the package's.");
                continue;
            }

            foreach (var (culture, wording) in cultures)
            {
                if (culture.Length > 0 && !LocalizedString.IsCultureName(culture))
                {
                    errors.Add(field, SettingsErrors.VisitorTextCode, $"\"{culture}\" is not a culture name; \"\" stands for every language.");
                }
                else if (VisitorTextKeys.Problem(text, wording) is { } problem)
                {
                    errors.Add(field, SettingsErrors.VisitorTextCode, problem);
                }
            }
        }
    }

    // A label is the package's word when it is not given, and otherwise texts a form's own button label takes.
    private static void Label(SettingsErrors errors, string field, IReadOnlyDictionary<string, string>? texts)
    {
        if (texts is not null)
        {
            errors.Text(field, texts, html: false);
        }
    }
}
