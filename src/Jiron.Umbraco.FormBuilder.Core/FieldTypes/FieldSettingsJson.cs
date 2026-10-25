using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// How a field type's settings class meets its JSON (ADR-0055): camel-case names, enums as camel-case text (numbers
/// refused), no property the class lacks, nulls left out. Built-in and custom settings classes take the same path, the
/// reflection resolver, whose metadata is built once per class.
/// </summary>
internal static class FieldSettingsJson
{
    /// <summary>The options every settings class is read and written with.</summary>
    public static JsonSerializerOptions Options { get; } = FrozenOptions.Of(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        NumberHandling = JsonNumberHandling.Strict,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    });
}
