using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The canonical form of a definition and of settings objects (ADR-0002, ADR-0034): no whitespace; an object property
/// that is <c>null</c>, an empty array or an object with nothing left is left out; strings escaped as JSON.stringify
/// does (RFC 8785): only <c>"</c>, <c>\</c> and control characters, everything else as UTF-8. The output never
/// depends on the .NET version's encoder, so hashes stay stable across package updates. JSON that is kept as it was
/// given, such as an option list's configuration, is written the same way with every value (<see cref="Compact"/>).
/// </summary>
internal static class CanonicalJson
{
    /// <summary>
    /// The SHA-256 of canonical UTF-8 bytes in lowercase hex: a version's <c>DefinitionHash</c> and a projection's
    /// <c>PublicHash</c> (ADR-0034, ADR-0035).
    /// </summary>
    public static string Hash(ReadOnlySpan<byte> canonical) => Convert.ToHexStringLower(SHA256.HashData(canonical));

    /// <summary>The canonical text of a JSON value; <paramref name="sortProperties"/> sorts every object's properties ordinally.</summary>
    /// <exception cref="ArgumentException">A string or a property name holds a lone surrogate, which UTF-8 cannot carry.</exception>
    public static string Write(JsonElement value, bool sortProperties)
    {
        var text = new StringBuilder();
        WriteValue(text, value, sortProperties, leaveOut: true);
        return text.ToString();
    }

    /// <summary>
    /// The text of a JSON value that is kept as it was given: without whitespace and with the escapes of
    /// <see cref="Write"/>, but with every value, nulls and empty arrays and objects too, in the order given.
    /// </summary>
    /// <exception cref="ArgumentException">A string or a property name holds a lone surrogate.</exception>
    public static string Compact(JsonElement value)
    {
        var text = new StringBuilder();
        WriteValue(text, value, sortProperties: false, leaveOut: false);
        return text.ToString();
    }

    /// <summary>The canonical text as UTF-8 bytes.</summary>
    /// <exception cref="ArgumentException">A string or a property name holds a lone surrogate.</exception>
    public static byte[] WriteUtf8(JsonElement value, bool sortProperties)
    {
        try
        {
            return StrictText.Utf8.GetBytes(Write(value, sortProperties));
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("A string holds a lone surrogate.", nameof(value), exception);
        }
    }

    /// <summary>Whether a property with this value is left out: <c>null</c>, <c>[]</c>, or an object with nothing left.</summary>
    public static bool IsLeftOut(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => true,
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => value.EnumerateObject().All(property => IsLeftOut(property.Value)),
        _ => false,
    };

    /// <summary>Appends a string as canonical JSON.</summary>
    public static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case < ' ':
                    text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    text.Append(c);
                    break;
            }
        }

        text.Append('"');
    }

    private static void WriteValue(StringBuilder text, JsonElement value, bool sortProperties, bool leaveOut)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(text, value, sortProperties, leaveOut);
                break;
            case JsonValueKind.Array:
                text.Append('[');
                var first = true;
                foreach (var item in value.EnumerateArray())
                {
                    if (!first)
                    {
                        text.Append(',');
                    }

                    WriteValue(text, item, sortProperties, leaveOut);
                    first = false;
                }

                text.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(text, JsonPaths.TryGetString(value) ?? throw LoneSurrogate());
                break;
            case JsonValueKind.Number:
                // Definitions hold integers only, which System.Text.Json and the reader keep in canonical form.
                text.Append(value.GetRawText());
                break;
            case JsonValueKind.True:
                text.Append("true");
                break;
            case JsonValueKind.False:
                text.Append("false");
                break;
            default:
                text.Append("null");
                break;
        }
    }

    // Every name is read, a left-out property's too, so a name that holds a lone surrogate is refused wherever it is.
    private static void WriteObject(StringBuilder text, JsonElement value, bool sortProperties, bool leaveOut)
    {
        var properties = value.EnumerateObject()
            .Select(property => (Name: JsonPaths.TryGetName(property) ?? throw LoneSurrogate(), property.Value))
            .Where(property => !leaveOut || !IsLeftOut(property.Value));
        if (sortProperties)
        {
            properties = properties.OrderBy(property => property.Name, StringComparer.Ordinal);
        }

        text.Append('{');
        var first = true;
        foreach (var property in properties)
        {
            if (!first)
            {
                text.Append(',');
            }

            WriteString(text, property.Name);
            text.Append(':');
            WriteValue(text, property.Value, sortProperties, leaveOut);
            first = false;
        }

        text.Append('}');
    }

    // The JSON reader takes an escaped lone surrogate, which .NET cannot read as a string.
    private static ArgumentException LoneSurrogate() => new("A string or a property name holds a lone surrogate, which UTF-8 cannot carry.");
}
