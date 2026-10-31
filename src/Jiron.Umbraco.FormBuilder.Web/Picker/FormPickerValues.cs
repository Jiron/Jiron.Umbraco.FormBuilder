namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The form picker's stored value (ADR-0129): the picked forms' keys with hyphens, separated by commas, in the editor's
/// order. One key, the value 6.6c stored, reads the same.
/// </summary>
internal static class FormPickerValues
{
    /// <summary>The value's keys in order, each once, or null when a part is not a key; empty for no value.</summary>
    /// <param name="value">The stored value.</param>
    public static IReadOnlyList<Guid>? Read(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var keys = new List<Guid>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!Guid.TryParse(part, out var key) || key == Guid.Empty)
            {
                return null;
            }

            if (!keys.Contains(key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
