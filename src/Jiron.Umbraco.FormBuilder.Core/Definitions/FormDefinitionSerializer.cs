using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>The outcome of reading a definition: the model, or the problems that refused it.</summary>
/// <param name="Definition">The definition, upgraded to the current schema version; null when it was refused.</param>
/// <param name="Problems">Why it was refused, in document order; empty when it was read.</param>
/// <param name="SchemaVersion">The schema version the document was written in, when it could be read.</param>
internal sealed record DefinitionReadResult(FormDefinition? Definition, IReadOnlyList<DefinitionProblem> Problems, int? SchemaVersion)
{
    /// <summary>Whether the definition was read.</summary>
    public bool Succeeded => Definition is not null;

    public static DefinitionReadResult Refused(string code, string path, string message, int? schemaVersion = null) =>
        new(null, [new DefinitionProblem(code, path, message)], schemaVersion);
}

/// <summary>
/// Reads and writes form definitions (ADR-0002, ADR-0034). Reading is strict and refuses, with a code and a JSON path
/// for each problem, everything that makes a document unreadable; older schema versions are upgraded in memory first.
/// Writing produces the canonical form, whose SHA-256 is the version's <c>DefinitionHash</c>.
/// </summary>
internal sealed class FormDefinitionSerializer
{
    /// <summary>How deeply a document may nest; the deepest real definitions nest about 20 levels.</summary>
    public const int MaxDepth = 64;

    private const string SchemaVersionProperty = "schemaVersion";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal) { SchemaVersionProperty };

    private readonly FormDefinitionUpgrades _upgrades;
    private readonly DefinitionLimits _limits;

    public FormDefinitionSerializer(FormDefinitionUpgrades upgrades, DefinitionLimits limits)
    {
        _upgrades = upgrades;
        _limits = limits;
    }

    /// <summary>The SHA-256 of canonical UTF-8 bytes, in lowercase hex.</summary>
    public static string Hash(ReadOnlySpan<byte> canonical) => Convert.ToHexStringLower(SHA256.HashData(canonical));

    /// <summary>Reads a definition from JSON text.</summary>
    public DefinitionReadResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        byte[] bytes;
        try
        {
            if (StrictUtf8.GetByteCount(json) > _limits.MaxBytes)
            {
                return TooLarge();
            }

            bytes = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException)
        {
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, "The text holds a lone surrogate.");
        }

        return Read(bytes);
    }

    /// <summary>Reads a definition from UTF-8 bytes.</summary>
    public DefinitionReadResult Read(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > _limits.MaxBytes)
        {
            return TooLarge();
        }

        // The JSON reader checks UTF-8 inside strings only when a string is read, so check the whole text first.
        if (!System.Text.Unicode.Utf8.IsValid(utf8))
        {
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, "The bytes are not valid UTF-8.");
        }

        using var document = Parse(utf8, out var refusal);
        if (document is null)
        {
            return refusal!;
        }

        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidType, JsonPaths.Root, $"A definition is an object, not {JsonPaths.Describe(root.ValueKind)}.");
        }

        var versionRefusal = ReadSchemaVersion(root, out var version);
        if (versionRefusal is not null)
        {
            return versionRefusal;
        }

        if (version == _upgrades.Current)
        {
            return ReadCurrent(root, version);
        }

        if (FindDuplicate(root, JsonPaths.Root) is { } duplicate)
        {
            return new DefinitionReadResult(null, [duplicate], version);
        }

        string upgraded;
        try
        {
            upgraded = _upgrades.Upgrade(JsonNode.Parse(root.GetRawText())!.AsObject(), version).ToJsonString();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or InvalidCastException or ArgumentException or KeyNotFoundException)
        {
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidValue, JsonPaths.Root, $"Schema version {version} could not be upgraded: {exception.Message}", version);
        }

        using var upgradedDocument = JsonDocument.Parse(upgraded, new JsonDocumentOptions { MaxDepth = MaxDepth });
        return ReadCurrent(upgradedDocument.RootElement, version);
    }

    /// <summary>The canonical form of a definition as UTF-8 bytes.</summary>
    /// <exception cref="ArgumentException">The definition holds what the canonical form cannot carry, such as a lone surrogate; the save checks refuse it first.</exception>
    public static byte[] Write(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        byte[] serialized;
        try
        {
            serialized = JsonSerializer.SerializeToUtf8Bytes(definition, DefinitionJsonContext.Default.FormDefinition);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException($"The definition cannot be written: {exception.Message}", nameof(definition), exception);
        }

        using var document = JsonDocument.Parse(serialized, new JsonDocumentOptions { MaxDepth = 1000 });
        return CanonicalJson.WriteUtf8(document.RootElement, sortProperties: false);
    }

    /// <summary>The canonical form of a definition as text.</summary>
    public static string WriteString(FormDefinition definition) => Encoding.UTF8.GetString(Write(definition));

    /// <summary>The problems a save refuses in a definition built in code (a read definition has none).</summary>
    public IReadOnlyList<DefinitionProblem> CheckSave(FormDefinition definition) => DefinitionSaveChecks.Check(definition, _limits);

    private static JsonDocument? Parse(ReadOnlySpan<byte> utf8, out DefinitionReadResult? refusal)
    {
        refusal = null;
        try
        {
            return JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException exception)
        {
            var position = $"line {exception.LineNumber + 1}, byte {exception.BytePositionInLine + 1}";
            if (IsTooDeep(utf8))
            {
                refusal = DefinitionReadResult.Refused(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"The document nests deeper than {MaxDepth} levels ({position}).");
            }
            else
            {
                refusal = DefinitionReadResult.Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, $"The text is not JSON ({position}).");
            }

            return null;
        }
    }

    // Parsing again without the depth limit tells a document that is only too deep from one that is not JSON at all.
    private static bool IsTooDeep(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = 4096 });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static DefinitionProblem? FindDuplicate(JsonElement value, string path)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    var child = JsonPaths.Property(path, property.Name);
                    if (!names.Add(property.Name))
                    {
                        return new DefinitionProblem(DefinitionErrors.DuplicateProperty, child, $"The property \"{property.Name}\" appears twice.");
                    }

                    if (FindDuplicate(property.Value, child) is { } problem)
                    {
                        return problem;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (FindDuplicate(item, JsonPaths.Index(path, index++)) is { } problem)
                    {
                        return problem;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private DefinitionReadResult TooLarge() =>
        DefinitionReadResult.Refused(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"A definition has at most {_limits.MaxBytes} bytes in UTF-8.");

    private DefinitionReadResult? ReadSchemaVersion(JsonElement root, out int version)
    {
        version = 0;
        var path = JsonPaths.Property(JsonPaths.Root, SchemaVersionProperty);
        var count = root.EnumerateObject().Count(property => property.Name == SchemaVersionProperty);
        if (count == 0)
        {
            return DefinitionReadResult.Refused(DefinitionErrors.MissingProperty, path, "\"schemaVersion\" is required.");
        }

        if (count > 1)
        {
            return DefinitionReadResult.Refused(DefinitionErrors.DuplicateProperty, path, "The property \"schemaVersion\" appears twice.");
        }

        var value = root.GetProperty(SchemaVersionProperty);
        if (value.ValueKind != JsonValueKind.Number || !SettingsJson.IsCanonicalInteger(value.GetRawText(), 9) || !value.TryGetInt32(out version))
        {
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidType, path, "\"schemaVersion\" is an integer.");
        }

        if (version > _upgrades.Current || version < _upgrades.Oldest)
        {
            return DefinitionReadResult.Refused(
                DefinitionErrors.UnsupportedSchemaVersion,
                path,
                $"Schema version {version} cannot be read; this package reads versions {_upgrades.Oldest} to {_upgrades.Current}.",
                version);
        }

        return null;
    }

    private DefinitionReadResult ReadCurrent(JsonElement root, int sourceVersion)
    {
        var shape = new DefinitionShapeReader(DefinitionJsonContext.Default.Options);
        var node = shape.ReadRoot(root, typeof(FormDefinition), Skipped);
        if (node is null)
        {
            return new DefinitionReadResult(null, shape.Problems, sourceVersion);
        }

        FormDefinition definition;
        try
        {
            definition = node.Deserialize(DefinitionJsonContext.Default.FormDefinition)!;
        }
        catch (JsonException exception)
        {
            // The shape reader refuses everything System.Text.Json would; this is the safety net.
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidValue, exception.Path ?? JsonPaths.Root, exception.Message, sourceVersion);
        }

        var problems = DefinitionSaveChecks.Check(definition, _limits);
        return problems.Count > 0
            ? new DefinitionReadResult(null, problems, sourceVersion)
            : new DefinitionReadResult(definition, [], sourceVersion);
    }
}
