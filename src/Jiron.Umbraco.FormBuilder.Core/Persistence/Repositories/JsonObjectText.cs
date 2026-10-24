using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>The check for the JSON columns that hold an object: an entry's context and an event's details.</summary>
internal static class JsonObjectText
{
    /// <summary>Whether <paramref name="json"/> is one well-formed JSON object.</summary>
    public static bool IsObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
