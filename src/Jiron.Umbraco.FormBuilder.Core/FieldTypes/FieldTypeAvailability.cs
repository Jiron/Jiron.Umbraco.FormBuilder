namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// Whether a field type can be used on the site (section 4.3.2, ADR-0057): a type that needs configuration the site lacks,
/// such as a provider's keys, is not configured, and the designer's palette greys it out with the reason.
/// </summary>
public sealed record FieldTypeAvailability
{
    private FieldTypeAvailability(bool isAvailable, string? reason)
    {
        IsAvailable = isAvailable;
        Reason = reason;
    }

    /// <summary>The type can be used.</summary>
    public static FieldTypeAvailability Available { get; } = new(true, null);

    /// <summary>Whether the type can be used.</summary>
    public bool IsAvailable { get; }

    /// <summary>Why it cannot, for editors; null when it can.</summary>
    public string? Reason { get; }

    /// <summary>The type needs configuration the site lacks.</summary>
    /// <param name="reason">
    /// What is missing, as a sentence written for editors ("No captcha provider has its keys."), which the palette shows
    /// with "Not configured": plain text, or a key of the backoffice's localisation that starts with <c>#</c>.
    /// </param>
    /// <exception cref="ArgumentException">The reason is blank.</exception>
    public static FieldTypeAvailability NotConfigured(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new FieldTypeAvailability(false, reason);
    }
}
