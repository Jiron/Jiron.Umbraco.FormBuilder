using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// Runs a vector's <c>post</c> through the no-JS post's reader (ROADMAP 6.4b, ADR-0100, ADR-0110): the form's fields as
/// the page names them, on one page, and the page's names and values as a browser posts them, read as the surface
/// action reads a post. The runtime's reader in <c>packages/logic</c> runs the same cases.
/// </summary>
internal static class PostVector
{
    /// <summary>The post as both runners write it: <c>{ "fields", "rows", "context", "query" }</c>.</summary>
    public static JsonObject Run(string id, JsonElement post)
    {
        var number = 0;
        FormField Field(JsonElement given)
        {
            if (given.TryGetProperty("builtIn", out _))
            {
                throw new AssertionException($"{id}: a site's own type under a built-in alias is the runtime's alone; test it in Vitest.");
            }

            var alias = given.TryGetProperty("alias", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;
            return new FormField
            {
                Key = Key(++number),
                Type = given.GetProperty("type").GetString()!,
                Alias = alias,
                Label = new LocalizedText(alias ?? "content"),
                Settings = given.TryGetProperty("settings", out var settings) ? SettingsObject.Parse(settings.GetRawText()) : null,
                Fields = given.TryGetProperty("fields", out var children) ? [.. children.EnumerateArray().Select(Field)] : [],
            };
        }

        var definition = new FormDefinition
        {
            Key = Key(0),
            Pages = [new FormPage { Key = Key(1000), Groups = [new FormGroup { Key = Key(1001), Fields = [.. post.GetProperty("fields").EnumerateArray().Select(Field)] }] }],
        };

        // A form collection holds a name's values together, in the order posted, as ASP.NET Core reads a body.
        var names = new Dictionary<string, List<string?>>(StringComparer.Ordinal);
        foreach (var entry in post.GetProperty("entries").EnumerateArray())
        {
            var name = entry[0].GetString()!;
            if (!names.TryGetValue(name, out var values))
            {
                names[name] = values = [];
            }

            values.Add(entry[1].GetString());
        }

        var reader = FormPostReader.Read(
            new FormCollection(names.ToDictionary(pair => pair.Key, pair => new StringValues([.. pair.Value]), StringComparer.Ordinal)),
            definition,
            TestFieldTypes.Registry);
        var (fields, rows) = reader.Posted();
        return new JsonObject
        {
            ["fields"] = Answers(fields),
            ["rows"] = new JsonObject(rows.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)new JsonArray([.. pair.Value.Select(row => (JsonNode)Answers(row))])))),
            ["context"] = new JsonObject(reader.Context.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value))),
            ["query"] = new JsonObject(reader.Query.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)Texts(pair.Value)))),
        };
    }

    private static JsonObject Answers(IReadOnlyDictionary<string, PostedAnswer> answers) =>
        new(answers.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)Answer(pair.Value))));

    private static JsonObject Answer(PostedAnswer answer)
    {
        var json = new JsonObject { ["values"] = Texts(answer.Values) };
        if (answer.Parts is { } parts)
        {
            json["parts"] = new JsonArray([.. parts.Select(part => (JsonNode)new JsonArray(part.Key, Texts(part.Value)))]);
        }

        return json;
    }

    private static JsonArray Texts(IEnumerable<string?> texts) => new([.. texts.Select(text => (JsonNode?)text)]);

    private static Guid Key(int number) => new($"6b000000-0000-4000-8000-{number:x12}");
}
