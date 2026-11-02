using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Signatures;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// A typed name against Core's <see cref="TypedSignature"/> (ROADMAP 6.4n, ADR-0153): the name the browser cleans is the
/// server's, and the PNG the browser makes of it reads back on the server as the server's own does.
/// </summary>
internal static class TypedNameVector
{
    public static JsonNode Clean(JsonElement typedName) => new JsonObject
    {
        ["name"] = TypedSignature.Clean(typedName.GetProperty("typed").GetString()),
    };

    public static JsonNode Read(JsonElement expected)
    {
        var hex = expected.GetProperty("png").GetString()!;
        var png = Convert.FromHexString(hex);
        var size = PngHeader.Read(png) ?? throw new InvalidOperationException("The header of the PNG does not read.");
        return new JsonObject
        {
            ["png"] = hex,
            ["name"] = TypedSignature.NameOf(png),
            ["width"] = size.Width,
            ["height"] = size.Height,
        };
    }
}
