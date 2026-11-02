using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// A file's first bytes against Core's <see cref="FileKinds"/> (ROADMAP 6.4m, ADR-0152): what the browser checks before it
/// sends a file is what the server checks as it reads one.
/// </summary>
internal static class FileKindVector
{
    public static JsonNode Run(JsonElement fileKind)
    {
        var extension = fileKind.GetProperty("extension").GetString()!;
        var head = Convert.FromHexString(fileKind.GetProperty("head").GetString()!);
        return new JsonObject
        {
            ["executable"] = FileKinds.IsExecutable(head),
            ["matches"] = FileKinds.Matches(extension, head),
            ["hidesMarkup"] = FileKinds.HidesMarkup(extension, head),
        };
    }
}
