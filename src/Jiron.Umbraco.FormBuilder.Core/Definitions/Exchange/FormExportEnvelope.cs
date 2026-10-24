using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;

/// <summary>A form as an export file holds it, <c>jfb-form-export/1</c> (ADR-0002, ADR-0046).</summary>
/// <param name="FormKey">The form's key where it was exported, which is also its definition's key.</param>
/// <param name="Alias">The form's alias there.</param>
/// <param name="Name">The form's name there.</param>
/// <param name="PackageVersion">The version of Jiron Form Builder that wrote the file.</param>
/// <param name="ExportedUtc">When it was written.</param>
/// <param name="Definition">The definition, upgraded to the current schema when it was read.</param>
/// <param name="OptionLists">The library option lists the definition names that the exporting site had.</param>
internal sealed record FormExportEnvelope(
    Guid FormKey,
    string Alias,
    string Name,
    string PackageVersion,
    DateTimeOffset ExportedUtc,
    FormDefinition Definition,
    IReadOnlyList<ExportedOptionList> OptionLists);

/// <summary>A library option list as an export carries it.</summary>
/// <param name="Key">Its key, which definitions name it by.</param>
/// <param name="Alias">Its alias.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">Where its options come from.</param>
/// <param name="Config">The source's configuration.</param>
/// <param name="Items">The items of a manual or uploaded list.</param>
/// <param name="CacheSeconds">How long the options of a source are cached.</param>
internal sealed record ExportedOptionList(Guid Key, string Alias, string Name, string Kind, JsonObject Config, JsonArray Items, int CacheSeconds);

/// <summary>An envelope that was read, or why it was refused.</summary>
/// <param name="Envelope">The envelope, or null when it was refused.</param>
/// <param name="Problems">Why it was refused, with paths into the file; empty when it was read.</param>
internal sealed record EnvelopeReadResult(FormExportEnvelope? Envelope, IReadOnlyList<DefinitionProblem> Problems);

/// <summary>
/// Writes and reads export envelopes (ADR-0046). The writer indents by two spaces and escapes every character outside
/// ASCII; the reader is as strict as the definition reader, checks the format first, and reads the definition like a
/// save: measured without its indentation against the site's limit, upgraded from an older schema, refused from a newer.
/// </summary>
internal static class FormExportFormat
{
    /// <summary>The format this version writes and reads.</summary>
    public const string Format = "jfb-form-export/1";

    /// <summary>How many times the definition limit an envelope may take, for its indentation and its option lists.</summary>
    public const int EnvelopeFactor = 4;

    /// <summary>How deeply an envelope may nest: a definition's depth inside it, and room for option list items.</summary>
    public const int MaxDepth = 4 * FormDefinitionSerializer.MaxDepth;

    /// <summary>The longest package version an envelope may name.</summary>
    public const int MaxPackageVersionLength = 64;

    private const string DefinitionProperty = "definition";

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, NewLine = "\n", MaxDepth = MaxDepth };
    private static readonly string[] RootProperties = ["format", "packageVersion", "exportedUtc", "form", DefinitionProperty, "optionLists"];
    private static readonly string[] FormProperties = ["key", "alias", "name"];
    private static readonly string[] ListProperties = ["key", "alias", "name", "kind", "config", "items", "cacheSeconds"];

    /// <summary>The envelope as the text of its file.</summary>
    public static string Write(FormExportEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var definition = JsonNode.Parse(FormDefinitionSerializer.Write(envelope.Definition), documentOptions: new JsonDocumentOptions { MaxDepth = FormDefinitionSerializer.MaxDepth });
        var lists = new JsonArray();
        foreach (var list in envelope.OptionLists)
        {
            lists.Add(new JsonObject
            {
                ["key"] = list.Key.ToString("D"),
                ["alias"] = list.Alias,
                ["name"] = list.Name,
                ["kind"] = list.Kind,
                ["config"] = list.Config.DeepClone(),
                ["items"] = list.Items.DeepClone(),
                ["cacheSeconds"] = list.CacheSeconds,
            });
        }

        var root = new JsonObject
        {
            ["format"] = Format,
            ["packageVersion"] = envelope.PackageVersion,
            ["exportedUtc"] = envelope.ExportedUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            ["form"] = new JsonObject { ["key"] = envelope.FormKey.ToString("D"), ["alias"] = envelope.Alias, ["name"] = envelope.Name },
            [DefinitionProperty] = definition,
            ["optionLists"] = lists,
        };
        return root.ToJsonString(Indented);
    }

    /// <summary>Reads an envelope from the text of its file.</summary>
    /// <param name="json">The file's text.</param>
    /// <param name="serializer">The definition reader, with the site's limits.</param>
    public static EnvelopeReadResult Read(string json, FormDefinitionSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(serializer);
        byte[] bytes;
        try
        {
            if (StrictUtf8.GetByteCount(json) > MaxBytes(serializer))
            {
                return TooLarge(serializer);
            }

            bytes = StrictUtf8.GetBytes(json);
        }
        catch (EncoderFallbackException)
        {
            return Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, "The text holds a lone surrogate.");
        }

        return Read(bytes, serializer);
    }

    /// <summary>Reads an envelope from the UTF-8 bytes of its file.</summary>
    /// <param name="utf8">The file's bytes.</param>
    /// <param name="serializer">The definition reader, with the site's limits.</param>
    public static EnvelopeReadResult Read(ReadOnlySpan<byte> utf8, FormDefinitionSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        if (utf8.Length > MaxBytes(serializer))
        {
            return TooLarge(serializer);
        }

        if (!System.Text.Unicode.Utf8.IsValid(utf8))
        {
            return Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, "The bytes are not valid UTF-8.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { MaxDepth = MaxDepth });
        }
        catch (JsonException exception)
        {
            return Refused(DefinitionErrors.InvalidJson, JsonPaths.Root, $"The text is not JSON, or nests deeper than {MaxDepth} levels (line {exception.LineNumber + 1}, byte {exception.BytePositionInLine + 1}).");
        }

        using (document)
        {
            return new Reader(serializer).Read(document.RootElement);
        }
    }

    private static int MaxBytes(FormDefinitionSerializer serializer) => serializer.Limits.MaxBytes * EnvelopeFactor;

    private static EnvelopeReadResult TooLarge(FormDefinitionSerializer serializer) =>
        Refused(DefinitionErrors.LimitExceeded, JsonPaths.Root, $"An export file has at most {MaxBytes(serializer)} bytes.");

    private static EnvelopeReadResult Refused(string code, string path, string message) => new(null, [new DefinitionProblem(code, path, message)]);

    // One read of one envelope, collecting its problems.
    private sealed class Reader(FormDefinitionSerializer serializer)
    {
        private readonly List<DefinitionProblem> _problems = [];

        public EnvelopeReadResult Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Refused(DefinitionErrors.InvalidType, JsonPaths.Root, $"An export file is an object, not {JsonPaths.Describe(root.ValueKind)}.");
            }

            // The format comes first: a file of another format is refused for that, whatever else it holds.
            if (FormatProblem(root) is { } format)
            {
                return new EnvelopeReadResult(null, [format]);
            }

            if (Properties(root, JsonPaths.Root, RootProperties, optional: "optionLists") is not { } properties)
            {
                return Failed();
            }

            var packageVersion = PackageVersion(properties["packageVersion"]);
            var exportedUtc = Instant(properties["exportedUtc"], JsonPaths.Property(JsonPaths.Root, "exportedUtc"));
            var form = Form(properties["form"]);
            var definition = Definition(properties[DefinitionProperty]);
            var lists = properties.TryGetValue("optionLists", out var listsElement) ? OptionLists(listsElement) : [];
            if (form is { } named && definition is not null && definition.Key != named.Key)
            {
                Fail(DefinitionErrors.InvalidValue, JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, DefinitionProperty), "key"), "The definition's key is not the form's key.");
            }

            if (_problems.Count > 0 || packageVersion is null || exportedUtc is null || form is null || definition is null || lists is null)
            {
                return Failed();
            }

            return new EnvelopeReadResult(new FormExportEnvelope(form.Value.Key, form.Value.Alias, form.Value.Name, packageVersion, exportedUtc.Value, definition, lists), []);
        }

        private static DefinitionProblem? FormatProblem(JsonElement root)
        {
            var path = JsonPaths.Property(JsonPaths.Root, "format");
            foreach (var property in root.EnumerateObject())
            {
                if (JsonPaths.TryGetName(property) != "format")
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return new DefinitionProblem(DefinitionErrors.InvalidType, path, $"The format is a string, not {JsonPaths.Describe(property.Value.ValueKind)}.");
                }

                var format = JsonPaths.TryGetString(property.Value);
                return format == Format
                    ? null
                    : new DefinitionProblem(DefinitionErrors.UnsupportedFormat, path, $"This version of Jiron Form Builder reads {Format} files, not \"{format}\"; a newer version wrote it.");
            }

            return new DefinitionProblem(DefinitionErrors.MissingProperty, path, "An export file names its format.");
        }

        private string? PackageVersion(JsonElement value)
        {
            var path = JsonPaths.Property(JsonPaths.Root, "packageVersion");
            if (String(value, path) is not { } version)
            {
                return null;
            }

            if (version.Length == 0 || version.Length > MaxPackageVersionLength)
            {
                Fail(version.Length == 0 ? DefinitionErrors.InvalidValue : DefinitionErrors.LimitExceeded, path, $"A package version has 1 to {MaxPackageVersionLength} characters.");
                return null;
            }

            return version;
        }

        private (Guid Key, string Alias, string Name)? Form(JsonElement value)
        {
            var path = JsonPaths.Property(JsonPaths.Root, "form");
            if (Properties(value, path, FormProperties, optional: null) is not { } properties)
            {
                return null;
            }

            var key = Key(properties["key"], JsonPaths.Property(path, "key"));
            var alias = String(properties["alias"], JsonPaths.Property(path, "alias"));
            var name = String(properties["name"], JsonPaths.Property(path, "name"));
            return key is { } formKey && alias is not null && name is not null ? (formKey, alias, name) : null;
        }

        private FormDefinition? Definition(JsonElement value)
        {
            var path = JsonPaths.Property(JsonPaths.Root, DefinitionProperty);
            if (value.ValueKind != JsonValueKind.Object)
            {
                Fail(DefinitionErrors.InvalidType, path, $"A definition is an object, not {JsonPaths.Describe(value.ValueKind)}.");
                return null;
            }

            // The definition is measured in its canonical text, without the file's indentation and escapes, as a save
            // would store it; what the canonical text leaves out (nulls, empty lists) means the same as what is there.
            byte[] compact;
            try
            {
                compact = CanonicalJson.WriteUtf8(value, sortProperties: false);
            }
            catch (ArgumentException)
            {
                Fail(DefinitionErrors.InvalidJson, path, "The definition holds a lone surrogate.");
                return null;
            }

            var read = serializer.Read(compact);
            foreach (var problem in read.Problems)
            {
                _problems.Add(problem with { Path = path + problem.Path[JsonPaths.Root.Length..] });
            }

            return read.Definition;
        }

        private List<ExportedOptionList>? OptionLists(JsonElement value)
        {
            var path = JsonPaths.Property(JsonPaths.Root, "optionLists");
            if (value.ValueKind != JsonValueKind.Array)
            {
                Fail(DefinitionErrors.InvalidType, path, $"The option lists are an array, not {JsonPaths.Describe(value.ValueKind)}.");
                return null;
            }

            var lists = new List<ExportedOptionList>();
            var keys = new HashSet<Guid>();
            var failed = false;
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                var at = JsonPaths.Index(path, index++);
                if (OptionList(item, at) is not { } list)
                {
                    failed = true;
                    continue;
                }

                if (!keys.Add(list.Key))
                {
                    Fail(DefinitionErrors.DuplicateKey, JsonPaths.Property(at, "key"), "Another option list of the file has this key.");
                    failed = true;
                    continue;
                }

                lists.Add(list);
            }

            return failed ? null : lists;
        }

        private ExportedOptionList? OptionList(JsonElement value, string path)
        {
            if (Properties(value, path, ListProperties, optional: null) is not { } properties)
            {
                return null;
            }

            var key = Key(properties["key"], JsonPaths.Property(path, "key"));
            var alias = String(properties["alias"], JsonPaths.Property(path, "alias"));
            var name = String(properties["name"], JsonPaths.Property(path, "name"));
            var kind = String(properties["kind"], JsonPaths.Property(path, "kind"));
            var config = Node(properties["config"], JsonPaths.Property(path, "config"), JsonValueKind.Object, "an object") as JsonObject;
            var items = Node(properties["items"], JsonPaths.Property(path, "items"), JsonValueKind.Array, "an array") as JsonArray;
            var cacheSeconds = properties["cacheSeconds"];
            int? seconds = cacheSeconds.ValueKind == JsonValueKind.Number && cacheSeconds.TryGetInt32(out var number) && number >= 0 ? number : null;
            if (seconds is null)
            {
                Fail(cacheSeconds.ValueKind == JsonValueKind.Number ? DefinitionErrors.InvalidValue : DefinitionErrors.InvalidType, JsonPaths.Property(path, "cacheSeconds"), "The cache time is a whole number of seconds, 0 or more.");
            }

            return key is { } listKey && alias is not null && name is not null && kind is not null && config is not null && items is not null && seconds is { } cache
                ? new ExportedOptionList(listKey, alias, name, kind, config, items, cache)
                : null;
        }

        // The properties of an object, each name once and none unknown; every one but the optional one is required.
        private Dictionary<string, JsonElement>? Properties(JsonElement value, string path, string[] known, string? optional)
        {
            if (value.ValueKind != JsonValueKind.Object)
            {
                Fail(DefinitionErrors.InvalidType, path, $"This is an object, not {JsonPaths.Describe(value.ValueKind)}.");
                return null;
            }

            var before = _problems.Count;
            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (JsonPaths.TryGetName(property) is not { } name)
                {
                    Fail(DefinitionErrors.InvalidJson, path, "A property name holds a lone surrogate.");
                }
                else if (!known.Contains(name, StringComparer.Ordinal))
                {
                    Fail(DefinitionErrors.UnknownProperty, JsonPaths.Property(path, name), $"An export file has no property \"{name}\" here.");
                }
                else if (!properties.TryAdd(name, property.Value))
                {
                    Fail(DefinitionErrors.DuplicateProperty, JsonPaths.Property(path, name), $"The property \"{name}\" is given twice.");
                }
            }

            foreach (var name in known)
            {
                if (name != optional && !properties.ContainsKey(name))
                {
                    Fail(DefinitionErrors.MissingProperty, JsonPaths.Property(path, name), $"The property \"{name}\" is missing.");
                }
            }

            return _problems.Count == before ? properties : null;
        }

        private string? String(JsonElement value, string path)
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                Fail(DefinitionErrors.InvalidType, path, $"This is a string, not {JsonPaths.Describe(value.ValueKind)}.");
                return null;
            }

            var text = JsonPaths.TryGetString(value);
            if (text is null)
            {
                Fail(DefinitionErrors.InvalidJson, path, "The text holds a lone surrogate.");
            }

            return text;
        }

        private Guid? Key(JsonElement value, string path)
        {
            if (String(value, path) is not { } text)
            {
                return null;
            }

            // As in a definition: lowercase with hyphens, and not the empty key.
            if (!Guid.TryParseExact(text, "D", out var key) || key == Guid.Empty || key.ToString("D") != text)
            {
                Fail(DefinitionErrors.InvalidValue, path, "A key is a GUID in lowercase, such as 3f2504e0-4f89-41d3-9a0c-0305e82c3301.");
                return null;
            }

            return key;
        }

        private DateTimeOffset? Instant(JsonElement value, string path)
        {
            if (String(value, path) is null)
            {
                return null;
            }

            if (!value.TryGetDateTimeOffset(out var instant))
            {
                Fail(DefinitionErrors.InvalidValue, path, "The time is an ISO 8601 date and time, such as 2026-10-24T08:30:00.000Z.");
                return null;
            }

            return instant.ToUniversalTime();
        }

        private JsonNode? Node(JsonElement value, string path, JsonValueKind kind, string what)
        {
            if (value.ValueKind != kind)
            {
                Fail(DefinitionErrors.InvalidType, path, $"This is {what}, not {JsonPaths.Describe(value.ValueKind)}.");
                return null;
            }

            return kind == JsonValueKind.Object ? JsonObject.Create(value.Clone()) : JsonArray.Create(value.Clone());
        }

        private void Fail(string code, string path, string message) => _problems.Add(new DefinitionProblem(code, path, message));

        private EnvelopeReadResult Failed() => new(null, _problems.ToList());
    }
}
