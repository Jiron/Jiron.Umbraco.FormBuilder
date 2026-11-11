using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.AI.Chat;

/// <summary>
/// Reads an AI's reply as the untrusted text it is (ADR-0018): a provider that ignores the schema may wrap the JSON in a
/// code fence or a sentence, so the reply's outermost object is read, shallowly and within a size, and anything else is
/// unusable.
/// </summary>
internal static class AIReplies
{
    /// <summary>The longest reply read.</summary>
    public const int MaxLength = 500_000;

    /// <summary>How deeply a reply may nest; a draft nests about ten levels.</summary>
    public const int MaxDepth = 24;

    /// <summary>The reply's outermost JSON object.</summary>
    /// <exception cref="AIUnavailableException">The reply holds no JSON object, or one too large or too deep.</exception>
    public static JsonElement ObjectOf(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var start = reply.IndexOf('{', StringComparison.Ordinal);
        var end = reply.LastIndexOf('}');
        if (reply.Length > MaxLength || start < 0 || end <= start)
        {
            throw Unusable();
        }

        try
        {
            using var document = JsonDocument.Parse(reply.AsMemory(start, end - start + 1), new JsonDocumentOptions { MaxDepth = MaxDepth });
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw Unusable();
        }
    }

    /// <summary>A property's text, trimmed and cut at <paramref name="max"/> characters on a word's end; null for none, a null, an empty text or no text.</summary>
    public static string? TextOf(JsonElement element, string name, int max)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Cut(value.GetString(), max);
    }

    /// <summary>A text trimmed and cut at <paramref name="max"/> characters, on a word's end where one is near; null for an empty one.</summary>
    public static string? Cut(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length <= max)
        {
            return trimmed;
        }

        var space = trimmed.LastIndexOf(' ', max - 1);
        return (space > max / 2 ? trimmed[..space] : trimmed[..max]).TrimEnd();
    }

    /// <summary>A property's array, or an empty one.</summary>
    public static IEnumerable<JsonElement> ArrayOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    /// <summary>A property's boolean, or false.</summary>
    public static bool FlagOf(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>The refusal of a reply the add-on cannot use.</summary>
    public static AIUnavailableException Unusable() => new(AIProblems.ReplyUnusable, "The AI's reply is not one the add-on can use.");
}
