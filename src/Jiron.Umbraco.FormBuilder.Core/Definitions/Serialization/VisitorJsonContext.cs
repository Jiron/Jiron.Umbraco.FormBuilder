using System.Text.Json;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The source-generated metadata of a visitor's form (ADR-0084): the public projection's naming and converters, except
/// that a text is written as the one string it holds, since <see cref="VisitorForm.Map"/> left it one culture's.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    MaxDepth = FormDefinitionSerializer.ModelDepth,
    Converters = [typeof(UtcInstantConverter), typeof(StrictStringConverter), typeof(VisitorTextConverter<LocalizedText>), typeof(VisitorTextConverter<LocalizedHtml>)])]
[JsonSerializable(typeof(PublicForm))]
[JsonSerializable(typeof(RuleGroup))]
[JsonSerializable(typeof(RuleCondition))]
internal sealed partial class VisitorJsonContext : JsonSerializerContext;

/// <summary>Writes a text as its default text, the one a visitor's form holds; a visitor's form is never read back.</summary>
/// <typeparam name="T">A plain or a rich text.</typeparam>
internal sealed class VisitorTextConverter<T> : JsonConverter<T>
    where T : LocalizedString
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("A visitor's form is written only.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStringValue(StrictStringConverter.Checked(value.Default));
    }
}
