using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jiron.Umbraco.FormBuilder.Core.Audit;

/// <summary>
/// The details of an audit entry (section 4.10, ADR-0041): keys, numbers, flags, enum values, times, and identifiers such
/// as setting paths, aliases and formats (letters, digits, <c>.</c>, <c>_</c>, <c>-</c>, <c>[</c> and <c>]</c>, at most 200
/// characters). Free text never fits, so a message, an email address or a name with a space cannot slip in. A number, a
/// time or a single word still could hold what a visitor typed (an amount, a birthday, a first name, a phone number
/// without spaces), so details take only what the code itself knows, such as keys, counts, states and settings, and
/// never a value of an entry: details about an entry name its fields, files and the entry itself by key.
/// </summary>
internal sealed class AuditDetails
{
    /// <summary>The longest identifier, which a setting path with a long key needs.</summary>
    public const int MaxIdentifierLength = 200;

    /// <summary>The most identifiers or keys one list holds.</summary>
    public const int MaxListLength = 200;

    private readonly JsonObject _json = [];

    /// <summary>True when nothing was added.</summary>
    public bool IsEmpty => _json.Count == 0;

    /// <summary>Adds a key; a null key is left out.</summary>
    public AuditDetails Add(string name, Guid? value) => Set(name, value is { } key ? JsonValue.Create(key) : null);

    /// <summary>Adds a number; null is left out.</summary>
    public AuditDetails Add(string name, long? value) => Set(name, value is { } number ? JsonValue.Create(number) : null);

    /// <summary>Adds a flag.</summary>
    public AuditDetails Add(string name, bool value) => Set(name, JsonValue.Create(value));

    /// <summary>Adds a moment, in UTC; null is left out.</summary>
    public AuditDetails Add(string name, DateTimeOffset? value) =>
        Set(name, value is { } at ? JsonValue.Create(at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)) : null);

    /// <summary>Adds one defined value of an enum, in camelCase.</summary>
    /// <exception cref="ArgumentException">The value is not one defined value: record a combination of flags as a number.</exception>
    public AuditDetails Add<TEnum>(string name, TEnum value)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentException("Audit details take one defined value of an enum; record a combination of flags as a number.", nameof(value));
        }

        return Set(name, JsonValue.Create(Identifier(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()))));
    }

    /// <summary>Adds an identifier, such as a setting path, an alias or a format; null is left out.</summary>
    /// <exception cref="ArgumentException">The value is not an identifier.</exception>
    public AuditDetails AddIdentifier(string name, string? value) =>
        Set(name, value is null ? null : JsonValue.Create(Identifier(value)));

    /// <summary>Adds a list of identifiers, for example the setting paths a save changed.</summary>
    /// <exception cref="ArgumentException">A value is not an identifier, or there are more than 200.</exception>
    public AuditDetails AddIdentifiers(string name, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Set(name, List(values.Select(value => JsonValue.Create(Identifier(value)))));
    }

    /// <summary>Adds a list of numbers, for example the versions a cleanup removed.</summary>
    /// <exception cref="ArgumentException">There are more than 200.</exception>
    public AuditDetails AddNumbers(string name, IEnumerable<long> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Set(name, List(values.Select(value => JsonValue.Create(value))));
    }

    /// <summary>Adds a list of keys, for example the fields a change touched.</summary>
    /// <exception cref="ArgumentException">There are more than 200.</exception>
    public AuditDetails AddKeys(string name, IEnumerable<Guid> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return Set(name, List(keys.Select(key => JsonValue.Create(key))));
    }

    /// <summary>
    /// True when <paramref name="value"/> may be recorded as an identifier: 1 to 200 of ASCII letters, digits, <c>.</c>,
    /// <c>_</c>, <c>-</c>, <c>[</c> and <c>]</c>.
    /// </summary>
    public static bool IsIdentifier(string? value) =>
        value is { Length: > 0 and <= MaxIdentifierLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '[' or ']');

    /// <summary>The details as a JSON object; <c>{}</c> when nothing was added.</summary>
    public string ToJson() => _json.ToJsonString();

    private static string Identifier(string value) =>
        IsIdentifier(value)
            ? value
            : throw new ArgumentException($"Audit details take identifiers only (ASCII letters, digits, '.', '_', '-', '[' and ']'; at most {MaxIdentifierLength}), never free text.", nameof(value));

    private static JsonArray List(IEnumerable<JsonValue> values)
    {
        var list = values.ToArray<JsonNode?>();
        return list.Length <= MaxListLength
            ? new JsonArray(list)
            : throw new ArgumentException($"Audit details take at most {MaxListLength} values in a list.", nameof(values));
    }

    private AuditDetails Set(string name, JsonNode? value)
    {
        Identifier(name);
        if (value is not null)
        {
            _json[name] = value;
        }

        return this;
    }
}
