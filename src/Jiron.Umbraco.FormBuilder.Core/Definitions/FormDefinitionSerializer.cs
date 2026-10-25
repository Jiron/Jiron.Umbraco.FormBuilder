using System.Collections.Frozen;
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
/// Writing produces the canonical form, whose SHA-256 is the version's <c>DefinitionHash</c>. Documents from outside
/// and saves meet the site's limits; what the site stored is read with the highest limits (ADR-0042).
/// </summary>
internal sealed class FormDefinitionSerializer
{
    /// <summary>How deeply a document may nest; the deepest real definitions nest about 20 levels.</summary>
    public const int MaxDepth = 64;

    /// <summary>
    /// How deeply System.Text.Json may nest while it reads and writes the model: deeper than a document, because it
    /// meets the empty lists and objects that the canonical form leaves out one level below the deepest object.
    /// </summary>
    public const int ModelDepth = 2 * MaxDepth;

    private const string SchemaVersionProperty = "schemaVersion";

    private static readonly FrozenSet<string> Skipped = new[] { SchemaVersionProperty }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions UpgradedText = new() { MaxDepth = 16 * MaxDepth };

    private readonly FormDefinitionUpgrades _upgrades;
    private readonly DefinitionLimits _limits;

    public FormDefinitionSerializer(FormDefinitionUpgrades upgrades, DefinitionLimits limits)
    {
        _upgrades = upgrades;
        _limits = limits;

        // The reader of what the site stored: the same chain with the highest limits, built once with this one.
        Stored = limits == DefinitionLimits.Ceiling ? this : new FormDefinitionSerializer(upgrades, DefinitionLimits.Ceiling);
    }

    /// <summary>The limits this reader applies.</summary>
    public DefinitionLimits Limits => _limits;

    /// <summary>The reader <see cref="ReadStored"/> uses: this one when its limits are the ceiling.</summary>
    internal FormDefinitionSerializer Stored { get; }

    /// <summary>Reads a definition from JSON text.</summary>
    public DefinitionReadResult Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return StrictText.ToUtf8(json, _limits.MaxBytes, TooLarge(), out var problem) is { } bytes
            ? Read(bytes)
            : new DefinitionReadResult(null, [problem!], null);
    }

    /// <summary>
    /// Reads a definition the site stored, with <see cref="DefinitionLimits.Ceiling"/> instead of the site's limits: a
    /// host that lowers a limit refuses to save a form above it, but the form keeps reading (ADR-0042).
    /// </summary>
    public DefinitionReadResult ReadStored(string json) => Stored.Read(json);

    /// <summary>Reads a definition from UTF-8 bytes.</summary>
    public DefinitionReadResult Read(ReadOnlySpan<byte> utf8)
    {
        if (StrictText.Check(utf8, _limits.MaxBytes, TooLarge()) is { } refused)
        {
            return new DefinitionReadResult(null, [refused], null);
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

        // Everything after this reads property names, which .NET cannot do for a name with a lone surrogate.
        if (CheckNames(root, JsonPaths.Location.At(JsonPaths.Root), duplicates: false) is { } badName)
        {
            return new DefinitionReadResult(null, [badName], null);
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

        // An upgrader reads the document as a JsonObject, which would keep one of two equal names without a word.
        if (CheckNames(root, JsonPaths.Location.At(JsonPaths.Root), duplicates: true) is { } duplicate)
        {
            return new DefinitionReadResult(null, [duplicate], version);
        }

        byte[] upgraded;
        try
        {
            var node = _upgrades.Upgrade(JsonNode.Parse(root.GetRawText())!.AsObject(), version);
            upgraded = JsonSerializer.SerializeToUtf8Bytes(node, UpgradedText);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // An upgrader is code that meets old documents nobody has seen yet: whatever it throws refuses the document.
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidValue, JsonPaths.Root, $"Schema version {version} could not be upgraded: {exception.Message}", version);
        }

        if (NestsDeeperThan(upgraded, MaxDepth))
        {
            return DefinitionReadResult.Refused(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"Upgraded from schema version {version}, the document nests deeper than {MaxDepth} levels.", version);
        }

        using var upgradedDocument = JsonDocument.Parse(upgraded, new JsonDocumentOptions { MaxDepth = MaxDepth });
        return ReadCurrent(upgradedDocument.RootElement, version);
    }

    /// <summary>The canonical form of a definition as UTF-8 bytes.</summary>
    /// <exception cref="ArgumentException">
    /// The definition holds what the canonical form cannot carry, such as a lone surrogate, or nests deeper than a
    /// document may; <see cref="CheckSave"/> refuses it first.
    /// </exception>
    public static byte[] Write(FormDefinition definition) =>
        TryWriteCanonical(definition, out var problem) ?? throw new ArgumentException(problem!.Message, nameof(definition));

    /// <summary>The canonical form of a definition as text.</summary>
    public static string WriteString(FormDefinition definition) => Encoding.UTF8.GetString(Write(definition));

    /// <summary>
    /// The problems a save refuses in a definition built in code, the same a read would refuse in its canonical form;
    /// a read definition has none.
    /// </summary>
    public IReadOnlyList<DefinitionProblem> CheckSave(FormDefinition definition)
    {
        var problems = DefinitionSaveChecks.Check(definition, _limits);
        if (problems.Count > 0)
        {
            return problems;
        }

        // The checks on the model found nothing the writer refuses, so what is left is the document's size and depth;
        // what the writer still refuses is reported as what it is.
        if (TryWriteCanonical(definition, out var problem) is not { } canonical)
        {
            return [problem!];
        }

        return canonical.Length > _limits.MaxBytes ? [TooLarge()] : [];
    }

    // The canonical bytes, or null with what keeps them from being written: a document deeper than a reader reads
    // (limit_exceeded), or a value the canonical form cannot carry, such as a lone surrogate (invalid_value). The
    // converters refuse what they cannot write with ArgumentException, so a JsonException from System.Text.Json
    // itself means the model nests deeper than it writes.
    private static byte[]? TryWriteCanonical(FormDefinition definition, out DefinitionProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(definition);
        problem = null;
        try
        {
            var serialized = JsonSerializer.SerializeToUtf8Bytes(definition, DefinitionJsonContext.Default.FormDefinition);
            using var document = JsonDocument.Parse(serialized, new JsonDocumentOptions { MaxDepth = ModelDepth });
            var canonical = CanonicalJson.WriteUtf8(document.RootElement, sortProperties: false);
            if (!NestsDeeperThan(canonical, MaxDepth))
            {
                return canonical;
            }
        }
        catch (ArgumentException exception)
        {
            problem = new DefinitionProblem(DefinitionErrors.InvalidValue, JsonPaths.Root, $"The definition cannot be written: {exception.Message}");
            return null;
        }
        catch (JsonException)
        {
            // Deeper than ModelDepth, so deeper than a reader reads: the problem below.
        }

        problem = new DefinitionProblem(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"The definition nests deeper than {MaxDepth} levels, which no reader reads.");
        return null;
    }

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
            refusal = NestsDeeperThan(utf8, MaxDepth)
                ? DefinitionReadResult.Refused(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"The document nests deeper than {MaxDepth} levels ({position}).")
                : DefinitionReadResult.Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, $"The text is not JSON ({position}).");
            return null;
        }
    }

    // Whether the text opens more than maxDepth nested objects and arrays before it ends or stops being JSON: this
    // tells a document that is only too deep from one that is not JSON, whatever its depth, without parsing it.
    private static bool NestsDeeperThan(ReadOnlySpan<byte> utf8, int maxDepth)
    {
        var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = int.MaxValue });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray && reader.CurrentDepth >= maxDepth)
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON before it was too deep.
        }

        return false;
    }

    // The first property name that cannot be read (a lone surrogate) and, when asked, the first named twice.
    private static DefinitionProblem? CheckNames(JsonElement value, JsonPaths.Location location, bool duplicates)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = duplicates ? new HashSet<string>(StringComparer.Ordinal) : null;
                foreach (var property in value.EnumerateObject())
                {
                    if (JsonPaths.TryGetName(property) is not { } name)
                    {
                        return new DefinitionProblem(DefinitionErrors.InvalidValue, location.ToString(), "A property name holds a lone surrogate.");
                    }

                    var child = location.Property(name);
                    if (names is not null && !names.Add(name))
                    {
                        return new DefinitionProblem(DefinitionErrors.DuplicateProperty, child.ToString(), $"The property \"{name}\" appears twice.");
                    }

                    if (CheckNames(property.Value, child, duplicates) is { } problem)
                    {
                        return problem;
                    }
                }

                return null;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    if (CheckNames(item, location.Item(index++), duplicates) is { } problem)
                    {
                        return problem;
                    }
                }

                return null;
            default:
                return null;
        }
    }

    private DefinitionProblem TooLarge() =>
        new(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"A definition has at most {_limits.MaxBytes} bytes in UTF-8.");

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
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            // The shape reader refuses everything System.Text.Json would; this is the safety net.
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidValue, (exception as JsonException)?.Path ?? JsonPaths.Root, exception.Message, sourceVersion);
        }

        var problems = DefinitionSaveChecks.Check(definition, _limits);
        return problems.Count > 0
            ? new DefinitionReadResult(null, problems, sourceVersion)
            : new DefinitionReadResult(definition, [], sourceVersion);
    }
}
