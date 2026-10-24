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

    /// <summary>The code of a field a site cannot override.</summary>
    public const string Scope = "scope";

    private readonly List<SettingsError> _errors = [];

    /// <summary>Everything found so far.</summary>
    public IReadOnlyList<SettingsError> Errors => _errors;

    /// <summary>Adds an error for a field of this section, given by its path within the section.</summary>
    public void Add(string field, string code, string message) => _errors.Add(new SettingsError($"{section}.{field}", code, message));

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
