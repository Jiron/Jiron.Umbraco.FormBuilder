using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>A settings validation error (ADR-0008).</summary>
/// <param name="Field">The field, keyed <c>section.field</c>, for example <c>general.timeZone</c>.</param>
/// <param name="Code">A stable code the backoffice translates, such as <c>timeZone</c> or <c>range</c>.</param>
/// <param name="Message">An English explanation.</param>
internal sealed record SettingsError(string Field, string Code, string Message);

/// <summary>Validates one settings section; the server decides (ADR-0008, ADR-0043).</summary>
/// <typeparam name="T">The section.</typeparam>
internal interface ISettingsValidator<T>
    where T : ISettingsSection
{
    /// <summary>The section with its values in their canonical form, before it is validated and stored; by default as it is.</summary>
    T Normalize(T section) => section;

    /// <summary>Adds every problem of <paramref name="section"/> to <paramref name="errors"/>.</summary>
    void Validate(T section, SettingsErrors errors);
}

/// <summary>Collects the errors of one section and offers the checks the sections share.</summary>
/// <param name="section">The section's alias, which keys every error.</param>
internal sealed class SettingsErrors(string section)
{
    /// <summary>The code of a value outside what the host or the field allows.</summary>
    public const string Range = "range";

    /// <summary>The code of a time zone the server cannot resolve as an IANA zone (ADR-0005).</summary>
    public const string TimeZoneCode = "timeZone";

    /// <summary>The code of a base URL that is not an <c>https</c> or <c>http</c> origin (ADR-0067).</summary>
    public const string BaseUrlCode = "baseUrl";

    /// <summary>The code of a required-marker style a site cannot have: <c>inherit</c> is a form's (ADR-0095).</summary>
    public const string RequiredMarkerCode = "requiredMarker";

    /// <summary>The code of a theme's alias that is too long or not on one line (ADR-0105).</summary>
    public const string ThemeCode = "theme";

    /// <summary>The code of a blocked address range that is not one in CIDR notation (ADR-0139).</summary>
    public const string RangeCode = "addressRange";

    /// <summary>The code of a site's protection level that a site cannot have (ADR-0141).</summary>
    public const string ProtectionLevelCode = "protectionLevel";

    /// <summary>The code of a site's IP handling set to <c>inherit</c>, which is a form's (ADR-0224).</summary>
    public const string IPHandlingCode = "ipHandling";

    /// <summary>The code of a site's content rules set to <c>inherit</c>, which is a form's (ADR-0140).</summary>
    public const string ContentRulesCode = "contentRules";

    /// <summary>The code of a blocked word that is empty, too long or not on one line (ADR-0140).</summary>
    public const string WordCode = "word";

    /// <summary>The code of a blocked domain that is not a domain (ADR-0140).</summary>
    public const string DomainCode = "domain";

    /// <summary>The code of a value that is not one mail address (ADR-0159).</summary>
    public const string AddressCode = "address";

    /// <summary>The code of a value that must be a reference to a secret (ADR-0161).</summary>
    public const string SecretCode = "secretReference";

    /// <summary>The code of a field a site cannot override.</summary>
    public const string Scope = "scope";

    /// <summary>The code of a value that must be given.</summary>
    public const string Required = "required";

    /// <summary>The code of texts that are not a localised text: a culture name that is not one, or one named twice.</summary>
    public const string TextCode = "text";

    private readonly List<SettingsError> _errors = [];

    /// <summary>Everything found so far.</summary>
    public IReadOnlyList<SettingsError> Errors => _errors;

    /// <summary>Adds an error for a field of this section, given by its path within the section.</summary>
    public void Add(string field, string code, string message) => _errors.Add(new SettingsError($"{section}.{field}", code, message));

    /// <summary>A whole number from <paramref name="min"/> to <paramref name="max"/>; otherwise a <see cref="Range"/> error.</summary>
    public void Between(string field, int value, int min, int max)
    {
        if (value < min || value > max)
        {
            Add(field, Range, string.Create(CultureInfo.InvariantCulture, $"Must be a whole number from {min} to {max}."));
        }
    }

    /// <summary>
    /// An IANA time zone this server resolves, as "today" looks it up (ADR-0005, ADR-0030), spelled as the server spells
    /// it (ADR-0043). Windows names and offsets are refused, because the browser does not take the one and the server not
    /// the other. Another letter case is refused too: Windows finds it only once the exact spelling has been looked up,
    /// so the answer would depend on what the server did before.
    /// </summary>
    public void TimeZone(string field, string? value)
    {
        if (value is null || CalendarValues.FindTimeZone(value) is not { } zone || !string.Equals(zone.Id, value, StringComparison.Ordinal))
        {
            Add(field, TimeZoneCode, "Must be an IANA time zone this server knows, in its exact spelling, such as Europe/Berlin.");
        }
    }

    /// <summary>
    /// Texts per culture that a definition takes (ADR-0002): a default text (the culture <c>""</c>) that is not only white
    /// space, culture names, and the rules of a label on one line or, when <paramref name="html"/>, of rich text. The
    /// codes of the definition's checks are kept, such as <c>limit_exceeded</c>.
    /// </summary>
    public void Text(string field, IReadOnlyDictionary<string, string>? texts, bool html)
    {
        if (texts is null || !texts.TryGetValue(string.Empty, out var fallback) || string.IsNullOrWhiteSpace(fallback))
        {
            Add(field, Required, "Needs a default text, for the culture \"\", that is not only white space.");
            return;
        }

        LocalizedString text;
        try
        {
            text = html ? new LocalizedHtml(texts) : new LocalizedText(texts);
        }
        catch (ArgumentException exception)
        {
            Add(field, TextCode, exception.Message);
            return;
        }

        foreach (var problem in DefinitionSaveChecks.CheckText(text, html))
        {
            Add(field, problem.Code, problem.Message);
        }
    }
}

/// <summary>
/// Thrown when a settings save has values the section does not allow: nothing was written. The API answers
/// <c>422 validation_failed</c> with the errors.
/// </summary>
internal sealed class FormBuilderSettingsValidationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FormBuilderSettingsValidationException(IReadOnlyList<SettingsError> errors)
        : base($"The settings have {errors.Count} invalid values: {string.Join(", ", errors.Select(e => e.Field))}.")
    {
        Errors = errors;
    }

    /// <summary>What is wrong, field by field.</summary>
    public IReadOnlyList<SettingsError> Errors { get; }
}

/// <summary>
/// Thrown when a settings save changes what the host configuration locks (ADR-0008): nothing was written. The API
/// answers <c>409 setting_locked</c>, naming the field and the configuration key.
/// </summary>
internal sealed class FormBuilderSettingLockedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public FormBuilderSettingLockedException(string field, string configurationKey)
        : base($"{field} is locked by the host configuration ({configurationKey}).")
    {
        Field = field;
        ConfigurationKey = configurationKey;
    }

    /// <summary>The section (a locked section) or the field, keyed <c>section.field</c>.</summary>
    public string Field { get; }

    /// <summary>The configuration key that locks it, for example <c>JironFormBuilder:LockedSections:0</c>.</summary>
    public string ConfigurationKey { get; }
}
