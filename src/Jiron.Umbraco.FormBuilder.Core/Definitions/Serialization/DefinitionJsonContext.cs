using System.Text.Json.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The source-generated metadata of the definition model (ADR-0002): camelCase names, nothing written for null, and
/// strict reading. The reader walks this metadata to validate a document's shape before it is read
/// (<see cref="DefinitionShapeReader"/>), so the model and the checks never drift apart.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    Converters = [typeof(UtcInstantConverter), typeof(StrictStringConverter)])]
[JsonSerializable(typeof(FormDefinition))]
[JsonSerializable(typeof(RuleGroup))]
[JsonSerializable(typeof(RuleCondition))]
internal sealed partial class DefinitionJsonContext : JsonSerializerContext;
