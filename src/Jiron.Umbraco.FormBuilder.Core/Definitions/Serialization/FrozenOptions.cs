using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// Serializer options as a static field holds them: read-only from the start, so no caller can change them for every
/// other one (the quality bar; ROADMAP 4.8h).
/// </summary>
internal static class FrozenOptions
{
    /// <summary>The options, made read-only; when they name no resolver, the reflection resolver is filled in.</summary>
    public static JsonSerializerOptions Of(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
