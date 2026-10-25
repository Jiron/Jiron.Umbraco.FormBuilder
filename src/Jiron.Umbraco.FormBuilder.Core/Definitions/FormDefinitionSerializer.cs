using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
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
    private const string Document = "A definition";

    private static readonly FrozenSet<string> Skipped = new[] { SchemaVersionProperty }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions UpgradedText = FrozenOptions.Of(new() { MaxDepth = 16 * MaxDepth });

    // The writer's options: the model's metadata, where a value the model requires stops the writer when it is missing
    // instead of being left out as every null is, which would make a document no reader reads.
    private static readonly JsonSerializerOptions Writing = FrozenOptions.Of(new(DefinitionJsonContext.Default.Options)
    {
        TypeInfoResolver = DefinitionJsonContext.Default.WithAddedModifier(RefuseMissing),
    });

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

        // The strict encoder made bytes within the limit and valid UTF-8, so they are not checked again.
        return StrictText.ToUtf8(json, _limits.MaxBytes, Document, out var problem) is { } bytes
            ? ReadChecked(bytes)
            : new DefinitionReadResult(null, [problem!], null);
    }

    /// <summary>
    /// Reads a definition the site stored, with <see cref="DefinitionLimits.Ceiling"/> instead of the site's limits: a
    /// host that lowers a limit refuses to save a form above it, but the form keeps reading (ADR-0042).
    /// </summary>
    public DefinitionReadResult ReadStored(string json) => Stored.Read(json);

    /// <summary>Reads a definition from UTF-8 bytes.</summary>
    public DefinitionReadResult Read(ReadOnlySpan<byte> utf8) =>
        StrictText.Check(utf8, _limits.MaxBytes, Document) is { } refused ? new DefinitionReadResult(null, [refused], null) : ReadChecked(utf8);

    /// <summary>
    /// The canonical form of a definition as UTF-8 bytes. A definition built in code goes through
    /// <see cref="CheckSave"/> first: the writer refuses only what it cannot write at all.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The definition holds what the canonical form cannot carry, such as a lone surrogate, a value its enum does not
    /// define or a missing value the model requires, or nests deeper than a document may; <see cref="CheckSave"/>
    /// refuses it first, at its path.
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

        return canonical.Length > _limits.MaxBytes ? [StrictText.TooLarge(Document, _limits.MaxBytes)] : [];
    }

    // The canonical bytes, or null with what keeps them from being written. Depth alone decides limit_exceeded: the
    // canonical text nests deeper than a reader reads, or the writer stopped while it was already deeper than that
    // (System.Text.Json stops at ModelDepth). Whatever else stops the writer is a value the canonical form cannot
    // carry, such as a lone surrogate, a value its enum does not define or a missing required value (invalid_value).
    private static byte[]? TryWriteCanonical(FormDefinition definition, out DefinitionProblem? problem)
    {
        ArgumentNullException.ThrowIfNull(definition);
        problem = null;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { MaxDepth = ModelDepth, SkipValidation = true }))
        {
            try
            {
                JsonSerializer.Serialize(writer, definition, (JsonTypeInfo<FormDefinition>)Writing.GetTypeInfo(typeof(FormDefinition)));
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
            {
                problem = writer.CurrentDepth > MaxDepth
                    ? DefinitionSaveChecks.NestsTooDeep()
                    : new DefinitionProblem(DefinitionErrors.InvalidValue, JsonPaths.Root, $"The definition cannot be written: {exception.Message}");
                return null;
            }
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory, new JsonDocumentOptions { MaxDepth = ModelDepth });
        var canonical = CanonicalJson.WriteUtf8(document.RootElement, sortProperties: false);
        if (StrictText.NestsDeeperThan(canonical, MaxDepth))
        {
            problem = DefinitionSaveChecks.NestsTooDeep();
            return null;
        }

        return canonical;
    }

    // A reference the model requires is written, or stops the writer when it is missing.
    private static void RefuseMissing(JsonTypeInfo info)
    {
        foreach (var property in info.Properties)
        {
            if (!property.PropertyType.IsValueType && !property.IsGetNullable)
            {
                var name = $"{info.Type.Name}.{property.Name}";
                property.ShouldSerialize = (_, value) => value is not null ? true : throw new ArgumentException($"{name} is missing, and the model requires it.");
            }
        }
    }

    // Reads bytes that are within the limit and valid UTF-8.
    private DefinitionReadResult ReadChecked(ReadOnlySpan<byte> utf8)
    {
        using var document = StrictText.Parse(utf8, MaxDepth, out var refused);
        if (document is null)
        {
            return new DefinitionReadResult(null, [refused!], null);
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

        if (StrictText.NestsDeeperThan(upgraded, MaxDepth))
        {
            return DefinitionReadResult.Refused(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"Upgraded from schema version {version}, the document nests deeper than {MaxDepth} levels.", version);
        }

        using var upgradedDocument = JsonDocument.Parse(upgraded, new JsonDocumentOptions { MaxDepth = MaxDepth });
        return ReadCurrent(upgradedDocument.RootElement, version);
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
        var shape = new DefinitionShapeReader();
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
            var path = exception is JsonException json ? RuleItemConverter.PathOf(json) : null;
            return DefinitionReadResult.Refused(DefinitionErrors.InvalidValue, path ?? JsonPaths.Root, exception.Message, sourceVersion);
        }

        var problems = DefinitionSaveChecks.Check(definition, _limits);
        return problems.Count > 0
            ? new DefinitionReadResult(null, problems, sourceVersion)
            : new DefinitionReadResult(definition, [], sourceVersion);
    }
}
