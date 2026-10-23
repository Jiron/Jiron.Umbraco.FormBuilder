using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// The settings of a field type, an action type, an option source or a captcha provider: a JSON object that belongs to
/// its owner, which validates it and binds it to its settings class (ADR-0002). It is kept in canonical form: a
/// property that is <c>null</c>, an empty array or an empty object is left out (its owner reads it as absent), and
/// properties are sorted by name (ordinal). Numbers are integers; a decimal that answers are compared with travels as
/// a string in the wire form (ADR-0005).
/// </summary>
[JsonConverter(typeof(SettingsObjectConverter))]
public sealed class SettingsObject : IEquatable<SettingsObject>
{
    private readonly string _json;

    private SettingsObject(JsonElement value, string json)
    {
        Value = value;
        _json = json;
    }

    /// <summary>The settings in canonical form.</summary>
    public JsonElement Value { get; }

    /// <summary>Reads settings from a JSON object.</summary>
    /// <exception cref="ArgumentException">
    /// The value is not an object, has a number that is not an integer of at most 15 digits, a <c>null</c> inside an
    /// array, a property named twice, or nothing left once empty properties are left out.
    /// </exception>
    public static SettingsObject From(JsonElement value)
    {
        if (!SettingsJson.TryCanonicalize(value, JsonPaths.Root, out var canonical, out var problem))
        {
            throw new ArgumentException(problem.Message, nameof(value));
        }

        using var document = JsonDocument.Parse(canonical);
        return new SettingsObject(document.RootElement.Clone(), canonical);
    }

    /// <summary>Reads settings from JSON text that holds an object.</summary>
    /// <exception cref="ArgumentException">See <see cref="From(JsonElement)"/>.</exception>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static SettingsObject Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        return From(document.RootElement);
    }

    /// <summary>The value of a setting, if present.</summary>
    public bool TryGetProperty(string name, out JsonElement value) => Value.TryGetProperty(name, out value);

    /// <summary>Binds the settings to their owner's settings class.</summary>
    public T? Bind<T>(JsonTypeInfo<T> typeInfo) => Value.Deserialize(typeInfo);

    /// <inheritdoc />
    public bool Equals(SettingsObject? other) => other is not null && string.Equals(_json, other._json, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SettingsObject);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_json);

    /// <summary>The settings as canonical JSON.</summary>
    public override string ToString() => _json;
}
