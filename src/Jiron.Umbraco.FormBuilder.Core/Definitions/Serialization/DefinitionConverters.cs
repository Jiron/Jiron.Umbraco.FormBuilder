using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

// The converters of the definition model. The reader validates every document's shape before System.Text.Json reads
// it (DefinitionShapeReader), so these read only well-formed input; they still refuse what they cannot read. What they
// cannot write they refuse with ArgumentException, and the save checks refuse the same values first, at their paths.

/// <summary>
/// A definition word as its camelCase name, never as a number (ADR-0002): exactly the word, in its letter case, on
/// reading; on writing, a value the enum does not define is refused with the save checks' message.
/// </summary>
internal sealed class DefinitionEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && DefinitionWords<T>.TryRead(reader.GetString()!, out var value)
            ? value
            : throw new JsonException($"Expected one of {string.Join(", ", DefinitionWords<T>.All)}.");

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(DefinitionWords<T>.Of(value) ?? throw new ArgumentException(DefinitionWords<T>.NotAWord(value), nameof(value)));
}

/// <summary>An instant in UTC as <c>YYYY-MM-DDTHH:mm:ssZ</c>, whole seconds only (ADR-0002, ADR-0034).</summary>
internal sealed class UtcInstantConverter : JsonConverter<DateTime>
{
    public const string Format = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        TryParse(reader.GetString(), out var instant) ? instant : throw new JsonException("An instant is YYYY-MM-DDTHH:mm:ssZ.");

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        if (!IsWritable(value))
        {
            throw new ArgumentException("An instant is in UTC and has whole seconds.", nameof(value));
        }

        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }

    /// <summary>Whether the text is an instant in the definition's form.</summary>
    public static bool TryParse(string? text, out DateTime instant) =>
        DateTime.TryParseExact(
            text,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out instant);

    /// <summary>Whether the instant can be written without losing anything: UTC, whole seconds.</summary>
    public static bool IsWritable(DateTime value) => value.Kind == DateTimeKind.Utc && value.Ticks % TimeSpan.TicksPerSecond == 0;
}

/// <summary>
/// Every string of a definition: written only when it is well-formed UTF-16. System.Text.Json would write a lone
/// surrogate as U+FFFD, which would change the text without a word; the save checks refuse such a text first.
/// </summary>
internal sealed class StrictStringConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() : throw new JsonException("Expected a string.");

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(Checked(value));

    /// <summary>The string, if it is there and holds no lone surrogate.</summary>
    /// <exception cref="ArgumentNullException">The string is null where the model needs one.</exception>
    /// <exception cref="ArgumentException">The string holds a lone surrogate.</exception>
    public static string Checked(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return IsWellFormed(value) ? value : throw new ArgumentException("A text of the definition holds a lone surrogate.", nameof(value));
    }

    /// <summary>Whether the string is well-formed UTF-16: every surrogate is half of a pair.</summary>
    public static bool IsWellFormed(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The string with each lone surrogate replaced by U+FFFD, as UTF-8 writes it; a well-formed one as it is.</summary>
    /// <param name="text">The string, such as a dictionary item's text, which no check held to the definition's rules.</param>
    public static string WellFormed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return IsWellFormed(text) ? text : Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>A localised text as an object from culture to text, cultures sorted ordinally (<c>""</c> first).</summary>
internal sealed class LocalizedStringConverter<T> : JsonConverter<T>
    where T : LocalizedString
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A localised text is an object.");
        }

        var texts = new List<KeyValuePair<string, string>>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var culture = reader.GetString()!;
            reader.Read();
            texts.Add(new KeyValuePair<string, string>(culture, reader.GetString() ?? throw new JsonException("A text is a string.")));
        }

        try
        {
            return (T)(object)(typeof(T) == typeof(LocalizedText) ? new LocalizedText(texts) : new LocalizedHtml(texts));
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (culture, text) in value.Texts)
        {
            writer.WriteString(culture, StrictStringConverter.Checked(text));
        }

        writer.WriteEndObject();
    }
}

/// <summary>A default value as a string or an array of strings.</summary>
internal sealed class FieldDefaultValueConverter : JsonConverter<FieldDefaultValue>
{
    public override FieldDefaultValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return FieldDefaultValue.Of(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("A default value is a string or an array of strings.");
        }

        var values = new List<string>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            values.Add(reader.TokenType == JsonTokenType.String ? reader.GetString()! : throw new JsonException("A default value is a string or an array of strings."));
        }

        return FieldDefaultValue.OfList(values);
    }

    public override void Write(Utf8JsonWriter writer, FieldDefaultValue value, JsonSerializerOptions options)
    {
        if (!value.IsList)
        {
            writer.WriteStringValue(StrictStringConverter.Checked(value.Values[0]));
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value.Values)
        {
            writer.WriteStringValue(StrictStringConverter.Checked(item));
        }

        writer.WriteEndArray();
    }
}

/// <summary>An operand as a string, <c>{ "field": key }</c>, <c>{ "today": offset }</c> or an array of those.</summary>
internal sealed class ConditionOperandConverter : JsonConverter<ConditionOperand>
{
    public override ConditionOperand Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            return ReadOne(ref reader);
        }

        var items = new List<ConditionOperand>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            items.Add(ReadOne(ref reader));
        }

        return items.Count > 0 ? new ConditionOperand.List(items) : throw new JsonException("A list operand has at least one item.");
    }

    public override void Write(Utf8JsonWriter writer, ConditionOperand value, JsonSerializerOptions options)
    {
        if (value is ConditionOperand.List list)
        {
            writer.WriteStartArray();
            foreach (var item in list.Items)
            {
                WriteOne(writer, item);
            }

            writer.WriteEndArray();
            return;
        }

        WriteOne(writer, value);
    }

    private static ConditionOperand ReadOne(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ConditionOperand.Text(reader.GetString()!);
        }

        if (reader.TokenType != JsonTokenType.StartObject || !reader.Read() || reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException("An operand is a string, an answer or a relative date.");
        }

        var name = reader.GetString();
        reader.Read();
        ConditionOperand operand = name switch
        {
            "field" => new ConditionOperand.Field(reader.GetGuid()),
            "today" => new ConditionOperand.Today(reader.GetString()!),
            _ => throw new JsonException("An operand object names \"field\" or \"today\"."),
        };

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException("An operand object has one property.");
        }

        return operand;
    }

    private static void WriteOne(Utf8JsonWriter writer, ConditionOperand value)
    {
        switch (value)
        {
            case ConditionOperand.Text text:
                writer.WriteStringValue(StrictStringConverter.Checked(text.Value));
                break;
            case ConditionOperand.Field field:
                writer.WriteStartObject();
                writer.WriteString("field", field.Key);
                writer.WriteEndObject();
                break;
            case ConditionOperand.Today today:
                writer.WriteStartObject();
                writer.WriteString("today", StrictStringConverter.Checked(today.Offset));
                writer.WriteEndObject();
                break;
            default:
                throw new ArgumentException("A list operand holds no list.", nameof(value));
        }
    }
}

/// <summary>
/// A condition, or a nested group: an object with <c>match</c> or <c>conditions</c> is a group. The item is read once,
/// straight from the reader: a copy of it looks at the object's own property names first, skipping their values, so a
/// group nested many levels deep is not parsed again at every level. Only an item refused for something of its own is
/// read again, to name where in it the problem is; a group refused for an item inside it names that item's place by
/// skipping over the values before it, so each level costs one pass over its item, however deep the groups nest.
/// </summary>
internal sealed class RuleItemConverter : JsonConverter<RuleItem>
{
    public override RuleItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var start = reader;
        var copy = reader;
        var group = IsGroup(ref copy);
        try
        {
            return group
                ? Converter<RuleGroup>(options).Read(ref reader, typeof(RuleGroup), options)
                : Converter<RuleCondition>(options).Read(ref reader, typeof(RuleCondition), options);
        }
        catch (JsonException exception) when (exception.Path is null)
        {
            // Called straight, the model's converter keeps no path. An error of an item inside this group named its
            // part already, so this level adds the item's place in the conditions. Any other error is this item's own:
            // the serializer keeps a path, so the item is read again through it. The error then takes the path to the
            // item from the serializer that called this converter, and keeps the part inside the item.
            var at = start.TokenStartIndex;
            var rest = group && exception is ItemReadException inner && ConditionIndex(start, inner.Start) is { } index
                ? JsonPaths.Index(JsonPaths.Property(JsonPaths.Root, "conditions"), index) + inner.Rest[JsonPaths.Root.Length..]
                : PathInside(ref start, group, options);
            throw new ItemReadException(exception.Message, rest, at, exception);
        }
    }

    /// <summary>
    /// The full path of a reader error: the path System.Text.Json names, and the part inside a rule item that the
    /// item's own read named.
    /// </summary>
    public static string? PathOf(JsonException exception) =>
        exception is ItemReadException item ? (exception.Path ?? JsonPaths.Root) + item.Rest[JsonPaths.Root.Length..] : exception.Path;

    public override void Write(Utf8JsonWriter writer, RuleItem value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case RuleGroup group:
                JsonSerializer.Serialize(writer, group, (JsonTypeInfo<RuleGroup>)options.GetTypeInfo(typeof(RuleGroup)));
                break;
            case RuleCondition condition:
                JsonSerializer.Serialize(writer, condition, (JsonTypeInfo<RuleCondition>)options.GetTypeInfo(typeof(RuleCondition)));
                break;
            default:
                throw new ArgumentException($"Unknown rule item {value.GetType()}.", nameof(value));
        }
    }

    /// <summary>Whether an object is a nested group rather than a condition.</summary>
    public static bool IsGroup(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object && (element.TryGetProperty("match", out _) || element.TryGetProperty("conditions", out _));

    // The model's own converter of a type. Called straight on the reader, it reads the object once, where
    // JsonSerializer.Deserialize would first skip over the whole object to find where it ends.
    private static JsonConverter<T> Converter<T>(JsonSerializerOptions options) => (JsonConverter<T>)options.GetTypeInfo(typeof(T)).Converter;

    // The same test on a reader at the start of a value; it moves the reader, so it is given a copy. The serializer
    // hands a converter the whole value, so every property's value can be skipped.
    private static bool IsGroup(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("match"u8) || reader.ValueTextEquals("conditions"u8))
            {
                return true;
            }

            if (!reader.TrySkip())
            {
                return false;
            }
        }

        return false;
    }

    // The place in a group's conditions of the item that starts at the index child, found by skipping over the values
    // before it; null when the group holds no such item. The reader is a copy at the start of the group.
    private static int? ConditionIndex(Utf8JsonReader reader, long child)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var conditions = reader.ValueTextEquals("conditions"u8);
            if (!reader.Read())
            {
                return null;
            }

            if (conditions && reader.TokenType == JsonTokenType.StartArray)
            {
                for (var index = 0; reader.Read() && reader.TokenType != JsonTokenType.EndArray; index++)
                {
                    if (reader.TokenStartIndex == child)
                    {
                        return index;
                    }

                    if (!reader.TrySkip())
                    {
                        return null;
                    }
                }
            }
            else if (!reader.TrySkip())
            {
                return null;
            }
        }

        return null;
    }

    // Where in a refused item, from "$", its read through the serializer stops; "$" when that read goes through.
    private static string PathInside(ref Utf8JsonReader start, bool group, JsonSerializerOptions options)
    {
        try
        {
            RuleItem? item = group
                ? JsonSerializer.Deserialize(ref start, (JsonTypeInfo<RuleGroup>)options.GetTypeInfo(typeof(RuleGroup)))
                : JsonSerializer.Deserialize(ref start, (JsonTypeInfo<RuleCondition>)options.GetTypeInfo(typeof(RuleCondition)));
            _ = item;
            return JsonPaths.Root;
        }
        catch (JsonException exception)
        {
            return PathOf(exception) ?? JsonPaths.Root;
        }
    }

    // A reader error inside a rule item, with the part of its path inside the item, from "$", and where the item starts
    // in the text the reader reads.
    private sealed class ItemReadException(string message, string rest, long start, Exception inner) : JsonException(message, inner)
    {
        public string Rest { get; } = rest;

        public long Start { get; } = start;
    }
}

/// <summary>Settings as their canonical JSON object.</summary>
internal sealed class SettingsObjectConverter : JsonConverter<SettingsObject>
{
    public override SettingsObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try
        {
            return SettingsObject.From(document.RootElement);
        }
        catch (ArgumentException exception)
        {
            throw new JsonException(exception.Message, exception);
        }
    }

    public override void Write(Utf8JsonWriter writer, SettingsObject value, JsonSerializerOptions options) => value.Value.WriteTo(writer);
}
