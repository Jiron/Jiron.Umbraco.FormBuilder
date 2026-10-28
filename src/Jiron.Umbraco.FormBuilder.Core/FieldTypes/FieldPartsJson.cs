using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The value of a field with parts, such as a name or an address (ADR-0058): one JSON object of the parts' texts in the
/// type's order, written canonically (ADR-0034), so equal answers store equal text.
/// </summary>
internal static class FieldPartsJson
{
    /// <summary>The object of the parts, in the order given; null when there is none.</summary>
    /// <exception cref="ArgumentException">A part's name or text is empty, or a name is given twice.</exception>
    public static string? Write(IEnumerable<KeyValuePair<string, string>> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var value = new JsonObject();
        foreach (var (name, text) in parts)
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ArgumentException.ThrowIfNullOrEmpty(text);
            if (value.ContainsKey(name))
            {
                throw new ArgumentException($"The part '{name}' is given twice.", nameof(parts));
            }

            value[name] = text;
        }

        return value.Count == 0 ? null : CanonicalJson.Write(JsonSerializer.SerializeToElement(value), sortProperties: false);
    }

    /// <summary>
    /// The parts of a value with parts, in their order: null unless the text is a JSON object of at least one part, each
    /// with a name and a text that are not empty, and no name twice. A text that holds a lone surrogate itself, outside
    /// any escape, is no JSON and has no parts, as the website reads it (ADR-0083); the JSON reader would throw for it.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>>? Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!StrictStringConverter.IsWellFormed(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var parts = new List<KeyValuePair<string, string>>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in document.RootElement.EnumerateObject())
            {
                if (part.Name.Length == 0 || part.Value.ValueKind != JsonValueKind.String
                    || part.Value.GetString() is not { Length: > 0 } text || !names.Add(part.Name))
                {
                    return null;
                }

                parts.Add(new(part.Name, text));
            }

            return parts.Count == 0 ? null : parts;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // InvalidOperationException: an escape that is no well-formed UTF-16, such as a lone surrogate.
            return null;
        }
    }

    /// <summary>The parts' texts in their order, joined by spaces: what a value with parts reads as, for its key.</summary>
    public static string Joined(IEnumerable<KeyValuePair<string, string>> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(' ', parts.Select(part => part.Value));
    }
}
