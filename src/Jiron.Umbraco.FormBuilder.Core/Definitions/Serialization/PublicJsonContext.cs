using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The source-generated metadata of the public projection (ADR-0035), with the definition's naming and converters,
/// so a text, a condition or a settings object is written the same in both.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    MaxDepth = FormDefinitionSerializer.ModelDepth,
    Converters = [typeof(UtcInstantConverter), typeof(StrictStringConverter)])]
[JsonSerializable(typeof(PublicForm))]
[JsonSerializable(typeof(RuleGroup))]
[JsonSerializable(typeof(RuleCondition))]
internal sealed partial class PublicJsonContext : JsonSerializerContext;
